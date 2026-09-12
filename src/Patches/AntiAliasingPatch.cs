using System.Reflection;
using HarmonyLib;
using UnityEngine.PostProcessing;

namespace ValheimTweaks.Patches
{
    /// <summary>
    /// Anti-aliasing.
    ///
    /// MSAA esta DESCARTADO nesta engine -- o diagnostico mediu a camera do jogo
    /// como "path = DeferredShading, MSAA_ok = False". Em deferred o Unity ignora
    /// MSAA. Entao a unica alavanca real e o post-processing.
    ///
    /// O Valheim usa o PostProcessing v1 e so faz isto:
    ///     m_postProcessing.profile.antialiasing.enabled = enabled;
    /// Nunca toca em .settings -- ou seja, nunca escolhe o METODO nem a qualidade.
    /// Fica no FXAA preset Default para sempre.
    ///
    /// O que da para fazer:
    ///   FXAA: 5 presets, de ExtremePerformance ate ExtremeQuality.
    ///   TAA:  jitterSpread / sharpen / stationaryBlending / motionBlending.
    ///         E o certo em deferred, e resolve serrilhado de geometria fina
    ///         (folhagem, corda de barco, cerca) que o FXAA so borra.
    ///         Custa fantasma em movimento -- por isso sharpen e blending sao
    ///         ajustaveis aqui em vez de fixos.
    /// </summary>
    internal static class AntiAliasingPatch
    {
        private static readonly FieldInfo PostField =
            AccessTools.Field(typeof(CameraEffects), "m_postProcessing");

        [HarmonyPatch(typeof(CameraEffects), "ApplySettings")]
        internal static class ApplySettingsHook
        {
            private static void Postfix() => Apply();
        }

        internal static void Apply()
        {
            if (!ModConfig.AaOverride.Value) return;

            var effects = CameraEffects.instance;
            if (effects == null || PostField == null) return;

            var behaviour = PostField.GetValue(effects) as PostProcessingBehaviour;
            var profile = behaviour?.profile;
            if (profile == null || profile.antialiasing == null) return;

            var s = profile.antialiasing.settings;

            if (ModConfig.AaUseTaa.Value)
            {
                s.method = AntialiasingModel.Method.Taa;
                s.taaSettings.jitterSpread = ModConfig.TaaJitterSpread.Value;
                s.taaSettings.sharpen = ModConfig.TaaSharpen.Value;
                s.taaSettings.stationaryBlending = ModConfig.TaaStationaryBlending.Value;
                s.taaSettings.motionBlending = ModConfig.TaaMotionBlending.Value;
            }
            else
            {
                s.method = AntialiasingModel.Method.Fxaa;
                s.fxaaSettings.preset = (AntialiasingModel.FxaaPreset)ModConfig.FxaaPreset.Value;
            }

            profile.antialiasing.settings = s;   // struct: precisa reatribuir
            profile.antialiasing.enabled = true;

            Plugin.Log.LogInfo($"AA: metodo={s.method} " +
                (s.method == AntialiasingModel.Method.Taa
                    ? $"jitter={s.taaSettings.jitterSpread:0.##} sharpen={s.taaSettings.sharpen:0.##}"
                    : $"preset={s.fxaaSettings.preset}"));
        }
    }
}
