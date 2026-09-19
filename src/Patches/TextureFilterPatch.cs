using UnityEngine;

namespace ValheimTweaks.Patches
{
    /// <summary>
    /// Texture filtering.
    ///
    /// FINDING: Valheim has the "anisotropic textures" option in the menu, reads it from the prefs
    /// (GraphicsSettingsManager line 479), stores it in GraphicsSettingsState, saves it back...
    /// and NEVER applies it. There is not a single "QualitySettings.anisotropicFiltering"
    /// in the whole assembly_valheim.dll. The checkbox does nothing.
    ///
    /// Here we apply it for real. Gain: ground, road, terrain and wooden floor
    /// stop blurring at a shallow angle. Cost on a modern GPU: negligible.
    ///
    /// Does not use Harmony: the game exposes a public event for this.
    /// GraphicsSettingsManager.GraphicsSettingsChanged fires every time it re-applies
    /// its own settings -- so we re-apply on top of it afterward.
    /// </summary>
    internal static class TextureFilterPatch
    {
        private static bool _subscribed;

        internal static void Apply()
        {
            if (!_subscribed)
            {
                GraphicsSettingsManager.GraphicsSettingsChanged += OnGameReapplied;
                _subscribed = true;
            }

            ApplyNow();
        }

        // The game just rewrote QualitySettings; put ours on top.
        private static void OnGameReapplied() => ApplyNow();

        private static void ApplyNow()
        {
            if (ModConfig.ForceAnisotropic.Value)
            {
                int level = Mathf.Clamp(ModConfig.AnisotropicLevel.Value, 1, 16);

                QualitySettings.anisotropicFiltering = level > 1
                    ? AnisotropicFiltering.ForceEnable
                    : AnisotropicFiltering.Disable;

                // ForceEnable alone uses the per-texture level; the global limits
                // ensure that every material actually receives the requested level.
                Texture.SetGlobalAnisotropicFilteringLimits(level, level);
            }

            if (ModConfig.FullResTextures.Value && QualitySettings.globalTextureMipmapLimit != 0)
            {
                QualitySettings.globalTextureMipmapLimit = 0;
            }
        }
    }
}
