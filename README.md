# Harmony Mod

Harmony provides runtime patching support for RimWorld mods. Enable it above
Core and mods that depend on Harmony.

RimWorld 1.7 uses Harmony 3 for .NET 10. RimWorld 1.6 uses official Harmony 2.4.3
for .NET Framework 4.7.2 with the unchanged released mod wrapper. The 1.4 and
1.5 assemblies are preserved.

Extract the `HarmonyMod` folder into the game's `Mods` directory. Enable only
one copy of the Harmony mod.

The Mods list and corner label show the mod's release version.
Hover the label to see the loaded Harmony library version as well. Include both
versions in bug reports.

Stacktrace enhancement preserves the original trace and adds Harmony patch
owners. Reference IDs identify matching traces; every occurrence retains its
full trace. The enhancement and reference-caching settings remain available.

In RimWorld 1.7, the default `harmony.log.txt` is stored beside `Player.log` in
the game's log directory. Set `HARMONY_LOG_FILE` before the first Harmony log
access to choose another location; `HARMONY_NO_LOG` and custom log writers
remain supported.

See `BUILDING.md` in the source project for build instructions.
