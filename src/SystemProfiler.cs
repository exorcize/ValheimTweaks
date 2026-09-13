using System;
using System.Diagnostics;
using System.Text;
using UnityEngine;

namespace ValheimTweaks
{
    /// <summary>
    /// Mede ONDE o tempo do frame e gasto, por sistema do jogo.
    ///
    /// Motivacao: gastei varias rodadas testando levers (limite de instanciacao,
    /// orcamento de geracao de zona, simulation distance, fatia de GC) e todos
    /// cairam dentro do ruido. Testar palpite um por um nao estava convergindo.
    /// Isto para de adivinhar: cronometra os sistemas quentes e, quando um frame
    /// estoura, imprime a repartição DAQUELE frame.
    ///
    /// E o mesmo caminho que fechou o caso do CS2: trace e pilha, nao palpite.
    ///
    /// Custo: dois Stopwatch.GetTimestamp por sistema por frame. Da ordem de
    /// dezenas de nanossegundos -- irrelevante perto dos ms que estamos cacando,
    /// mas por isso mesmo so fica ligado durante a medicao.
    /// </summary>
    internal static class SystemProfiler
    {
        internal enum Sys
        {
            ZDOMan,              // rede/ZDO -- custo de host, por peer
            CreateDestroyObjects,// streaming de objetos do ZNetScene
            ZNetSceneUpdate,
            ZoneSystem,          // geracao/carregamento de zona
            ZdoRelease,          // transferencia de posse de ZDO (host, a cada 2s, por peer)
            SpawnZone,           // geracao de zona nova: instancia tudo e destroi
            Clutter,             // grama
            HeightmapRegen,      // rebuild de mesh de terreno (spiky)
            COUNT
        }

        private static readonly double TicksToMs = 1000.0 / Stopwatch.Frequency;

        private static readonly double[] FrameMs = new double[(int)Sys.COUNT];
        private static readonly double[] TotalMs = new double[(int)Sys.COUNT];
        private static readonly double[] WorstMs = new double[(int)Sys.COUNT];
        private static readonly int[] Calls = new int[(int)Sys.COUNT];

        private static int _zonesGeradas;

        private static int _lastFrame = -1;
        private static int _frames;
        private static bool _enabled;

        // Guarda a repartição do pior frame visto, para imprimir no fim.
        private static readonly double[] WorstFrameBreakdown = new double[(int)Sys.COUNT];
        private static double _worstFrameMs;

        internal static bool Enabled => _enabled;

        internal static void SetEnabled(bool on)
        {
            if (_enabled == on) return;
            _enabled = on;
            if (on) Reset();
            Plugin.Log.LogInfo($"[SYS] instrumentacao {(on ? "LIGADA" : "desligada")}");
        }

        private static void Reset()
        {
            Array.Clear(TotalMs, 0, TotalMs.Length);
            Array.Clear(WorstMs, 0, WorstMs.Length);
            Array.Clear(Calls, 0, Calls.Length);
            Array.Clear(WorstFrameBreakdown, 0, WorstFrameBreakdown.Length);
            _worstFrameMs = 0;
            _frames = 0;
            _zonesGeradas = 0;
        }

        /// <summary>
        /// Conta zonas geradas na janela. Serve para separar "mundo novo estreando"
        /// (custo que some sozinho) de "problema permanente".
        /// </summary>
        internal static void CountZoneSpawn() { if (_enabled) _zonesGeradas++; }

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

        /// <summary>Fecha o frame anterior quando o frameCount muda.</summary>
        private static void RollFrameIfNeeded()
        {
            int f = Time.frameCount;
            if (f == _lastFrame) return;

            if (_lastFrame >= 0)
            {
                double soma = 0;
                for (int i = 0; i < (int)Sys.COUNT; i++) soma += FrameMs[i];
                if (soma > _worstFrameMs)
                {
                    _worstFrameMs = soma;
                    Array.Copy(FrameMs, WorstFrameBreakdown, FrameMs.Length);
                }
                _frames++;
            }

            Array.Clear(FrameMs, 0, FrameMs.Length);
            _lastFrame = f;
        }

        internal static string Report()
        {
            if (_frames < 10) return "[SYS] poucos frames instrumentados.";

            var sb = new StringBuilder();
            sb.AppendLine();
            sb.AppendLine("--------------- ONDE O TEMPO VAI ---------------");
            sb.AppendLine($"  {_frames} frames instrumentados");
            sb.AppendLine($"  {"sistema",-22} {"ms/frame",9} {"pior ms",9} {"chamadas",9}");

            double somaMedia = 0;
            for (int i = 0; i < (int)Sys.COUNT; i++)
            {
                double porFrame = TotalMs[i] / _frames;
                somaMedia += porFrame;
                sb.AppendLine($"  {(Sys)i,-22} {porFrame,9:0.000} {WorstMs[i],9:0.00} {Calls[i],9}");
            }
            sb.AppendLine($"  {"SOMA INSTRUMENTADA",-22} {somaMedia,9:0.000}");
            sb.AppendLine();
            sb.AppendLine($"  PIOR FRAME: {_worstFrameMs:0.00} ms dentro do instrumentado");
            for (int i = 0; i < (int)Sys.COUNT; i++)
            {
                if (WorstFrameBreakdown[i] > 0.01)
                    sb.AppendLine($"     {(Sys)i,-22} {WorstFrameBreakdown[i],9:0.00} ms");
            }
            sb.AppendLine();
            sb.AppendLine($"  zonas GERADAS nesta janela: {_zonesGeradas}");
            sb.AppendLine(_zonesGeradas > 0
                ? "     -> mundo ainda estreando aqui. Este custo e pago UMA VEZ por zona"
                : "     -> nenhuma zona nova: area ja explorada, custo de geracao = 0");
            sb.AppendLine($"  ZDOs no mundo: {(ZDOMan.instance != null ? ZDOMan.instance.NrOfObjects() : -1)}");
            sb.AppendLine("  (o que nao aparece aqui e render, fisica, animacao e IA)");
            sb.AppendLine("-----------------------------------------------");
            return sb.ToString();
        }
    }
}
