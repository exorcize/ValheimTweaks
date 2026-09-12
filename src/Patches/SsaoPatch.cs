using System.Reflection;
using AmplifyOcclusion;
using HarmonyLib;

namespace ValheimTweaks.Patches
{
    /// <summary>
    /// Oclusao de ambiente (SSAO) -- sombra de contato: canto de parede, base de
    /// arvore, embaixo de movel. E o que tira o aspecto "chapado" da cena.
    ///
    /// O Valheim usa Amplify Occlusion, mas entrega so dois niveis e TRAVA a
    /// qualidade pela metade nos dois (CameraEffects.SetSSAO):
    ///
    ///     case 1:  m_amplifyOcclusion.Downsample = true;  SampleCount = Low;
    ///     default: m_amplifyOcclusion.Downsample = true;  SampleCount = Medium;
    ///
    /// Downsample = true esta HARDCODED nos dois casos -- ou seja, mesmo no
    /// "maximo" do menu o efeito roda em meia resolucao, e o teto de amostras e
    /// Medium quando o efeito suporta VeryHigh.
    ///
    /// Aqui a gente destrava: resolucao cheia e SampleCount ate VeryHigh.
    /// Tambem permite LIGAR o efeito sem passar pelo menu, o que deixa o A/B
    /// ser feito pelo arquivo de config, sem o jogador mexer em nada.
    /// </summary>
    internal static class SsaoPatch
    {
        private static readonly FieldInfo AoField =
            AccessTools.Field(typeof(CameraEffects), "m_amplifyOcclusion");

        // O jogo reaplica os efeitos aqui; entramos logo depois.
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
