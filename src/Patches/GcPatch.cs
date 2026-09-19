using UnityEngine.Scripting;

namespace ValheimTweaks.Patches
{
    /// <summary>
    /// Garbage collector -- the other classic source of hitching in Unity/Mono games.
    ///
    /// When the GC is not incremental, it stops the whole world to collect: a
    /// normal 4 ms frame becomes one of 30-80 ms. This is felt as a stutter, and it
    /// disappears entirely in the average fps.
    ///
    /// Valheim's boot.config ships with "gc-max-time-slice=3", which indicates
    /// incremental GC with a 3 ms slice. But 3 ms is still almost an entire frame
    /// at 240 fps -- it is worth testing smaller slices, which spread the collection
    /// over more frames instead of concentrating it.
    ///
    /// incrementalTimeSliceNanoseconds is settable at runtime, so this goes into
    /// the A/B along with the rest, without restarting.
    /// </summary>
    internal static class GcPatch
    {
        internal static void Apply()
        {
            float ms = ModConfig.GcSliceMs.Value;
            if (ms <= 0f) return; // 0 = do not touch

            if (!GarbageCollector.isIncremental)
            {
                Plugin.Log.LogWarning(
                    "Incremental GC is OFF in this build -- the slice has no effect. " +
                    "Collections will stop the whole world.");
                return;
            }

            ulong ns = (ulong)(ms * 1_000_000f);
            if (GarbageCollector.incrementalTimeSliceNanoseconds == ns) return;

            GarbageCollector.incrementalTimeSliceNanoseconds = ns;
            Plugin.Log.LogInfo($"GC: incremental slice -> {ms:0.##} ms");
        }

        internal static string Describe()
        {
            return GarbageCollector.isIncremental
                ? $"incremental, slice = {GarbageCollector.incrementalTimeSliceNanoseconds / 1_000_000f:0.##} ms, mode = {GarbageCollector.GCMode}"
                : $"NOT incremental (stops the world when collecting), mode = {GarbageCollector.GCMode}";
        }
    }
}
