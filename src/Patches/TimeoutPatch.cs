using System.Reflection;
using HarmonyLib;
using UnityEngine;

namespace ValheimTweaks.Patches
{
    /// <summary>
    /// Timeout de rede.
    ///
    /// Como o jogo faz (assembly_valheim, ZRpc):
    ///     private static float m_timeout;
    ///     static ZRpc() { m_timeout = 600f; }                    // default generoso
    ///     public static void SetLongTimeout(bool enable) {
    ///         if (enable) m_timeout = 90f; else m_timeout = 30f;
    ///     }
    /// E ZNet.Start() chama SetLongTimeout(enable: false) -> trava em 30s no Steam.
    /// Consumido em ZRpc.UpdatePing: se m_timeSinceLastPing > m_timeout, m_socket.Close().
    ///
    /// Em vez de editar a DLL, damos Postfix em SetLongTimeout: pega todo call site
    /// (ZNet.Start e os dois do ZPlayFabSocket) e sobrevive a patch do jogo.
    /// </summary>
    internal static class TimeoutPatch
    {
        // m_timeout e private static -> acesso por reflection cacheada.
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
                Plugin.Log.LogError("ZRpc.m_timeout nao encontrado - o jogo mudou? Timeout nao aplicado.");
                return;
            }

            float desired = ModConfig.TimeoutSeconds.Value;
            float current = (float)TimeoutField.GetValue(null);
            if (Mathf.Approximately(current, desired)) return;

            TimeoutField.SetValue(null, desired);
            Plugin.Log.LogInfo($"Timeout de RPC: {current}s -> {desired}s");
        }
    }
}
