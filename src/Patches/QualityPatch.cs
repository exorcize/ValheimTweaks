using UnityEngine;

namespace ValheimTweaks.Patches
{
    /// <summary>
    /// Overrides on QualitySettings, on top of what the game applies.
    ///
    /// Does not use Harmony: GraphicsSettingsManager.GraphicsSettingsChanged is a
    /// public event that fires every time the game rewrites the settings.
    /// We hook in after it.
    ///
    /// Game mapping (GraphicsSettingsManager), for reference:
    ///   GetLodBias:      0 -> 1.0   1 -> 1.5   2 -> 2.0   3 -> 5.0
    ///   ShadowQuality:   0 -> 2 cascades / 80m / Low
    ///                    1 -> 3 cascades / 120m / Medium
    ///                    2 -> 4 cascades / 150m / High
    ///   maxQueuedFrames: fixed at 2, no menu option
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
            // --- LOD bias: what makes items on the ground / resource / stone disappear up close.
            // The pop-out distance is LINEAR in this value.
            float bias = ModConfig.LodBiasOverride.Value;
            if (bias > 0f && !Mathf.Approximately(QualitySettings.lodBias, bias))
            {
                Plugin.Log.LogInfo($"lodBias: {QualitySettings.lodBias} -> {bias}");
                QualitySettings.lodBias = bias;
            }

            // --- Shadows: the menu stops at 150m / 4 cascades.
            float shadowDist = ModConfig.ShadowDistance.Value;
            if (shadowDist > 0f && !Mathf.Approximately(QualitySettings.shadowDistance, shadowDist))
            {
                QualitySettings.shadowDistance = shadowDist;
            }

            int cascades = ModConfig.ShadowCascades.Value;
            if (cascades > 0 && QualitySettings.shadowCascades != cascades)
            {
                // Unity only accepts 0, 1, 2 or 4 cascades.
                QualitySettings.shadowCascades = (cascades == 3) ? 4 : cascades;
            }

            // --- Shadow map resolution: the menu stops at High (ShadowQuality 2).
            int shadowRes = ModConfig.ShadowResolution.Value;
            if (shadowRes > 0)
            {
                var alvo = (ShadowResolution)(shadowRes - 1);
                if (QualitySettings.shadowResolution != alvo)
                    QualitySettings.shadowResolution = alvo;
            }

            // --- Tesselation: REAL geometry displacement on the terrain, not a normal map.
            // The game only toggles it via the menu (ApplyShaderKeywords). Since it is a global
            // shader keyword, the cost is all on the GPU -- which on this machine has headroom to spare.
            int tess = ModConfig.Tesselation.Value;
            if (tess > 0)
            {
                if (tess == 1) Shader.EnableKeyword("TESSELATION_ON");
                else Shader.DisableKeyword("TESSELATION_ON");
            }

            // --- Pre-rendered frames: the game locks at 2. 1 cuts one frame of
            // latency (costs a little fps in a GPU-bound scenario; here the GPU has headroom).
            int queued = ModConfig.MaxQueuedFrames.Value;
            if (queued > 0 && QualitySettings.maxQueuedFrames != queued)
            {
                Plugin.Log.LogInfo($"maxQueuedFrames: {QualitySettings.maxQueuedFrames} -> {queued}");
                QualitySettings.maxQueuedFrames = queued;
            }
        }
    }
}
