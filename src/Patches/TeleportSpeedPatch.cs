using HarmonyLib;

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
    /// O piso de 8 segundos vale para teleporte distante mesmo quando o destino
    /// já carregou. É daí que vem a demora: não é carregamento, é espera imposta.
    ///
    /// Em vez de trocar as três constantes por transpiler, multiplicamos o dt que
    /// alimenta o cronômetro. Todos os limiares chegam proporcionalmente antes e a
    /// lógica fica intacta -- inclusive o IsAreaReady, que continua segurando o
    /// teleporte se o destino realmente não estiver pronto. Ou seja: some a espera
    /// artificial, não a espera real.
    ///
    /// Com multiplicador 4: os 8 s viram 2 s, e a espera inicial 0,5 s.
    ///
    /// É local. O estado de teleporte é do próprio jogador e não trafega na rede,
    /// então cada um ajusta o seu sem afetar os outros.
    /// </summary>
    internal static class TeleportSpeedPatch
    {
        [HarmonyPatch(typeof(Player), "UpdateTeleport")]
        internal static class UpdateTeleportHook
        {
            private static void Prefix(ref float dt)
            {
                float mult = ModConfig.TeleportSpeed.Value;
                if (mult > 1f) dt *= mult;
            }
        }
    }
}
