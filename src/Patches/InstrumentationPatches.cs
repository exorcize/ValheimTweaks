using HarmonyLib;

namespace ValheimTweaks.Patches
{
    /// <summary>
    /// Cronometros nos sistemas quentes do jogo.
    ///
    /// Padrao Harmony: o Prefix devolve o timestamp em __state, o Postfix fecha.
    /// __state e por-chamada, entao aguenta reentrancia e recursao sem embaralhar.
    ///
    /// Os alvos foram escolhidos por serem os suspeitos de custo por frame do lado
    /// do HOST e do streaming de mundo -- que e o cenario do usuario (ele hospeda
    /// para 4 peers com near=5, 121 zonas).
    ///
    /// Tudo so acumula quando SystemProfiler.Enabled, entao fora da medicao o
    /// custo e uma leitura de bool.
    /// </summary>
    internal static class InstrumentationPatches
    {
        [HarmonyPatch(typeof(ZDOMan), nameof(ZDOMan.Update))]
        internal static class ZDOManUpdate
        {
            private static void Prefix(out long __state) => __state = SystemProfiler.Begin();
            private static void Postfix(long __state) => SystemProfiler.End(SystemProfiler.Sys.ZDOMan, __state);
        }

        [HarmonyPatch(typeof(ZNetScene), "CreateDestroyObjects")]
        internal static class CreateDestroy
        {
            private static void Prefix(out long __state) => __state = SystemProfiler.Begin();
            private static void Postfix(long __state) => SystemProfiler.End(SystemProfiler.Sys.CreateDestroyObjects, __state);
        }

        [HarmonyPatch(typeof(ZNetScene), "Update")]
        internal static class ZNetSceneUpdate
        {
            private static void Prefix(out long __state) => __state = SystemProfiler.Begin();
            private static void Postfix(long __state) => SystemProfiler.End(SystemProfiler.Sys.ZNetSceneUpdate, __state);
        }

        [HarmonyPatch(typeof(ZoneSystem), "Update")]
        internal static class ZoneSystemUpdate
        {
            private static void Prefix(out long __state) => __state = SystemProfiler.Begin();
            private static void Postfix(long __state) => SystemProfiler.End(SystemProfiler.Sys.ZoneSystem, __state);
        }

        [HarmonyPatch(typeof(ClutterSystem), "LateUpdate")]
        internal static class ClutterLateUpdate
        {
            private static void Prefix(out long __state) => __state = SystemProfiler.Begin();
            private static void Postfix(long __state) => SystemProfiler.End(SystemProfiler.Sys.Clutter, __state);
        }

        // Geracao de zona: instancia toda a vegetacao e destroi em seguida, so para
        // registrar os ZDOs. Custo UMA VEZ por zona (SetZoneGenerated), mas enorme.
        // Hipotese a testar: em mundo novo isto domina o engasgo de caminhada, e
        // some sozinho conforme o mundo e explorado.
        [HarmonyPatch(typeof(ZoneSystem), "SpawnZone")]
        internal static class SpawnZoneHook
        {
            private static void Prefix(out long __state) => __state = SystemProfiler.Begin();

            private static void Postfix(long __state, bool __result)
            {
                SystemProfiler.End(SystemProfiler.Sys.SpawnZone, __state);
                if (__result) SystemProfiler.CountZoneSpawn();
            }
        }

        // Rebuild de mesh de terreno: raro mas caro, tipico de pico isolado.
        [HarmonyPatch(typeof(Heightmap), nameof(Heightmap.Regenerate))]
        internal static class HeightmapRegenerate
        {
            private static void Prefix(out long __state) => __state = SystemProfiler.Begin();
            private static void Postfix(long __state) => SystemProfiler.End(SystemProfiler.Sys.HeightmapRegen, __state);
        }
    }
}
