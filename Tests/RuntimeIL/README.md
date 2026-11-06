# Runtime IL prototype

These tests check the runtime IL adapter against the installed RimWorld 1.7 runtime.
Harmony Mod enables the adapter from 3.0.2.0 through `EnableRuntimeILPatching`.
It uses the installed RimWorld 1.7 apphost and exits from a startup hook before game initialization.
It does not load a colony, enable the profiler or mutate the player's configuration.

The general Harmony extension is developed on `v3` in the Harmony repository.
The adapter sources are `Source/RuntimeILBody.cs` and `Source/RuntimeILPatching.cs`.
They compile only when `HARMONY_RUNTIME_IL_EXPERIMENT` is defined.
The separate test project supplies that constant and references the actual shipped runtime assemblies.
It also needs a locally built Harmony DLL containing `MethodBodyPatching`.

Run from this worktree:

```sh
HARMONY_ASSEMBLY_PATH=/absolute/path/to/0Harmony.dll \
HARMONY_RUNTIME_IL_ARTIFACTS=/absolute/path/to/ignored/test-output \
  python3 Tests/RuntimeIL/run.py
```

Set `RIMWORLD_ASSEMBLY_DIR` to override the installed game directory.
Set `HARMONY_DOTNET` to override the SDK executable.
Set `HARMONY_RUNTIME_IL_MODE=multi` for the reflection-only test using two isolated Harmony engines.
Set `HARMONY_RUNTIME_IL_MODE=protocol` to install a compatible four-slot shared record before loading Harmony.
Set `HARMONY_RUNTIME_IL_MODE=readbody` to check all 22 exception clauses of the installed game's `MapPostTick`.
That mode compares reflection clauses, serializes the baseline, patches and unpatches, and forces compilation after each update.
The runtime can return success with a smaller clause buffer. The adapter must also check the reported count.
`DOTNET_TieredCompilation=0` and `DOTNET_JitDisasm='RuntimeILTests:InlineCaller'` capture optimized inlining before and after replacement.

The command builds, runs the selected test and verifies its completion marker.
A comma-separated `HARMONY_RUNTIME_IL_MODE` builds once and runs each mode in its own process and data directory.
It saves full logs and prints `ok` only after all steps succeed.
No command here builds or stages the distributed wrapper, installs a mod, publishes a release or uploads anything.

The adapter installs the complete transformed body directly through a priority-0 runtime transformer. Ordinary calls have no delegate-slot forwarding.
It requires the 1.7.4998 runtime API. The wrapper discovers that capability before loading the optional adapter.
The shared Harmony encoder exports code, locals, signatures and exception sections through a host token importer.
The adapter prepares the installed target and restores the previous body after a target compilation error.
It leaves the separately returned MethodInfo uncompiled until someone invokes it.
The default probe checks that this snapshot still works after final unpatch.
An open generic definition has no such copy. `Patch()` returns the definition itself, and the installer receives null as the replacement.
It also collects an unused copy while its patch and an old frame remain active.
Invalid candidate IL enters the accepted install/compile/rollback interval before rejection.
Concurrent invocations during that interval can encounter the candidate body.

Instance constructors and type initializers are eligible like ordinary methods. The runtime replaces a type initializer's body without running it. The default probe checks that.
Field injection rewrites default bodies, then calls `Injection.HarmonyRegenerate`. The adapter sets that delegate and rebuilds its stage from the new input.
A detach caused by a changed input is reported only if nobody regenerates the stage within one second. Other stage failures are reported at once.

The adapter does not support re-entering a rebuild of the same target.
The engine keeps runtime updater calls serial and non-reentrant.
The `reentry` mode separately proves nested patching of different targets with serial runtime updates.

Set `HARMONY_RUNTIME_IL_MODE=failure` to check target compilation and rollback, including locals and exception regions.
Set `HARMONY_RUNTIME_IL_MODE=reentry` for the nesting check. The default mode covers behavior and hot-call timing.
The `multi` mode uses the actual direct body installer across two isolated Harmony engines.

The runtime now owns token memoization and replacement-body dependencies. The adapter keeps no duplicate token cache or permanent callback roots.
Standalone tests verify collectible signature dependencies, retirement after revert, explicit keepAlive across an old frame, and direct DynamicMethod tokens.
DynamicMethod tokens deliberately retain the method for the module's lifetime. That is separate from ordinary collectible dependency lifetime.
The native contract mode also measures token reuse for equal runtime strings and identical generic instantiations.
Both returned one distinct token over 20 requests on build 4998. No host cache is needed for those tested cases.
Nongeneric structured helpers use DynamicMethod directly. Ten helper rebuilds retained no extra assemblies in the focused probe.

The transformer captures raw stage input and replays frozen module-scoped body bytes. It never runs Harmony generation inside runtime callbacks.
The native profiler runs later at priority 1000. `coexist` checks downstream instrumentation and owner changes.
Changed upstream input detaches the Harmony stage and reports a host error outside the runtime lock.
Reads preserve that fault. The host can call `RuntimeILProvider.Rebuild(method)` outside runtime callbacks, with Harmony operations quiescent.
A failed reconstruction keeps the working generation while the default body is unchanged. Otherwise the target stays detached with its fault.
Generic constructed views normalize to the provider-owned definition for this explicit rebuild.
The adapter does not automatically rebase EnC or arbitrary upstream changes.
A final unpatch keeps a regenerated baseline, matching Harmony's existing behavior. An upstream change can therefore also detach that retained stage.
Direct ReplaceMethodBody calls that bypass a managed pipeline target are superseded by its next rebuild, per the runtime contract.
The old standalone active-IL collision guard is not a property of this pipeline integration.

The adapter supports fully open generic definitions and, through them, their closed instantiations. A prefix, postfix or finalizer on an instantiation runs under a type guard. An instantiation with a transpiler or an Infix patch gets a closed body of its own, built with the definition's patches and entered from the definition's body under the same guard. Partially open methods keep their existing mechanism.
Generic ordinary callbacks use declaring-type parameters followed by method parameters. Type guards select narrower behavior.
Run `HARMONY_RUNTIME_IL_MODE=generics` for the focused generic checks, including a closed member of a constrained definition as its own witness and per-instantiation `__state`.
The adapter compiles the patched instantiations of a definition and representative witnesses after each family install. Its default witnesses use object/int, respecting class/struct flags.
A requested closed instantiation is its own witness: the adapter reads the family through `MethodBodyPatching.Instantiations`.
Build 5000 adds `MethodBodyUpdater.PrepareGenericVariants`, which compiles every instantiation the runtime has already seen. The adapter calls it after each family install and rollback.
A constrained definition without any constructible witness therefore installs too. Its unseen instantiations compile on first call, so invalid transpiler output for such a case surfaces there instead of in `Patch()`.
The optional internal registration callback still supplies explicit closed validation methods.
Infix callbacks retain their existing nongeneric contract; generated helpers can carry generic context.
It refuses stale upstream input. Later pipeline stages remain free to instrument its output.
It also declines collectible targets, synchronized methods, varargs and bodies with function pointer locals. Custom modifiers and function pointer types in the signature are accepted.
Retain the default installer for those targets.

The mod registers its optional adapter from a wrapper module initializer.
That build uses normal framework references for the wrapper and runtime-specific references only for the adapter DLL.
Sam reports field injection is not yet wired into this engine pipeline.

Set `HARMONY_RUNTIME_IL_MODE=runtime-contracts` for the native lifetime and signature checks.
Set `HARMONY_RUNTIME_IL_MODE=coexist` for callback failure, read preservation, failed reconstruction, explicit rebase and instrumented-owner checks.
Set `HARMONY_RUNTIME_IL_MODE=live` to build a private baseline for the existing `waylith/development_tests` dispatcher.
Load that directory and run `VerifyProfilerCoexistence` inside the isolated game. This uses the actual native profiler and restores its settings.

Set `HARMONY_RUNTIME_IL_MODE=hot-generics` for warmed and late generic instantiations under default tiering.
It checks 60 million patched calls and final unpatch. JIT disassembly can verify the actual tier transitions.

Set `HARMONY_RUNTIME_IL_MODE=compat,compat-detour` for the compatibility probe. Both modes patch the same generic and free-patch shapes that mods use; `compat-detour` runs them without registering the provider, so the output shows what Harmony's ordinary detour does on the same build.
On 1.7.5000 the detour cannot patch an open generic definition at all and a closed reference-type instantiation silently has no effect, while value-type instantiations work. The provider installs the definition for every instantiation and a closed instantiation for exactly its own type arguments.
The `free-first` case writes a body directly, like PrepatcherCompat did on 4998: the next Harmony rebuild supersedes it. The `free-stage` case writes through a priority -2000 stage, like `FreePatchStage` on 5000: Harmony's stage input then includes the free patch, and unpatch keeps it.

The shipped wrapper skips the provider when the game runs with `-harmony-detours` or `HARMONY_DETOURS=1`. Every patch then installs through the MonoMod detour. Use it to tell a provider problem from a mod problem.
