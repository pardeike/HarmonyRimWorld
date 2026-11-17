Updates Harmony for RimWorld 1.7 to 3.0.0-rc.6. RimWorld 1.6 and older are unchanged.

This is the first Steam Workshop update since 3.0.1.0, so it also brings the changes from 3.0.2.0. On RimWorld 1.7, Harmony installs patches through the game's own method-body replacement, and patches on generic methods take effect. Harmony's JIT hook no longer has the code path behind a rare crash while RimWorld 1.7 starts. A transpiler that jumps to a label it never places is rejected, and the error names the transpiler.

For mod authors: a patch that takes `__args` now gets the current argument values. Before, a patch that only read `__args` could undo a `ref` or `out` change from an earlier patch or from the original method.

For mod authors: a `ref object` patch parameter for a value-type argument now writes its value back to the argument. The boxed value must have the argument's exact type. For a `long` argument, `value = 17` throws `InvalidCastException`. Write `value = 17L`.

For mod authors: `MethodType` has new members for the unsigned right shift operator, the checked operators and the compound assignment operators.
