#if HARMONY_RUNTIME_IL_EXPERIMENT
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using WaylithClr;

namespace HarmonyMod;

// Translate the runtime's borrowed view into the existing BCL body contract. No Harmony IL parser is duplicated.
internal sealed class RuntimeILBody : MethodBody
{
	readonly byte[] code;
	readonly byte[] signature;
	readonly MethodBase context;
	LocalVariableInfo[] locals;
	ExceptionHandlingClause[] clauses;
	readonly MethodBodyClause[] nativeClauses;
	readonly int maxStack, localToken;
	readonly bool initLocals;

	internal static RuntimeILBody Read(MethodBase method)
	{
		var storage = new MethodBodyClause[16];
		MethodBodyView body;
		while (!MethodBodyUpdater.TryGetDefaultMethodBody(method, storage, out body) || body.ClauseCount > storage.Length)
		{
			if (body.ClauseCount <= storage.Length) throw new NotSupportedException("Baseline body unavailable: " + method);
			storage = new MethodBodyClause[body.ClauseCount];
		}
		return new(method, body, storage);
	}
	internal static byte[] ActiveIL(MethodBase method)
	{
		var storage = new MethodBodyClause[16];
		MethodBodyView body;
		// Success reports body availability. ClauseCount can still exceed the caller's buffer.
		while (!MethodBodyUpdater.TryGetCurrentMethodBody(method, storage, out body) || body.ClauseCount > storage.Length)
		{
			if (body.ClauseCount <= storage.Length) throw new NotSupportedException("Active body unavailable: " + method);
			storage = new MethodBodyClause[body.ClauseCount];
		}
		return body.IL.ToArray();
	}

	internal RuntimeILBody(MethodBase method, in MethodBodyView body, ReadOnlySpan<MethodBodyClause> storage)
	{
		context = method;
		code = body.IL.ToArray();
		maxStack = body.MaxStackSize;
		localToken = body.LocalSignatureToken;
		initLocals = body.InitLocals;
		signature = body.LocalSignature.ToArray();
		nativeClauses = storage[..body.ClauseCount].ToArray();
	}

	RuntimeILBody(MethodBase method, RuntimeILBody source)
	{
		context = method;
		code = source.code;
		signature = source.signature;
		maxStack = source.maxStack;
		localToken = source.localToken;
		initLocals = source.initLocals;
		nativeClauses = source.nativeClauses;
	}

	internal RuntimeILBody WithContext(MethodBase method) => new(method, this);

	internal bool Matches(in MethodBodyView body, ReadOnlySpan<MethodBodyClause> storage)
	{
		if (!body.IL.SequenceEqual(code) || !body.LocalSignature.SequenceEqual(signature)
			 || body.MaxStackSize != maxStack || body.InitLocals != initLocals || body.ClauseCount != nativeClauses.Length) return false;
		for (var index = 0; index < nativeClauses.Length; index++)
			if (!nativeClauses[index].Equals(storage[index])) return false;
		return true;
	}

	static unsafe LocalVariableInfo[] DecodeLocals(MethodBase method, byte[] signature)
	{
		if (signature.Length == 0 || signature.Length == 2 && signature[0] == 7 && signature[1] == 0) return [];
		fixed (byte* p = signature)
		{
			var reader = new BlobReader(p, signature.Length);
			var decoder = new SignatureDecoder<Local, object>(new LocalTypes(method), null, null);
			return decoder.DecodeLocalSignature(ref reader).Select((type, index) => (LocalVariableInfo)new Variable(type, index)).ToArray();
		}
	}

	internal byte[] Serialize()
	{
		var bytes = new BlobBuilder();
		var body = new MethodBodyStreamEncoder(bytes).AddMethodBody(code.Length, maxStack, nativeClauses.Length, false,
			 MetadataTokens.StandaloneSignatureHandle(localToken & 0xffffff), initLocals ? MethodBodyAttributes.InitLocals : MethodBodyAttributes.None);
		new BlobWriter(body.Instructions).WriteBytes(code);
		var regions = body.ExceptionRegions;
		foreach (var clause in nativeClauses)
			regions = regions.Add((ExceptionRegionKind)clause.Flags, clause.TryOffset, clause.TryLength, clause.HandlerOffset, clause.HandlerLength,
				 clause.Flags == 0 ? MetadataTokens.EntityHandle(clause.ClassTokenOrFilterOffset) : default,
				 clause.Flags == ExceptionHandlingClauseOptions.Filter ? clause.ClassTokenOrFilterOffset : 0);
		return bytes.ToArray();
	}

	public override byte[] GetILAsByteArray() => (byte[])code.Clone();
	public override IList<LocalVariableInfo> LocalVariables => Array.AsReadOnly(locals ??= DecodeLocals(context, signature));
	public override IList<ExceptionHandlingClause> ExceptionHandlingClauses => Array.AsReadOnly(clauses ??=
		 nativeClauses.Select(clause => (ExceptionHandlingClause)new Clause(context, clause)).ToArray());
	public override int MaxStackSize => maxStack;
	public override int LocalSignatureMetadataToken => localToken;
	public override bool InitLocals => initLocals;

	readonly record struct Local(Type Type, bool Pinned = false);
	sealed class Variable(Local local, int index) : LocalVariableInfo
	{
		public override Type LocalType => local.Type;
		public override bool IsPinned => local.Pinned;
		public override int LocalIndex => index;
	}

	sealed class Clause(MethodBase method, MethodBodyClause clause) : ExceptionHandlingClause
	{
		public override ExceptionHandlingClauseOptions Flags => (ExceptionHandlingClauseOptions)clause.Flags;
		public override int TryOffset => clause.TryOffset;
		public override int TryLength => clause.TryLength;
		public override int HandlerOffset => clause.HandlerOffset;
		public override int HandlerLength => clause.HandlerLength;
		public override int FilterOffset => Flags == ExceptionHandlingClauseOptions.Filter ? clause.ClassTokenOrFilterOffset : throw new InvalidOperationException();
		public override Type CatchType => Flags == ExceptionHandlingClauseOptions.Clause ? method.Module.ResolveType(clause.ClassTokenOrFilterOffset,
			 method.DeclaringType?.GetGenericArguments(), method.IsGenericMethod ? method.GetGenericArguments() : null) : null;
	}

	sealed class LocalTypes(MethodBase method) : ISignatureTypeProvider<Local, object>
	{
		public Local GetArrayType(Local element, ArrayShape shape) => new(element.Type.MakeArrayType(shape.Rank));
		public Local GetByReferenceType(Local element) => new(element.Type.MakeByRefType());
		public Local GetPointerType(Local element) => new(element.Type.MakePointerType());
		public Local GetSZArrayType(Local element) => new(element.Type.MakeArrayType());
		public Local GetPinnedType(Local element) => element with { Pinned = true };
		public Local GetGenericInstantiation(Local type, ImmutableArray<Local> arguments) => new(type.Type.MakeGenericType(arguments.Select(item => item.Type).ToArray()));
		public Local GetGenericMethodParameter(object context, int index) => new(method.GetGenericArguments()[index]);
		public Local GetGenericTypeParameter(object context, int index) => new(method.DeclaringType.GetGenericArguments()[index]);
		public Local GetModifiedType(Local modifier, Local type, bool required) => throw new NotSupportedException("Modified locals are outside this prototype");
		public Local GetFunctionPointerType(MethodSignature<Local> signature) => throw new NotSupportedException("Function pointer locals are outside this prototype");
		public Local GetTypeFromDefinition(MetadataReader reader, TypeDefinitionHandle handle, byte kind) => Resolve(MetadataTokens.GetToken(handle));
		public Local GetTypeFromReference(MetadataReader reader, TypeReferenceHandle handle, byte kind) => Resolve(MetadataTokens.GetToken(handle));
		public Local GetTypeFromSpecification(MetadataReader reader, object context, TypeSpecificationHandle handle, byte kind) => Resolve(MetadataTokens.GetToken(handle));
		Local Resolve(int token) => new(method.Module.ResolveType(token, method.DeclaringType?.GetGenericArguments(),
			 method.IsGenericMethod ? method.GetGenericArguments() : null));
		public Local GetPrimitiveType(PrimitiveTypeCode code) => new(code switch
		{
			PrimitiveTypeCode.Void => typeof(void),
			PrimitiveTypeCode.Boolean => typeof(bool),
			PrimitiveTypeCode.Char => typeof(char),
			PrimitiveTypeCode.SByte => typeof(sbyte),
			PrimitiveTypeCode.Byte => typeof(byte),
			PrimitiveTypeCode.Int16 => typeof(short),
			PrimitiveTypeCode.UInt16 => typeof(ushort),
			PrimitiveTypeCode.Int32 => typeof(int),
			PrimitiveTypeCode.UInt32 => typeof(uint),
			PrimitiveTypeCode.Int64 => typeof(long),
			PrimitiveTypeCode.UInt64 => typeof(ulong),
			PrimitiveTypeCode.Single => typeof(float),
			PrimitiveTypeCode.Double => typeof(double),
			PrimitiveTypeCode.String => typeof(string),
			PrimitiveTypeCode.Object => typeof(object),
			PrimitiveTypeCode.IntPtr => typeof(IntPtr),
			PrimitiveTypeCode.UIntPtr => typeof(UIntPtr),
			PrimitiveTypeCode.TypedReference => typeof(TypedReference),
			_ => throw new NotSupportedException("Local type " + code)
		});
	}
}
#endif
