using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using UnityEngine;

namespace ValheimTweaks.Patches
{
    /// <summary>
    /// Store items in nearby chests.
    ///
    /// Two modes, both active:
    ///   - mark an item (key over it) and then dump all marked ones
    ///   - store the item under the cursor right away
    ///
    /// ---- Why reuse Container.StackAll instead of touching the inventory ----
    /// Writing directly to a chest's inventory corrupts the save in multiplayer:
    /// another player may have it open, the chest may be inside a protected area,
    /// and the one who owns the data is the owner of the ZDO, which may be another
    /// machine.
    ///
    /// The game already has the handshake for this:
    ///
    ///     Container.StackAll()
    ///       -> RPC_RequestStack  (owner checks IsOwner / IsInUse / CheckAccess)
    ///            -> grants: ForceSendZDO + SetOwner(uid)
    ///                 -> RPC_StackResponse: m_inventory.StackAll(player's inv)
    ///
    /// We call that and let the game handle ownership, chest in use and guard stone.
    /// Since the RPC transfers ownership to us before the StackAll, touching the
    /// inventory inside that window is legitimate -- it's what the game itself does.
    ///
    /// ---- The filter ----
    /// Inventory.StackAll moves everything the destination already contains. To
    /// respect the marking, a Prefix replaces the loop -- but ONLY while our
    /// operation is in progress (s_filterUntil). Outside of it, the game's quick-stack
    /// and that of other mods stays intact. That's how conflicts are avoided.
    ///
    /// Since the RPC is asynchronous, the filter lasts for a time window instead of
    /// a single call: the responses from the various chests arrive on different
    /// frames.
    /// </summary>
    internal static class QuickStorePatch
    {
        private const string MarkKey = "vt_store";

        private static readonly MethodInfo GetHoveredElementMethod =
            AccessTools.Method(typeof(InventoryGrid), "GetHoveredElement");

        // Inventory.Changed is private; it's what notifies the UI and marks the ZDO as dirty.
        private static readonly MethodInfo ChangedMethod =
            AccessTools.Method(typeof(Inventory), "Changed", new[] { typeof(bool), typeof(bool) });

        private static void Notify(Inventory inv)
        {
            if (inv != null) ChangedMethod?.Invoke(inv, new object[] { false, false });
        }

        // Filter active during the operation. null = normal game behavior.
        private static System.Func<ItemDrop.ItemData, bool> s_filter;
        private static float s_filterUntil;
        private static int s_moved;

        private static bool FilterActive => s_filter != null && Time.realtimeSinceStartup < s_filterUntil;

        // ------------------------------------------------------------------
        // Marking (persisted on the item, survives a save and inventory swaps)
        // ------------------------------------------------------------------
        internal static bool IsMarked(ItemDrop.ItemData item)
            => item?.m_customData != null && item.m_customData.ContainsKey(MarkKey);

        private static void ToggleMark(ItemDrop.ItemData item)
        {
            if (item?.m_customData == null) return;

            if (item.m_customData.Remove(MarkKey))
            {
                Toast($"{item.m_shared.m_name} " + Lang.T("unmarked", "desmarcado"));
            }
            else
            {
                item.m_customData[MarkKey] = "1";
                Toast($"{item.m_shared.m_name} " + Lang.T("marked to store", "marcado para guardar"));
            }
            Notify(Player.m_localPlayer?.GetInventory());
        }

        private static void Toast(string text)
        {
            if (Player.m_localPlayer != null)
                Player.m_localPlayer.Message(MessageHud.MessageType.Center,
                    text);
        }

        // ------------------------------------------------------------------
        // Item under the cursor
        // ------------------------------------------------------------------
        private static ItemDrop.ItemData ItemUnderCursor()
        {
            var gui = InventoryGui.instance;
            if (gui == null || gui.m_playerGrid == null || GetHoveredElementMethod == null) return null;

            var el = GetHoveredElementMethod.Invoke(gui.m_playerGrid, null) as InventoryElement;
            if (el == null) return null;

            var inv = gui.m_playerGrid.GetInventory();
            return inv?.GetItemAt(el.Position.x, el.Position.y);
        }

        // ------------------------------------------------------------------
        // Dump
        // ------------------------------------------------------------------
        private static List<Container> NearbyChests()
        {
            var found = new List<Container>();
            var player = Player.m_localPlayer;
            if (player == null) return found;

            int mask = LayerMask.GetMask("piece", "piece_nonsolid");
            var colliders = Physics.OverlapSphere(
                player.transform.position, ModConfig.StoreRadius.Value, mask);

            foreach (var c in colliders)
            {
                var cont = c.GetComponentInParent<Container>();
                if (cont == null || found.Contains(cont)) continue;

                // A chest without a valid ZDO is still loading; skipping avoids a lost RPC.
                var nview = cont.GetComponent<ZNetView>();
                if (nview == null || !nview.IsValid()) continue;

                found.Add(cont);
            }
            return found;
        }

        // Inventory -> Container. The StackAll Prefix only receives the inventory, and
        // we need the Container to find out the chest's name.
        private static readonly Dictionary<Inventory, Container> Owner =
            new Dictionary<Inventory, Container>();

        private static void Dump(System.Func<ItemDrop.ItemData, bool> filter, string what)
        {
            var chests = NearbyChests();
            if (chests.Count == 0)
            {
                Toast(Lang.T("No chest nearby", "Nenhum baú por perto"));
                return;
            }

            Owner.Clear();
            foreach (var b in chests)
            {
                var inv = b.GetInventory();
                if (inv != null) Owner[inv] = b;
            }

            s_filter = filter;
            s_moved = 0;
            // Generous window: each chest responds on its own frame.
            s_filterUntil = Time.realtimeSinceStartup + 3f;

            foreach (var chest in chests) chest.StackAll();

            Plugin.Log.LogInfo($"[STORE] {what} -> {chests.Count} chest(s) in {ModConfig.StoreRadius.Value}m");
        }

        // ------------------------------------------------------------------
        // Replaces the StackAll loop while our operation runs
        // ------------------------------------------------------------------
        [HarmonyPatch(typeof(Inventory), nameof(Inventory.StackAll))]
        internal static class StackAllHook
        {
            private static bool Prefix(Inventory __instance, Inventory fromInventory,
                                       ref int __result)
            {
                if (!FilterActive) return true; // normal game, we don't interfere

                var player = Player.m_localPlayer;
                if (player == null) return true;

                int moved = 0;
                var items = new List<ItemDrop.ItemData>(fromInventory.GetAllItems());

                Owner.TryGetValue(__instance, out var chest);
                string chestName = StoreHudPatch.ChestName(chest);

                // 1st pass: only where the chest ALREADY has the item (game behavior).
                foreach (var item in items)
                {
                    if (!s_filter(item) || player.IsItemEquiped(item)) continue;
                    if (!__instance.ContainsItemByName(item.m_shared.m_name)) continue;

                    int amount = item.m_stack;
                    // Remove the mark before moving: it's only valid while the item is yours.
                    item.m_customData?.Remove(MarkKey);
                    if (__instance.AddItem(item))
                    {
                        fromInventory.RemoveItem(item);
                        moved++;
                        StoreHudPatch.Add(item, amount, chestName);
                    }
                }

                // 2nd pass (optional): any chest with space.
                if (ModConfig.StoreFallbackAnyChest.Value)
                {
                    foreach (var item in new List<ItemDrop.ItemData>(fromInventory.GetAllItems()))
                    {
                        if (!s_filter(item) || player.IsItemEquiped(item)) continue;

                        int amount = item.m_stack;
                        if (__instance.AddItem(item))
                        {
                            fromInventory.RemoveItem(item);
                            moved++;
                            StoreHudPatch.Add(item, amount, chestName);
                        }
                    }
                }

                if (moved > 0)
                {
                    Notify(__instance);
                    Notify(fromInventory);
                    s_moved += moved;
                    Toast(string.Format(
                        Lang.T("{0} item(s) stored", "{0} item(ns) guardado(s)"), s_moved));
                }

                __result = moved;
                return false; // skips the original
            }
        }

        // ------------------------------------------------------------------
        // Visual mark: tints the icon of the marked slot
        // ------------------------------------------------------------------
        [HarmonyPatch(typeof(InventoryGrid), "UpdateGui")]
        internal static class UpdateGuiHook
        {
            private static readonly Color Tint = new Color(0.55f, 0.85f, 1f, 1f);

            private static void Postfix(InventoryGrid __instance)
            {
                var inv = __instance.GetInventory();
                if (inv == null) return;

                // The mark lives on the item itself (m_customData), so it travels along
                // when it goes to the chest -- and it turned blue in there. The mark only
                // makes sense in YOUR inventory: outside of it, don't even tint.
                var gui = InventoryGui.instance;
                bool isPlayerGrid = gui != null && __instance == gui.m_playerGrid;
                if (!isPlayerGrid)
                {
                    foreach (var el in __instance.GetComponentsInChildren<InventoryElement>(true))
                        if (el.m_icon != null) el.m_icon.color = Color.white;
                    return;
                }

                foreach (var el in __instance.GetComponentsInChildren<InventoryElement>(true))
                {
                    if (el.m_icon == null || !el.m_icon.enabled) continue;

                    var item = inv.GetItemAt(el.Position.x, el.Position.y);
                    // Always rewrite, both ways: the game reuses the elements between
                    // openings and a tinted icon would get stuck.
                    el.m_icon.color = (item != null && IsMarked(item)) ? Tint : Color.white;
                }
            }
        }

        // ------------------------------------------------------------------
        // Keys
        // ------------------------------------------------------------------
        internal static void Update()
        {
            if (!InventoryGui.IsVisible()) return;
            if (Chat.instance != null && Chat.instance.HasFocus()) return;
            if (Console.IsVisible() || TextInput.IsVisible()) return;

            if (ModConfig.StoreMarkKey.Value.IsDown())
            {
                var item = ItemUnderCursor();
                if (item != null) ToggleMark(item);
            }

            if (ModConfig.StoreHoveredKey.Value.IsDown())
            {
                var item = ItemUnderCursor();
                if (item != null) Dump(i => i == item, item.m_shared.m_name);
            }

            if (ModConfig.StoreMarkedKey.Value.IsDown())
            {
                Dump(IsMarked, "marked items");
            }
        }
    }
}
