using System.Reflection;
using HarmonyLib;
using UnityEngine;

namespace ValheimTweaks.Patches
{
    /// <summary>
    /// Network timeout.
    ///
    /// How the game does it (assembly_valheim, ZRpc):
    ///     private static float m_timeout;
    ///     static ZRpc() { m_timeout = 600f; }                    // generous default
    ///     public static void SetLongTimeout(bool enable) {
    ///         if (enable) m_timeout = 90f; else m_timeout = 30f;
    ///     }
    /// And ZNet.Start() calls SetLongTimeout(enable: false) -> locks at 30s on Steam.
    /// Consumed in ZRpc.UpdatePing: if m_timeSinceLastPing > m_timeout, m_socket.Close().
    ///
    /// Instead of editing the DLL, we Postfix SetLongTimeout: it catches every call site
    /// (ZNet.Start and the two in ZPlayFabSocket) and survives a game patch.
    /// </summary>
    internal static class TimeoutPatch
    {
        // m_timeout is private static -> accessed via cached reflection.
        private static readonly FieldInfo TimeoutField =
            AccessTools.Field(typeof(ZRpc), "m_timeout");

        [HarmonyPatch(typeof(ZRpc), nameof(ZRpc.SetLongTimeout))]
        internal static class SetLongTimeoutHook
        {
            private static void Postfix() => Apply();
        }

        internal static float Current =>
            TimeoutField != null ? (float)TimeoutField.GetValue(null) : -1f;

        internal static void Apply()
        {
            if (!ModConfig.TimeoutEnabled.Value) return;

            if (TimeoutField == null)
            {
                Plugin.Log.LogError("ZRpc.m_timeout not found - did the game change? Timeout not applied.");
                return;
            }

            float desired = ModConfig.TimeoutSeconds.Value;
            float current = (float)TimeoutField.GetValue(null);
            if (Mathf.Approximately(current, desired)) return;

            TimeoutField.SetValue(null, desired);
            Plugin.Log.LogInfo($"RPC timeout: {current}s -> {desired}s");
        }
    }
}
