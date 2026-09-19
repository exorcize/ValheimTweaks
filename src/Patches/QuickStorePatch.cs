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
    /// operation is in progress (s_filtroAte). Outside of it, the game's quick-stack
    /// and that of other mods stays intact. That's how conflicts are avoided.
    ///
    /// Since the RPC is asynchronous, the filter lasts for a time window instead of
    /// a single call: the responses from the various chests arrive on different
    /// frames.
    /// </summary>
    internal static class QuickStorePatch
    {
        private const string ChaveMarca = "vt_store";

        private static readonly MethodInfo GetHoveredElementMethod =
            AccessTools.Method(typeof(InventoryGrid), "GetHoveredElement");

        // Inventory.Changed is private; it's what notifies the UI and marks the ZDO as dirty.
        private static readonly MethodInfo ChangedMethod =
            AccessTools.Method(typeof(Inventory), "Changed", new[] { typeof(bool), typeof(bool) });

        private static void Notificar(Inventory inv)
        {
            if (inv != null) ChangedMethod?.Invoke(inv, new object[] { false, false });
        }

        // Filter active during the operation. null = normal game behavior.
        private static System.Func<ItemDrop.ItemData, bool> s_filtro;
        private static float s_filtroAte;
        private static int s_movidos;

        private static bool FiltroAtivo => s_filtro != null && Time.realtimeSinceStartup < s_filtroAte;

        // ------------------------------------------------------------------
        // Marking (persisted on the item, survives a save and inventory swaps)
        // ------------------------------------------------------------------
        internal static bool EstaMarcado(ItemDrop.ItemData item)
            => item?.m_customData != null && item.m_customData.ContainsKey(ChaveMarca);

        private static void AlternarMarca(ItemDrop.ItemData item)
        {
            if (item?.m_customData == null) return;

            if (item.m_customData.Remove(ChaveMarca))
            {
                Aviso($"{item.m_shared.m_name} " + Lang.T("unmarked", "desmarcado"));
            }
            else
            {
                item.m_customData[ChaveMarca] = "1";
                Aviso($"{item.m_shared.m_name} " + Lang.T("marked to store", "marcado para guardar"));
            }
            Notificar(Player.m_localPlayer?.GetInventory());
        }

        private static void Aviso(string texto)
        {
            if (Player.m_localPlayer != null)
                Player.m_localPlayer.Message(MessageHud.MessageType.Center,
                    texto);
        }

        // ------------------------------------------------------------------
        // Item under the cursor
        // ------------------------------------------------------------------
        private static ItemDrop.ItemData ItemSobCursor()
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
        private static List<Container> BausProximos()
        {
            var achados = new List<Container>();
            var player = Player.m_localPlayer;
            if (player == null) return achados;

            int mascara = LayerMask.GetMask("piece", "piece_nonsolid");
            var colisores = Physics.OverlapSphere(
                player.transform.position, ModConfig.StoreRadius.Value, mascara);

            foreach (var c in colisores)
            {
                var cont = c.GetComponentInParent<Container>();
                if (cont == null || achados.Contains(cont)) continue;

                // A chest without a valid ZDO is still loading; skipping avoids a lost RPC.
                var nview = cont.GetComponent<ZNetView>();
                if (nview == null || !nview.IsValid()) continue;

                achados.Add(cont);
            }
            return achados;
        }

        // Inventory -> Container. The StackAll Prefix only receives the inventory, and
        // we need the Container to find out the chest's name.
        private static readonly Dictionary<Inventory, Container> Dono =
            new Dictionary<Inventory, Container>();

        private static void Despejar(System.Func<ItemDrop.ItemData, bool> filtro, string oQue)
        {
            var baus = BausProximos();
            if (baus.Count == 0)
            {
                Aviso(Lang.T("No chest nearby", "Nenhum baú por perto"));
                return;
            }

            Dono.Clear();
            foreach (var b in baus)
            {
                var inv = b.GetInventory();
                if (inv != null) Dono[inv] = b;
            }

            s_filtro = filtro;
            s_movidos = 0;
            // Generous window: each chest responds on its own frame.
            s_filtroAte = Time.realtimeSinceStartup + 3f;

            foreach (var bau in baus) bau.StackAll();

            Plugin.Log.LogInfo($"[STORE] {oQue} -> {baus.Count} chest(s) in {ModConfig.StoreRadius.Value}m");
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
                if (!FiltroAtivo) return true; // normal game, we don't interfere

                var player = Player.m_localPlayer;
                if (player == null) return true;

                int movidos = 0;
                var itens = new List<ItemDrop.ItemData>(fromInventory.GetAllItems());

                Dono.TryGetValue(__instance, out var bau);
                string nomeBau = StoreHudPatch.NomeDoBau(bau);

                // 1st pass: only where the chest ALREADY has the item (game behavior).
                foreach (var item in itens)
                {
                    if (!s_filtro(item) || player.IsItemEquiped(item)) continue;
                    if (!__instance.ContainsItemByName(item.m_shared.m_name)) continue;

                    int qtd = item.m_stack;
                    // Remove the mark before moving: it's only valid while the item is yours.
                    item.m_customData?.Remove(ChaveMarca);
                    if (__instance.AddItem(item))
                    {
                        fromInventory.RemoveItem(item);
                        movidos++;
                        StoreHudPatch.Adicionar(item, qtd, nomeBau);
                    }
                }

                // 2nd pass (optional): any chest with space.
                if (ModConfig.StoreFallbackAnyChest.Value)
                {
                    foreach (var item in new List<ItemDrop.ItemData>(fromInventory.GetAllItems()))
                    {
                        if (!s_filtro(item) || player.IsItemEquiped(item)) continue;

                        int qtd = item.m_stack;
                        if (__instance.AddItem(item))
                        {
                            fromInventory.RemoveItem(item);
                            movidos++;
                            StoreHudPatch.Adicionar(item, qtd, nomeBau);
                        }
                    }
                }

                if (movidos > 0)
                {
                    Notificar(__instance);
                    Notificar(fromInventory);
                    s_movidos += movidos;
                    Aviso(string.Format(
                        Lang.T("{0} item(s) stored", "{0} item(ns) guardado(s)"), s_movidos));
                }

                __result = movidos;
                return false; // skips the original
            }
        }

        // ------------------------------------------------------------------
        // Visual mark: tints the icon of the marked slot
        // ------------------------------------------------------------------
        [HarmonyPatch(typeof(InventoryGrid), "UpdateGui")]
        internal static class UpdateGuiHook
        {
            private static readonly Color Tom = new Color(0.55f, 0.85f, 1f, 1f);

            private static void Postfix(InventoryGrid __instance)
            {
                var inv = __instance.GetInventory();
                if (inv == null) return;

                // The mark lives on the item itself (m_customData), so it travels along
                // when it goes to the chest -- and it turned blue in there. The mark only
                // makes sense in YOUR inventory: outside of it, don't even tint.
                var gui = InventoryGui.instance;
                bool ehGradeDoJogador = gui != null && __instance == gui.m_playerGrid;
                if (!ehGradeDoJogador)
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
                    el.m_icon.color = (item != null && EstaMarcado(item)) ? Tom : Color.white;
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
                var item = ItemSobCursor();
                if (item != null) AlternarMarca(item);
            }

            if (ModConfig.StoreHoveredKey.Value.IsDown())
            {
                var item = ItemSobCursor();
                if (item != null) Despejar(i => i == item, item.m_shared.m_name);
            }

            if (ModConfig.StoreMarkedKey.Value.IsDown())
            {
                Despejar(EstaMarcado, "marked items");
            }
        }
    }
}
