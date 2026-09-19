using HarmonyLib;

namespace ValheimTweaks.Patches
{
    /// <summary>
    /// Simulation distance -- the host's biggest CPU cost.
    ///
    /// ZoneSystem loads a square of radius "near": (2n+1)^2 zones.
    ///     near = 2 (vanilla) ->  25 zones
    ///     near = 3           ->  49 zones
    ///     near = 5           -> 121 zones   <- almost 5x vanilla
    /// And the host pays that per connected peer (ZDOMan.FindSectorObjects).
    ///
    /// Cutting it is not free: "near" also defines the VISUAL range of the terrain and
    /// water -- Heightmap.GetLodHideDistance() = near * sqrt(zoneSize^2 * 2),
    /// with ZoneSystem.m_zoneSize = 64 -> ~90.5 m per unit.
    ///     near = 5 -> ~452 m      near = 3 -> ~272 m      near = 2 -> ~181 m
    ///
    /// ---- How the "host is in charge" works (all of this is from the game itself) ----
    /// ZNet.GetSyncedSimulationDistance() returns min(local_desired, m_simulationDistance),
    /// where m_simulationDistance on the client comes from the server. The flow:
    ///     client -> RPC_RequestValidSimulationDistance -> server clamps -> returns
    /// In other words: the server is a CEILING, and the client can only stay BELOW it.
    ///
    /// So it is enough to intercept GetSimulationDistance() -- on the host that becomes the
    /// ceiling for everyone, on the client it becomes only its own wish. No ServerSync needed:
    /// the synchronization is already the game's. We only need to re-trigger the handshake when
    /// the value changes at runtime.
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

                // During teleport, use a reduced value: the destination only becomes
                // ready when the central zone finishes loading assets, and with
                // 121 zones requesting assets at the same time it gets stuck in the queue.
                // Measured: 14.7s and 16.8s with near=5 versus 3.7s and 1.5s with near=2.
                if (TeleportSpeedPatch.IsTeleporting)
                {
                    int reduced = ModConfig.TeleportSimDistance.Value;
                    if (reduced > 0) near = System.Math.Min(near, reduced);
                }

                if (near == __result.NearSimulationDistance) return;

                __result = new SimulationDistance(near, ModConfig.SimDistanceFar.Value);
            }
        }

        /// <summary>
        /// Re-negotiates with the peers after changing the value at runtime.
        /// On the server this applies locally and pushes the new ceiling to everyone;
        /// on the client, it requests validation from the server.
        /// </summary>
        internal static void Apply()
        {
            var znet = ZNet.instance;
            if (znet == null) return; // outside the world: applies on its own on the next load

            znet.SimulationDistanceServerHandshake();

            var sim = znet.GetSyncedSimulationDistance();
            int zones = (2 * sim.NearSimulationDistance + 1) * (2 * sim.NearSimulationDistance + 1);
            Plugin.Log.LogInfo(
                $"SimulationDistance: near={sim.NearSimulationDistance} far={sim.FarSimulationDistance} " +
                $"-> {zones} zones ({(znet.IsServer() ? "host: ceiling for everyone" : "client: limited by host")})");
        }
    }
}
