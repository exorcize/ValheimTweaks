using UnityEngine;

namespace ValheimTweaks.Patches
{
    /// <summary>
    /// Display mode -- the most likely explanation for "good numbers, bad feel".
    ///
    /// The game menu CANNOT set exclusive fullscreen:
    ///
    ///     // Valheim.SettingsGui/GraphicsSettings.cs:1039
    ///     Screen.SetResolution(w, h,
    ///         m_fullscreenToggle.isOn ? FullScreenMode.FullScreenWindow
    ///                                 : FullScreenMode.Windowed,
    ///         resolution.refreshRateRatio);
    ///
    /// The button toggles between Windowed and FullScreenWindow (borderless). Both
    /// go through DWM composition. There is a hidden console command
    /// ("exclusivefullscreen", and it is not a cheat), but it depends on the console being enabled.
    ///
    /// ---- Why this becomes perceived stutter ----
    /// Measured: PresentMode = "Composed: Flip" in 100% of frames, that is, the DWM
    /// composes. At ~209 fps on a 360 Hz panel:
    ///
    ///     360 / 209 = 1.72 screen updates per rendered frame
    ///
    /// Since you cannot show 1.72 of a time, each frame appears for 1 OR 2 refreshes,
    /// alternating irregularly. This is JUDDER: the motion moves and stops, and
    /// the frametime can be perfectly smooth while it happens. It is exactly
    /// the outcome of the CS2 case -- great frametime, bad feel.
    ///
    /// Exclusive fullscreen takes the DWM out of the path (independent flip): the GPU sends
    /// directly to the screen. It allows tearing, but motion becomes continuous.
    ///
    /// Alternative without exclusive: cap fps to a divisor of the refresh (180 = 360/2,
    /// or 120 = 360/3). Each frame then lasts a whole number of refreshes and
    /// the judder disappears. It stays as an option because the user prefers high fps.
    /// </summary>
    internal static class DisplayModePatch
    {
        internal static void Apply()
        {
            int modo = ModConfig.DisplayMode.Value;
            if (modo <= 0) return; // 0 = do not touch

            FullScreenMode alvo = modo switch
            {
                1 => FullScreenMode.ExclusiveFullScreen,
                2 => FullScreenMode.FullScreenWindow,
                _ => FullScreenMode.Windowed,
            };

            if (Screen.fullScreenMode == alvo) return;

            // In exclusive, the resolution must be a real monitor mode. The current
            // window (e.g. 1267x1048) is not -- so we use the desktop one.
            var res = Screen.currentResolution;
            int w = ModConfig.DisplayWidth.Value > 0 ? ModConfig.DisplayWidth.Value : res.width;
            int h = ModConfig.DisplayHeight.Value > 0 ? ModConfig.DisplayHeight.Value : res.height;

            Plugin.Log.LogInfo(
                $"Display mode: {Screen.fullScreenMode} ({Screen.width}x{Screen.height}) " +
                $"-> {alvo} ({w}x{h} @ {res.refreshRateRatio.value:0.##}Hz)");

            Screen.SetResolution(w, h, alvo, res.refreshRateRatio);
        }
    }
}
