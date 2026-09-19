using System.Reflection;
using HarmonyLib;
using UnityEngine.PostProcessing;

namespace ValheimTweaks.Patches
{
    /// <summary>
    /// Anti-aliasing.
    ///
    /// MSAA is RULED OUT on this engine -- the diagnostic measured the game camera
    /// as "path = DeferredShading, MSAA_ok = False". In deferred, Unity ignores
    /// MSAA. So the only real lever is post-processing.
    ///
    /// Valheim uses PostProcessing v1 and only does this:
    ///     m_postProcessing.profile.antialiasing.enabled = enabled;
    /// It never touches .settings -- that is, it never chooses the METHOD or the quality.
    /// It stays on the FXAA Default preset forever.
    ///
    /// What can be done:
    ///   FXAA: 5 presets, from ExtremePerformance to ExtremeQuality.
    ///   TAA:  jitterSpread / sharpen / stationaryBlending / motionBlending.
    ///         It is the right choice in deferred, and it resolves aliasing on thin
    ///         geometry (foliage, boat rope, fence) that FXAA merely blurs.
    ///         It costs ghosting in motion -- which is why sharpen and blending are
    ///         adjustable here instead of fixed.
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

            profile.antialiasing.settings = s;   // struct: must reassign
            profile.antialiasing.enabled = true;

            Plugin.Log.LogInfo($"AA: method={s.method} " +
                (s.method == AntialiasingModel.Method.Taa
                    ? $"jitter={s.taaSettings.jitterSpread:0.##} sharpen={s.taaSettings.sharpen:0.##}"
                    : $"preset={s.fxaaSettings.preset}"));
        }
    }
}
