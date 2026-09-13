using UnityEngine.Scripting;

namespace ValheimTweaks.Patches
{
    /// <summary>
    /// Coletor de lixo -- a outra fonte classica de engasgo em jogo Unity/Mono.
    ///
    /// Quando o GC nao e incremental, ele para o mundo inteiro para coletar: um
    /// frame normal de 4 ms vira um de 30-80 ms. Isso e sentido como travada, e
    /// some completamente na media de fps.
    ///
    /// O boot.config do Valheim traz "gc-max-time-slice=3", o que indica GC
    /// incremental com fatia de 3 ms. Mas 3 ms ainda e quase um frame inteiro
    /// a 240 fps -- vale testar fatias menores, que espalham a coleta por mais
    /// frames em vez de concentrar.
    ///
    /// incrementalTimeSliceNanoseconds e settavel em runtime, entao isso entra
    /// no A/B junto com o resto, sem reiniciar.
    /// </summary>
    internal static class GcPatch
    {
        internal static void Apply()
        {
            float ms = ModConfig.GcSliceMs.Value;
            if (ms <= 0f) return; // 0 = nao mexer

            if (!GarbageCollector.isIncremental)
            {
                Plugin.Log.LogWarning(
                    "GC incremental esta DESLIGADO neste build -- a fatia nao tem efeito. " +
                    "Coletas vao parar o mundo inteiro.");
                return;
            }

            ulong ns = (ulong)(ms * 1_000_000f);
            if (GarbageCollector.incrementalTimeSliceNanoseconds == ns) return;

            GarbageCollector.incrementalTimeSliceNanoseconds = ns;
            Plugin.Log.LogInfo($"GC: fatia incremental -> {ms:0.##} ms");
        }

        internal static string Describe()
        {
            return GarbageCollector.isIncremental
                ? $"incremental, fatia = {GarbageCollector.incrementalTimeSliceNanoseconds / 1_000_000f:0.##} ms, modo = {GarbageCollector.GCMode}"
                : $"NAO incremental (para o mundo ao coletar), modo = {GarbageCollector.GCMode}";
        }
    }
}
