using System;
using System.Diagnostics;
using System.Text;
using UnityEngine;

namespace ValheimTweaks
{
    /// <summary>
    /// Measures WHERE the frame time is spent, per game system.
    ///
    /// Motivation: I spent several rounds testing levers (instantiation limit,
    /// zone generation budget, simulation distance, GC slice) and they all
    /// fell within the noise. Testing guesses one by one wasn't converging.
    /// This stops guessing: it times the hot systems and, when a frame
    /// blows up, prints THAT frame's breakdown.
    ///
    /// It's the same path that closed the CS2 case: trace and stack, not guesswork.
    ///
    /// Cost: two Stopwatch.GetTimestamp per system per frame. On the order of
    /// tens of nanoseconds -- irrelevant next to the ms we're hunting, but for
    /// that very reason it only stays on during measurement.
    /// </summary>
    internal static class SystemProfiler
    {
        internal enum Sys
        {
            ZDOMan,              // network/ZDO -- host cost, per peer
            CreateDestroyObjects,// object streaming from ZNetScene
            ZNetSceneUpdate,
            ZoneSystem,          // zone generation/loading
            ZdoRelease,          // ZDO ownership transfer (host, every 2s, per peer)
            SpawnZone,           // new zone generation: instantiates everything and destroys
            Clutter,             // grass
            HeightmapRegen,      // terrain mesh rebuild (spiky)
            COUNT
        }

        private static readonly double TicksToMs = 1000.0 / Stopwatch.Frequency;

        private static readonly double[] FrameMs = new double[(int)Sys.COUNT];
        private static readonly double[] TotalMs = new double[(int)Sys.COUNT];
        private static readonly double[] WorstMs = new double[(int)Sys.COUNT];
        private static readonly int[] Calls = new int[(int)Sys.COUNT];

        private static int _zonesGenerated;

        private static int _lastFrame = -1;
        private static int _frames;
        private static bool _enabled;

        // Stores the breakdown of the worst frame seen, to print at the end.
        private static readonly double[] WorstFrameBreakdown = new double[(int)Sys.COUNT];
        private static double _worstFrameMs;

        internal static bool Enabled => _enabled;

        internal static void SetEnabled(bool on)
        {
            if (_enabled == on) return;
            _enabled = on;
            if (on) Reset();
            Plugin.Log.LogInfo($"[SYS] instrumentation {(on ? "ON" : "off")}");
        }

        private static void Reset()
        {
            Array.Clear(TotalMs, 0, TotalMs.Length);
            Array.Clear(WorstMs, 0, WorstMs.Length);
            Array.Clear(Calls, 0, Calls.Length);
            Array.Clear(WorstFrameBreakdown, 0, WorstFrameBreakdown.Length);
            _worstFrameMs = 0;
            _frames = 0;
            _zonesGenerated = 0;
        }

        /// <summary>
        /// Counts zones generated in the window. It separates "new world still warming up"
        /// (a cost that goes away on its own) from "permanent problem".
        /// </summary>
        internal static void CountZoneSpawn() { if (_enabled) _zonesGenerated++; }

        internal static long Begin() => _enabled ? Stopwatch.GetTimestamp() : 0L;

        internal static void End(Sys sys, long started)
        {
            if (!_enabled || started == 0L) return;

            RollFrameIfNeeded();

            double ms = (Stopwatch.GetTimestamp() - started) * TicksToMs;
            int i = (int)sys;
            FrameMs[i] += ms;
            TotalMs[i] += ms;
            Calls[i]++;
            if (ms > WorstMs[i]) WorstMs[i] = ms;
        }

        /// <summary>Closes the previous frame when frameCount changes.</summary>
        private static void RollFrameIfNeeded()
        {
            int f = Time.frameCount;
            if (f == _lastFrame) return;

            if (_lastFrame >= 0)
            {
                double sum = 0;
                for (int i = 0; i < (int)Sys.COUNT; i++) sum += FrameMs[i];
                if (sum > _worstFrameMs)
                {
                    _worstFrameMs = sum;
                    Array.Copy(FrameMs, WorstFrameBreakdown, FrameMs.Length);
                }
                _frames++;
            }

            Array.Clear(FrameMs, 0, FrameMs.Length);
            _lastFrame = f;
        }

        internal static string Report()
        {
            if (_frames < 10) return "[SYS] too few instrumented frames.";

            var sb = new StringBuilder();
            sb.AppendLine();
            sb.AppendLine("--------------- WHERE THE TIME GOES ---------------");
            sb.AppendLine($"  {_frames} instrumented frames");
            sb.AppendLine($"  {"system",-22} {"ms/frame",9} {"worst ms",9} {"calls",9}");

            double sumAverage = 0;
            for (int i = 0; i < (int)Sys.COUNT; i++)
            {
                double perFrame = TotalMs[i] / _frames;
                sumAverage += perFrame;
                sb.AppendLine($"  {(Sys)i,-22} {perFrame,9:0.000} {WorstMs[i],9:0.00} {Calls[i],9}");
            }
            sb.AppendLine($"  {"INSTRUMENTED SUM",-22} {sumAverage,9:0.000}");
            sb.AppendLine();
            sb.AppendLine($"  WORST FRAME: {_worstFrameMs:0.00} ms within the instrumented portion");
            for (int i = 0; i < (int)Sys.COUNT; i++)
            {
                if (WorstFrameBreakdown[i] > 0.01)
                    sb.AppendLine($"     {(Sys)i,-22} {WorstFrameBreakdown[i],9:0.00} ms");
            }
            sb.AppendLine();
            sb.AppendLine($"  zones GENERATED in this window: {_zonesGenerated}");
            sb.AppendLine(_zonesGenerated > 0
                ? "     -> world still warming up here. This cost is paid ONCE per zone"
                : "     -> no new zones: area already explored, generation cost = 0");
            sb.AppendLine($"  ZDOs in the world: {(ZDOMan.instance != null ? ZDOMan.instance.NrOfObjects() : -1)}");
            sb.AppendLine("  (what doesn't appear here is render, physics, animation and AI)");
            sb.AppendLine("-----------------------------------------------");
            return sb.ToString();
        }
    }
}
