using System.Reflection;
using HarmonyLib;

namespace ValheimTweaks.Patches
{
    /// <summary>
    /// Orcamento de geracao de zonas -- candidato forte para engasgo em mundo novo.
    ///
    /// ZoneSystem tem um gerador fatiado no tempo. O default do campo e razoavel:
    ///     private float m_timeSlicedGenerationTimeBudget = 0.01f;   // 10 ms
    ///
    /// Mas o Update() do servidor sobrescreve isso enquanto as locations nao
    /// terminaram de gerar:
    ///     if (ZNet.instance.IsServer() &amp;&amp; !LocationsGenerated) {
    ///         if (intro/cinematica) budget = GetTimeBudgetForTargetFrameRate(.., 1/150);
    ///         else                  budget = 0.1f;        // <-- 100 MILISSEGUNDOS
    ///         return;
    ///     }
    ///
    /// Esse valor e o limiar de "ja gastei tempo demais, paro por este frame",
    /// checado em tres lacos de geracao. Ou seja: fora da tela de intro, o jogo
    /// se permite queimar ate 100 ms num unico frame gerando mundo. A 240 fps o
    /// frame dura 4 ms -- um frame de 100 ms e uma travada visivel de verdade.
    ///
    /// Repare na ironia: durante a INTRO ele mira 150 fps (6,6 ms), e assim que
    /// a intro acaba e voce comeca a jogar, ele afrouxa para 100 ms.
    ///
    /// Vale so enquanto LocationsGenerated == false, ou seja, em mundo novo --
    /// exatamente o caso do mundo LAB. Por isso o sintoma aparece agora.
    ///
    /// O patch reescreve o campo DEPOIS do Update do jogo, entao ganha da
    /// atribuicao dele todo frame. Trade: as locations demoram mais para
    /// terminar de gerar, mas em fatias que cabem dentro de um frame.
    /// </summary>
    internal static class ZoneGenBudgetPatch
    {
        private static readonly FieldInfo BudgetField =
            AccessTools.Field(typeof(ZoneSystem), "m_timeSlicedGenerationTimeBudget");

        [HarmonyPatch(typeof(ZoneSystem), "Update")]
        internal static class UpdateHook
        {
            private static void Postfix(ZoneSystem __instance)
            {
                float ms = ModConfig.ZoneGenBudgetMs.Value;
                if (ms <= 0f || BudgetField == null) return;

                float desejado = ms / 1000f;
                float atual = (float)BudgetField.GetValue(__instance);
                if (atual > desejado) BudgetField.SetValue(__instance, desejado);
            }
        }

        /// <summary>Para o diagnostico: o valor que o jogo esta usando agora.</summary>
        internal static float CurrentBudgetMs
        {
            get
            {
                var zs = ZoneSystem.instance;
                if (zs == null || BudgetField == null) return -1f;
                return (float)BudgetField.GetValue(zs) * 1000f;
            }
        }
    }
}
