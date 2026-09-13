using UnityEngine;

namespace ValheimTweaks.Patches
{
    /// <summary>
    /// Modo de tela -- a explicacao mais provavel para "numeros bons, sensacao ruim".
    ///
    /// O menu do jogo NAO consegue por em tela cheia exclusiva:
    ///
    ///     // Valheim.SettingsGui/GraphicsSettings.cs:1039
    ///     Screen.SetResolution(w, h,
    ///         m_fullscreenToggle.isOn ? FullScreenMode.FullScreenWindow
    ///                                 : FullScreenMode.Windowed,
    ///         resolution.refreshRateRatio);
    ///
    /// O botao alterna entre Windowed e FullScreenWindow (borderless). Os dois
    /// passam pela composicao do DWM. Existe um comando de console escondido
    /// ("exclusivefullscreen", e nao e cheat), mas depende do console habilitado.
    ///
    /// ---- Por que isso vira travada percebida ----
    /// Medido: PresentMode = "Composed: Flip" em 100% dos frames, ou seja, o DWM
    /// compoe. Com ~209 fps num painel de 360 Hz:
    ///
    ///     360 / 209 = 1,72 atualizacoes de tela por frame renderizado
    ///
    /// Como nao da para mostrar 1,72 vez, cada frame aparece por 1 OU 2 refreshes,
    /// alternando de forma irregular. Isso e JUDDER: o movimento anda e para, e
    /// o frametime pode estar perfeitamente liso enquanto acontece. E exatamente
    /// o desfecho do caso do CS2 -- frametime otimo, sensacao ruim.
    ///
    /// Tela cheia exclusiva tira o DWM do caminho (independent flip): a GPU manda
    /// direto para a tela. Permite tearing, mas o movimento fica continuo.
    ///
    /// Alternativa sem exclusiva: limitar fps a um divisor do refresh (180 = 360/2,
    /// ou 120 = 360/3). Cada frame passa a durar um numero inteiro de refreshes e
    /// o judder some. Fica como opcao porque o usuario prefere fps alto.
    /// </summary>
    internal static class DisplayModePatch
    {
        internal static void Apply()
        {
            int modo = ModConfig.DisplayMode.Value;
            if (modo <= 0) return; // 0 = nao mexer

            FullScreenMode alvo = modo switch
            {
                1 => FullScreenMode.ExclusiveFullScreen,
                2 => FullScreenMode.FullScreenWindow,
                _ => FullScreenMode.Windowed,
            };

            if (Screen.fullScreenMode == alvo) return;

            // Em exclusiva a resolucao precisa ser um modo real do monitor. A janela
            // atual (ex.: 1267x1048) nao e -- por isso usamos a do desktop.
            var res = Screen.currentResolution;
            int w = ModConfig.DisplayWidth.Value > 0 ? ModConfig.DisplayWidth.Value : res.width;
            int h = ModConfig.DisplayHeight.Value > 0 ? ModConfig.DisplayHeight.Value : res.height;

            Plugin.Log.LogInfo(
                $"Modo de tela: {Screen.fullScreenMode} ({Screen.width}x{Screen.height}) " +
                $"-> {alvo} ({w}x{h} @ {res.refreshRateRatio.value:0.##}Hz)");

            Screen.SetResolution(w, h, alvo, res.refreshRateRatio);
        }
    }
}
