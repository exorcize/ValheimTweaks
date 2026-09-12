using BepInEx.Configuration;

namespace ValheimTweaks
{
    /// <summary>
    /// Todas as opcoes do mod em um lugar so.
    ///
    /// As secoes sao numeradas porque o ConfigurationManager (F1) ordena
    /// alfabeticamente -- sem o numero a ordem fica aleatoria.
    ///
    /// Toda entrada com AcceptableValueRange vira SLIDER no F1.
    /// Toda entrada bool vira SWITCH. Enum vira dropdown.
    /// </summary>
    internal static class ModConfig
    {
        // ---------- 00 - Geral ----------
        internal static ConfigEntry<bool> HotReload;

        // ---------- 01 - Diagnostico ----------
        internal static ConfigEntry<bool> DumpOnWorldLoad;
        internal static ConfigEntry<bool> DumpNow;

        // ---------- 02 - Rede ----------
        internal static ConfigEntry<bool> TimeoutEnabled;
        internal static ConfigEntry<float> TimeoutSeconds;

        // ---------- 03 - Visual ----------
        internal static ConfigEntry<bool> ForceAnisotropic;
        internal static ConfigEntry<int> AnisotropicLevel;
        internal static ConfigEntry<bool> FullResTextures;

        internal static void Init(ConfigFile cfg)
        {
            HotReload = cfg.Bind("00 - Geral", "HotReload", true,
                "Recarrega este arquivo automaticamente quando ele muda no disco e " +
                "reaplica tudo sem fechar o jogo. Deixe ligado durante os testes.");

            // --- Diagnostico ---
            DumpOnWorldLoad = cfg.Bind("01 - Diagnostico", "DumpOnWorldLoad", true,
                "Ao entrar no mundo, escreve no log o estado real do pipeline de render " +
                "(rendering path da camera, MSAA, anisotropico, LOD, sombras, sim distance). " +
                "E assim que a gente descobre o que o jogo realmente esta fazendo.");

            DumpNow = cfg.Bind("01 - Diagnostico", "DumpNow", false,
                "Marque para forcar um dump agora. Volta sozinho para false depois de rodar.");

            // --- Rede ---
            // ZNet.Start() chama ZRpc.SetLongTimeout(false), que trava m_timeout em 30s.
            // (o ctor estatico de ZRpc usa 600s, mas e sobrescrito no boot)
            TimeoutEnabled = cfg.Bind("02 - Rede", "TimeoutEnabled", true,
                "Sobrescreve o timeout de RPC do Valheim. " +
                "PRECISA estar ativo nas DUAS pontas: quem nao tiver o mod derruba a conexao " +
                "pelo lado dele no tempo padrao.");

            TimeoutSeconds = cfg.Bind("02 - Rede", "TimeoutSeconds", 90f,
                new ConfigDescription(
                    "Segundos sem ping antes de fechar o socket. Padrao do jogo: 30 (Steam) / 90 (crossplay).",
                    new AcceptableValueRange<float>(30f, 600f)));

            // --- Visual ---
            // O jogo tem a opcao "texturas anisotropicas" no menu, salva ela nas prefs,
            // e NUNCA a aplica: nao existe QualitySettings.anisotropicFiltering em
            // lugar nenhum do assembly_valheim.dll. Este bloco corrige isso.
            ForceAnisotropic = cfg.Bind("03 - Visual", "ForceAnisotropic", true,
                "Forca filtro anisotropico. O jogo tem a opcao no menu mas nunca a aplica " +
                "(bug do proprio Valheim). Deixa chao, estrada e piso nitidos em angulo raso. " +
                "Custo praticamente zero.");

            AnisotropicLevel = cfg.Bind("03 - Visual", "AnisotropicLevel", 16,
                new ConfigDescription(
                    "Nivel de anisotropia (1 = desligado, 16 = maximo).",
                    new AcceptableValueRange<int>(1, 16)));

            FullResTextures = cfg.Bind("03 - Visual", "FullResTextures", true,
                "Garante mipmap limit 0 (textura em resolucao cheia). " +
                "So tem efeito se algo tiver reduzido isso.");
        }
    }
}
