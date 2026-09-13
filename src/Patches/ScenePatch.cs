namespace ValheimTweaks.Patches
{
    /// <summary>
    /// Trava hora do dia e clima -- cena controlada para comparacao visual.
    ///
    /// Motivacao direta: duas medicoes minhas foram invalidadas hoje porque a cena
    /// mudou no meio (a nevoa se dissipou e o SSAO apareceu "acelerando" o frame,
    /// o que e impossivel). Sem travar hora e clima, qualquer A/B visual ou de
    /// custo esta comparando cenas diferentes.
    ///
    /// E puramente LOCAL. EnvMan so usa isto para o render:
    ///
    ///     // EnvMan.cs:353
    ///     if (m_debugTimeOfDay) m_smoothDayFraction = m_debugTime;
    ///
    /// Nao toca em m_totalSeconds (o tempo real do mundo), nao vai para os peers,
    /// nao marca o mundo. Os campos sao publicos, entao nao precisa de reflection.
    ///
    /// m_debugTime: 0 = meia-noite, 0.25 = amanhecer, 0.5 = meio-dia,
    ///              0.75 = entardecer.
    /// </summary>
    internal static class ScenePatch
    {
        private static string _climaAplicado;

        internal static void Apply()
        {
            var env = EnvMan.instance;
            if (env == null) return; // fora de mundo; reaplica no proximo load

            bool travar = ModConfig.FreezeTimeOfDay.Value;
            if (env.m_debugTimeOfDay != travar)
            {
                env.m_debugTimeOfDay = travar;
                Plugin.Log.LogInfo($"Hora do dia {(travar ? "TRAVADA" : "liberada")}");
            }
            if (travar) env.m_debugTime = ModConfig.TimeOfDay.Value;

            string clima = ModConfig.ForceWeather.Value?.Trim() ?? "";
            if (clima != _climaAplicado)
            {
                _climaAplicado = clima;
                env.SetForceEnvironment(clima);
                Plugin.Log.LogInfo(clima.Length > 0
                    ? $"Clima forcado: {clima}"
                    : "Clima liberado (volta ao normal do bioma)");
            }
        }
    }
}
