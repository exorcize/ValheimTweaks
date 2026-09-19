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
        private static bool _loggedWorld;

        internal static void Tick()
        {
            if (!ModConfig.ChestAuditLog.Value) return;

            var player = Player.m_localPlayer;
            if (player == null)
            {
                _loggedWorld = false;
                _seen.Clear();
                return;
            }
            if (_loggedWorld) return;
            _loggedWorld = true;

            LogWorld(player);
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

            return $"'{name}' zdo={zid} owner={owner}";
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
        // ==================================================================
        [HarmonyPatch(typeof(Container), "RPC_RequestOpen")]
        internal static class RequestOpenHook
        {
            private static void Postfix(Container __instance, long uid)
                => Plugin.Log.LogInfo($"[AUDIT] open request from uid={uid} -> {Describe(__instance)}");
        }

        [HarmonyPatch(typeof(Container), "RPC_RequestStack")]
        internal static class RequestStackHook
        {
            private static void Postfix(Container __instance, long uid)
                => Plugin.Log.LogInfo($"[AUDIT] stack request from uid={uid} -> {Describe(__instance)}");
        }

        [HarmonyPatch(typeof(Container), "RPC_RequestTakeAll")]
        internal static class RequestTakeAllHook
        {
            private static void Postfix(Container __instance, long uid)
                => Plugin.Log.LogInfo($"[AUDIT] take-all request from uid={uid} -> {Describe(__instance)}");
        }

        [HarmonyPatch(typeof(Container), "RPC_OpenResponse")]
        internal static class OpenResponseHook
        {
            private static void Postfix(Container __instance, long uid, bool granted)
                => Plugin.Log.LogInfo($"[AUDIT] open response granted={granted} to uid={uid} -> {Describe(__instance)}");
        }

        [HarmonyPatch(typeof(Container), "RPC_StackResponse")]
        internal static class StackResponseHook
        {
            private static void Postfix(Container __instance, long uid, bool granted)
                => Plugin.Log.LogInfo($"[AUDIT] stack response granted={granted} to uid={uid} -> {Describe(__instance)}");
        }

        [HarmonyPatch(typeof(Container), "RPC_TakeAllResponse")]
        internal static class TakeAllResponseHook
        {
            private static void Postfix(Container __instance, long uid, bool granted)
                => Plugin.Log.LogInfo($"[AUDIT] take-all response granted={granted} to uid={uid} -> {Describe(__instance)}");
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
                    Plugin.Log.LogInfo($"[AUDIT] changed {Describe(__instance)}{Contents(__instance)} (was{before})");
            }
        }
    }
}
