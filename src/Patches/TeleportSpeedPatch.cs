using System.Reflection;
using HarmonyLib;
using UnityEngine;

namespace ValheimTweaks.Patches
{
    /// <summary>
    /// Portal speed.
    ///
    /// Player.UpdateTeleport has a timer with three fixed thresholds:
    ///
    ///     m_teleportTimer += dt;
    ///     if (!(m_teleportTimer > 2f)) return;              // initial wait
    ///     ... moves the player ...
    ///     if ((!(m_teleportTimer > 8f) &amp;&amp; m_distantTeleport)   // 8 s floor
    ///         || !ZNetScene.instance.IsAreaReady(alvo)) return;
    ///     ...
    ///     else if (m_teleportTimer > 15f || !m_distantTeleport)  // giving up
    ///
    /// Multiplying the dt that feeds the timer makes the three thresholds arrive
    /// proportionally earlier, without touching the logic. IsAreaReady keeps
    /// holding: the artificial wait goes away, not the real one.
    ///
    /// ---- Instrumentation ----
    /// There are two waits added together and they look alike from inside the
    /// game. The log separates them: how much time was timer floor and how much
    /// was the destination loading. Without this you can't tell whether increasing
    /// the multiplier still helps or whether the bottleneck became loading.
    ///
    /// It is local. The teleport state belongs to the player and does not travel
    /// over the network.
    /// </summary>
    internal static class TeleportSpeedPatch
    {
        private static readonly FieldInfo TargetPosField =
            AccessTools.Field(typeof(Player), "m_teleportTargetPos");

        private static bool _teleporting;
        private static float _startedAt;
        private static int _framesWaitingArea;
        private static int _framesTotal;

        /// <summary>
        /// Read by SimulationDistancePatch to reduce the distance only while the
        /// teleport lasts.
        /// </summary>
        internal static bool IsTeleporting => _teleporting;

        /// <summary>
        /// Makes ZoneSystem re-read the distance. ApplySettings() just copies the
        /// value from ZNet.GetSyncedSimulationDistance(), which in turn reads the
        /// "desired" value our patch just changed.
        ///
        /// On purpose does NOT call ZNet.ApplySimulationDistance nor the handshake:
        /// those touch the CAP the host sends to peers, and lowering that on every
        /// portal would drop our friends' simulation distance too. Here only the
        /// local side changes.
        /// </summary>
        private static void ReapplyDistance(bool restoring)
        {
            if (ZoneSystem.instance != null) ZoneSystem.instance.ApplySettings();

            // The visual range of water and terrain also derives from the distance.
            // During the teleport the screen is black, so it's only worth fixing on
            // the way back.
            if (restoring)
            {
                Water.ApplySettingsOnAll();
                Heightmap.ApplySettingsOnAll();
            }
        }

        [HarmonyPatch(typeof(Player), "UpdateTeleport")]
        internal static class UpdateTeleportHook
        {
            private static void Prefix(ref float dt)
            {
                float mult = ModConfig.TeleportSpeed.Value;
                if (mult > 1f) dt *= mult;
            }

            private static void Postfix(Player __instance)
            {
                if (__instance != Player.m_localPlayer) return;

                bool now = __instance.IsTeleporting();

                if (now && !_teleporting)
                {
                    _teleporting = true;
                    _startedAt = Time.realtimeSinceStartup;
                    _framesWaitingArea = 0;
                    _framesTotal = 0;
                    ReapplyDistance(restoring: false);
                }
                else if (now)
                {
                    _framesTotal++;

                    // Counts the frames in which the destination wasn't ready yet.
                    // If that's almost the whole teleport, the multiplier no longer
                    // helps -- what's missing is loading.
                    if (TargetPosField != null && ZNetScene.instance != null)
                    {
                        var target = (Vector3)TargetPosField.GetValue(__instance);
                        if (!ZNetScene.instance.IsAreaReady(target)) _framesWaitingArea++;
                    }
                }
                else if (_teleporting)
                {
                    _teleporting = false;
                    ReapplyDistance(restoring: true);

                    float total = Time.realtimeSinceStartup - _startedAt;
                    float pctArea = _framesTotal > 0
                        ? 100f * _framesWaitingArea / _framesTotal
                        : 0f;

                    Plugin.Log.LogInfo(
                        $"[TELEPORT] {total:0.00}s total | " +
                        $"{pctArea:0}% of the time waiting for the destination to load | " +
                        $"multiplier {ModConfig.TeleportSpeed.Value:0.#}x");

                    if (pctArea > 60f)
                        Plugin.Log.LogInfo(
                            "[TELEPORT] the bottleneck is LOADING the destination, not the timer. " +
                            "Increasing TeleportSpeed won't help anymore.");
                }
            }
        }
    }
}
