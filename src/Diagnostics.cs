using System.Text;
using UnityEngine;

namespace ValheimTweaks
{
    /// <summary>
    /// Dumps the REAL state of the render pipeline to the log.
    ///
    /// It exists to stop speculating. In particular it answers the question that
    /// decides whether MSAA is viable: what is the game camera's actualRenderingPath?
    /// MSAA only works in Forward -- in Deferred Unity ignores it.
    /// </summary>
    internal static class Diagnostics
    {
        internal static void Dump(string reason)
        {
            var sb = new StringBuilder();
            sb.AppendLine();
            sb.AppendLine("=============== ValheimTweaks :: diagnostics (" + reason + ") ===============");

            // ---- Cameras: the answer about MSAA is here ----
            sb.AppendLine("[CAMERAS]");
            var cams = Camera.allCameras;
            if (cams == null || cams.Length == 0)
            {
                sb.AppendLine("  (no active camera)");
            }
            else
            {
                foreach (var c in cams)
                {
                    if (c == null) continue;
                    sb.AppendLine($"  {c.name,-22} path={c.actualRenderingPath,-16} " +
                                  $"(requested={c.renderingPath}) MSAA_ok={c.allowMSAA} HDR={c.allowHDR} " +
                                  $"fov={c.fieldOfView:0.#} far={c.farClipPlane:0} depth={c.depth}");
                }
                sb.AppendLine("  -> MSAA is only worth it if actualRenderingPath == Forward");
            }

            // ---- QualitySettings ----
            sb.AppendLine("[QUALITY]");
            sb.AppendLine($"  antiAliasing(MSAA) = {QualitySettings.antiAliasing}   (the game NEVER sets this)");
            sb.AppendLine($"  anisotropicFiltering = {QualitySettings.anisotropicFiltering}   (the game NEVER sets this)");
            sb.AppendLine($"  lodBias = {QualitySettings.lodBias}");
            sb.AppendLine($"  shadowDistance = {QualitySettings.shadowDistance}  cascades = {QualitySettings.shadowCascades}  res = {QualitySettings.shadowResolution}");
            sb.AppendLine($"  pixelLightCount = {QualitySettings.pixelLightCount}");
            sb.AppendLine($"  softParticles = {QualitySettings.softParticles}");
            sb.AppendLine($"  maxQueuedFrames = {QualitySettings.maxQueuedFrames}");
            sb.AppendLine($"  globalTextureMipmapLimit = {QualitySettings.globalTextureMipmapLimit}");
            sb.AppendLine($"  vSyncCount = {QualitySettings.vSyncCount}");

            // ---- Point lights (torches, campfires) ----
            sb.AppendLine("[LIGHT LOD]");
            sb.AppendLine($"  m_lightLimit = {LightLod.m_lightLimit}   (-1 = unlimited)");
            sb.AppendLine($"  m_shadowLimit = {LightLod.m_shadowLimit}  (-1 = unlimited)");

            // ---- Grass ----
            var clutter = ClutterSystem.instance;
            sb.AppendLine("[CLUTTER]");
            if (clutter != null)
                sb.AppendLine($"  m_distance = {clutter.m_distance}  m_grassPatchSize = {clutter.m_grassPatchSize}");
            else
                sb.AppendLine("  (ClutterSystem doesn't exist yet)");

            // ---- Render/fog ----
            sb.AppendLine("[RENDER SETTINGS]");
            sb.AppendLine($"  fog = {RenderSettings.fog}  density = {RenderSettings.fogDensity:0.#####}  mode = {RenderSettings.fogMode}");
            sb.AppendLine($"  ambientMode = {RenderSettings.ambientMode}  ambientLight = {RenderSettings.ambientLight}");

            // ---- Network / world ----
            sb.AppendLine("[NETWORK]");
            var znet = ZNet.instance;
            if (znet != null)
            {
                var sim = znet.GetSyncedSimulationDistance();
                int zonas = (2 * sim.NearSimulationDistance + 1) * (2 * sim.NearSimulationDistance + 1);
                float zoneSize = ZoneSystem.instance != null ? ZoneSystem.instance.m_zoneSize : 64f;
                float distCriaturas = sim.NearSimulationDistance * zoneSize + zoneSize * 0.5f;
                float distCenario = sim.TotalSimulationDistance * zoneSize + zoneSize * 0.5f;
                sb.AppendLine($"  IsServer = {znet.IsServer()}");
                sb.AppendLine($"  SimulationDistance: near = {sim.NearSimulationDistance}  far = {sim.FarSimulationDistance}  classic = {sim.IsClassic}");
                sb.AppendLine($"    -> {zonas} simulated zones (vanilla near=2 -> 25 zones)");
                sb.AppendLine($"    -> CREATURES (enemies/animals) visible up to ~{distCriaturas:0} m   [= near]");
                sb.AppendLine($"    -> distant scenery up to ~{distCenario:0} m   [= near+far, the number the menu shows]");
                sb.AppendLine($"  ZRpc timeout atual = {Patches.TimeoutPatch.Current}s");
            }
            else
            {
                sb.AppendLine("  (outside a world)");
            }

            // ---- Sources of stutter ----
            sb.AppendLine("[STUTTER]");
            sb.AppendLine($"  GC: {Patches.GcPatch.Describe()}");
            var zs = ZoneSystem.instance;
            if (zs != null)
            {
                sb.AppendLine($"  LocationsGenerated = {zs.LocationsGenerated}" +
                              (zs.LocationsGenerated ? "" : "   <- NEW world: the game uses a 100ms/frame budget"));
                sb.AppendLine($"  zone generation budget = {Patches.ZoneGenBudgetPatch.CurrentBudgetMs:0.##} ms/frame");
            }
            sb.AppendLine($"  ZDO ownership interval = {ModConfig.ZdoReleaseIntervalSec.Value:0.#}s");

            DumpWorldRates(sb);

            // ---- Screen ----
            sb.AppendLine("[SCREEN]");
            sb.AppendLine($"  {Screen.width}x{Screen.height} @ {Screen.currentResolution.refreshRateRatio.value:0.##}Hz  " +
                          $"fullScreenMode = {Screen.fullScreenMode}");
            sb.AppendLine($"  -> MaximizedWindow/Windowed goes through DWM and costs latency. " +
                          $"To MEASURE fps use borderless (screen capture works); to PLAY use exclusive.");

            DumpItemDrops(sb);

            sb.AppendLine("======================================================================");

            Plugin.Log.LogInfo(sb.ToString());
        }

        /// <summary>
        /// Lists the multipliers and flags the world actually has.
        ///
        /// The World Modifiers menu exposes only 5 categories (Combat, DeathPenalty,
        /// Resources, Raids, Portals), but the GlobalKeys enum has 41 functional
        /// keys, all read by Game.UpdateWorldRates via trySetScalarKey.
        /// The menu -> key mapping comes from prefab data, not code, so
        /// you can't tell from reading what the menu covers. This shows the
        /// REAL state, which is what matters.
        /// </summary>
        private static void DumpWorldRates(StringBuilder sb)
        {
            sb.AppendLine("[WORLD: MULTIPLIERS]");

            void Rate(string nome, float v)
            {
                if (!Mathf.Approximately(v, 1f)) sb.AppendLine($"  {nome,-22} {v:0.##}  <- changed");
            }

            Rate("PlayerDamage", Game.m_playerDamageRate);
            Rate("EnemyDamage", Game.m_enemyDamageRate);
            Rate("EnemyLevelUp", Game.m_enemyLevelUpRate);
            Rate("EnemySpeedSize", Game.m_enemySpeedSize);
            Rate("Resource", Game.m_resourceRate);
            Rate("Event/Raids", Game.m_eventRate);
            Rate("Stamina", Game.m_staminaRate);
            Rate("StaminaRegen", Game.m_staminaRegenRate);
            Rate("MoveStamina", Game.m_moveStaminaRate);
            Rate("Eitr", Game.m_eitrRate);
            Rate("Durability", Game.m_durabilityRate);
            Rate("Food", Game.m_foodRate);
            Rate("Adrenaline", Game.m_adrenalineRate);
            Rate("SkillGain", Game.m_skillGainRate);
            Rate("SkillReduction", Game.m_skillReductionRate);
            Rate("CarryWeight", Game.m_carryWeightRate);
            sb.AppendLine($"  WorldLevel             {Game.m_worldLevel}");
            sb.AppendLine("  (only what differs from 1.0 shows)");

            var zs = ZoneSystem.instance;
            if (zs == null) { sb.AppendLine("  (ZoneSystem absent)"); return; }

            sb.AppendLine("[WORLD: ACTIVE FLAGS]");
            // Only those that change game rules -- the progress ones (defeated_*) are left out.
            GlobalKeys[] interessantes =
            {
                GlobalKeys.TeleportAll, GlobalKeys.NoPortals, GlobalKeys.NoBossPortals,
                GlobalKeys.DeathKeepEquip, GlobalKeys.DeathKeepInventory,
                GlobalKeys.DeathDeleteItems, GlobalKeys.DeathDeleteUnequipped, GlobalKeys.DeathSkillsReset,
                GlobalKeys.NoBuildCost, GlobalKeys.NoCraftCost, GlobalKeys.NoWorkbench,
                GlobalKeys.NoBuildingFall, GlobalKeys.DungeonBuild,
                GlobalKeys.AllPiecesUnlocked, GlobalKeys.AllRecipesUnlocked,
                GlobalKeys.PassiveMobs, GlobalKeys.NoMap,
                GlobalKeys.NoHeavySnow, GlobalKeys.AllHeavySnow, GlobalKeys.NoPseudoDrops
            };

            bool alguma = false;
            foreach (var k in interessantes)
            {
                if (zs.GetGlobalKey(k)) { sb.AppendLine($"  {k}"); alguma = true; }
            }
            if (!alguma) sb.AppendLine("  none (world at default)");
        }

        /// <summary>
        /// Measures why a ground item disappears from up close, instead of theorizing.
        ///
        /// ItemDrop has no culling code: what hides it is Unity's LODGroup.
        /// The distance at which a LOD switches (or the object disappears) comes from:
        ///     dist = (size * lodBias) / (2 * screenRelativeHeight * tan(fovV/2))
        /// The last LOD with the lowest threshold is the disappearance point.
        /// </summary>
        private static void DumpItemDrops(StringBuilder sb)
        {
            sb.AppendLine("[GROUND ITEMS]");

            var drops = Object.FindObjectsByType<ItemDrop>(FindObjectsSortMode.None);
            if (drops == null || drops.Length == 0)
            {
                sb.AppendLine("  (no ItemDrop in the scene -- drop something on the ground and run DumpNow)");
                return;
            }

            var cam = Camera.allCameras.Length > 0 ? Camera.allCameras[0] : null;
            float fovRad = (cam != null ? cam.fieldOfView : 65f) * Mathf.Deg2Rad;
            float tanHalf = Mathf.Tan(fovRad * 0.5f);
            float bias = QualitySettings.lodBias;
            Vector3 origin = Player.m_localPlayer != null
                ? Player.m_localPlayer.transform.position
                : (cam != null ? cam.transform.position : Vector3.zero);

            sb.AppendLine($"  {drops.Length} item(s) in the scene. current lodBias = {bias}");

            int shown = 0;
            foreach (var d in drops)
            {
                if (d == null || shown >= 6) continue;
                shown++;

                float dist = Vector3.Distance(origin, d.transform.position);
                var lod = d.GetComponentInChildren<LODGroup>();
                string nome = d.name.Replace("(Clone)", "");

                if (lod == null)
                {
                    sb.AppendLine($"  {nome,-24} {dist,6:0.0}m  no LODGroup (not LOD culling)");
                    continue;
                }

                var lods = lod.GetLODs();
                float menorThreshold = 1f;
                foreach (var l in lods)
                    if (l.screenRelativeTransitionHeight < menorThreshold)
                        menorThreshold = l.screenRelativeTransitionHeight;

                float cull = menorThreshold > 0f
                    ? (lod.size * bias) / (2f * menorThreshold * tanHalf)
                    : -1f;

                sb.AppendLine($"  {nome,-24} {dist,6:0.0}m  LODs={lods.Length} size={lod.size:0.00} " +
                              $"cullAt={menorThreshold:0.0000} -> disappears at ~{cull:0}m");
            }

            sb.AppendLine("  -> doubling lodBias doubles these distances (linear).");
        }
    }
}
