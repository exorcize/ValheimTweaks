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
        internal static ConfigEntry<bool> FogEnabled;
        internal static ConfigEntry<float> FogDensityScale;
        internal static ConfigEntry<float> AmbientBrightness;
        internal static ConfigEntry<bool> TrilightAmbient;
        internal static ConfigEntry<float> LodBiasOverride;
        internal static ConfigEntry<bool> SsaoOverride;
        internal static ConfigEntry<int> SsaoSampleCount;
        internal static ConfigEntry<bool> SsaoFullResolution;
        internal static ConfigEntry<float> SsaoIntensity;
        internal static ConfigEntry<float> SsaoRadius;
        internal static ConfigEntry<float> SsaoPower;

        // ---------- 04 - Performance ----------
        internal static ConfigEntry<bool> SimDistanceEnabled;
        internal static ConfigEntry<int> SimDistanceNear;
        internal static ConfigEntry<int> SimDistanceFar;

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

            // Medido no LAB: fogDensity 0,03 Exponential + ambiente Flat cinza 0,382.
            // E dai que vem o aspecto lavado.
            FogEnabled = cfg.Bind("03 - Visual", "FogEnabled", true,
                "Desmarcar remove a nevoa por completo. Fica artificial, mas serve " +
                "para enxergar quanto do visual e nevoa.");

            FogDensityScale = cfg.Bind("03 - Visual", "FogDensityScale", 1.0f,
                new ConfigDescription(
                    "Multiplica a densidade da nevoa de TODO bioma e hora do dia. " +
                    "1.0 = vanilla. 0.5 = metade (horizonte abre sem perder o clima).",
                    new AcceptableValueRange<float>(0f, 2f)));

            AmbientBrightness = cfg.Bind("03 - Visual", "AmbientBrightness", 1.0f,
                new ConfigDescription(
                    "Multiplica a luz ambiente. Acima de 1 clareia as sombras (menos " +
                    "'buraco preto' em interior), abaixo de 1 aumenta o contraste.",
                    new AcceptableValueRange<float>(0.25f, 2f)));

            TrilightAmbient = cfg.Bind("03 - Visual", "TrilightAmbient", false,
                "EXPERIMENTAL. O jogo usa AmbientMode.Flat (uma cor vinda de todo lado, " +
                "o mais chapado). Trilight separa ceu/horizonte/chao e da volume. " +
                "Pode brigar com os shaders custom do Valheim -- teste antes de manter.");

            // O que faz item no chao, pedra e recurso sumirem de perto: LODGroup +
            // lodBias. O menu ("Nivel de detalhamento") mapeia 0:1.0 1:1.5 2:2.0 3:5.0
            // e para por ai. Ver LodPatch.
            LodBiasOverride = cfg.Bind("03 - Visual", "LodBiasOverride", 0f,
                new ConfigDescription(
                    "Sobrescreve o LOD bias. 0 = nao sobrescrever (usa o menu do jogo). " +
                    "E ISTO que controla a que distancia item no chao, recurso, pedra e " +
                    "arvore somem -- a distancia e LINEAR no valor (dobrar = dobrar o alcance). " +
                    "Menu do jogo no maximo = 5.0. Acima disso, so aqui.",
                    new AcceptableValueRange<float>(0f, 20f)));

            // CameraEffects.SetSSAO deixa Downsample = true HARDCODED nos dois niveis
            // e limita o teto a Medium. Aqui destrava. Ver SsaoPatch.
            SsaoOverride = cfg.Bind("03 - Visual", "SsaoOverride", true,
                "Assume o controle da oclusao de ambiente (sombra de contato). " +
                "LIGA o efeito mesmo se o menu do jogo estiver com ele desligado.");

            SsaoSampleCount = cfg.Bind("03 - Visual", "SsaoSampleCount", 2,
                new ConfigDescription(
                    "0=Low 1=Medium 2=High 3=VeryHigh. O menu do jogo nunca passa de Medium.",
                    new AcceptableValueRange<int>(0, 3)));

            SsaoFullResolution = cfg.Bind("03 - Visual", "SsaoFullResolution", true,
                "Roda o SSAO em resolucao cheia. O jogo forca meia resolucao SEMPRE, " +
                "ate no nivel mais alto do menu.");

            SsaoIntensity = cfg.Bind("03 - Visual", "SsaoIntensity", 1.0f,
                new ConfigDescription("Forca da oclusao.", new AcceptableValueRange<float>(0f, 3f)));

            SsaoRadius = cfg.Bind("03 - Visual", "SsaoRadius", 2.0f,
                new ConfigDescription(
                    "Raio em metros. Pequeno = so cantos; grande = sombreado amplo.",
                    new AcceptableValueRange<float>(0.25f, 8f)));

            SsaoPower = cfg.Bind("03 - Visual", "SsaoPower", 1.8f,
                new ConfigDescription(
                    "Curva de contraste da oclusao. Acima escurece mais o nucleo.",
                    new AcceptableValueRange<float>(0.5f, 6f)));

            // O maior custo de CPU do host. Ver SimulationDistancePatch para a matematica.
            SimDistanceEnabled = cfg.Bind("04 - Performance", "SimDistanceEnabled", true,
                "Sobrescreve a simulation distance. No HOST isso vira o teto de todos os " +
                "jogadores (o jogo ja sincroniza; cliente so pode ficar abaixo).");

            // NEAR = onde as CRIATURAS existem.
            // ZDOMan.FindSectorObjects usa FindObjects() no anel 1..near (todos os ZDOs)
            // e FindDistantObjects() no anel ate total (SO os ZDOs com ZNetView.m_distant,
            // que e cenario estatico). Inimigo e animal nunca sao "distant" -- entao a
            // distancia em que voce ENXERGA bicho e exatamente o near.
            //   distancia = near * 64 + 32   (ZoneSystem.m_zoneSize = 64)
            //   near 2:160m  3:224m  4:288m  5:352m  6:416m  7:480m  8:544m
            // Custo: zonas ativas = (2n+1)^2, e o host paga por peer conectado.
            SimDistanceNear = cfg.Bind("04 - Performance", "SimDistanceNear", 5,
                new ConfigDescription(
                    "Raio de zonas SIMULADAS. E isto que define a que distancia voce ve " +
                    "inimigos e animais: near*64+32 metros. " +
                    "2:160m(25 zonas)  3:224m(49)  4:288m(81)  5:352m(121)  6:416m(169)  7:480m(225). " +
                    "O menu do jogo para no 6; acima disso e so pelo mod. Caro para o host.",
                    new AcceptableValueRange<int>(1, 8)));

            SimDistanceFar = cfg.Bind("04 - Performance", "SimDistanceFar", 2,
                new ConfigDescription(
                    "Anel extra de 'ghost zones' alem do near. So instancia objetos marcados " +
                    "como distant (cenario estatico) -- SEM criaturas e SEM simulacao, entao e " +
                    "BARATO. Estende so o cenario ao longe. Vanilla = 2.",
                    new AcceptableValueRange<int>(0, 6)));
        }
    }
}
