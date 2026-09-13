using System;
using System.IO;
using BepInEx;
using BepInEx.Logging;
using HarmonyLib;
using UnityEngine;

namespace ValheimTweaks
{
    [BepInPlugin(GUID, NAME, VERSION)]
    public class Plugin : BaseUnityPlugin
    {
        public const string GUID = "com.kyoka.valheimtweaks";
        public const string NAME = "ValheimTweaks";
        public const string VERSION = "0.21.0";

        internal static ManualLogSource Log;
        internal static Plugin Instance;

        private Harmony _harmony;
        private FileSystemWatcher _watcher;

        // Escrito pela thread do FileSystemWatcher, lido pela thread principal.
        // API do Unity so pode ser tocada na main thread -- por isso o pending.
        private volatile bool _reloadPending;
        private float _reloadAt;

        private bool _dumpedThisWorld;

        private void Awake()
        {
            Instance = this;
            Log = Logger;

            ModConfig.Init(Config);

            _harmony = new Harmony(GUID);
            _harmony.PatchAll();

            Config.SettingChanged += OnSettingChanged;
            SetupHotReload();

            ApplyAll("boot");
            Log.LogInfo($"{NAME} {VERSION} carregado. Config: {Config.ConfigFilePath}");
        }

        private void OnDestroy()
        {
            _watcher?.Dispose();
            _harmony?.UnpatchSelf();
        }

        // ------------------------------------------------------------------
        // Hot reload: editar o .cfg no disco aplica em jogo, sem reiniciar.
        // E o que torna o ciclo de teste viavel (ver README).
        // ------------------------------------------------------------------
        private void SetupHotReload()
        {
            try
            {
                string path = Config.ConfigFilePath;
                string dir = Path.GetDirectoryName(path);
                if (string.IsNullOrEmpty(dir) || !Directory.Exists(dir))
                {
                    Log.LogWarning("Hot reload desligado: diretorio de config nao encontrado.");
                    return;
                }

                _watcher = new FileSystemWatcher(dir, Path.GetFileName(path))
                {
                    NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.Size,
                    EnableRaisingEvents = true
                };
                _watcher.Changed += (_, __) => _reloadPending = true;
                _watcher.Created += (_, __) => _reloadPending = true;
            }
            catch (Exception e)
            {
                Log.LogWarning($"Hot reload indisponivel: {e.Message}");
            }
        }

        private void Update()
        {
            FrameProfiler.Tick();

            if (_reloadPending && ModConfig.HotReload.Value)
            {
                // Debounce: editores gravam em varias etapas e disparam N eventos.
                if (_reloadAt <= 0f) _reloadAt = Time.realtimeSinceStartup + 0.25f;

                if (Time.realtimeSinceStartup >= _reloadAt)
                {
                    _reloadPending = false;
                    _reloadAt = 0f;
                    try
                    {
                        Config.Reload();
                        ApplyAll("hot-reload");
                    }
                    catch (Exception e)
                    {
                        Log.LogError($"Falha ao recarregar config: {e}");
                    }
                }
            }

            // Dump uma vez por mundo carregado, quando o player ja existe.
            if (Player.m_localPlayer != null)
            {
                if (!_dumpedThisWorld)
                {
                    _dumpedThisWorld = true;
                    if (ModConfig.DumpOnWorldLoad.Value) Diagnostics.Dump("mundo carregado");
                }
            }
            else
            {
                _dumpedThisWorld = false;
            }
        }

        private void OnSettingChanged(object sender, EventArgs e)
        {
            // Dump sob demanda: marcar DumpNow=true no arquivo dispara e se auto-desmarca.
            if (ModConfig.DumpNow.Value)
            {
                ModConfig.DumpNow.Value = false;
                Diagnostics.Dump("pedido manual");
            }

            if (ModConfig.ProfileNow.Value)
            {
                ModConfig.ProfileNow.Value = false;
                if (!FrameProfiler.IsRunning)
                    FrameProfiler.Start($"t{DateTime.Now:HHmmss}", ModConfig.ProfileSeconds.Value);
            }
        }

        /// <summary>Reaplica tudo que e settavel em runtime.</summary>
        internal static void ApplyAll(string reason)
        {
            try
            {
                Patches.TextureFilterPatch.Apply();
                Patches.QualityPatch.Apply();
                Patches.LightLodPatch.Apply();
                Patches.SsaoPatch.Apply();
                Patches.AntiAliasingPatch.Apply();
                Patches.FovPatch.Apply();
                Patches.DisplayModePatch.Apply();
                Patches.ScenePatch.Apply();
                Patches.GcPatch.Apply();
                Patches.TimeoutPatch.Apply();
                Patches.SimulationDistancePatch.Apply();
                // AmbientPatch nao precisa de Apply: o Postfix em EnvMan.SetEnv
                // le a config a cada troca de ambiente, entao o slider e continuo.
                Log.LogInfo($"Configuracoes aplicadas ({reason}).");
            }
            catch (Exception e)
            {
                Log.LogError($"Erro aplicando configuracoes ({reason}): {e}");
            }
        }
    }
}
