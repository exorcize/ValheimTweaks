using System;
using System.Collections.Generic;
using System.Text;
using UnityEngine;

namespace ValheimTweaks
{
    /// <summary>
    /// Medidor de frametime interno.
    ///
    /// O PresentMon precisa de elevacao e, neste setup, morre depois de ~2s --
    /// tempo demais gasto brigando com ferramenta externa. O mod ja roda DENTRO
    /// do processo do jogo, entao mede o loop de frame direto.
    ///
    /// Nao substitui o PresentMon para tudo (nao separa CPU de GPU, nao ve o modo
    /// de apresentacao), mas para "essa mudanca ajudou ou nao?" e o instrumento
    /// certo: sem elevacao, sem injetor, e do mesmo lado do problema.
    ///
    /// Lembrete do caso do CS2: media alta nao quer dizer sensacao boa. Por isso
    /// o relatorio da mais peso a P99 e a CONTAGEM DE ENGASGOS do que a media.
    /// </summary>
    internal static class FrameProfiler
    {
        private static readonly List<float> Samples = new List<float>(64 * 1024);
        private static bool _running;
        private static float _endsAt;
        private static string _tag;

        internal static bool IsRunning => _running;

        internal static void Start(string tag, float seconds)
        {
            Samples.Clear();
            _tag = tag;
            _running = true;
            _endsAt = Time.realtimeSinceStartup + seconds;
            SystemProfiler.SetEnabled(ModConfig.ProfileSystems.Value);
            Plugin.Log.LogInfo($"[PROFILER] capturando {seconds:0}s como '{tag}'...");
        }

        /// <summary>Chamado todo frame pelo Plugin.Update.</summary>
        internal static void Tick()
        {
            if (!_running) return;

            // unscaledDeltaTime: nao sofre com timeScale, e o tempo de parede do frame.
            Samples.Add(Time.unscaledDeltaTime * 1000f);

            if (Time.realtimeSinceStartup >= _endsAt) Report();
        }

        private static void Report()
        {
            _running = false;

            if (Samples.Count < 30)
            {
                Plugin.Log.LogWarning($"[PROFILER] '{_tag}': poucos frames ({Samples.Count}).");
                return;
            }

            var sorted = new List<float>(Samples);
            sorted.Sort();

            float soma = 0f;
            foreach (var s in Samples) soma += s;
            float media = soma / Samples.Count;
            float p50 = Pct(sorted, 0.50f);

            // Engasgo = frame que levou mais que o dobro da mediana. E o que a
            // pessoa sente como travada, e some completamente na media.
            float limiar = p50 * 2f;
            int engasgos = 0;
            float piorSeq = 0f;
            foreach (var s in Samples)
            {
                if (s > limiar) engasgos++;
                if (s > piorSeq) piorSeq = s;
            }

            float duracao = soma / 1000f;

            var sb = new StringBuilder();
            sb.AppendLine();
            sb.AppendLine($"=============== PROFILER :: {_tag} ===============");
            sb.AppendLine($"  {Samples.Count} frames em {duracao:0.0}s");
            sb.AppendLine($"  media    {media,6:0.00} ms  ({1000f / media:0} fps)");
            sb.AppendLine($"  P50      {p50,6:0.00} ms  ({1000f / p50:0} fps)");
            sb.AppendLine($"  P95      {Pct(sorted, 0.95f),6:0.00} ms");
            sb.AppendLine($"  P99      {Pct(sorted, 0.99f),6:0.00} ms  <- o que se sente");
            sb.AppendLine($"  P99.9    {Pct(sorted, 0.999f),6:0.00} ms");
            sb.AppendLine($"  MAX      {piorSeq,6:0.00} ms");
            sb.AppendLine($"  1% low   {1000f / Pct(sorted, 0.99f),6:0} fps");
            sb.AppendLine($"  engasgos {engasgos} frames acima de {limiar:0.0}ms " +
                          $"({(100f * engasgos / Samples.Count):0.00}% dos frames, " +
                          $"{engasgos / Mathf.Max(duracao, 0.001f):0.0}/s)");
            sb.AppendLine("==================================================");

            if (SystemProfiler.Enabled)
            {
                sb.Append(SystemProfiler.Report());
                SystemProfiler.SetEnabled(false);
            }

            Plugin.Log.LogInfo(sb.ToString());
            Samples.Clear();
        }

        private static float Pct(List<float> sorted, float p)
        {
            int i = Mathf.Clamp((int)Math.Floor(p * (sorted.Count - 1)), 0, sorted.Count - 1);
            return sorted[i];
        }
    }
}
