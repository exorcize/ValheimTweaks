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
    /// That is attribution, not proof: the other client can claim a chest and empty it fast
    /// enough that both reach us in one update, and that reads as an overwrite. The guards
    /// below bound the damage of being wrong -- never more than the move put in, never what
    /// came back to the backpack by another route. For a loss we cannot pin down we only
    /// report; the snapshot is the recovery that works from a record instead of an inference.
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
            internal ItemDrop.ItemData Sample;
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
                                   string key, int units, ItemDrop.ItemData sample)
        {
            if (!ModConfig.StoreRollback.Value) return;
            if (chest == null || destination == null || source == null || sample == null) return;
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
                Sample = sample.Clone(),
                Expires = Time.realtimeSinceStartup
                        + Mathf.Max(1f, ModConfig.StoreVerifySeconds.Value),
            });
        }

        /// <summary>
        /// The chest just read new contents off its ZDO. This is the only moment a store can
        /// be undone, so it is the only moment worth looking.
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
                        $"[CHESTS] {missing}x '{w.Key}' left '{where}' after being stored, but the "
                      + "chest was another player's by then, so they may have taken it. Not putting "
                      + "it back. If nobody did, RestoreSnapshotNow recovers it.");
                    continue;
                }

                Plugin.Log.LogError(
                    $"[CHESTS] OVERWRITTEN: the network discarded {missing}x '{w.Key}' stored into "
                  + $"'{where}' (our revision {wrote} replaced by {now} while the chest was still "
                  + "ours, so nobody could have taken it). Returning it to your backpack.");

                GiveBack(w, missing, where);
            }
        }

        private static void GiveBack(Watch w, int missing, string where)
        {
            int maxStack = Mathf.Max(1, w.Sample.m_shared.m_maxStackSize);
            int left = missing;
            int before = Count(w.Source, w.Key);
            int start = before;

            while (left > 0)
            {
                var part = w.Sample.Clone();
                part.m_stack = Mathf.Min(left, maxStack);
                w.Source.AddItem(part);

                int added = Count(w.Source, w.Key) - before;
                if (added <= 0) break;   // backpack has no room
                before += added;
                left -= added;
            }

            // Two stores into the same chest are two watches, and one overwrite kills both.
            // The one still pending would see the units this restore just added and take them
            // for the player having fetched the item themselves, then discount its own claim
            // to nothing -- 10 of 20 recovered. Its baseline has to move with the backpack.
            int returned = before - start;
            if (returned > 0)
                foreach (var other in _watches)
                    if (other.Source == w.Source && other.Key == w.Key)
                        other.SourceTotal += returned;

            if (left > 0)
                Plugin.Log.LogError($"[CHESTS] LOST {left}x '{w.Key}': it was discarded from "
                                  + $"'{where}' and your backpack had no room for it.");
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
        /// </summary>
        [HarmonyPatch(typeof(Container), "Load")]
        internal static class LoadHook
        {
            private static void Postfix(Container __instance) => OnLoaded(__instance);
        }
    }
}
