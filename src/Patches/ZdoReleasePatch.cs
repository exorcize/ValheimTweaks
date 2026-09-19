using System.Collections.Generic;
using System.Reflection.Emit;
using HarmonyLib;

namespace ValheimTweaks.Patches
{
    /// <summary>
    /// ZDO ownership transfer -- periodic cost exclusive to the HOST.
    ///
    ///     private void ReleaseZDOS(float dt) {
    ///         m_releaseZDOTimer += dt;
    ///         if (!(m_releaseZDOTimer > 2f)) return;          // every 2 s
    ///         m_releaseZDOTimer = 0f;
    ///         ReleaseNearbyZDOS(ZNet.instance.GetReferencePosition(), m_sessionID);
    ///         foreach (ZDOPeer peer in m_peers)
    ///             ReleaseNearbyZDOS(peer.m_peer.m_refPos, peer.m_peer.m_uid);
    ///     }
    ///
    /// And ReleaseNearbyZDOS does, for EACH call:
    ///     FindSectorObjects(zone, simDistance, m_tempNearObjects)   // scans the
    ///                                                              // 121 zones
    /// and then iterates every collected ZDO calling GetPosition(),
    /// ZNetScene.InActiveArea() and IsInPeerActiveArea().
    ///
    /// With the user hosting for 4 friends: 5 complete scans of the near
    /// ring every 2 seconds, all in a single frame. Cost O(zones * (peers+1)),
    /// and "zones" is (2*near+1)^2 -- 121 at near=5 versus 25 in vanilla.
    ///
    /// Periodic and concentrated in one frame = exactly the signature of a hitch.
    ///
    /// ---- IMPORTANT: this has NOT been measured yet ----
    /// Today I already made a mistake once by optimizing before measuring (I limited
    /// instantiation per frame with a convincing mechanical explanation, and the effect was noise).
    /// So here we do NOT touch the ownership logic -- only the INTERVAL, which is
    /// reversible and does not change behavior, and by default stays at the game's value.
    /// The instrumentation (SystemProfiler.Sys.ZdoRelease) measures first.
    ///
    /// The transpiler replaces only the constant 2f -- it appears once in the method.
    /// Doubling the interval cuts the cost in half; the price is that object ownership
    /// takes longer to migrate between players.
    /// </summary>
    internal static class ZdoReleasePatch
    {
        internal static float GetInterval() => ModConfig.ZdoReleaseIntervalSec.Value;

        [HarmonyPatch(typeof(ZDOMan), "ReleaseZDOS")]
        internal static class ReleaseZDOSHook
        {
            private static void Prefix(out long __state) => __state = SystemProfiler.Begin();

            private static void Postfix(long __state) =>
                SystemProfiler.End(SystemProfiler.Sys.ZdoRelease, __state);

            private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
            {
                var getter = AccessTools.Method(typeof(ZdoReleasePatch), nameof(GetInterval));
                bool swapped = false;

                foreach (var ins in instructions)
                {
                    if (!swapped && ins.opcode == OpCodes.Ldc_R4 && ins.operand is float f && f == 2f)
                    {
                        yield return new CodeInstruction(OpCodes.Call, getter);
                        swapped = true;
                        continue;
                    }
                    yield return ins;
                }

                if (!swapped)
                {
                    Plugin.Log.LogError(
                        "ZdoReleasePatch: constant 2f not found in ReleaseZDOS. " +
                        "The interval is NOT configurable (the game changed).");
                }
            }
        }
    }
}
