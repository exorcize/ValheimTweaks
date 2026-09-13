using System.Collections.Generic;
using System.Reflection.Emit;
using HarmonyLib;

namespace ValheimTweaks.Patches
{
    /// <summary>
    /// Transferencia de posse de ZDO -- custo periodico exclusivo do HOST.
    ///
    ///     private void ReleaseZDOS(float dt) {
    ///         m_releaseZDOTimer += dt;
    ///         if (!(m_releaseZDOTimer > 2f)) return;          // a cada 2 s
    ///         m_releaseZDOTimer = 0f;
    ///         ReleaseNearbyZDOS(ZNet.instance.GetReferencePosition(), m_sessionID);
    ///         foreach (ZDOPeer peer in m_peers)
    ///             ReleaseNearbyZDOS(peer.m_peer.m_refPos, peer.m_peer.m_uid);
    ///     }
    ///
    /// E ReleaseNearbyZDOS faz, para CADA chamada:
    ///     FindSectorObjects(zone, simDistance, m_tempNearObjects)   // varre as
    ///                                                              // 121 zonas
    /// e depois itera todo ZDO coletado chamando GetPosition(),
    /// ZNetScene.InActiveArea() e IsInPeerActiveArea().
    ///
    /// Com o usuario hospedando para 4 amigos: 5 varreduras completas do anel
    /// near a cada 2 segundos, tudo num unico frame. Custo O(zonas * (peers+1)),
    /// e "zonas" e (2*near+1)^2 -- 121 em near=5 contra 25 no vanilla.
    ///
    /// Periodico e concentrado num frame = exatamente a assinatura de engasgo.
    ///
    /// ---- IMPORTANTE: isto ainda NAO foi medido ----
    /// Hoje eu ja errei uma vez otimizando antes de medir (limitei instanciacao
    /// por frame com uma explicacao mecanica convincente, e o efeito era ruido).
    /// Entao aqui NAO se mexe na logica de posse -- so no INTERVALO, que e
    /// reversivel e nao muda comportamento, e por padrao fica no valor do jogo.
    /// A instrumentacao (SystemProfiler.Sys.ZdoRelease) mede primeiro.
    ///
    /// Transpiler troca so a constante 2f -- ela aparece uma unica vez no metodo.
    /// Dobrar o intervalo corta o custo pela metade; o preco e posse de objeto
    /// demorar mais para migrar entre jogadores.
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
                bool trocado = false;

                foreach (var ins in instructions)
                {
                    if (!trocado && ins.opcode == OpCodes.Ldc_R4 && ins.operand is float f && f == 2f)
                    {
                        yield return new CodeInstruction(OpCodes.Call, getter);
                        trocado = true;
                        continue;
                    }
                    yield return ins;
                }

                if (!trocado)
                {
                    Plugin.Log.LogError(
                        "ZdoReleasePatch: constante 2f nao encontrada em ReleaseZDOS. " +
                        "O intervalo NAO esta configuravel (o jogo mudou).");
                }
            }
        }
    }
}
