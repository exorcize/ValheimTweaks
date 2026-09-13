using HarmonyLib;
using UnityEngine;

namespace ValheimTweaks.Patches
{
    /// <summary>
    /// Luzes pontuais (tocha, fogueira, forja) e grama.
    ///
    /// O jogo JA tem um limitador dinamico pronto -- LightLod ordena as luzes por
    /// distancia e liga so as N primeiras -- mas o menu so expoe 4 degraus fixos
    /// (GraphicsSettingsManager):
    ///     GetPointLightLimit:       0 -> 4   1 -> 15   2 -> 40   3 -> -1 (ilimitado)
    ///     GetPointLightShadowLimit: 0 -> 0   1 -> 1    2 -> 3    3 -> -1
    ///
    /// Sombra de luz pontual e das coisas mais caras do jogo numa base construida,
    /// mas 3 e pouco e "ilimitado" derruba tudo. O meio-termo (6, 8, 10) nao existe
    /// no menu -- so aqui.
    ///
    /// ClutterSystem.m_distance controla ate onde a grama e desenhada. Medido em
    /// runtime: 45 (o default do codigo e 40; o prefab sobrescreve).
    ///
    /// Convencao das configs: -2 = nao sobrescrever, -1 = ilimitado, 0+ = limite.
    /// </summary>
    internal static class LightLodPatch
    {
        private const int DontOverride = -2;

        private static bool _subscribed;

        internal static void Apply()
        {
            if (!_subscribed)
            {
                // O jogo aplica os limites dentro de ApplyLightLod, disparado por este evento.
                GraphicsSettingsManager.GraphicsSettingsChanged += ApplyNow;
                _subscribed = true;
            }

            ApplyNow();
        }

        private static void ApplyNow()
        {
            int lights = ModConfig.PointLightLimit.Value;
            if (lights > DontOverride && LightLod.m_lightLimit != lights)
            {
                LightLod.m_lightLimit = lights;
                Plugin.Log.LogInfo($"LightLod.m_lightLimit -> {lights}");
            }

            int shadows = ModConfig.PointLightShadowLimit.Value;
            if (shadows > DontOverride && LightLod.m_shadowLimit != shadows)
            {
                LightLod.m_shadowLimit = shadows;
                Plugin.Log.LogInfo($"LightLod.m_shadowLimit -> {shadows}");
            }

            var clutter = ClutterSystem.instance;
            if (clutter == null) return;

            float grass = ModConfig.ClutterDistance.Value;
            if (grass > 0f && !Mathf.Approximately(clutter.m_distance, grass))
            {
                clutter.m_distance = grass;
                Plugin.Log.LogInfo($"ClutterSystem.m_distance -> {grass}");
            }

            // Densidade da grama. O menu mapeia Low->amount/4, Med->amount/2,
            // High->amount; m_amountScale multiplica por cima disso e e publico.
            // Trocar o valor exige limpar os patches ja gerados, senao a densidade
            // nova so aparece em terreno novo -- por isso o ClearAll.
            float escala = ModConfig.ClutterAmountScale.Value;
            if (escala > 0f && !Mathf.Approximately(clutter.m_amountScale, escala))
            {
                clutter.m_amountScale = escala;
                AccessTools.Method(typeof(ClutterSystem), "ClearAll")?.Invoke(clutter, null);
                Plugin.Log.LogInfo($"ClutterSystem.m_amountScale -> {escala} (patches regenerados)");
            }
        }
    }
}
