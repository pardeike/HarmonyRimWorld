# In-game release checks

The automated smoke suite verifies loading and patching, not complete game or
mod compatibility. Before publishing a release:

- Start RimWorld 1.7 with one Harmony copy above Core. Check the startup log for
  the mod version and the actual loaded library version, and for initialization
  or loader errors. The corner label and tooltip should show the same release
  versions, including prerelease labels, without source commit hashes. The log
  should retain the complete library build identifier.
- Enable Harmony debugging in a test mod. Check that `harmony.log.txt` appears
  beside `Player.log`, including startup output, and that no new desktop log is
  created. An explicit `HARMONY_LOG_FILE` must retain its chosen destination.
- Test the usual mod set: generate a map, load an existing save, play briefly,
  save and reload. Check for new Harmony or patching errors.
- Check repeated errors: every occurrence must retain its exception frames and
  available Harmony patch-owner annotations. A reference ID must not replace
  the complete exception trace. The separate logging caller trace may also appear.
- Check enhancement and caching settings, including saved settings from the
  previous release. Disabling caching should omit reference IDs; disabling
  enhancement should preserve the runtime's native formatting.
- On RimWorld 1.6, check that load folders select the unchanged released wrapper
  with Harmony 2.4.3, and that no .NET 10 assembly is loaded from 1.7.
- Compare the tested mod payload with the release manifest and publication
  staging before uploading. Rebuild and repeat affected checks if it changes.
