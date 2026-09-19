using HarmonyLib;
using UnityEngine;

namespace ValheimTweaks.Patches
{
    /// <summary>
    /// Point lights (torch, campfire, forge) and grass.
    ///
    /// The game ALREADY has a ready-made dynamic limiter -- LightLod sorts the lights by
    /// distance and enables only the first N -- but the menu only exposes 4 fixed steps
    /// (GraphicsSettingsManager):
    ///     GetPointLightLimit:       0 -> 4   1 -> 15   2 -> 40   3 -> -1 (unlimited)
    ///     GetPointLightShadowLimit: 0 -> 0   1 -> 1    2 -> 3    3 -> -1
    ///
    /// Point light shadows are among the most expensive things in the game in a built
    /// base, but 3 is too little and "unlimited" brings everything down. The middle ground
    /// (6, 8, 10) does not exist in the menu -- only here.
    ///
    /// ClutterSystem.m_distance controls how far the grass is drawn. Measured at
    /// runtime: 45 (the code default is 40; the prefab overrides it).
    ///
    /// Config convention: -2 = do not override, -1 = unlimited, 0+ = limit.
    /// </summary>
    internal static class LightLodPatch
    {
        private const int DontOverride = -2;

        private static bool _subscribed;

        internal static void Apply()
        {
            if (!_subscribed)
            {
                // The game applies the limits inside ApplyLightLod, triggered by this event.
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

            // Grass density. The menu maps Low->amount/4, Med->amount/2,
            // High->amount; m_amountScale multiplies on top of that and is public.
            // Changing the value requires clearing the already generated patches, otherwise
            // the new density only shows up on new terrain -- hence the ClearAll.
            float scale = ModConfig.ClutterAmountScale.Value;
            if (scale > 0f && !Mathf.Approximately(clutter.m_amountScale, scale))
            {
                clutter.m_amountScale = scale;
                AccessTools.Method(typeof(ClutterSystem), "ClearAll")?.Invoke(clutter, null);
                Plugin.Log.LogInfo($"ClutterSystem.m_amountScale -> {scale} (patches regenerated)");
            }
        }
    }
}
