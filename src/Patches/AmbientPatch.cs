using HarmonyLib;
using UnityEngine;
using UnityEngine.Rendering;

namespace ValheimTweaks.Patches
{
    /// <summary>
    /// Nevoa e luz ambiente -- a "cara" do jogo.
    ///
    /// EnvMan.SetEnv() e o unico ponto que escreve fog/ambiente, por bioma e por
    /// hora do dia. Um Postfix aqui governa a atmosfera inteira do jogo.
    ///
    /// Medido no mundo LAB: fogDensity = 0,03 Exponential, ambientMode = Flat,
    /// ambientLight = cinza 0,382 uniforme. E dai que vem o aspecto lavado.
    ///
    /// ---- CUIDADO COM A ORDEM ----
    /// Dentro do proprio SetEnv, DEPOIS de escrever RenderSettings.ambientLight,
    /// o jogo faz:
    ///     Shader.SetGlobalColor(s_ambientColor, RenderSettings.ambientLight);
    /// Os shaders do Valheim leem esse global, nao o RenderSettings. Entao mexer
    /// so no RenderSettings num Postfix deixaria os dois dessincronizados
    /// (Unity com um valor, shader com outro). Por isso reescrevemos o global.
    /// </summary>
    internal static class AmbientPatch
    {
        private static readonly int AmbientColorId = Shader.PropertyToID("_AmbientColor");

        [HarmonyPatch(typeof(EnvMan), "SetEnv")]
        internal static class SetEnvHook
        {
            private static void Postfix()
            {
                // --- Nevoa ---
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

                // --- Ambiente ---
                float brightness = ModConfig.AmbientBrightness.Value;
                bool trilight = ModConfig.TrilightAmbient.Value;
                if (Mathf.Approximately(brightness, 1f) && !trilight) return;

                Color amb = RenderSettings.ambientLight * brightness;
                amb.a = 1f;
                RenderSettings.ambientLight = amb;

                if (trilight)
                {
                    // Flat = uma cor vinda de todo lado (o mais barato e mais chapado).
                    // Trilight separa ceu / horizonte / chao e da volume aos objetos.
                    RenderSettings.ambientMode = AmbientMode.Trilight;
                    RenderSettings.ambientSkyColor = amb * 1.15f;
                    RenderSettings.ambientEquatorColor = amb;
                    RenderSettings.ambientGroundColor = amb * 0.6f;
                }

                // Obrigatorio: o jogo ja setou este global com o valor ANTIGO.
                Shader.SetGlobalColor(AmbientColorId, amb);
            }
        }
    }
}
