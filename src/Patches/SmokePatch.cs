using HarmonyLib;

namespace ValheimTweaks.Patches
{
    /// <summary>
    /// Fumaca -- custo de FISICA, nao de render.
    ///
    /// Cada particula de fumaca e um GameObject com Rigidbody de verdade
    /// (Smoke.Awake: m_body = GetComponent&lt;Rigidbody&gt;()). Numa base com varias
    /// fogueiras, forja e fornos, isso vira dezenas de corpos rigidos simulados
    /// o tempo todo. E um dos custos mais bobos do jogo.
    ///
    /// O teto e aplicado em SmokeSpawner:
    ///     if (Smoke.GetTotalSmoke() > 100) Smoke.FadeOldest();
    /// com "100" vindo de "private const int m_maxGlobalSmoke = 100" -- const, ou
    /// seja INLINADO no IL. Nao da para mudar escrevendo o campo.
    ///
    /// Truque para evitar Transpiler: damos Postfix em GetTotalSmoke() e devolvemos
    /// a contagem DESLOCADA. Com offset = 100 - maxDesejado, a comparacao
    ///     (count + 100 - max) > 100      vira      count > max
    /// que e exatamente o teto novo, sem tocar no IL do call site.
    ///
    /// Segundo ajuste: o jogo apaga a fumaca MAIS ANTIGA, que costuma ser a que
    /// esta bem na tua frente. Smoke.FadeMostDistant() ja existe no jogo e nunca e
    /// chamado -- redirecionamos para ele. A fumaca que some passa a ser a mais
    /// longe, que e a que ninguem ve.
    /// </summary>
    internal static class SmokePatch
    {
        private const int VanillaCap = 100;

        [HarmonyPatch(typeof(Smoke), nameof(Smoke.GetTotalSmoke))]
        internal static class GetTotalSmokeHook
        {
            private static void Postfix(ref int __result)
            {
                int max = ModConfig.MaxSmoke.Value;
                if (max <= 0 || max == VanillaCap) return;

                __result += VanillaCap - max;
            }
        }

        [HarmonyPatch(typeof(Smoke), nameof(Smoke.FadeOldest))]
        internal static class FadeOldestHook
        {
            // Retornar false pula o metodo original.
            private static bool Prefix()
            {
                if (!ModConfig.FadeDistantSmokeFirst.Value) return true;

                Smoke.FadeMostDistant();
                return false;
            }
        }
    }
}
