#if HARMONY_RUNTIME_IL_EXPERIMENT
using HarmonyLib;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.ExceptionServices;
using System.Threading;
using System.Threading.Tasks;
using Waylith.Injection;
using WaylithClr;

namespace HarmonyMod;

// Harmony constructs once outside runtime callbacks. The pipeline replays immutable, module-scoped body bytes.
internal static class RuntimeILPatching
{
	static readonly ConcurrentDictionary<RuntimeMethodHandle, Target> targets = new();
	static readonly Transformer transformer = CreateTransformer();
	static Func<MethodBase, MethodBase[]> genericValidation;
	static Action<string> reportFailure;
	// Build 5000 compiles every instantiation the runtime has seen. Older runtimes compile only the witnesses.
	static readonly Action<RuntimeMethodHandle> prepareVariants
		= typeof(MethodBodyUpdater).GetMethod("PrepareGenericVariants", [typeof(RuntimeMethodHandle)])?.CreateDelegate<Action<RuntimeMethodHandle>>();
	// Field injection rewrites a default body, then asks Harmony to regenerate. Report a detach only when nobody regenerates in time.
	static readonly TimeSpan regenerateGrace = TimeSpan.FromSeconds(1);

	internal static string Status => $"priority=0, tracked={targets.Count}, applied={targets.Values.Count(target => Volatile.Read(ref target.Applied))}, "
		 + $"detached={targets.Values.Count(target => Volatile.Read(ref target.Detached))}, broken={targets.Values.Count(target => target.Broken)}";

	sealed class Generation(RuntimeILBody input, byte[] body)
	{
		internal readonly RuntimeILBody Input = input;
		internal readonly byte[] Body = body;
	}

	sealed class Target(MethodBase original)
	{
		internal readonly MethodBase Original = original;
		internal RuntimeILBody Input, PreparedInput;
		internal Generation Active, Offered;
		internal Exception Fault;
		internal bool Applied, Detached, InputChanged, Broken, Rebasing;
	}

	sealed class Transformer() : MethodBodyTransformer(0)
	{
		protected override bool StaysAttached => true;
		protected override bool CanTransform(RuntimeMethodHandle method) => targets.ContainsKey(method);

		protected override bool TryTransform(RuntimeMethodHandle method, in MethodBodyView body,
			 ReadOnlySpan<MethodBodyClause> clauses, MethodBodyBuilder result)
		{
			var target = targets[method];
			var generation = Volatile.Read(ref target.Active);
			if (generation is null)
			{
				Volatile.Write(ref target.Input, new(target.Original, body, clauses));
				return false;
			}
			if (Volatile.Read(ref target.InputChanged) || !generation.Input.Matches(body, clauses))
			{
				Volatile.Write(ref target.InputChanged, true);
				Volatile.Write(ref target.Detached, true);
				throw new InvalidOperationException("Harmony input changed. Its stage is detached; explicitly rebuild Harmony patches for " + target.Original);
			}
			result.Write(generation.Body);
			Volatile.Write(ref target.Offered, generation);
			return true;
		}

		protected override void OnParticipationChanged(ReadOnlySpan<RuntimeMethodHandle> entered, ReadOnlySpan<RuntimeMethodHandle> left)
		{
			foreach (var method in entered) if (targets.TryGetValue(method, out var target)) Volatile.Write(ref target.Applied, true);
			foreach (var method in left) if (targets.TryGetValue(method, out var target)) Volatile.Write(ref target.Applied, false);
		}

		protected override void OnDefaultBodiesChanged(ReadOnlySpan<RuntimeMethodHandle> methods)
		{
			foreach (var method in methods)
				if (targets.TryGetValue(method, out var target) && Volatile.Read(ref target.Active) is not null)
					Volatile.Write(ref target.InputChanged, true);
		}

		protected override void OnTransformFailed(RuntimeMethodHandle method, Exception exception)
		{
			var target = targets[method];
			Volatile.Write(ref target.Fault, exception);
			Volatile.Write(ref target.Detached, true);
			// Return so every consumer receives its notifications. Report outside the runtime lock.
			var report = reportFailure;
			if (report is null) return;
			if (!Volatile.Read(ref target.InputChanged))
			{
				ThreadPool.QueueUserWorkItem(_ => report("Harmony's runtime stage was detached for " + target.Original + ": " + exception));
				return;
			}
			_ = Task.Delay(regenerateGrace).ContinueWith(_ =>
			{
				if (Volatile.Read(ref target.Detached))
					report("Harmony patches stay inactive until the host regenerates them for " + target.Original + ": " + exception);
			});
		}
	}

	static Transformer CreateTransformer()
	{
		var value = new Transformer();
		value.Register();
		return value;
	}

	internal static void Register(Func<MethodBase, bool> include, Func<MethodBase, MethodBase[]> validateGenerics = null, Action<string> reportError = null)
	{
		if (!MethodBodyUpdater.IsSupported) throw new NotSupportedException("Runtime method-body replacement is unavailable");
		genericValidation = validateGenerics;
		reportFailure = reportError;
		MethodBodyPatching.SetEligibility(method => include(method) && Eligible(method));
		MethodBodyPatching.SetBodySource(ReadBody);
		MethodBodyPatching.SetInstaller(Install);
		var previous = Injection.HarmonyRegenerate;
		Injection.HarmonyRegenerate = method => Regenerate(method) || previous?.Invoke(method) is true;
	}

	// The host passes the original, or a Harmony-generated replacement, whose default body it rewrote.
	static bool Regenerate(MethodBase method)
	{
		var original = method is MethodInfo info ? Harmony.GetOriginalMethod(info) : method;
		if (original is null || !targets.ContainsKey(MethodBodyUpdater.GetDefinitionHandle(original))) return false;
		Rebase(original, force: true);
		return true;
	}

	static bool Eligible(MethodBase method) => Ineligible(method) is null;

	// The reason the runtime stage cannot take this method, or null.
	// Ordinary methods, instance constructors and type initializers qualify. The runtime replaces a type initializer's body without running it.
	// The installed body keeps the target's own signature. Custom modifiers and function pointer parameters need no check.
	static string Ineligible(MethodBase method)
	{
		if (method is not (MethodInfo or ConstructorInfo)) return "it is not a method or constructor";
		if (method.Module.Assembly.IsCollectible) return "its assembly is collectible";
		if (method.IsAbstract) return "it is abstract";
		if ((method.CallingConvention & CallingConventions.VarArgs) != 0) return "it takes variable arguments";
		if ((method.GetMethodImplementationFlags() & MethodImplAttributes.Synchronized) != 0) return "it is a synchronized method";
		if (method.GetMethodBody() is null) return "it has no IL body";
		// A closed instantiation installs through its generic definition, so the definition decides. Partially open methods stay declined.
		if (method.IsGenericMethod || method.DeclaringType?.IsGenericType == true)
		{
			var definition = method.Module.ResolveMethod(method.MetadataToken);
			if (method != definition) return method.ContainsGenericParameters ? "it is a partially open generic method" : Ineligible(definition);
		}
		// The baseline reader declines locals it cannot express, such as function pointer locals.
		try { _ = RuntimeILBody.Read(method).LocalVariables; return null; }
		catch (NotSupportedException error) { return "the runtime reader cannot express its locals: " + error.Message; }
	}

	static Target GetTarget(MethodBase method) => targets.GetOrAdd(MethodBodyUpdater.GetDefinitionHandle(method), _ => new(method));

	static void Rebuild(Target target, Generation expected)
	{
		Volatile.Write(ref target.Offered, null);
		var failure = transformer.Rebuild(MethodBodyUpdater.GetDefinitionHandle(target.Original));
		if (failure is not null) ExceptionDispatchInfo.Capture(failure).Throw();
		if (expected is not null && (!ReferenceEquals(Volatile.Read(ref target.Offered), expected) || !Volatile.Read(ref target.Applied)))
			throw new InvalidOperationException("Harmony's runtime stage did not accept this generation: " + target.Original, target.Fault);
	}

	static MethodBody ReadBody(MethodBase method)
	{
		var target = GetTarget(method);
		if (target.Broken) throw new InvalidOperationException("A previous runtime installation left an unknown state: " + method, target.Fault);
		if (Volatile.Read(ref target.Detached) && !target.Rebasing)
			throw new InvalidOperationException("Harmony's runtime stage is detached. The host must explicitly rebuild it: " + method, target.Fault);
		if (Volatile.Read(ref target.Input) is null) Rebuild(target, null);
		target.PreparedInput = target.Input ?? throw new NotSupportedException("Pipeline baseline unavailable: " + method);
		return target.PreparedInput.WithContext(method);
	}

	static void Install(MethodBase original, MethodInfo replacement, Func<Func<object, int>, byte[]> writeBody)
	{
		// Same-target builds and host runtime calls remain serial and non-reentrant.
		var target = GetTarget(original);
		if (target.Broken) throw new InvalidOperationException("A previous runtime installation left an unknown state: " + original, target.Fault);
		if (Volatile.Read(ref target.Detached) && !target.Rebasing) throw new InvalidOperationException("Harmony input changed during construction: " + original, target.Fault);
		if (target.Input is null) Rebuild(target, null);
		var validation = original.ContainsGenericParameters ? ValidationTargets(original) : [original];
		// The runtime memoizes module tokens and retains the dependencies of active or running bodies.
		var body = writeBody(value => value switch
			{
				Type type => MethodBodyUpdater.GetTokenFor(original.Module, type),
				KeyValuePair<Type, bool> { Value: true } instance => MethodBodyUpdater.GetTokenForInstantiation(original.Module, instance.Key),
				// A member on its self-instantiated declaring type, or a generic method over its own parameters
				KeyValuePair<MemberInfo, bool> { Value: true, Key: MethodBase method } => MethodBodyUpdater.GetTokenFor(original.Module, method),
				KeyValuePair<MemberInfo, bool> { Value: true, Key: FieldInfo field } => MethodBodyUpdater.GetTokenFor(original.Module, field),
				// A bare member keeps its metadata meaning. On a generic definition that is the definition itself.
				// The runtime has no token for a generic method definition on a constructed type, so that case is refused.
				MethodBase method when method.DeclaringType is { IsGenericTypeDefinition: true } || method.IsGenericMethodDefinition
					=> method.DeclaringType is { IsConstructedGenericType: true } ? 0 : MethodBodyUpdater.GetTokenForDefinition(original.Module, method),
				MethodBase method => MethodBodyUpdater.GetTokenFor(original.Module, method),
				FieldInfo field when field.DeclaringType is { IsGenericTypeDefinition: true } => MethodBodyUpdater.GetTokenForDefinition(original.Module, field),
				FieldInfo field => MethodBodyUpdater.GetTokenFor(original.Module, field),
				string text => MethodBodyUpdater.GetTokenFor(original.Module, text),
				byte[] signature => MethodBodyUpdater.GetTokenForSignature(original.Module, signature),
				_ => throw new NotSupportedException("Unsupported body token: " + value)
			});
		var previous = target.Active;
		var detached = Volatile.Read(ref target.Detached);
		var generation = new Generation(target.PreparedInput ?? target.Input, body);
		Volatile.Write(ref target.Active, generation);
		try
		{
			Rebuild(target, generation);
			foreach (var method in validation) MethodBodyUpdater.PrepareMethodBody(method);
			PrepareVariants(original);
			Volatile.Write(ref target.Detached, false);
			Volatile.Write(ref target.Fault, null);
		}
		catch (Exception installError)
		{
			if (Volatile.Read(ref target.InputChanged))
			{
				Volatile.Write(ref target.Detached, true);
				throw;
			}
			try
			{
				Volatile.Write(ref target.Active, previous);
				Rebuild(target, previous);
				foreach (var method in validation) MethodBodyUpdater.PrepareMethodBody(method);
				PrepareVariants(original);
				if (!detached) Volatile.Write(ref target.Fault, null);
				Volatile.Write(ref target.Detached, detached);
			}
			catch (Exception recoveryError)
			{
				target.Broken = true;
				target.Fault = recoveryError;
				throw new AggregateException("Compilation and recovery failed: " + original, installError, recoveryError);
			}
			throw;
		}
	}

	// Call outside runtime callbacks, with ordinary Harmony operations quiescent. Reads never clear a detached fault.
	// A forced rebase rebuilds from the current default body even before the pipeline reported a change.
	internal static void Rebase(MethodBase original, bool force = false)
	{
		if (!targets.TryGetValue(MethodBodyUpdater.GetDefinitionHandle(original), out var target))
			throw new InvalidOperationException("The method is not managed by this provider: " + original);
		if (target.Broken) throw new InvalidOperationException("A previous runtime installation left an unknown state: " + original, target.Fault);
		if (!force && !Volatile.Read(ref target.Detached)) return;
		// A working generation survives a failed reconstruction while the default body is unchanged.
		var previous = Volatile.Read(ref target.Detached) ? null : Volatile.Read(ref target.Active);
		target.Rebasing = true;
		try
		{
			Volatile.Write(ref target.Active, null);
			Volatile.Write(ref target.Input, null);
			target.PreparedInput = null;
			Volatile.Write(ref target.InputChanged, false);
			Rebuild(target, null);
			try { MethodBodyPatching.Rebuild(target.Original); }
			catch (Exception rebuildError) when (previous is not null && !target.Broken && Volatile.Read(ref target.Active) is null)
			{
				Restore(target, previous, rebuildError);
				throw;
			}
		}
		finally { target.Rebasing = false; }
	}

	// Reinstalls the previous generation after a failed reconstruction. A changed default body leaves the target detached with its fault.
	static void Restore(Target target, Generation previous, Exception rebuildError)
	{
		Volatile.Write(ref target.Active, previous);
		try { Rebuild(target, previous); }
		catch (Exception recoveryError)
		{
			if (Volatile.Read(ref target.InputChanged)) return;
			target.Broken = true;
			target.Fault = recoveryError;
			throw new AggregateException("Reconstruction and recovery failed: " + target.Original, rebuildError, recoveryError);
		}
	}

	static MethodBase[] ValidationTargets(MethodBase definition)
	{
		// Every patched instantiation of the family must compile, plus explicit or default witnesses when the constraints allow them.
		// A definition without any witness installs anyway: the runtime compiles its known variants, and new ones compile on first call.
		// Harmony pins the member being installed before construction, so a first closed patch is its own witness.
		var patched = MethodBodyPatching.Instantiations(definition).ToArray();
		var witnesses = genericValidation?.Invoke(definition);
		if (witnesses is not null) witnesses = [.. witnesses, .. patched];
		if (witnesses is null)
		{
			var parameters = (definition.DeclaringType?.GetGenericArguments() ?? []).Concat(definition.GetGenericArguments()).ToArray();
			var defaults = new List<MethodBase>();
			foreach (var type in new[] { typeof(object), typeof(int) })
				try
				{
					var arguments = parameters.Select(parameter =>
						 (parameter.GenericParameterAttributes & GenericParameterAttributes.NotNullableValueTypeConstraint) != 0 ? typeof(int)
						 : (parameter.GenericParameterAttributes & GenericParameterAttributes.ReferenceTypeConstraint) != 0 ? typeof(object) : type).ToArray();
					defaults.Add(Close(definition, arguments));
				}
				catch (ArgumentException) { }
			witnesses = [.. defaults.Concat(patched).Distinct()];
		}
		if (witnesses.Any(method => method is null || method.ContainsGenericParameters || method.Module != definition.Module || method.MetadataToken != definition.MetadataToken))
			throw new NotSupportedException("Closed validation instantiations must belong to this generic definition: " + definition);
		return witnesses;
	}

	static void PrepareVariants(MethodBase original)
	{
		if (original.ContainsGenericParameters) prepareVariants?.Invoke(MethodBodyUpdater.GetDefinitionHandle(original));
	}

	internal static MethodBase Close(MethodBase definition, Type[] arguments)
	{
		var count = definition.DeclaringType?.GetGenericArguments().Length ?? 0;
		if (count != 0)
		{
			var owner = definition.DeclaringType.MakeGenericType(arguments.Take(count).ToArray());
			definition = MethodBase.GetMethodFromHandle(definition.MethodHandle, owner.TypeHandle);
		}
		return definition is MethodInfo { IsGenericMethodDefinition: true } method ? method.MakeGenericMethod(arguments.Skip(count).ToArray()) : definition;
	}

}
#endif
