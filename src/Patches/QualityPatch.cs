using UnityEngine;

namespace ValheimTweaks.Patches
{
    /// <summary>
    /// Overrides em QualitySettings, por cima do que o jogo aplica.
    ///
    /// Nao usa Harmony: GraphicsSettingsManager.GraphicsSettingsChanged e um
    /// evento publico que dispara toda vez que o jogo reescreve as settings.
    /// Entramos depois dele.
    ///
    /// Mapeamento do jogo (GraphicsSettingsManager), para referencia:
    ///   GetLodBias:      0 -> 1.0   1 -> 1.5   2 -> 2.0   3 -> 5.0
    ///   ShadowQuality:   0 -> 2 cascades / 80m / Low
    ///                    1 -> 3 cascades / 120m / Medium
    ///                    2 -> 4 cascades / 150m / High
    ///   maxQueuedFrames: fixo em 2, sem opcao no menu
    /// </summary>
    internal static class QualityPatch
    {
        private static bool _subscribed;

        internal static void Apply()
        {
            if (!_subscribed)
            {
                GraphicsSettingsManager.GraphicsSettingsChanged += ApplyNow;
                _subscribed = true;
            }

            ApplyNow();
        }

        private static void ApplyNow()
        {
            // --- LOD bias: o que faz item no chao / recurso / pedra sumir de perto.
            // A distancia de sumico e LINEAR neste valor.
            float bias = ModConfig.LodBiasOverride.Value;
            if (bias > 0f && !Mathf.Approximately(QualitySettings.lodBias, bias))
            {
                Plugin.Log.LogInfo($"lodBias: {QualitySettings.lodBias} -> {bias}");
                QualitySettings.lodBias = bias;
            }

            // --- Sombras: o menu para em 150m / 4 cascades.
            float shadowDist = ModConfig.ShadowDistance.Value;
            if (shadowDist > 0f && !Mathf.Approximately(QualitySettings.shadowDistance, shadowDist))
            {
                QualitySettings.shadowDistance = shadowDist;
            }

            int cascades = ModConfig.ShadowCascades.Value;
            if (cascades > 0 && QualitySettings.shadowCascades != cascades)
            {
                // Unity so aceita 0, 1, 2 ou 4 cascades.
                QualitySettings.shadowCascades = (cascades == 3) ? 4 : cascades;
            }

            // --- Resolucao do shadow map: o menu para em High (ShadowQuality 2).
            int shadowRes = ModConfig.ShadowResolution.Value;
            if (shadowRes > 0)
            {
                var alvo = (ShadowResolution)(shadowRes - 1);
                if (QualitySettings.shadowResolution != alvo)
                    QualitySettings.shadowResolution = alvo;
            }

            // --- Tesselacao: deslocamento REAL de geometria no terreno, nao normal map.
            // O jogo so liga/desliga pelo menu (ApplyShaderKeywords). Como e keyword
            // global de shader, o custo e todo de GPU -- que nesta maquina sobra.
            int tess = ModConfig.Tesselation.Value;
            if (tess > 0)
            {
                if (tess == 1) Shader.EnableKeyword("TESSELATION_ON");
                else Shader.DisableKeyword("TESSELATION_ON");
            }

            // --- Frames pre-renderizados: o jogo trava em 2. 1 corta um frame de
            // latencia (custa um pouco de fps em cenario GPU-bound; aqui a GPU sobra).
            int queued = ModConfig.MaxQueuedFrames.Value;
            if (queued > 0 && QualitySettings.maxQueuedFrames != queued)
            {
                Plugin.Log.LogInfo($"maxQueuedFrames: {QualitySettings.maxQueuedFrames} -> {queued}");
                QualitySettings.maxQueuedFrames = queued;
            }
        }
    }
}
