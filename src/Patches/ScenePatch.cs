namespace ValheimTweaks.Patches
{
    /// <summary>
    /// Freezes time of day and weather -- controlled scene for visual comparison.
    ///
    /// Direct motivation: two of my measurements were invalidated today because
    /// the scene changed midway (the fog dissipated and SSAO appeared to "speed
    /// up" the frame, which is impossible). Without freezing time and weather, any
    /// visual or cost A/B is comparing different scenes.
    ///
    /// It is purely LOCAL. EnvMan only uses this for rendering:
    ///
    ///     // EnvMan.cs:353
    ///     if (m_debugTimeOfDay) m_smoothDayFraction = m_debugTime;
    ///
    /// Doesn't touch m_totalSeconds (the world's real time), doesn't go to the
    /// peers, doesn't mark the world. The fields are public, so no reflection is
    /// needed.
    ///
    /// m_debugTime: 0 = midnight, 0.25 = dawn, 0.5 = noon,
    ///              0.75 = dusk.
    /// </summary>
    internal static class ScenePatch
    {
        private static string _appliedWeather;

        internal static void Apply()
        {
            var env = EnvMan.instance;
            if (env == null) return; // out of world; reapplies on the next load

            bool freeze = ModConfig.FreezeTimeOfDay.Value;
            if (env.m_debugTimeOfDay != freeze)
            {
                env.m_debugTimeOfDay = freeze;
                Plugin.Log.LogInfo($"Time of day {(freeze ? "FROZEN" : "released")}");
            }
            if (freeze) env.m_debugTime = ModConfig.TimeOfDay.Value;

            string weather = ModConfig.ForceWeather.Value?.Trim() ?? "";
            if (weather != _appliedWeather)
            {
                _appliedWeather = weather;
                env.SetForceEnvironment(weather);
                Plugin.Log.LogInfo(weather.Length > 0
                    ? $"Weather forced: {weather}"
                    : "Weather released (back to the biome's normal)");
            }
        }
    }
}
