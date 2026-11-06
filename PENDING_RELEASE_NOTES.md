Updates Harmony for RimWorld 1.7 to 3.0.0-rc.5.

On RimWorld 1.7, Harmony installs patches through the game's own method-body replacement. Patches on generic methods now take effect. The native detour remains the fallback for methods the game cannot replace.

A transpiler that jumps to a label it never places is now rejected, and the error names the transpiler. Before, such a patch could leave a method that never returns. Mod authors need to correct these patches.

Harmony no longer rejects a transpiler whose code paths reach an instruction with different stack depths. RimWorld 1.7 checks this itself and accepts the cases that worked on 1.6.
