using System.Reflection;
using AmplifyOcclusion;
using HarmonyLib;

namespace ValheimTweaks.Patches
{
    /// <summary>
    /// Ambient occlusion (SSAO) -- contact shadow: wall corner, tree base,
    /// under furniture. It is what removes the "flat" look of the scene.
    ///
    /// Valheim uses Amplify Occlusion, but delivers only two levels and LOCKS
    /// the quality at half in both (CameraEffects.SetSSAO):
    ///
    ///     case 1:  m_amplifyOcclusion.Downsample = true;  SampleCount = Low;
    ///     default: m_amplifyOcclusion.Downsample = true;  SampleCount = Medium;
    ///
    /// Downsample = true is HARDCODED in both cases -- that is, even at the
    /// menu's "maximum" the effect runs at half resolution, and the sample cap is
    /// Medium when the effect supports VeryHigh.
    ///
    /// Here we unlock it: full resolution and SampleCount up to VeryHigh.
    /// It also allows TURNING ON the effect without going through the menu, which lets the A/B
    /// be done from the config file, without the player touching anything.
    /// </summary>
    internal static class SsaoPatch
    {
        private static readonly FieldInfo AoField =
            AccessTools.Field(typeof(CameraEffects), "m_amplifyOcclusion");

        // The game re-applies the effects here; we hook in right after.
        [HarmonyPatch(typeof(CameraEffects), "ApplySettings")]
        internal static class ApplySettingsHook
        {
            private static void Postfix() => Apply();
        }

        internal static void Apply()
        {
            if (!ModConfig.SsaoOverride.Value) return;

            var effects = CameraEffects.instance;
            if (effects == null || AoField == null) return;

            var ao = AoField.GetValue(effects) as AmplifyOcclusionEffect;
            if (ao == null) return;

            ao.enabled = true;
            ao.Downsample = !ModConfig.SsaoFullResolution.Value;
            ao.SampleCount = (SampleCountLevel)ModConfig.SsaoSampleCount.Value;
            ao.Intensity = ModConfig.SsaoIntensity.Value;
            ao.Radius = ModConfig.SsaoRadius.Value;
            ao.PowerExponent = ModConfig.SsaoPower.Value;

            Plugin.Log.LogInfo(
                $"SSAO: samples={ao.SampleCount} fullRes={!ao.Downsample} " +
                $"intensity={ao.Intensity:0.##} radius={ao.Radius:0.##} power={ao.PowerExponent:0.##}");
        }
    }
}
