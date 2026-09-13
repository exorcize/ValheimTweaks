using BepInEx.Configuration;
using UnityEngine;

namespace ValheimTweaks
{
    /// <summary>
    /// Opções do mod. As seções são numeradas porque o Configuration Manager
    /// ordena alfabeticamente.
    ///
    /// Convenções usadas nas opções:
    ///   float 0   = não sobrescrever (usa o que o menu do jogo definir)
    ///   int  -2   = não sobrescrever, -1 = sem limite (nas opções de luz)
    /// </summary>
    internal static class ModConfig
    {
        // ---------- 00 - Geral ----------
        internal static ConfigEntry<bool> HotReload;

        // ---------- 01 - Diagnostico ----------
        internal static ConfigEntry<bool> DumpOnWorldLoad;
        internal static ConfigEntry<bool> DumpNow;
        internal static ConfigEntry<bool> ProfileNow;
        internal static ConfigEntry<float> ProfileSeconds;
        internal static ConfigEntry<bool> ProfileSystems;

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
        internal static ConfigEntry<bool> AaOverride;
        internal static ConfigEntry<bool> AaUseTaa;
        internal static ConfigEntry<int> FxaaPreset;
        internal static ConfigEntry<float> TaaJitterSpread;
        internal static ConfigEntry<float> TaaSharpen;
        internal static ConfigEntry<float> TaaStationaryBlending;
        internal static ConfigEntry<float> TaaMotionBlending;
        internal static ConfigEntry<float> ShadowDistance;
        internal static ConfigEntry<int> ShadowCascades;
        internal static ConfigEntry<int> PointLightLimit;
        internal static ConfigEntry<int> PointLightShadowLimit;
        internal static ConfigEntry<float> ClutterDistance;
        internal static ConfigEntry<float> FieldOfView;
        internal static ConfigEntry<int> ShadowResolution;
        internal static ConfigEntry<int> Tesselation;
        internal static ConfigEntry<float> ClutterAmountScale;
        internal static ConfigEntry<bool> FreezeTimeOfDay;
        internal static ConfigEntry<float> TimeOfDay;
        internal static ConfigEntry<string> ForceWeather;
        internal static ConfigEntry<int> DisplayMode;
        internal static ConfigEntry<int> DisplayWidth;
        internal static ConfigEntry<int> DisplayHeight;

        // ---------- 04 - Performance ----------
        internal static ConfigEntry<int> MaxQueuedFrames;
        internal static ConfigEntry<float> ZoneGenBudgetMs;
        internal static ConfigEntry<float> ZdoReleaseIntervalSec;
        internal static ConfigEntry<float> GcSliceMs;
        internal static ConfigEntry<int> MaxSmoke;
        internal static ConfigEntry<bool> FadeDistantSmokeFirst;
        internal static ConfigEntry<float> TeleportSpeed;
        internal static ConfigEntry<int> TeleportSimDistance;
        internal static ConfigEntry<BepInEx.Configuration.KeyboardShortcut> StoreMarkKey;
        internal static ConfigEntry<BepInEx.Configuration.KeyboardShortcut> StoreMarkedKey;
        internal static ConfigEntry<BepInEx.Configuration.KeyboardShortcut> StoreHoveredKey;
        internal static ConfigEntry<float> StoreRadius;
        internal static ConfigEntry<bool> StoreFallbackAnyChest;
        internal static ConfigEntry<bool> StoreHudEnabled;
        internal static ConfigEntry<float> StoreHudX;
        internal static ConfigEntry<float> StoreHudY;
        internal static ConfigEntry<float> StoreHudSeconds;
        internal static ConfigEntry<int> StoreHudMaxLines;
        internal static ConfigEntry<float> StoreHudFontSize;
        internal static ConfigEntry<bool> AutoRepairOnOpen;
        internal static ConfigEntry<bool> RepairButtonRepairsAll;
        internal static ConfigEntry<bool> SimDistanceEnabled;
        internal static ConfigEntry<int> SimDistanceNear;
        internal static ConfigEntry<int> SimDistanceFar;

        internal static void Init(ConfigFile cfg)
        {
            HotReload = cfg.Bind("00 - Geral", "HotReload", true,
                "Aplica mudanças deste arquivo na hora, sem precisar fechar o jogo.");

            // ------------------------------------------------------------------
            // 01 - Diagnóstico
            // ------------------------------------------------------------------
            DumpOnWorldLoad = cfg.Bind("01 - Diagnostico", "DumpOnWorldLoad", true,
                "Ao entrar no mundo, anota no log um resumo das configurações de vídeo em uso.");

            DumpNow = cfg.Bind("01 - Diagnostico", "DumpNow", false,
                "Gera esse resumo agora. Desmarca sozinho depois.");

            ProfileNow = cfg.Bind("01 - Diagnostico", "ProfileNow", false,
                "Mede o desempenho pelos próximos segundos e escreve o resultado no log. " +
                "Desmarca sozinho depois.");

            ProfileSeconds = cfg.Bind("01 - Diagnostico", "ProfileSeconds", 20f,
                new ConfigDescription("Duração da medição, em segundos.",
                    new AcceptableValueRange<float>(5f, 120f)));

            ProfileSystems = cfg.Bind("01 - Diagnostico", "ProfileSystems", true,
                "Inclui na medição o tempo que cada parte do jogo consome por quadro.");

            // ------------------------------------------------------------------
            // 02 - Rede
            // ------------------------------------------------------------------
            TimeoutEnabled = cfg.Bind("02 - Rede", "TimeoutEnabled", true,
                "Ajusta quanto tempo o jogo aguenta uma conexão sem resposta antes de " +
                "derrubar. Todos no servidor precisam do mod e do mesmo valor, porque cada " +
                "um encerra a própria conexão.");

            TimeoutSeconds = cfg.Bind("02 - Rede", "TimeoutSeconds", 90f,
                new ConfigDescription(
                    "Segundos até desistir de uma conexão parada. O jogo usa 30.",
                    new AcceptableValueRange<float>(30f, 600f)));

            // ------------------------------------------------------------------
            // 03 - Visual
            // ------------------------------------------------------------------
            ForceAnisotropic = cfg.Bind("03 - Visual", "ForceAnisotropic", true,
                "Deixa chão, estradas e pisos nítidos quando vistos de ângulo, em vez de " +
                "borrados. Custo quase zero.");

            AnisotropicLevel = cfg.Bind("03 - Visual", "AnisotropicLevel", 16,
                new ConfigDescription("Intensidade do filtro. 1 desliga, 16 é o máximo.",
                    new AcceptableValueRange<int>(1, 16)));

            FullResTextures = cfg.Bind("03 - Visual", "FullResTextures", true,
                "Mantém as texturas em resolução cheia.");

            FogEnabled = cfg.Bind("03 - Visual", "FogEnabled", true,
                "Desmarcar remove a névoa por completo. Fica artificial, mas serve para ver " +
                "o quanto dela está escondendo a paisagem.");

            FogDensityScale = cfg.Bind("03 - Visual", "FogDensityScale", 1.0f,
                new ConfigDescription(
                    "Quanta névoa o jogo desenha. 1.0 é o padrão; abaixo disso o horizonte " +
                    "abre e as cores ao longe param de lavar. Não custa desempenho.",
                    new AcceptableValueRange<float>(0f, 2f)));

            AmbientBrightness = cfg.Bind("03 - Visual", "AmbientBrightness", 1.0f,
                new ConfigDescription(
                    "Clareia ou escurece a luz ambiente. Acima de 1 as sombras ficam menos " +
                    "fechadas; abaixo de 1 aumenta o contraste.",
                    new AcceptableValueRange<float>(0.25f, 2f)));

            TrilightAmbient = cfg.Bind("03 - Visual", "TrilightAmbient", false,
                "Luz ambiente vinda de céu, horizonte e chão separadamente, em vez de uma cor " +
                "só. Dá mais volume aos objetos, mas pode mudar o tom das cenas. Experimental.");

            LodBiasOverride = cfg.Bind("03 - Visual", "LodBiasOverride", 0f,
                new ConfigDescription(
                    "Distância em que itens no chão, pedras e detalhes deixam de ser " +
                    "desenhados. Dobrar o valor dobra a distância, e cobra desempenho na " +
                    "mesma medida. 0 usa o que estiver no menu do jogo.",
                    new AcceptableValueRange<float>(0f, 20f)));

            SsaoOverride = cfg.Bind("03 - Visual", "SsaoOverride", true,
                "Sombra suave onde os objetos encostam no chão e nos cantos. É o que tira o " +
                "aspecto de coisa colada por cima do cenário.");

            SsaoSampleCount = cfg.Bind("03 - Visual", "SsaoSampleCount", 2,
                new ConfigDescription(
                    "Qualidade da sombra de contato: 0 baixa, 1 média, 2 alta, 3 muito alta.",
                    new AcceptableValueRange<int>(0, 3)));

            SsaoFullResolution = cfg.Bind("03 - Visual", "SsaoFullResolution", true,
                "Calcula a sombra de contato em resolução cheia. Fica mais definida e custa " +
                "mais GPU. Desmarque se precisar economizar.");

            SsaoIntensity = cfg.Bind("03 - Visual", "SsaoIntensity", 1.0f,
                new ConfigDescription("Força da sombra de contato.",
                    new AcceptableValueRange<float>(0f, 3f)));

            SsaoRadius = cfg.Bind("03 - Visual", "SsaoRadius", 2.0f,
                new ConfigDescription(
                    "Alcance da sombra, em metros. Pequeno marca só os cantos; grande " +
                    "sombreia áreas inteiras.",
                    new AcceptableValueRange<float>(0.25f, 8f)));

            SsaoPower = cfg.Bind("03 - Visual", "SsaoPower", 1.8f,
                new ConfigDescription("Contraste da sombra. Mais alto escurece o miolo.",
                    new AcceptableValueRange<float>(0.5f, 6f)));

            AaOverride = cfg.Bind("03 - Visual", "AaOverride", true,
                "Controla o anti-serrilhado, que o jogo só liga e desliga sem deixar " +
                "escolher a qualidade.");

            AaUseTaa = cfg.Bind("03 - Visual", "AaUseTaa", false,
                "Usa TAA no lugar do FXAA. Bordas bem mais limpas em folhagem, cordas e " +
                "cercas, mas pode deixar rastro atrás de coisas em movimento.");

            FxaaPreset = cfg.Bind("03 - Visual", "FxaaPreset", 4,
                new ConfigDescription(
                    "Qualidade do FXAA, de 0 (mais rápido) a 4 (mais limpo). Só vale com " +
                    "AaUseTaa desmarcado.",
                    new AcceptableValueRange<int>(0, 4)));

            TaaJitterSpread = cfg.Bind("03 - Visual", "TaaJitterSpread", 0.75f,
                new ConfigDescription("Suavização do TAA. Maior suaviza mais e borra mais.",
                    new AcceptableValueRange<float>(0.1f, 1f)));

            TaaSharpen = cfg.Bind("03 - Visual", "TaaSharpen", 0.3f,
                new ConfigDescription("Compensa o borrão do TAA. Demais cria halo nas bordas.",
                    new AcceptableValueRange<float>(0f, 3f)));

            TaaStationaryBlending = cfg.Bind("03 - Visual", "TaaStationaryBlending", 0.95f,
                new ConfigDescription("Estabilidade da imagem com a câmera parada.",
                    new AcceptableValueRange<float>(0f, 0.99f)));

            TaaMotionBlending = cfg.Bind("03 - Visual", "TaaMotionBlending", 0.85f,
                new ConfigDescription("Baixe este se aparecer rastro atrás do que se move.",
                    new AcceptableValueRange<float>(0f, 0.99f)));

            ShadowDistance = cfg.Bind("03 - Visual", "ShadowDistance", 0f,
                new ConfigDescription(
                    "Até onde as sombras do sol aparecem, em metros. O menu do jogo vai até " +
                    "150. 0 usa o menu.",
                    new AcceptableValueRange<float>(0f, 500f)));

            ShadowCascades = cfg.Bind("03 - Visual", "ShadowCascades", 0,
                new ConfigDescription(
                    "Divisões da sombra do sol: mais divisões deixam a sombra nítida também " +
                    "ao longe. Aceita 1, 2 ou 4. 0 usa o menu.",
                    new AcceptableValueRange<int>(0, 4)));

            ShadowResolution = cfg.Bind("03 - Visual", "ShadowResolution", 0,
                new ConfigDescription(
                    "Nitidez das sombras: 1 baixa, 2 média, 3 alta, 4 muito alta. O menu do " +
                    "jogo para em alta. 0 usa o menu.",
                    new AcceptableValueRange<int>(0, 4)));

            PointLightLimit = cfg.Bind("03 - Visual", "PointLightLimit", -2,
                new ConfigDescription(
                    "Quantas tochas e fogueiras ficam acesas ao mesmo tempo. O jogo oferece " +
                    "4, 15, 40 ou sem limite. Aqui dá para pôr qualquer número. " +
                    "-2 usa o menu, -1 é sem limite.",
                    new AcceptableValueRange<int>(-2, 64)));

            PointLightShadowLimit = cfg.Bind("03 - Visual", "PointLightShadowLimit", -2,
                new ConfigDescription(
                    "Quantas dessas luzes projetam sombra. É o ajuste mais pesado numa base " +
                    "cheia de fogo. O jogo oferece 0, 1, 3 ou sem limite; algo entre 6 e 8 " +
                    "costuma ser o meio-termo. -2 usa o menu, -1 é sem limite.",
                    new AcceptableValueRange<int>(-2, 32)));

            ClutterDistance = cfg.Bind("03 - Visual", "ClutterDistance", 0f,
                new ConfigDescription(
                    "Até onde a grama é desenhada, em metros. O jogo usa 45, e é por isso que " +
                    "a grama parece nascer à sua frente enquanto anda. Aumentar afasta esse " +
                    "limite, mas a área cresce ao quadrado e cobra caro. 0 usa o padrão.",
                    new AcceptableValueRange<float>(0f, 150f)));

            ClutterAmountScale = cfg.Bind("03 - Visual", "ClutterAmountScale", 0f,
                new ConfigDescription(
                    "Densidade da grama, independente do alcance. 2.0 dobra a quantidade. " +
                    "0 usa o padrão.",
                    new AcceptableValueRange<float>(0f, 4f)));

            Tesselation = cfg.Bind("03 - Visual", "Tesselation", 0,
                new ConfigDescription(
                    "Dá relevo de verdade ao terreno em vez de textura plana. Só pesa na GPU. " +
                    "0 usa o menu, 1 liga, 2 desliga.",
                    new AcceptableValueRange<int>(0, 2)));

            FieldOfView = cfg.Bind("03 - Visual", "FieldOfView", 0f,
                new ConfigDescription(
                    "Campo de visão. O jogo usa 65 e não deixa mudar. Acima de 100 as bordas " +
                    "distorcem. 0 mantém o padrão.",
                    new AcceptableValueRange<float>(0f, 120f)));

            DisplayMode = cfg.Bind("03 - Visual", "DisplayMode", 0,
                new ConfigDescription(
                    "Modo de janela: 1 tela cheia exclusiva, 2 sem bordas, 3 janela. " +
                    "A exclusiva costuma deixar o movimento mais suave, e o menu do jogo não " +
                    "oferece essa opção. 0 mantém como está.",
                    new AcceptableValueRange<int>(0, 3)));

            DisplayWidth = cfg.Bind("03 - Visual", "DisplayWidth", 0,
                new ConfigDescription("Largura. 0 usa a resolução da área de trabalho.",
                    new AcceptableValueRange<int>(0, 7680)));

            DisplayHeight = cfg.Bind("03 - Visual", "DisplayHeight", 0,
                new ConfigDescription("Altura. 0 usa a resolução da área de trabalho.",
                    new AcceptableValueRange<int>(0, 4320)));

            FreezeTimeOfDay = cfg.Bind("03 - Visual", "FreezeTimeOfDay", false,
                "Trava a hora do dia na sua tela, útil para comparar ajustes sem o sol se " +
                "mexer. Vale só para você: o horário do mundo e dos outros jogadores segue " +
                "normal.");

            TimeOfDay = cfg.Bind("03 - Visual", "TimeOfDay", 0.5f,
                new ConfigDescription(
                    "Hora usada quando a anterior está marcada. 0 meia-noite, 0.25 amanhecer, " +
                    "0.5 meio-dia, 0.75 entardecer.",
                    new AcceptableValueRange<float>(0f, 1f)));

            ForceWeather = cfg.Bind("03 - Visual", "ForceWeather", "",
                "Trava o clima na sua tela. Vazio segue o normal do bioma. " +
                "Ex.: Clear, Misty, Rain, ThunderStorm, SnowStorm.");

            // ------------------------------------------------------------------
            // 04 - Performance
            // ------------------------------------------------------------------
            // ------------------------------------------------------------------
            // 05 - Conveniencia
            // ------------------------------------------------------------------
            TeleportSpeed = cfg.Bind("05 - Conveniencia", "TeleportSpeed", 4f,
                new ConfigDescription(
                    "Acelera o portal. O jogo impoe uma espera fixa de 8 segundos em viagem " +
                    "longa, mesmo quando o destino ja carregou; 4 transforma isso em 2 " +
                    "segundos. A verificacao de destino pronto continua valendo, entao some " +
                    "so a espera artificial, nunca a real. 1 mantem o padrao.",
                    new AcceptableValueRange<float>(1f, 8f)));

            TeleportSimDistance = cfg.Bind("05 - Conveniencia", "TeleportSimDistance", 2,
                new ConfigDescription(
                    "Reduz a distancia de simulacao SO durante a travessia do portal e " +
                    "restaura ao chegar. E de longe o maior ganho: o destino so libera " +
                    "quando a zona central termina de carregar, e com a distancia normal ela " +
                    "disputa a fila com mais de cem zonas. Medido: 15s no padrao contra 1,5s " +
                    "com 2 e 0,7s com 1. Valores menores sao mais rapidos mas podem deixar " +
                    "objetos aparecendo aos poucos ao chegar. 0 desliga. " +
                    "Nao afeta os outros jogadores.",
                    new AcceptableValueRange<int>(0, 8)));

            // --- Guardar nos baus proximos ---
            StoreMarkKey = cfg.Bind("05 - Conveniencia", "StoreMarkKey",
                new KeyboardShortcut(KeyCode.LeftAlt),
                "Com o inventario aberto, aponte um item e aperte esta tecla para marca-lo " +
                "como 'guardar nos baus'. O item marcado fica com o icone azulado e a marca " +
                "acompanha o item, mesmo se ele mudar de lugar.");

            StoreMarkedKey = cfg.Bind("05 - Conveniencia", "StoreMarkedKey",
                new KeyboardShortcut(KeyCode.Y),
                "Guarda de uma vez todos os itens marcados nos baus proximos.");

            StoreHoveredKey = cfg.Bind("05 - Conveniencia", "StoreHoveredKey",
                new KeyboardShortcut(KeyCode.LeftControl),
                "Guarda na hora apenas o item apontado, sem precisar marcar antes.");

            StoreRadius = cfg.Bind("05 - Conveniencia", "StoreRadius", 12f,
                new ConfigDescription(
                    "Distancia em metros para procurar baus.",
                    new AcceptableValueRange<float>(2f, 50f)));

            StoreFallbackAnyChest = cfg.Bind("05 - Conveniencia", "StoreFallbackAnyChest", false,
                "Por padrao um item so entra em bau que JA tenha aquele item, para nao " +
                "baguncar a organizacao. Ligando isto, o que sobrar vai parar em qualquer " +
                "bau com espaco.");

            StoreHudEnabled = cfg.Bind("05 - Conveniencia", "StoreHudEnabled", true,
                "Mostra no canto inferior esquerdo a lista do que foi guardado, com o icone " +
                "do item e, quando o bau tem nome proprio, uma seta e o nome.");

            StoreHudX = cfg.Bind("05 - Conveniencia", "StoreHudX", 20f,
                new ConfigDescription("Distancia da borda esquerda, em pixels.",
                    new AcceptableValueRange<float>(0f, 800f)));

            StoreHudY = cfg.Bind("05 - Conveniencia", "StoreHudY", 240f,
                new ConfigDescription(
                    "Altura a partir da borda de baixo, em pixels. O padrao ja passa por cima " +
                    "da HUD de vida e comida; ajuste se ficar sobreposto na sua resolucao.",
                    new AcceptableValueRange<float>(0f, 900f)));

            StoreHudSeconds = cfg.Bind("05 - Conveniencia", "StoreHudSeconds", 4f,
                new ConfigDescription("Quanto tempo cada linha fica na tela.",
                    new AcceptableValueRange<float>(1f, 20f)));

            StoreHudMaxLines = cfg.Bind("05 - Conveniencia", "StoreHudMaxLines", 8,
                new ConfigDescription("Maximo de linhas ao mesmo tempo.",
                    new AcceptableValueRange<int>(1, 20)));

            StoreHudFontSize = cfg.Bind("05 - Conveniencia", "StoreHudFontSize", 14f,
                new ConfigDescription("Tamanho da fonte da lista.",
                    new AcceptableValueRange<float>(8f, 28f)));

            AutoRepairOnOpen = cfg.Bind("05 - Conveniencia", "AutoRepairOnOpen", true,
                "Repara todo o equipamento gasto assim que voce abre o inventario perto de " +
                "uma bancada, forja ou estacao equivalente. Reparo no Valheim nao gasta " +
                "material, entao isso so poupa cliques.");

            RepairButtonRepairsAll = cfg.Bind("05 - Conveniencia", "RepairButtonRepairsAll", true,
                "O botao de reparo conserta tudo de uma vez em vez de um item por clique.");

            SimDistanceEnabled = cfg.Bind("04 - Performance", "SimDistanceEnabled", true,
                "Controla a distância em que o mundo continua ativo ao seu redor. Quem " +
                "hospeda define o teto para todos; os outros podem usar menos, nunca mais.");

            SimDistanceNear = cfg.Bind("04 - Performance", "SimDistanceNear", 5,
                new ConfigDescription(
                    "Distância em que inimigos, animais e objetos existem de fato. Cada passo " +
                    "aqui são 64 metros a mais e bem mais trabalho para quem hospeda: " +
                    "2 dá 160m, 3 dá 224m, 5 dá 352m, 6 dá 416m. O menu do jogo para no 6.",
                    new AcceptableValueRange<int>(1, 8)));

            SimDistanceFar = cfg.Bind("04 - Performance", "SimDistanceFar", 2,
                new ConfigDescription(
                    "Anel extra só de cenário ao fundo, sem criaturas e sem simulação. " +
                    "É barato: dá horizonte sem o custo do ajuste acima. O jogo usa 2.",
                    new AcceptableValueRange<int>(0, 6)));

            MaxQueuedFrames = cfg.Bind("04 - Performance", "MaxQueuedFrames", 0,
                new ConfigDescription(
                    "Quadros preparados com antecedência. 1 responde mais rápido ao mouse; " +
                    "2 é o padrão do jogo. 0 não mexe.",
                    new AcceptableValueRange<int>(0, 4)));

            MaxSmoke = cfg.Bind("04 - Performance", "MaxSmoke", 0,
                new ConfigDescription(
                    "Limite de partículas de fumaça. Cada uma é simulada com física, então " +
                    "numa base com muitas fogueiras isso pesa. O jogo usa 100. 0 não mexe.",
                    new AcceptableValueRange<int>(0, 100)));

            FadeDistantSmokeFirst = cfg.Bind("04 - Performance", "FadeDistantSmokeFirst", true,
                "Ao bater no limite, some primeiro com a fumaça mais distante em vez da mais " +
                "antiga, que costuma ser justamente a que está na sua frente.");


            ZoneGenBudgetMs = cfg.Bind("04 - Performance", "ZoneGenBudgetMs", 0f,
                new ConfigDescription(
                    "Tempo máximo por quadro gerando terreno novo, em milissegundos. " +
                    "Ajuda em mundo recém-criado, onde o jogo se permite pausas longas. " +
                    "0 não mexe.",
                    new AcceptableValueRange<float>(0f, 100f)));

            ZdoReleaseIntervalSec = cfg.Bind("04 - Performance", "ZdoReleaseIntervalSec", 2f,
                new ConfigDescription(
                    "De quanto em quanto tempo o servidor redistribui os objetos entre os " +
                    "jogadores. Aumentar alivia quem hospeda com muita gente conectada, ao " +
                    "custo de objetos demorarem mais para trocar de dono. O jogo usa 2.",
                    new AcceptableValueRange<float>(1f, 10f)));

            GcSliceMs = cfg.Bind("04 - Performance", "GcSliceMs", 0f,
                new ConfigDescription(
                    "Quanto tempo por quadro o jogo gasta liberando memória. Valores menores " +
                    "espalham esse trabalho em vez de concentrar. 0 não mexe.",
                    new AcceptableValueRange<float>(0f, 10f)));
        }
    }
}
