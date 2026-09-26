using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;

namespace ValheimTweaks.Patches
{
    /// <summary>
    /// Watches a store until the network either keeps it or throws it away, and says which.
    ///
    /// ---- Why not a timer ----
    /// The first version of this looked at the chest 2s after the move and, later, at 10s and
    /// 30s too. Both halves were wrong. Too early, because the overwrite arrives when the
    /// chest changes owner and that was 27s to 3min away in a real session. And blind, because
    /// counting items on a clock cannot tell "the network discarded my write" from "somebody
    /// took it": ten pine cones stored and taken back by hand looked exactly like a loss, and
    /// the check handed out ten more.
    ///
    /// ---- What the revision gives us ----
    /// Saving a chest runs ZDO.Set, which does DataRevision++. So our write has a number. From
    /// then on, every update the network accepts for that chest replaces the contents we wrote
    /// (ZDOMan.RPC_ZDOData deserializes only when num4 > DataRevision, and Container.Load then
    /// copies it into the inventory). So we do not have to guess WHEN to look: we look exactly
    /// when the revision moves off ours, which is the only moment our write can be undone.
    ///
    /// ---- Telling an overwrite from a withdrawal ----
    /// Only the owner may write to a chest. So if the update that removed our items arrived
    /// while WE were still the owner as far as we had seen, nobody else was in a position to
    /// have taken them, and what we are looking at is our own write being rolled back. Once we
    /// see the chest handed to someone else with our items still in it, they can take them
    /// legitimately and we stop claiming anything.
    ///
    /// ---- Why it reports and never puts anything back ----
    /// The attribution above is not proof, and two holes showed up that cannot be closed from
    /// one machine. RPC_ZDOData applies an ownership change on its own, with no data and so no
    /// Load, which means the chest can become someone else's without us ever seeing it; a
    /// withdrawal after that reads as an overwrite. And the other client can claim a chest and
    /// empty it fast enough that both reach us in a single update.
    ///
    /// Recreating an item on a signal that good-but-not-certain trades a loss we can name for a
    /// duplication we cannot see. This file used to put the item back, which was the wrong way
    /// round -- so it states the loss precisely, at the moment it happens, with the chest and the
    /// amount, and leaves recovery to the snapshot, which restores from a record instead of an
    /// inference.
    /// </summary>
    internal static class WriteConfirm
    {
        private class Watch
        {
            internal Container Chest;
            internal Inventory Source;
            internal string Key;
            /// <summary>Units this move put in: the ceiling on anything we may claim back.</summary>
            internal int Units;
            /// <summary>The chest's total for this item right after our save.</summary>
            internal int Total;
            /// <summary>Units in the backpack right after our save.</summary>
            internal int SourceTotal;
            /// <summary>The revision our save produced. The contents are ours while this holds.</summary>
            internal uint Revision;
            /// <summary>Was the chest ours at the last update we saw? Drives the attribution.</summary>
            internal bool WasOurs;
            internal float Expires;
        }

        private static readonly List<Watch> _watches = new List<Watch>();

        private static bool OwnedByUs(Container chest)
        {
            var nview = chest != null ? chest.GetComponent<ZNetView>() : null;
            return nview != null && nview.IsValid() && nview.IsOwner();
        }

        private static int Count(Inventory inv, string key)
        {
            if (inv == null) return 0;
            int n = 0;
            foreach (var it in inv.GetAllItems())
                if (ChestSearchPatch.KeyOf(it) == key) n += it.m_stack;
            return n;
        }

        /// <summary>
        /// Starts watching a store. Call it right after the move, when the save has already
        /// run and the revision is the one our write produced.
        /// </summary>
        internal static void Track(Container chest, Inventory destination, Inventory source,
                                   string key, int units)
        {
            if (!ModConfig.StoreRollback.Value) return;
            if (chest == null || destination == null || source == null) return;
            if (units <= 0) return;

            _watches.Add(new Watch
            {
                Chest = chest,
                Source = source,
                Key = key,
                Units = units,
                Total = Count(destination, key),
                SourceTotal = Count(source, key),
                Revision = StoreDeferral.Revision(chest),
                WasOurs = OwnedByUs(chest),
                Expires = Time.realtimeSinceStartup
                        + Mathf.Max(1f, ModConfig.StoreVerifySeconds.Value),
            });
        }

        /// <summary>
        /// The chest just read new contents off its ZDO -- from the NETWORK, not from a save
        /// made on this machine. This is the only moment a store can be undone, so it is the
        /// only moment worth looking.
        /// </summary>
        internal static void OnLoaded(Container chest)
        {
            if (_watches.Count == 0 || chest == null) return;

            for (int i = _watches.Count - 1; i >= 0; i--)
            {
                var w = _watches[i];
                if (w.Chest != chest) continue;

                uint now = StoreDeferral.Revision(chest);
                bool ours = OwnedByUs(chest);

                // Still the revision we wrote: this Load changed nothing about our items.
                if (now == w.Revision) { w.WasOurs = ours; continue; }

                int have = Count(chest.GetInventory(), w.Key);
                int gone = w.Total - have;

                if (gone <= 0)
                {
                    // Our write survived this update. Adopt the new revision and keep watching:
                    // the handover that erases it may still be ahead.
                    w.Revision = now;
                    w.Total = have;
                    w.WasOurs = ours;
                    continue;
                }

                // Whatever came back to the backpack by another route was not lost.
                int back = Count(w.Source, w.Key) - w.SourceTotal;
                int missing = Mathf.Clamp(gone - Mathf.Max(0, back), 0, w.Units);

                uint wrote = w.Revision;
                _watches.RemoveAt(i);
                if (missing <= 0) continue;

                string where = Chests.VisibleName(chest);

                if (!w.WasOurs)
                {
                    Plugin.Log.LogWarning(
                        $"[CHESTS] {missing}x '{w.Key}' left '{where}' after being stored, and the "
                      + "chest was another player's by then, so they may simply have taken it.");
                    continue;
                }

                Plugin.Log.LogError(
                    $"[CHESTS] OVERWRITTEN: {missing}x '{w.Key}' stored into '{where}' is gone -- "
                  + $"the network replaced our revision {wrote} with {now} while the chest still "
                  + "looked ours. Nothing was put back, because this cannot be told apart from the "
                  + "other player having taken it. RestoreSnapshotNow recovers it from the last "
                  + "snapshot if nobody did.");
            }
        }

        /// <summary>Retires watches that ran out of time, reporting a loss we never saw land.</summary>
        internal static void Tick()
        {
            if (_watches.Count == 0) return;

            float now = Time.realtimeSinceStartup;
            for (int i = _watches.Count - 1; i >= 0; i--)
            {
                var w = _watches[i];
                if (w.Chest != null && now < w.Expires) continue;

                _watches.RemoveAt(i);
                if (w.Chest == null) continue;   // zone unloaded; nothing observable

                // A last look, for a loss that happened without the chest ever reloading.
                int gone = w.Total - Count(w.Chest.GetInventory(), w.Key);
                int back = Count(w.Source, w.Key) - w.SourceTotal;
                int missing = Mathf.Clamp(gone - Mathf.Max(0, back), 0, w.Units);

                if (missing > 0)
                    Plugin.Log.LogWarning(
                        $"[CHESTS] {missing}x '{w.Key}' is no longer in "
                      + $"'{Chests.VisibleName(w.Chest)}' and no network update explained it. Not "
                      + "putting it back; RestoreSnapshotNow recovers it if nobody took it.");
            }
        }

        /// <summary>
        /// Container.Load is where the ZDO becomes the inventory, so it is where an overwrite
        /// becomes visible. Separate from the audit's hook on purpose: this one has to run
        /// whether or not the audit log is turned on.
        ///
        /// ---- Why the return value is the whole point ----
        /// Load answers false when DataRevision already equals m_lastRevision, and a save made
        /// on THIS machine sets m_lastRevision as it writes. So a local change -- ours, or
        /// another mod's -- bumps the revision and then makes Load a no-op, while only data
        /// that came off the network makes it return true and actually replace the inventory.
        ///
        /// Comparing revisions alone would therefore read any local write as an overwrite.
        /// SmartCraftStorage is exactly that case: it takes materials straight out of a
        /// chest's inventory for crafting, fuel and feeding, which runs Container.Save. Store
        /// 40 wood with this mod, let it spend that wood from the same chest, and the check
        /// would have "recovered" 40 wood into the backpack out of nothing.
        /// </summary>
        [HarmonyPatch(typeof(Container), "Load")]
        internal static class LoadHook
        {
            private static void Postfix(Container __instance, bool __result)
            {
                if (__result) OnLoaded(__instance);
            }
        }
    }
}
