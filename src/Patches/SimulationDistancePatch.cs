using HarmonyLib;

namespace ValheimTweaks.Patches
{
    /// <summary>
    /// Simulation distance -- o maior custo de CPU do host.
    ///
    /// ZoneSystem carrega um quadrado de raio "near": (2n+1)^2 zonas.
    ///     near = 2 (vanilla) ->  25 zonas
    ///     near = 3           ->  49 zonas
    ///     near = 5           -> 121 zonas   <- quase 5x o vanilla
    /// E o host paga isso por peer conectado (ZDOMan.FindSectorObjects).
    ///
    /// Nao e de graca cortar: "near" tambem define o alcance VISUAL do terreno e
    /// da agua -- Heightmap.GetLodHideDistance() = near * sqrt(zoneSize^2 * 2),
    /// com ZoneSystem.m_zoneSize = 64 -> ~90,5 m por unidade.
    ///     near = 5 -> ~452 m      near = 3 -> ~272 m      near = 2 -> ~181 m
    ///
    /// ---- Como o "host manda" funciona (tudo isso e do proprio jogo) ----
    /// ZNet.GetSyncedSimulationDistance() devolve min(desejado_local, m_simulationDistance),
    /// onde m_simulationDistance no cliente vem do servidor. O fluxo:
    ///     cliente -> RPC_RequestValidSimulationDistance -> servidor clampa -> devolve
    /// Ou seja: o servidor e um TETO, e o cliente so pode ficar ABAIXO dele.
    ///
    /// Entao basta interceptar GetSimulationDistance() -- no host isso vira o teto de
    /// todo mundo, no cliente vira apenas o desejo dele. Nao precisa de ServerSync:
    /// a sincronizacao ja e do jogo. So precisamos re-disparar o handshake quando
    /// o valor muda em runtime.
    /// </summary>
    internal static class SimulationDistancePatch
    {
        [HarmonyPatch(typeof(GraphicsSettingsManager), nameof(GraphicsSettingsManager.GetSimulationDistance))]
        internal static class GetSimulationDistanceHook
        {
            private static void Postfix(ref SimulationDistance __result)
            {
                if (!ModConfig.SimDistanceEnabled.Value) return;

                int near = ModConfig.SimDistanceNear.Value;

                // Durante o teleporte, usa um valor reduzido: o destino so fica
                // pronto quando a zona central termina de carregar os assets, e com
                // 121 zonas pedindo asset ao mesmo tempo ela fica na fila.
                // Medido: 14,7s e 16,8s com near=5 contra 3,7s e 1,5s com near=2.
                if (TeleportSpeedPatch.EmTeleporte)
                {
                    int reduzido = ModConfig.TeleportSimDistance.Value;
                    if (reduzido > 0) near = System.Math.Min(near, reduzido);
                }

                if (near == __result.NearSimulationDistance) return;

                __result = new SimulationDistance(near, ModConfig.SimDistanceFar.Value);
            }
        }

        /// <summary>
        /// Re-negocia com os peers depois de mudar o valor em runtime.
        /// No servidor isso aplica localmente e empurra o novo teto para todos;
        /// no cliente, pede validacao ao servidor.
        /// </summary>
        internal static void Apply()
        {
            var znet = ZNet.instance;
            if (znet == null) return; // fora de mundo: aplica sozinho no proximo load

            znet.SimulationDistanceServerHandshake();

            var sim = znet.GetSyncedSimulationDistance();
            int zonas = (2 * sim.NearSimulationDistance + 1) * (2 * sim.NearSimulationDistance + 1);
            Plugin.Log.LogInfo(
                $"SimulationDistance: near={sim.NearSimulationDistance} far={sim.FarSimulationDistance} " +
                $"-> {zonas} zonas ({(znet.IsServer() ? "host: teto para todos" : "cliente: limitado pelo host")})");
        }
    }
}
