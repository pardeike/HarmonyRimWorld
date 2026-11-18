Updates Harmony for RimWorld 1.7 to 3.0.0-rc.7. RimWorld 1.6 and older are unchanged.

Fixes a problem on RimWorld 1.7 where a mod could get Harmony's assembly instead of its own. It happened when a mod's constructor called `Assembly.GetExecutingAssembly()` and another mod patched that constructor. The affected mod could then fail to register its own patches.

For mod authors: on RimWorld 1.7, `Harmony.GetMethodFromStackframe` and `Harmony.GetOriginalMethodFromStackframe` now return the constructor for a constructor frame. Before, they returned `null`.
