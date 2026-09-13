using System.Reflection;
using HarmonyLib;
using UnityEngine;

namespace ValheimTweaks.Patches
{
    /// <summary>
    /// Velocidade do portal.
    ///
    /// Player.UpdateTeleport tem um cronômetro com três limiares fixos:
    ///
    ///     m_teleportTimer += dt;
    ///     if (!(m_teleportTimer > 2f)) return;              // espera inicial
    ///     ... move o jogador ...
    ///     if ((!(m_teleportTimer > 8f) &amp;&amp; m_distantTeleport)   // piso de 8 s
    ///         || !ZNetScene.instance.IsAreaReady(alvo)) return;
    ///     ...
    ///     else if (m_teleportTimer > 15f || !m_distantTeleport)  // desistência
    ///
    /// Multiplicar o dt que alimenta o cronômetro faz os três limiares chegarem
    /// proporcionalmente antes, sem tocar na lógica. O IsAreaReady continua
    /// segurando: some a espera artificial, não a real.
    ///
    /// ---- Instrumentação ----
    /// São duas esperas somadas e elas se parecem de dentro do jogo. O log separa:
    /// quanto tempo foi piso do cronômetro e quanto foi destino carregando.
    /// Sem isso não dá para saber se aumentar o multiplicador ainda ajuda ou se
    /// o limite passou a ser o carregamento.
    ///
    /// É local. O estado de teleporte é do próprio jogador e não trafega na rede.
    /// </summary>
    internal static class TeleportSpeedPatch
    {
        private static readonly FieldInfo TargetPosField =
            AccessTools.Field(typeof(Player), "m_teleportTargetPos");

        private static bool _teleportando;
        private static float _inicio;
        private static int _framesEsperandoArea;
        private static int _framesTotal;

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

                bool agora = __instance.IsTeleporting();

                if (agora && !_teleportando)
                {
                    _teleportando = true;
                    _inicio = Time.realtimeSinceStartup;
                    _framesEsperandoArea = 0;
                    _framesTotal = 0;
                }
                else if (agora)
                {
                    _framesTotal++;

                    // Conta os frames em que o destino ainda nao estava pronto.
                    // Se isso for quase todo o teleporte, o multiplicador nao ajuda
                    // mais -- o que falta e carregamento.
                    if (TargetPosField != null && ZNetScene.instance != null)
                    {
                        var alvo = (Vector3)TargetPosField.GetValue(__instance);
                        if (!ZNetScene.instance.IsAreaReady(alvo)) _framesEsperandoArea++;
                    }
                }
                else if (_teleportando)
                {
                    _teleportando = false;
                    float total = Time.realtimeSinceStartup - _inicio;
                    float pctArea = _framesTotal > 0
                        ? 100f * _framesEsperandoArea / _framesTotal
                        : 0f;

                    Plugin.Log.LogInfo(
                        $"[TELEPORTE] {total:0.00}s no total | " +
                        $"{pctArea:0}% do tempo esperando o destino carregar | " +
                        $"multiplicador {ModConfig.TeleportSpeed.Value:0.#}x");

                    if (pctArea > 60f)
                        Plugin.Log.LogInfo(
                            "[TELEPORTE] o gargalo e CARREGAMENTO do destino, nao o cronometro. " +
                            "Aumentar TeleportSpeed nao vai ajudar mais.");
                }
            }
        }
    }
}
