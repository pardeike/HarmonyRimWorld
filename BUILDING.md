# Building Harmony Mod

Run `./scripts/build-release.sh`. It downloads the latest official Harmony 3
NuGet version, including prereleases, then builds, runs smoke checks, packages
and installs the mod. No dependency version edits are needed for a new Harmony
release. The default package version is `3.*-*`; NuGet selects its `net10.0` asset.

Only `1.7/Assemblies` is rebuilt. The 1.4, 1.5 and 1.6 binaries are preserved and
hash-checked. RimWorld 1.6 uses official Harmony 2.4.3 for .NET Framework 4.7.2
with its existing released mod wrapper.

Set `RIMWORLD_ASSEMBLY_DIR` to the installed RimWorld assembly directory if it
is not at the default macOS Steam path. Set `RIMWORLD_MOD_DIR` to the game's
physical Mods directory. Quit RimWorld before deployment. The Release build
updates `HarmonyMod/` and sibling `HarmonyMod.zip` as part of building.

Use `--no-deploy` to omit installation. `./scripts/build-offline.sh` builds with
already downloaded packages. Builds run offline after the explicit public
package download; missing dependencies fail instead of being downloaded by the
project. No build command publishes or uploads the mod.

`Directory.Build.props` owns the normal numeric mod version. About metadata and
assembly metadata use that version without temporary timestamps. The loaded
Harmony library retains its official NuGet version. Both versions appear in
the startup log and version tooltip.

`Source/packages.lock.json` records the resolved package version and content
hash; it does not lock future builds to that version. `Harmony-provenance.json`
records the staged library's version, framework and hash. `Release-payload.json`
records the completed payload's hashes for release verification.

Release CI packages and verifies the tracked payload rather than rebuilding
against game binaries. Publishing requires manually dispatching the workflow
with the intended release tag. Full in-game compatibility is tested separately
from the smoke checks.
