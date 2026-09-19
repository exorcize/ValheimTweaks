using HarmonyLib;

namespace ValheimTweaks.Patches
{
    /// <summary>
    /// Timers on the game's hot systems.
    ///
    /// Harmony pattern: the Prefix returns the timestamp in __state, the Postfix closes it.
    /// __state is per-call, so it handles reentrancy and recursion without getting mixed up.
    ///
    /// The targets were chosen because they are the suspects for per-frame cost on the
    /// HOST side and for world streaming -- which is the user's scenario (he hosts
    /// for 4 peers with near=5, 121 zones).
    ///
    /// Everything only accumulates when SystemProfiler.Enabled, so outside of
    /// measuring the cost is a single bool read.
    /// </summary>
    internal static class InstrumentationPatches
    {
        [HarmonyPatch(typeof(ZDOMan), nameof(ZDOMan.Update))]
        internal static class ZDOManUpdate
        {
            private static void Prefix(out long __state) => __state = SystemProfiler.Begin();
            private static void Postfix(long __state) => SystemProfiler.End(SystemProfiler.Sys.ZDOMan, __state);
        }

        [HarmonyPatch(typeof(ZNetScene), "CreateDestroyObjects")]
        internal static class CreateDestroy
        {
            private static void Prefix(out long __state) => __state = SystemProfiler.Begin();
            private static void Postfix(long __state) => SystemProfiler.End(SystemProfiler.Sys.CreateDestroyObjects, __state);
        }

        [HarmonyPatch(typeof(ZNetScene), "Update")]
        internal static class ZNetSceneUpdate
        {
            private static void Prefix(out long __state) => __state = SystemProfiler.Begin();
            private static void Postfix(long __state) => SystemProfiler.End(SystemProfiler.Sys.ZNetSceneUpdate, __state);
        }

        [HarmonyPatch(typeof(ZoneSystem), "Update")]
        internal static class ZoneSystemUpdate
        {
            private static void Prefix(out long __state) => __state = SystemProfiler.Begin();
            private static void Postfix(long __state) => SystemProfiler.End(SystemProfiler.Sys.ZoneSystem, __state);
        }

        [HarmonyPatch(typeof(ClutterSystem), "LateUpdate")]
        internal static class ClutterLateUpdate
        {
            private static void Prefix(out long __state) => __state = SystemProfiler.Begin();
            private static void Postfix(long __state) => SystemProfiler.End(SystemProfiler.Sys.Clutter, __state);
        }

        // Zone generation: instantiates all the vegetation and destroys it right after,
        // just to register the ZDOs. Cost ONCE per zone (SetZoneGenerated), but huge.
        // Hypothesis to test: in a new world this dominates the walking hitch, and it
        // goes away on its own as the world is explored.
        [HarmonyPatch(typeof(ZoneSystem), "SpawnZone")]
        internal static class SpawnZoneHook
        {
            private static void Prefix(out long __state) => __state = SystemProfiler.Begin();

            private static void Postfix(long __state, bool __result)
            {
                SystemProfiler.End(SystemProfiler.Sys.SpawnZone, __state);
                if (__result) SystemProfiler.CountZoneSpawn();
            }
        }

        // Terrain mesh rebuild: rare but expensive, typical of an isolated spike.
        [HarmonyPatch(typeof(Heightmap), nameof(Heightmap.Regenerate))]
        internal static class HeightmapRegenerate
        {
            private static void Prefix(out long __state) => __state = SystemProfiler.Begin();
            private static void Postfix(long __state) => SystemProfiler.End(SystemProfiler.Sys.HeightmapRegen, __state);
        }
    }
}
