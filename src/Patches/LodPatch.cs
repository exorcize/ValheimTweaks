using UnityEngine;

namespace ValheimTweaks.Patches
{
    /// <summary>
    /// LOD bias -- o que faz item no chao, pedra e recurso sumirem de perto.
    ///
    /// ItemDrop.cs NAO tem nenhum codigo de distancia ou visibilidade. O sumico
    /// vem do LODGroup do Unity: cada prefab tem um ultimo nivel marcado como
    /// "Culled", e o objeto desaparece quando sua ALTURA EM TELA cai abaixo do
    /// limiar. Item no chao e pequeno, entao cruza o limiar pertissimo.
    ///
    /// QualitySettings.lodBias multiplica essa distancia. O jogo aplica assim
    /// (GraphicsSettingsManager.GetLodBias):
    ///     nivel 0 -> 1.0    nivel 1 -> 1.5    nivel 2 -> 2.0    nivel 3 -> 5.0
    /// O menu ("Nivel de detalhamento") para no 3. Aqui a gente passa disso.
    ///
    /// Custo: vale para TUDO (arvore, pedra, construcao), entao mexe em draw calls.
    /// No caso desta maquina a GPU esta ociosa e o gargalo e CPU de host, entao
    /// e justamente o tipo de coisa que sai barata.
    /// </summary>
    internal static class LodPatch
    {
        private static bool _subscribed;

        internal static void Apply()
        {
            if (!_subscribed)
            {
                GraphicsSettingsManager.GraphicsSettingsChanged += ApplyNow;
                _subscribed = true;
            }

            ApplyNow();
        }

        private static void ApplyNow()
        {
            float bias = ModConfig.LodBiasOverride.Value;
            if (bias <= 0f) return; // 0 = nao sobrescrever, deixa o jogo mandar

            if (!Mathf.Approximately(QualitySettings.lodBias, bias))
            {
                Plugin.Log.LogInfo($"lodBias: {QualitySettings.lodBias} -> {bias}");
                QualitySettings.lodBias = bias;
            }
        }
    }
}
