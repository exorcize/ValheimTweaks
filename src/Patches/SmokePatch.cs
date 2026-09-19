using HarmonyLib;

namespace ValheimTweaks.Patches
{
    /// <summary>
    /// Smoke -- PHYSICS cost, not render.
    ///
    /// Each smoke particle is a GameObject with a real Rigidbody
    /// (Smoke.Awake: m_body = GetComponent&lt;Rigidbody&gt;()). In a base with several
    /// campfires, a forge and ovens, that becomes dozens of rigid bodies simulated
    /// all the time. It is one of the silliest costs in the game.
    ///
    /// The cap is applied in SmokeSpawner:
    ///     if (Smoke.GetTotalSmoke() > 100) Smoke.FadeOldest();
    /// with "100" coming from "private const int m_maxGlobalSmoke = 100" -- a const, that is,
    /// INLINED in the IL. You cannot change it by writing the field.
    ///
    /// Trick to avoid a Transpiler: we Postfix GetTotalSmoke() and return
    /// the SHIFTED count. With offset = 100 - desiredMax, the comparison
    ///     (count + 100 - max) > 100      becomes      count > max
    /// which is exactly the new cap, without touching the call site's IL.
    ///
    /// Second adjustment: the game removes the OLDEST smoke, which tends to be the
    /// one right in front of you. Smoke.FadeMostDistant() already exists in the game and is
    /// never called -- we redirect to it. The smoke that disappears then becomes the
    /// farthest one, which is the one nobody sees.
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
            // Returning false skips the original method.
            private static bool Prefix()
            {
                if (!ModConfig.FadeDistantSmokeFirst.Value) return true;

                Smoke.FadeMostDistant();
                return false;
            }
        }
    }
}
