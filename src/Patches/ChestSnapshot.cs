using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using UnityEngine;

namespace ValheimTweaks.Patches
{
    /// <summary>
    /// Periodic snapshot of every loaded chest, plus a manual restore.
    ///
    /// ---- Why ----
    /// Items vanishing is the one problem that cannot be undone by code once the item is
    /// gone -- unless there is a record of it. This keeps one: every snapshot writes each
    /// chest's contents, position and id to a small text file. Restore compares the chosen
    /// snapshot with the chests loaded right now and puts back exactly what is missing.
    ///
    /// ---- Scope ----
    /// Only chests loaded around the player are captured; a chest far away (zone not
    /// loaded) is not in the file and cannot be restored from it. Snapshots are small and
    /// rotated, so keeping a few hours of history costs nothing.
    /// </summary>
    internal static class ChestSnapshot
    {
        private static float _next;

        private static string Dir =>
            Path.Combine(Path.GetDirectoryName(Plugin.Instance.Config.ConfigFilePath),
                         "ValheimTweaks-snapshots");

        internal static void Tick()
        {
            if (!ModConfig.ChestSnapshotEnabled.Value) return;
            if (Player.m_localPlayer == null) return;
            if (Time.realtimeSinceStartup < _next) return;

            _next = Time.realtimeSinceStartup + Mathf.Max(0.5f, ModConfig.ChestSnapshotMinutes.Value) * 60f;
            Save();
        }

        // ==================================================================
        // Save
        // ==================================================================
        internal static void Save()
        {
            try
            {
                Directory.CreateDirectory(Dir);

                var sb = new StringBuilder();
                sb.AppendLine("# ValheimTweaks chest snapshot");
                sb.AppendLine("# " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));

                int count = 0;
                foreach (var chest in LoadedChests())
                {
                    var zdo = ZdoOf(chest);
                    var inv = chest.GetInventory();
                    if (zdo == null || inv == null) continue;

                    Vector3 p = chest.transform.position;
                    sb.Append(Key(zdo)).Append('\t')
                      .Append(p.x.ToString("0", CultureInfo.InvariantCulture)).Append(',')
                      .Append(p.z.ToString("0", CultureInfo.InvariantCulture)).Append('\t')
                      .Append((StoreHudPatch.ChestName(chest) ?? "").Replace('\t', ' ')).Append('\t');

                    var items = new StringBuilder();
                    foreach (var item in inv.GetAllItems())
                    {
                        if (item == null || item.m_shared == null) continue;
                        if (items.Length > 0) items.Append(';');
                        items.Append(item.m_shared.m_name).Append('|')
                             .Append(item.m_quality).Append('|')
                             .Append(item.m_stack).Append('|')
                             .Append(item.m_durability.ToString("0.##", CultureInfo.InvariantCulture));
                    }
                    sb.Append(items).AppendLine();
                    count++;
                }

                string file = Path.Combine(Dir, $"snapshot-{DateTime.Now:yyyyMMdd-HHmmss}.txt");
                File.WriteAllText(file, sb.ToString());
                Prune();

                Plugin.Log.LogInfo($"[SNAPSHOT] {count} chest(s) -> {Path.GetFileName(file)}");
            }
            catch (Exception e)
            {
                Plugin.Log.LogError($"[SNAPSHOT] failed: {e.Message}");
            }
        }

        private static void Prune()
        {
            try
            {
                var files = new DirectoryInfo(Dir).GetFiles("snapshot-*.txt");
                Array.Sort(files, (a, b) => string.CompareOrdinal(a.Name, b.Name));
                for (int i = 0; i < files.Length - 30; i++) files[i].Delete();
            }
            catch { /* housekeeping only */ }
        }

        // ==================================================================
        // Restore
        // ==================================================================
        internal static void Restore()
        {
            try
            {
                var dir = new DirectoryInfo(Dir);
                if (!dir.Exists)
                {
                    Plugin.Log.LogWarning("[RESTORE] no snapshot folder yet.");
                    return;
                }

                FileInfo file = null;
                string wanted = ModConfig.RestoreSnapshotFile.Value ?? "";
                if (wanted.Length > 0)
                {
                    var f = new FileInfo(Path.Combine(Dir, wanted));
                    if (f.Exists) file = f;
                    else Plugin.Log.LogWarning($"[RESTORE] file '{wanted}' not found; using the latest.");
                }

                if (file == null)
                {
                    var files = dir.GetFiles("snapshot-*.txt");
                    if (files.Length == 0)
                    {
                        Plugin.Log.LogWarning("[RESTORE] no snapshot to restore from.");
                        return;
                    }
                    Array.Sort(files, (a, b) => string.CompareOrdinal(a.Name, b.Name));
                    file = files[files.Length - 1];
                }

                var saved = Parse(file);

                int restored = 0, chests = 0;
                var skipped = new List<string>();

                foreach (var chest in LoadedChests())
                {
                    var zdo = ZdoOf(chest);
                    var inv = chest.GetInventory();
                    if (zdo == null || inv == null) continue;
                    if (!saved.TryGetValue(Key(zdo), out var want)) continue;

                    // Writing into a chest owned by another player's client is the very thing
                    // that destroys items: the save goes out with an outdated revision, the
                    // network discards it and the chest rolls back. A restore that does that
                    // would look like it worked and quietly recreate nothing -- the worst
                    // possible outcome for the one tool meant to recover a loss. So those
                    // chests are named instead, and the player is told how to make them ours.
                    if (!Chests.Usable(chest))
                    {
                        skipped.Add(Chests.VisibleName(chest) + " " + Key(zdo));
                        continue;
                    }

                    bool touched = false;
                    foreach (var kv in want)
                    {
                        int missing = kv.Value.Stack - CountOf(inv, kv.Value.Name, kv.Value.Quality);
                        if (missing <= 0) continue;

                        var template = Template(kv.Value.Name);
                        if (template == null)
                        {
                            Plugin.Log.LogWarning($"[RESTORE] cannot recreate '{kv.Value.Name}'");
                            continue;
                        }

                        int left = missing;
                        while (left > 0)
                        {
                            int chunk = Mathf.Min(left, Mathf.Max(1, template.m_shared.m_maxStackSize));
                            int before = CountOf(inv, kv.Value.Name, kv.Value.Quality);

                            var data = template.Clone();
                            data.m_quality = kv.Value.Quality;
                            data.m_stack = chunk;
                            data.m_durability = kv.Value.Durability;
                            inv.AddItem(data);

                            int added = CountOf(inv, kv.Value.Name, kv.Value.Quality) - before;
                            if (added <= 0) break;   // chest full
                            left -= added;
                            restored += added;
                            touched = true;
                        }

                        if (touched)
                            Plugin.Log.LogWarning($"[RESTORE] {kv.Value.Name} x{missing} -> "
                                                + $"{Chests.VisibleName(chest)} ({Key(zdo)})");
                    }

                    if (touched) chests++;
                }

                Plugin.Log.LogWarning($"[RESTORE] from {file.Name}: {restored} unit(s) into "
                                    + $"{chests} chest(s). Items in chests whose zone was not "
                                    + "loaded cannot be restored.");

                if (skipped.Count > 0)
                    Plugin.Log.LogWarning(
                        $"[RESTORE] {skipped.Count} chest(s) NOT touched because another player's "
                      + "client owns them, or they are in use, or a guard stone blocks them -- "
                      + "writing there would be discarded by the network. Stand next to them with "
                      + "the other player away, then run RestoreSnapshotNow again: "
                      + string.Join(", ", skipped));
            }
            catch (Exception e)
            {
                Plugin.Log.LogError($"[RESTORE] failed: {e}");
            }
        }

        private class Item
        {
            internal string Name;
            internal int Quality;
            internal int Stack;
            internal float Durability;
        }

        private static Dictionary<string, Dictionary<string, Item>> Parse(FileInfo file)
        {
            var result = new Dictionary<string, Dictionary<string, Item>>();

            foreach (var line in File.ReadAllLines(file.FullName))
            {
                if (line.Length == 0 || line[0] == '#') continue;
                var parts = line.Split('\t');
                if (parts.Length < 4) continue;

                var items = new Dictionary<string, Item>();
                foreach (var entry in parts[3].Split(';'))
                {
                    if (entry.Length == 0) continue;
                    var f = entry.Split('|');
                    if (f.Length < 4) continue;

                    var it = new Item
                    {
                        Name = f[0],
                        Quality = int.Parse(f[1], CultureInfo.InvariantCulture),
                        Stack = int.Parse(f[2], CultureInfo.InvariantCulture),
                        Durability = float.Parse(f[3], CultureInfo.InvariantCulture),
                    };

                    // The snapshot writes one entry per STACK, so the same item at the same
                    // quality shows up several times -- a chest with seven stacks of wood
                    // writes seven entries. Assigning here would keep only the last one and
                    // the restore would put back 50 of 350, silently. Restore compares
                    // against CountOf, which sums every unit of that name and quality, so
                    // the total is what belongs in the key.
                    string id = f[0] + "|" + it.Quality;
                    if (items.TryGetValue(id, out var same)) same.Stack += it.Stack;
                    else items[id] = it;
                }
                result[parts[0]] = items;
            }
            return result;
        }

        // ==================================================================
        // Helpers
        // ==================================================================
        private static IEnumerable<Container> LoadedChests()
            => UnityEngine.Object.FindObjectsByType<Container>(FindObjectsSortMode.None);

        private static ZDO ZdoOf(Container chest)
        {
            var nview = chest != null ? chest.GetComponent<ZNetView>() : null;
            return nview != null ? nview.GetZDO() : null;
        }

        private static string Key(ZDO zdo)
            => zdo.m_uid.UserID.ToString(CultureInfo.InvariantCulture) + ":"
             + zdo.m_uid.ID.ToString(CultureInfo.InvariantCulture);

        private static int CountOf(Inventory inv, string name, int quality)
        {
            int n = 0;
            foreach (var it in inv.GetAllItems())
                if (it != null && it.m_shared != null
                    && it.m_shared.m_name == name && it.m_quality == quality)
                    n += it.m_stack;
            return n;
        }

        private static ItemDrop.ItemData Template(string name)
        {
            var db = ObjectDB.instance;
            if (db == null || db.m_items == null) return null;

            foreach (var go in db.m_items)
            {
                if (go == null) continue;
                var drop = go.GetComponent<ItemDrop>();
                if (drop == null || drop.m_itemData == null || drop.m_itemData.m_shared == null) continue;
                if (drop.m_itemData.m_shared.m_name == name) return drop.m_itemData;
            }
            return null;
        }
    }
}
