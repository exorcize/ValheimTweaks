using System.Text;
using UnityEngine;

namespace ValheimTweaks
{
    /// <summary>
    /// Despeja no log o estado REAL do pipeline de render.
    ///
    /// Existe para parar de especular. Em particular responde a pergunta que
    /// decide se MSAA e viavel: qual o actualRenderingPath da camera do jogo?
    /// MSAA so funciona em Forward -- em Deferred o Unity ignora.
    /// </summary>
    internal static class Diagnostics
    {
        internal static void Dump(string reason)
        {
            var sb = new StringBuilder();
            sb.AppendLine();
            sb.AppendLine("=============== ValheimTweaks :: diagnostico (" + reason + ") ===============");

            // ---- Cameras: a resposta sobre MSAA esta aqui ----
            sb.AppendLine("[CAMERAS]");
            var cams = Camera.allCameras;
            if (cams == null || cams.Length == 0)
            {
                sb.AppendLine("  (nenhuma camera ativa)");
            }
            else
            {
                foreach (var c in cams)
                {
                    if (c == null) continue;
                    sb.AppendLine($"  {c.name,-22} path={c.actualRenderingPath,-16} " +
                                  $"(pedido={c.renderingPath}) MSAA_ok={c.allowMSAA} HDR={c.allowHDR} " +
                                  $"fov={c.fieldOfView:0.#} far={c.farClipPlane:0} depth={c.depth}");
                }
                sb.AppendLine("  -> MSAA so vale a pena se actualRenderingPath == Forward");
            }

            // ---- QualitySettings ----
            sb.AppendLine("[QUALITY]");
            sb.AppendLine($"  antiAliasing(MSAA) = {QualitySettings.antiAliasing}   (o jogo NUNCA seta isso)");
            sb.AppendLine($"  anisotropicFiltering = {QualitySettings.anisotropicFiltering}   (o jogo NUNCA seta isso)");
            sb.AppendLine($"  lodBias = {QualitySettings.lodBias}");
            sb.AppendLine($"  shadowDistance = {QualitySettings.shadowDistance}  cascades = {QualitySettings.shadowCascades}  res = {QualitySettings.shadowResolution}");
            sb.AppendLine($"  pixelLightCount = {QualitySettings.pixelLightCount}");
            sb.AppendLine($"  softParticles = {QualitySettings.softParticles}");
            sb.AppendLine($"  maxQueuedFrames = {QualitySettings.maxQueuedFrames}");
            sb.AppendLine($"  globalTextureMipmapLimit = {QualitySettings.globalTextureMipmapLimit}");
            sb.AppendLine($"  vSyncCount = {QualitySettings.vSyncCount}");

            // ---- Luzes pontuais (tochas, fogueiras) ----
            sb.AppendLine("[LIGHT LOD]");
            sb.AppendLine($"  m_lightLimit = {LightLod.m_lightLimit}   (-1 = ilimitado)");
            sb.AppendLine($"  m_shadowLimit = {LightLod.m_shadowLimit}  (-1 = ilimitado)");

            // ---- Grama ----
            var clutter = ClutterSystem.instance;
            sb.AppendLine("[CLUTTER]");
            if (clutter != null)
                sb.AppendLine($"  m_distance = {clutter.m_distance}  m_grassPatchSize = {clutter.m_grassPatchSize}");
            else
                sb.AppendLine("  (ClutterSystem ainda nao existe)");

            // ---- Render/fog ----
            sb.AppendLine("[RENDER SETTINGS]");
            sb.AppendLine($"  fog = {RenderSettings.fog}  density = {RenderSettings.fogDensity:0.#####}  mode = {RenderSettings.fogMode}");
            sb.AppendLine($"  ambientMode = {RenderSettings.ambientMode}  ambientLight = {RenderSettings.ambientLight}");

            // ---- Rede / mundo ----
            sb.AppendLine("[REDE]");
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
                sb.AppendLine($"    -> {zonas} zonas simuladas (vanilla near=2 -> 25 zonas)");
                sb.AppendLine($"    -> CRIATURAS (inimigos/animais) visiveis ate ~{distCriaturas:0} m   [= near]");
                sb.AppendLine($"    -> cenario distante ate ~{distCenario:0} m   [= near+far, o numero que o menu mostra]");
                sb.AppendLine($"  ZRpc timeout atual = {Patches.TimeoutPatch.Current}s");
            }
            else
            {
                sb.AppendLine("  (fora de mundo)");
            }

            // ---- Fontes de engasgo ----
            sb.AppendLine("[ENGASGO]");
            sb.AppendLine($"  GC: {Patches.GcPatch.Describe()}");
            var zs = ZoneSystem.instance;
            if (zs != null)
            {
                sb.AppendLine($"  LocationsGenerated = {zs.LocationsGenerated}" +
                              (zs.LocationsGenerated ? "" : "   <- mundo NOVO: o jogo usa orcamento de 100ms/frame"));
                sb.AppendLine($"  orcamento de geracao de zona = {Patches.ZoneGenBudgetPatch.CurrentBudgetMs:0.##} ms/frame");
            }
            sb.AppendLine($"  teto de objetos/frame = " +
                          (ModConfig.MaxObjectsPerFrame.Value > 0
                              ? ModConfig.MaxObjectsPerFrame.Value.ToString()
                              : "sem limite (vanilla: Max(pendentes/100, 10), cresce sozinho)"));

            // ---- Tela ----
            sb.AppendLine("[TELA]");
            sb.AppendLine($"  {Screen.width}x{Screen.height} @ {Screen.currentResolution.refreshRateRatio.value:0.##}Hz  " +
                          $"fullScreenMode = {Screen.fullScreenMode}");
            sb.AppendLine($"  -> MaximizedWindow/Windowed passa pelo DWM e custa latencia. " +
                          $"Para MEDIR fps use borderless (captura de tela funciona); para JOGAR use exclusivo.");

            DumpItemDrops(sb);

            sb.AppendLine("======================================================================");

            Plugin.Log.LogInfo(sb.ToString());
        }

        /// <summary>
        /// Mede por que item no chao some de perto, em vez de teorizar.
        ///
        /// ItemDrop nao tem codigo de culling: quem esconde e o LODGroup do Unity.
        /// A distancia em que um LOD troca (ou o objeto some) sai de:
        ///     dist = (size * lodBias) / (2 * screenRelativeHeight * tan(fovV/2))
        /// O ultimo LOD com threshold mais baixo e o ponto de sumico.
        /// </summary>
        private static void DumpItemDrops(StringBuilder sb)
        {
            sb.AppendLine("[ITENS NO CHAO]");

            var drops = Object.FindObjectsByType<ItemDrop>(FindObjectsSortMode.None);
            if (drops == null || drops.Length == 0)
            {
                sb.AppendLine("  (nenhum ItemDrop na cena -- jogue algo no chao e rode DumpNow)");
                return;
            }

            var cam = Camera.allCameras.Length > 0 ? Camera.allCameras[0] : null;
            float fovRad = (cam != null ? cam.fieldOfView : 65f) * Mathf.Deg2Rad;
            float tanHalf = Mathf.Tan(fovRad * 0.5f);
            float bias = QualitySettings.lodBias;
            Vector3 origin = Player.m_localPlayer != null
                ? Player.m_localPlayer.transform.position
                : (cam != null ? cam.transform.position : Vector3.zero);

            sb.AppendLine($"  {drops.Length} item(s) na cena. lodBias atual = {bias}");

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
                    sb.AppendLine($"  {nome,-24} {dist,6:0.0}m  sem LODGroup (nao e culling de LOD)");
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
                              $"cullAt={menorThreshold:0.0000} -> some a ~{cull:0}m");
            }

            sb.AppendLine("  -> dobrar lodBias dobra essas distancias (linear).");
        }
    }
}
