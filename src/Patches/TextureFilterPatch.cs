using UnityEngine;

namespace ValheimTweaks.Patches
{
    /// <summary>
    /// Filtragem de textura.
    ///
    /// ACHADO: o Valheim tem a opcao "texturas anisotropicas" no menu, le ela das prefs
    /// (GraphicsSettingsManager linha 479), guarda no GraphicsSettingsState, salva de volta...
    /// e NUNCA a aplica. Nao existe um unico "QualitySettings.anisotropicFiltering"
    /// em toda a assembly_valheim.dll. O checkbox nao faz nada.
    ///
    /// Aqui a gente aplica de verdade. Ganho: chao, estrada, terreno e piso de madeira
    /// param de borrar em angulo raso. Custo numa GPU moderna: desprezivel.
    ///
    /// Nao usa Harmony: o jogo expoe um evento publico para isso.
    /// GraphicsSettingsManager.GraphicsSettingsChanged dispara toda vez que ele reaplica
    /// as proprias settings -- entao a gente reaplica por cima depois dele.
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

        // O jogo acabou de reescrever QualitySettings; poe o nosso por cima.
        private static void OnGameReapplied() => ApplyNow();

        private static void ApplyNow()
        {
            if (ModConfig.ForceAnisotropic.Value)
            {
                int level = Mathf.Clamp(ModConfig.AnisotropicLevel.Value, 1, 16);

                QualitySettings.anisotropicFiltering = level > 1
                    ? AnisotropicFiltering.ForceEnable
                    : AnisotropicFiltering.Disable;

                // ForceEnable sozinho usa o nivel por-textura; os limites globais
                // garantem que todo material realmente receba o nivel pedido.
                Texture.SetGlobalAnisotropicFilteringLimits(level, level);
            }

            if (ModConfig.FullResTextures.Value && QualitySettings.globalTextureMipmapLimit != 0)
            {
                QualitySettings.globalTextureMipmapLimit = 0;
            }
        }
    }
}
