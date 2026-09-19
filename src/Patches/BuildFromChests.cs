using System.Collections.Generic;
using HarmonyLib;
using TMPro;
using UnityEngine;

namespace ValheimTweaks.Patches
{
    /// <summary>
    /// Building with the hammer draws materials from nearby chests, not only the backpack.
    ///
    /// ---- How it hooks in ----
    /// The game gates building in two places, and both look only at m_inventory:
    ///
    ///     Player.HaveRequirements(Piece, RequirementMode) -> Inventory.CountItems / HaveItem
    ///     Player.ConsumeResources(...)                    -> Inventory.RemoveItem(name, amount, ...)
    ///
    /// Reimplementing those checks ourselves would mean copying the station, DLC and
    /// discovery rules and getting one of them wrong. Instead the chests are made to
    /// look like part of the backpack for the duration of the call:
    ///
    ///   - while HaveRequirements runs, CountItems/HaveItem add the nearby chest total
    ///   - while a placement runs, RemoveItem takes the shortfall from the chests and
    ///     lets vanilla remove the rest from the backpack
    ///
    /// The game's own logic stays the single source of truth.
    ///
    /// ---- Why a cache ----
    /// HaveRequirements runs once per build piece for the whole HUD list, many times per
    /// second. An OverlapSphere per call would be hundreds of physics queries per frame.
    /// We scan at most every RefreshSeconds (or after walking a couple of meters) and
    /// precompute a name -> count table, so the usual lookup is O(1).
    ///
    /// ---- Why only owned chests ----
    /// Chest contents belong to the ZDO owner. Removing from a chest you do not own
    /// desyncs it. The check and the removal share the same eligible set, so a build can
    /// never be allowed and then fail to pay.
    /// </summary>
    internal static class BuildFromChests
    {
        private static readonly List<Container> _chests = new List<Container>();
        private static readonly Dictionary<string, int> _totals = new Dictionary<string, int>();
        private static float _nextScan;
        private static Vector3 _lastPos;

        private static bool _checking;    // inside Player.HaveRequirements(Piece, ...)
        private static bool _consuming;   // inside Player.UpdatePlacement, while in build mode

        private static bool Enabled => ModConfig.BuildFromChestsEnabled.Value;

        internal static void Invalidate() => _nextScan = 0f;

        // ==================================================================
        // Cache
        // ==================================================================
        private static void EnsureFresh()
        {
            var player = Player.m_localPlayer;
            if (player == null)
            {
                _chests.Clear();
                _totals.Clear();
                return;
            }

            float now = Time.realtimeSinceStartup;
            if (now < _nextScan
                && (player.transform.position - _lastPos).sqrMagnitude < 4f)
                return;

            _nextScan = now + Mathf.Max(0.2f, ModConfig.BuildFromChestsRefresh.Value);
            _lastPos = player.transform.position;

            _chests.Clear();
            _totals.Clear();

            int mask = LayerMask.GetMask("piece", "piece_nonsolid");
            var colliders = Physics.OverlapSphere(
                player.transform.position, ModConfig.BuildFromChestsRadius.Value, mask);

            foreach (var c in colliders)
            {
                var cont = c.GetComponentInParent<Container>();
                if (cont == null || _chests.Contains(cont)) continue;
                if (!Eligible(cont)) continue;

                _chests.Add(cont);

                var inv = cont.GetInventory();
                if (inv == null) continue;

                foreach (var item in inv.GetAllItems())
                {
                    if (item == null || item.m_shared == null) continue;
                    if (item.m_worldLevel < Game.m_worldLevel) continue;

                    string name = item.m_shared.m_name;
                    _totals.TryGetValue(name, out int current);
                    _totals[name] = current + item.m_stack;
                }
            }
        }

        private static bool Eligible(Container cont) => Chests.Usable(cont);

        /// <summary>How much of <paramref name="name"/> the nearby chests hold.</summary>
        private static int ContainerCount(string name, int quality, bool matchWorldLevel)
        {
            if (string.IsNullOrEmpty(name)) return 0;
            EnsureFresh();

            // Building always asks for any quality at or above the world level, which is
            // exactly what the aggregate holds.
            if (quality < 0 && matchWorldLevel)
            {
                _totals.TryGetValue(name, out int total);
                return total;
            }

            int sum = 0;
            foreach (var cont in _chests)
            {
                var inv = cont.GetInventory();
                if (inv != null) sum += inv.CountItems(name, quality, matchWorldLevel);
            }
            return sum;
        }

        /// <summary>
        /// Removes up to <paramref name="amount"/> from the chests and returns how much
        /// actually came out. Called only for the shortfall the backpack cannot cover.
        /// </summary>
        private static int RemoveFromChests(string name, int amount, int quality, bool worldLevelBased)
        {
            if (amount <= 0) return 0;
            EnsureFresh();

            int removed = 0;
            foreach (var cont in _chests)
            {
                if (removed >= amount) break;

                var inv = cont.GetInventory();
                if (inv == null) continue;

                int have = inv.CountItems(name, quality, worldLevelBased);
                if (have <= 0) continue;

                int take = Mathf.Min(have, amount - removed);

                // Measure the removal instead of assuming it: if the game takes less
                // than asked, only what actually left is credited.
                inv.RemoveItem(name, take, quality, worldLevelBased);
                int left = inv.CountItems(name, quality, worldLevelBased);
                removed += Mathf.Max(0, have - left);
            }

            if (removed > 0) Invalidate();   // the aggregate is stale now
            return removed;
        }

        // ==================================================================
        // HaveRequirements: chests count for CountItems / HaveItem
        // ==================================================================
        [HarmonyPatch(typeof(Player), "HaveRequirements", new[] { typeof(Piece), typeof(Player.RequirementMode) })]
        internal static class HaveRequirementsHook
        {
            private static void Prefix() { _checking = Enabled && Player.m_localPlayer != null; }
            private static void Postfix() { _checking = false; }
        }

        [HarmonyPatch(typeof(Inventory), "CountItems", new[] { typeof(string), typeof(int), typeof(bool) })]
        internal static class CountItemsHook
        {
            private static void Postfix(Inventory __instance, string name, int quality,
                                        bool matchWorldLevel, ref int __result)
            {
                if (!_checking) return;
                if (__instance != Player.m_localPlayer?.GetInventory()) return;
                __result += ContainerCount(name, quality, matchWorldLevel);
            }
        }

        [HarmonyPatch(typeof(Inventory), "HaveItem", new[] { typeof(string), typeof(bool) })]
        internal static class HaveItemHook
        {
            private static void Postfix(Inventory __instance, string name,
                                        bool matchWorldLevel, ref bool __result)
            {
                if (__result || !_checking) return;
                if (__instance != Player.m_localPlayer?.GetInventory()) return;
                __result = ContainerCount(name, -1, matchWorldLevel) > 0;
            }
        }

        // ==================================================================
        // ConsumeResources: pay the shortfall from the chests
        // ==================================================================
        // The window is UpdatePlacement, not ConsumeResources. ConsumeResources is a
        // small method and Mono may inline it into its caller -- an inlined method
        // never passes through a Harmony detour, and the flag would stay false, letting
        // the piece be built for free. UpdatePlacement is large and drives both the
        // check and the consumption of a placement, so it is a safe window.
        [HarmonyPatch(typeof(Player), "UpdatePlacement", new[] { typeof(bool), typeof(float) })]
        internal static class UpdatePlacementHook
        {
            private static void Prefix(Player __instance)
            {
                // InPlaceMode() tells building apart from crafting at a station, so
                // crafting keeps using only the backpack.
                _consuming = Enabled
                          && ReferenceEquals(__instance, Player.m_localPlayer)
                          && __instance.InPlaceMode();
            }

            private static void Postfix() { _consuming = false; }
        }

        [HarmonyPatch(typeof(Inventory), "RemoveItem",
                      new[] { typeof(string), typeof(int), typeof(int), typeof(bool) })]
        internal static class RemoveItemHook
        {
            private static void Prefix(Inventory __instance, string name, ref int amount,
                                       int itemQuality, bool worldLevelBased)
            {
                if (!_consuming || amount <= 0) return;
                if (__instance != Player.m_localPlayer?.GetInventory()) return;

                int inBackpack = __instance.CountItems(name, itemQuality, worldLevelBased);
                if (inBackpack >= amount) return;

                int pulled = RemoveFromChests(name, amount - inBackpack, itemQuality, worldLevelBased);
                amount -= pulled;   // vanilla removes only what the backpack still holds
            }
        }

        // ==================================================================
        // HUD: the build material list turns white when the chests cover it
        // ==================================================================
        [HarmonyPatch(typeof(InventoryGui), "SetupRequirement",
                      new[] { typeof(Transform), typeof(Piece.Requirement), typeof(Player),
                              typeof(bool), typeof(int), typeof(int) })]
        internal static class SetupRequirementHook
        {
            private static void Postfix(Transform elementRoot, Piece.Requirement req, Player player,
                                        bool craft, int quality, int craftMultiplier, bool __result)
            {
                if (!__result || !Enabled || req?.m_resItem == null) return;
                if (player == null || !player.InPlaceMode()) return;

                string name = req.m_resItem.m_itemData.m_shared.m_name;
                int need = req.GetAmount(quality) * craftMultiplier;
                if (need <= 0) return;

                int have = player.GetInventory().CountItems(name, -1, true)
                         + ContainerCount(name, -1, true);
                if (have < need) return;

                var amount = elementRoot.Find("res_amount")?.GetComponent<TMP_Text>();
                if (amount != null) amount.color = Color.white;
            }
        }
    }
}
