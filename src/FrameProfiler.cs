using System;
using System.Collections.Generic;
using System.Text;
using UnityEngine;

namespace ValheimTweaks
{
    /// <summary>
    /// Internal frametime meter.
    ///
    /// PresentMon needs elevation and, in this setup, dies after ~2s -- too much
    /// time spent fighting an external tool. The mod already runs INSIDE the game
    /// process, so it measures the frame loop directly.
    ///
    /// It doesn't replace PresentMon for everything (it doesn't separate CPU from
    /// GPU, doesn't see the presentation mode), but for "did this change help or
    /// not?" it's the right instrument: no elevation, no injector, and on the same
    /// side of the problem.
    ///
    /// Reminder from the CS2 case: a high average doesn't mean a good feel. That's
    /// why the report gives more weight to P99 and the STUTTER COUNT than to the average.
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
            Plugin.Log.LogInfo($"[PROFILER] capturing {seconds:0}s as '{tag}'...");
        }

        /// <summary>Called every frame by Plugin.Update.</summary>
        internal static void Tick()
        {
            if (!_running) return;

            // unscaledDeltaTime: unaffected by timeScale, and is the frame's wall-clock time.
            Samples.Add(Time.unscaledDeltaTime * 1000f);

            if (Time.realtimeSinceStartup >= _endsAt) Report();
        }

        private static void Report()
        {
            _running = false;

            if (Samples.Count < 30)
            {
                Plugin.Log.LogWarning($"[PROFILER] '{_tag}': too few frames ({Samples.Count}).");
                return;
            }

            var sorted = new List<float>(Samples);
            sorted.Sort();

            float sum = 0f;
            foreach (var s in Samples) sum += s;
            float average = sum / Samples.Count;
            float p50 = Pct(sorted, 0.50f);

            // Stutter = a frame that took more than twice the median. It's what a
            // person feels as a hitch, and it vanishes completely in the average.
            float threshold = p50 * 2f;
            int stutters = 0;
            float worstSeq = 0f;
            foreach (var s in Samples)
            {
                if (s > threshold) stutters++;
                if (s > worstSeq) worstSeq = s;
            }

            float duration = sum / 1000f;

            var sb = new StringBuilder();
            sb.AppendLine();
            sb.AppendLine($"=============== PROFILER :: {_tag} ===============");
            sb.AppendLine($"  {Samples.Count} frames in {duration:0.0}s");
            sb.AppendLine($"  average  {average,6:0.00} ms  ({1000f / average:0} fps)");
            sb.AppendLine($"  P50      {p50,6:0.00} ms  ({1000f / p50:0} fps)");
            sb.AppendLine($"  P95      {Pct(sorted, 0.95f),6:0.00} ms");
            sb.AppendLine($"  P99      {Pct(sorted, 0.99f),6:0.00} ms  <- what you feel");
            sb.AppendLine($"  P99.9    {Pct(sorted, 0.999f),6:0.00} ms");
            sb.AppendLine($"  MAX      {worstSeq,6:0.00} ms");
            sb.AppendLine($"  1% low   {1000f / Pct(sorted, 0.99f),6:0} fps");
            sb.AppendLine($"  stutters {stutters} frames above {threshold:0.0}ms " +
                          $"({(100f * stutters / Samples.Count):0.00}% of frames, " +
                          $"{stutters / Mathf.Max(duration, 0.001f):0.0}/s)");
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
