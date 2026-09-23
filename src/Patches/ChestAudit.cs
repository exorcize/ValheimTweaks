using System.Collections.Generic;
using System.Text;
using HarmonyLib;
using UnityEngine;

namespace ValheimTweaks.Patches
{
    /// <summary>
    /// Forensic log for nearby chests, so a "my item vanished" report always has context.
    ///
    /// It records three things:
    ///   - the world identity map (local uid/session/player id and every connected peer),
    ///     so the numeric ids in the rest of the log can be turned into names;
    ///   - every container network handshake (open / stack / take-all request and
    ///     response), with the sender id, the chest ZDO id and its current owner. The
    ///     other player's actions show up here as requests processed on the owner;
    ///   - every change to a container's contents (and owner), so it is possible to see
    ///     which chest gained or lost what, and when.
    ///
    /// Kept behind ChestAuditLog / ChestAuditContents because it can produce a lot of
    /// lines in a busy base.
    /// </summary>
    internal static class ChestAudit
    {
        private static readonly Dictionary<ZDOID, string> _seen = new Dictionary<ZDOID, string>();
        private static readonly Dictionary<string, int> _backpack = new Dictionary<string, int>();
        private static bool _loggedWorld;
        private static float _nextBackpackScan;

        internal static void Tick()
        {
            if (!ModConfig.ChestAuditLog.Value)
            {
                _backpack.Clear();
                return;
            }

            var player = Player.m_localPlayer;
            if (player == null)
            {
                _loggedWorld = false;
                _seen.Clear();
                _backpack.Clear();
                return;
            }

            if (!_loggedWorld)
            {
                _loggedWorld = true;
                LogWorld(player);
            }

            if (!ModConfig.ChestAuditContents.Value) return;
            if (Time.realtimeSinceStartup < _nextBackpackScan) return;
            _nextBackpackScan = Time.realtimeSinceStartup + 0.5f;
            DiffBackpack(player);
        }

        internal static string Stamp() => System.DateTime.Now.ToString("HH:mm:ss");

        /// <summary>
        /// Every half second, compares the backpack with the previous snapshot and logs what
        /// entered or left it. This is the missing half of the audit: without it, an item
        /// that leaves the backpack without showing up in a chest has no trace at all.
        /// </summary>
        private static void DiffBackpack(Player player)
        {
            var inv = player.GetInventory();
            if (inv == null) return;

            var now = new Dictionary<string, int>();
            foreach (var item in inv.GetAllItems())
            {
                if (item == null || item.m_shared == null) continue;

                string name = Localization.instance.Localize(item.m_shared.m_name);
                if (item.m_shared.m_maxQuality > 1) name += "#" + item.m_quality;

                now.TryGetValue(name, out int have);
                now[name] = have + item.m_stack;
            }

            var sb = new StringBuilder();
            foreach (var kv in now)
            {
                _backpack.TryGetValue(kv.Key, out int before);
                if (kv.Value > before) sb.Append($" +{kv.Value - before} {kv.Key}");
                else if (kv.Value < before) sb.Append($" -{before - kv.Value} {kv.Key}");
            }
            foreach (var kv in _backpack)
                if (!now.ContainsKey(kv.Key)) sb.Append($" -{kv.Value} {kv.Key}");

            _backpack.Clear();
            foreach (var kv in now) _backpack[kv.Key] = kv.Value;

            if (sb.Length > 0)
                Plugin.Log.LogInfo($"[AUDIT] {Stamp()} backpack:{sb}");
        }

        private static void LogWorld(Player player)
        {
            var sb = new StringBuilder();
            sb.Append("[AUDIT] world up: local uid=")
              .Append(ZNet.GetUID())
              .Append(" session=").Append(ZDOMan.GetSessionID())
              .Append(" player='").Append(player.GetPlayerName())
              .Append("' id=").Append(player.GetPlayerID());

            var peers = ZNet.instance?.GetConnectedPeers();
            if (peers != null)
            {
                foreach (var p in peers)
                {
                    if (p == null) continue;
                    sb.Append(" | peer uid=").Append(p.m_uid)
                      .Append(" name='").Append(p.m_playerName).Append('\'');
                }
            }
            Plugin.Log.LogInfo(sb.ToString());
        }

        // ==================================================================
        // Description helpers
        // ==================================================================
        private static string Describe(Container chest)
        {
            if (chest == null) return "(null)";

            var nview = chest.GetComponent<ZNetView>();
            var zdo = nview != null ? nview.GetZDO() : null;
            string zid = zdo != null ? zdo.m_uid.ToString() : "?";
            long owner = zdo != null ? zdo.GetOwner() : 0;

            string name = StoreHudPatch.ChestName(chest);
            if (string.IsNullOrEmpty(name)) name = Localization.instance.Localize(chest.m_name);

            // Position, so a chest in the log can be walked to instead of guessed by its
            // contents. The ZDO id identifies it but says nothing about where it is.
            Vector3 p = chest.transform.position;
            return $"'{name}' zdo={zid} owner={owner} pos=({p.x:0},{p.z:0})";
        }

        private static string Contents(Container chest)
        {
            if (!ModConfig.ChestAuditContents.Value || chest == null) return "";

            var inv = chest.GetInventory();
            if (inv == null) return " [inv=null]";

            var parts = new List<string>();
            foreach (var item in inv.GetAllItems())
            {
                if (item == null || item.m_shared == null) continue;
                parts.Add($"{Localization.instance.Localize(item.m_shared.m_name)}x{item.m_stack}");
            }
            parts.Sort();
            return " [" + string.Join(", ", parts) + "]";
        }

        // ==================================================================
        // Container network handshakes
        //
        // The first long argument of an RPC method is filled by the network with
        // whoever SENT the packet, not the destination. On a request that is the
        // player asking; on a response it is the owner granting. The line names the
        // local player too, so it is clear on which client it was written.
        // ==================================================================
        internal static string Who =>
            Player.m_localPlayer != null ? Player.m_localPlayer.GetPlayerName() : "?";

        [HarmonyPatch(typeof(Container), "RPC_RequestOpen")]
        internal static class RequestOpenHook
        {
            private static void Postfix(Container __instance, long uid)
                => Plugin.Log.LogInfo($"[AUDIT] {Stamp()} {Who}: open request from uid={uid} -> {Describe(__instance)}");
        }

        [HarmonyPatch(typeof(Container), "RPC_RequestStack")]
        internal static class RequestStackHook
        {
            private static void Postfix(Container __instance, long uid)
                => Plugin.Log.LogInfo($"[AUDIT] {Stamp()} {Who}: stack request from uid={uid} -> {Describe(__instance)}");
        }

        [HarmonyPatch(typeof(Container), "RPC_RequestTakeAll")]
        internal static class RequestTakeAllHook
        {
            private static void Postfix(Container __instance, long uid)
                => Plugin.Log.LogInfo($"[AUDIT] {Stamp()} {Who}: take-all request from uid={uid} -> {Describe(__instance)}");
        }

        [HarmonyPatch(typeof(Container), "RPC_OpenResponse")]
        internal static class OpenResponseHook
        {
            private static void Postfix(Container __instance, long uid, bool granted)
                => Plugin.Log.LogInfo($"[AUDIT] {Stamp()} {Who}: open response granted={granted} by owner uid={uid} -> {Describe(__instance)}");
        }

        [HarmonyPatch(typeof(Container), "RPC_StackResponse")]
        internal static class StackResponseHook
        {
            private static void Postfix(Container __instance, long uid, bool granted)
                => Plugin.Log.LogInfo($"[AUDIT] {Stamp()} {Who}: stack response granted={granted} by owner uid={uid} -> {Describe(__instance)}");
        }

        [HarmonyPatch(typeof(Container), "RPC_TakeAllResponse")]
        internal static class TakeAllResponseHook
        {
            private static void Postfix(Container __instance, long uid, bool granted)
                => Plugin.Log.LogInfo($"[AUDIT] {Stamp()} {Who}: take-all response granted={granted} by owner uid={uid} -> {Describe(__instance)}");
        }

        // ==================================================================
        // Content / owner changes
        // ==================================================================
        [HarmonyPatch(typeof(Container), "Load")]
        internal static class LoadHook
        {
            private static void Postfix(Container __instance)
            {
                if (!ModConfig.ChestAuditLog.Value) return;

                var nview = __instance.GetComponent<ZNetView>();
                var zdo = nview != null ? nview.GetZDO() : null;
                if (zdo == null) return;

                string now = $"owner={zdo.GetOwner()}{Contents(__instance)}";
                _seen.TryGetValue(zdo.m_uid, out string before);
                if (before == now) return;
                _seen[zdo.m_uid] = now;

                // Only reports changes, not the first sighting of each chest.
                if (before != null)
                    Plugin.Log.LogInfo($"[AUDIT] {Stamp()} changed {Describe(__instance)}{Contents(__instance)} (was{before})");
            }
        }
    }
}
