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
        public const string VERSION = "0.33.19";

        internal static ManualLogSource Log;
        internal static Plugin Instance;

        private Harmony _harmony;
        private FileSystemWatcher _watcher;

        // Written by the FileSystemWatcher thread, read by the main thread.
        // Unity's API can only be touched on the main thread -- hence the pending flag.
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

            // A patch that doesn't take throws no error: it simply doesn't happen, and the
            // symptom becomes "it doesn't work". Listing what actually got patched at boot
            // costs nothing and has already saved a whole round of trial and error.
            try
            {
                var targets = new System.Collections.Generic.List<string>();
                foreach (var m in _harmony.GetPatchedMethods())
                    targets.Add($"{m.DeclaringType?.Name}.{m.Name}");
                targets.Sort();
                Log.LogInfo($"Patches applied ({targets.Count}): {string.Join(", ", targets.ToArray())}");
            }
            catch (Exception e)
            {
                Log.LogWarning($"Nao consegui listar os patches: {e.Message}");
            }

            // Which of our methods another mod also patches? Shared methods are where
            // conflicts live, and the game gives no error for them -- the symptom is just
            // "stopped working" or an item behaving oddly. Listing them at boot turns a
            // mystery into a name. Ask Harmony for the real owners, not a guess.
            try
            {
                var shared = new System.Collections.Generic.List<string>();
                foreach (var m in _harmony.GetPatchedMethods())
                {
                    var info = Harmony.GetPatchInfo(m);
                    if (info == null) continue;

                    var others = new System.Collections.Generic.SortedSet<string>();
                    foreach (var p in info.Prefixes) if (p.owner != GUID) others.Add(p.owner);
                    foreach (var p in info.Postfixes) if (p.owner != GUID) others.Add(p.owner);
                    foreach (var p in info.Transpilers) if (p.owner != GUID) others.Add(p.owner);

                    if (others.Count > 0)
                        shared.Add($"{m.DeclaringType?.Name}.{m.Name} <- {string.Join(", ", others)}");
                }

                if (shared.Count > 0)
                {
                    shared.Sort();
                    Log.LogWarning($"Methods shared with other mods ({shared.Count}): "
                                 + string.Join(" | ", shared));
                }
                else
                {
                    Log.LogInfo("No method of ours is patched by another mod.");
                }
            }
            catch (Exception e)
            {
                Log.LogWarning($"Nao consegui checar conflitos: {e.Message}");
            }

            Config.SettingChanged += OnSettingChanged;
            SetupHotReload();

            ApplyAll("boot");
            Log.LogInfo($"{NAME} {VERSION} loaded. Config: {Config.ConfigFilePath}");
        }

        private void OnDestroy()
        {
            _watcher?.Dispose();
            _harmony?.UnpatchSelf();
        }

        // ------------------------------------------------------------------
        // Hot reload: editing the .cfg on disk applies in-game, without restarting.
        // This is what makes the test cycle viable (see README).
        // ------------------------------------------------------------------
        private void SetupHotReload()
        {
            try
            {
                string path = Config.ConfigFilePath;
                string dir = Path.GetDirectoryName(path);
                if (string.IsNullOrEmpty(dir) || !Directory.Exists(dir))
                {
                    Log.LogWarning("Hot reload disabled: config directory not found.");
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
                Log.LogWarning($"Hot reload unavailable: {e.Message}");
            }
        }

        private void Update()
        {
            FrameProfiler.Tick();
            Patches.QuickStorePatch.Update();
            Patches.StoreHudPatch.Update();
            Patches.ChestPeekPatch.Update();
            Patches.AutoMinePatch.Update();
            Patches.ChestSearchPatch.Update();
            Patches.StoreDeferral.Tick();
            Patches.ChestAudit.Tick();
            Patches.ChestSnapshot.Tick();

            if (_reloadPending && ModConfig.HotReload.Value)
            {
                // Debounce: editors write in several stages and fire N events.
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
                        Log.LogError($"Failed to reload config: {e}");
                    }
                }
            }

            // Dump once per loaded world, when the player already exists.
            if (Player.m_localPlayer != null)
            {
                if (!_dumpedThisWorld)
                {
                    _dumpedThisWorld = true;
                    // Checked here so every plugin is loaded by now.
                    Patches.BuildFromChests.CheckConflicts();
                    Patches.ChestSnapshot.Save();   // baseline for the session
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
            // On-demand dump: setting DumpNow=true in the file triggers it and it un-sets itself.
            if (ModConfig.DumpNow.Value)
            {
                ModConfig.DumpNow.Value = false;
                Diagnostics.Dump("manual request");
            }

            if (ModConfig.DumpChestsNow.Value)
            {
                ModConfig.DumpChestsNow.Value = false;
                Patches.ChestAudit.DumpChests();
            }

            if (ModConfig.RestoreSnapshotNow.Value)
            {
                ModConfig.RestoreSnapshotNow.Value = false;
                Patches.ChestSnapshot.Restore();
            }

            if (ModConfig.ProfileNow.Value)
            {
                ModConfig.ProfileNow.Value = false;
                if (!FrameProfiler.IsRunning)
                    FrameProfiler.Start($"t{DateTime.Now:HHmmss}", ModConfig.ProfileSeconds.Value);
            }
        }

        private static string OnOff(bool v) => v ? "ON" : "off";

        /// <summary>Reapplies everything that is settable at runtime.</summary>
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
                Patches.BuildFromChests.Invalidate();   // radius or toggle may have changed

                // Feature state in the user's language. It answers
                // "is it on?" by reading the log, without having to open the F1 menu
                // and describe it over the phone.
                Log.LogInfo(
                    "Features: chest panel=" + OnOff(ModConfig.ChestSearchEnabled.Value)
                  + " | chest peek=" + OnOff(ModConfig.ChestPeekEnabled.Value)
                  + " | store in chests=" + OnOff(ModConfig.StoreHudEnabled.Value)
                  + " | build from chests=" + OnOff(ModConfig.BuildFromChestsEnabled.Value)
                  + " | auto repair=" + OnOff(ModConfig.AutoRepairOnOpen.Value)
                  + " | network timeout=" + (ModConfig.TimeoutEnabled.Value
                        ? ModConfig.TimeoutSeconds.Value.ToString("0") + "s" : "off"));
                // AmbientPatch doesn't need Apply: the Postfix on EnvMan.SetEnv
                // reads the config on every environment change, so the slider is continuous.
                Log.LogInfo($"Settings applied ({reason}).");
            }
            catch (Exception e)
            {
                Log.LogError($"Error applying settings ({reason}): {e}");
            }
        }
    }
}
