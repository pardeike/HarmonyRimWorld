using System;
using System.Reflection;
using System.Runtime.CompilerServices;
using WaylithClr;

internal static class PipelineTests
{
    [MethodImpl(MethodImplOptions.NoInlining)] public static int Target() => 3;
    static void Check(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }

    sealed class Stage(int priority, bool add) : MethodBodyTransformer(priority)
    {
        internal int Value, Calls;
        internal bool Throw, Invalid;
        protected override bool TryTransform(RuntimeMethodHandle method, in MethodBodyView body,
            ReadOnlySpan<MethodBodyClause> clauses, MethodBodyBuilder result)
        {
            Calls++;
            if (Throw) throw new InvalidOperationException("stage failure");
            if (Invalid) result.Write([0x0e, 0x26, 0x16, 0x2a]); // pop on an empty stack.
            else
            {
                var value = add ? body.IL[1] + Value : Value;
                result.Write([0x0e, 0x1f, checked((byte)value), 0x2a]);
            }
            return true;
        }
    }

    internal static void Run()
    {
        var target = typeof(PipelineTests).GetMethod(nameof(Target));
        var handle = MethodBodyUpdater.GetDefinitionHandle(target);
        var upstream = new Stage(0, false) { Value = 12 };
        var downstream = new Stage(100, true) { Value = 100 };
        upstream.Register(); downstream.Register();
        try
        {
            Check(upstream.Rebuild(handle) is null && downstream.Rebuild(handle) is null && Target() == 112, "Stage order");
            var calls = downstream.Calls;
            upstream.Value = 13;
            Check(upstream.Rebuild(handle) is null && Target() == 113 && downstream.Calls > calls, "Upstream invalidates downstream");
            downstream.Invalid = true;
            var failure = downstream.Rebuild(handle);
            Exception compilation = null;
            try { MethodBodyUpdater.PrepareMethodBody(target); } catch (Exception error) { compilation = error; }
            Console.WriteLine($"invalid rebuild={failure?.GetType().Name ?? "none"}; prepare={compilation?.GetType().Name ?? "none"}");
            if (compilation is null)
                try { Console.WriteLine("invalid call=" + Target()); } catch (Exception error) { Console.WriteLine("invalid call=" + error.GetType().Name); }
            Check(failure is not null || compilation is InvalidProgramException, "Invalid IL is rejected by a verification boundary");
            downstream.Invalid = false;
            Check(downstream.Rebuild(handle) is null && Target() == 113, "Explicit recovery rebuild");
            downstream.Throw = true;
            Check(downstream.Rebuild(handle) is InvalidOperationException && Target() == 13, "Failed stage is removed");
            downstream.Throw = false;
            Check(downstream.Rebuild(handle) is null && Target() == 113, "A removed stage can rejoin");
            downstream.Unregister();
            Check(Target() == 13, "Unregister preserves upstream");
        }
        finally { if (downstream.IsRegistered) downstream.Unregister(); upstream.Unregister(); }
        Check(Target() == 3, "Final unregister restores default");
    }
}
