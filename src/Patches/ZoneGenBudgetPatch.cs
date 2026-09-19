using System.Reflection;
using HarmonyLib;

namespace ValheimTweaks.Patches
{
    /// <summary>
    /// Zone generation budget -- a strong candidate for hitching in a new world.
    ///
    /// ZoneSystem has a time-sliced generator. The field default is reasonable:
    ///     private float m_timeSlicedGenerationTimeBudget = 0.01f;   // 10 ms
    ///
    /// But the server's Update() overrides it while the locations have not
    /// finished generating:
    ///     if (ZNet.instance.IsServer() &amp;&amp; !LocationsGenerated) {
    ///         if (intro/cinematic) budget = GetTimeBudgetForTargetFrameRate(.., 1/150);
    ///         else                  budget = 0.1f;        // <-- 100 MILLISECONDS
    ///         return;
    ///     }
    ///
    /// This value is the threshold for "I have already spent too much time, I stop for
    /// this frame", checked in three generation loops. That is: outside the intro
    /// screen, the game allows itself to burn up to 100 ms in a single frame generating
    /// world. At 240 fps the frame lasts 4 ms -- a 100 ms frame is a truly visible stutter.
    ///
    /// Notice the irony: during the INTRO it targets 150 fps (6.6 ms), and as soon as
    /// the intro ends and you start playing, it loosens to 100 ms.
    ///
    /// It only holds while LocationsGenerated == false, that is, in a new world --
    /// exactly the case of the LAB world. That is why the symptom appears now.
    ///
    /// The patch rewrites the field AFTER the game's Update, so it beats its
    /// assignment every frame. Trade-off: the locations take longer to
    /// finish generating, but in slices that fit within a frame.
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

                float desired = ms / 1000f;
                float current = (float)BudgetField.GetValue(__instance);
                if (current > desired) BudgetField.SetValue(__instance, desired);
            }
        }

        /// <summary>For diagnostics: the value the game is currently using.</summary>
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
