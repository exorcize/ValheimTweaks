using HarmonyLib;
using UnityEngine;
using UnityEngine.Rendering;

namespace ValheimTweaks.Patches
{
    /// <summary>
    /// Fog and ambient light -- the "face" of the game.
    ///
    /// EnvMan.SetEnv() is the only point that writes fog/ambient, per biome and per
    /// time of day. A Postfix here governs the entire atmosphere of the game.
    ///
    /// Measured in the LAB world: fogDensity = 0.03 Exponential, ambientMode = Flat,
    /// ambientLight = uniform gray 0.382. That is where the washed-out look comes from.
    ///
    /// ---- MIND THE ORDER ----
    /// Inside SetEnv itself, AFTER writing RenderSettings.ambientLight,
    /// the game does:
    ///     Shader.SetGlobalColor(s_ambientColor, RenderSettings.ambientLight);
    /// Valheim's shaders read that global, not RenderSettings. So touching
    /// only RenderSettings in a Postfix would leave the two out of sync
    /// (Unity with one value, the shader with another). That is why we rewrite the global.
    /// </summary>
    internal static class AmbientPatch
    {
        private static readonly int AmbientColorId = Shader.PropertyToID("_AmbientColor");

        [HarmonyPatch(typeof(EnvMan), "SetEnv")]
        internal static class SetEnvHook
        {
            private static void Postfix()
            {
                // --- Fog ---
                if (ModConfig.FogEnabled.Value)
                {
                    float scale = ModConfig.FogDensityScale.Value;
                    if (!Mathf.Approximately(scale, 1f))
                        RenderSettings.fogDensity *= scale;
                }
                else
                {
                    RenderSettings.fog = false;
                }

                // --- Ambient ---
                float brightness = ModConfig.AmbientBrightness.Value;
                bool trilight = ModConfig.TrilightAmbient.Value;
                if (Mathf.Approximately(brightness, 1f) && !trilight) return;

                Color amb = RenderSettings.ambientLight * brightness;
                amb.a = 1f;
                RenderSettings.ambientLight = amb;

                if (trilight)
                {
                    // Flat = one color coming from all sides (the cheapest and flattest).
                    // Trilight separates sky / horizon / ground and gives objects volume.
                    RenderSettings.ambientMode = AmbientMode.Trilight;
                    RenderSettings.ambientSkyColor = amb * 1.15f;
                    RenderSettings.ambientEquatorColor = amb;
                    RenderSettings.ambientGroundColor = amb * 0.6f;
                }

                // Mandatory: the game has already set this global with the OLD value.
                Shader.SetGlobalColor(AmbientColorId, amb);
            }
        }
    }
}
