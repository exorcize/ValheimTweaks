using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using UnityEngine;

namespace ValheimTweaks.Patches
{
    /// <summary>
    /// Repair everything at once.
    ///
    /// The game repairs one item per click: InventoryGui.RepairOneItem walks the
    /// worn pieces, repairs the first one it can and exits on the return. With
    /// full equipment that becomes five or six clicks every time you pass by the
    /// workbench.
    ///
    /// Repairing in Valheim consumes no material -- it only requires the right
    /// station. So doing everything at once is convenience, not an advantage: the
    /// result is the same as clicking until you're done.
    ///
    /// All local: it touches the durability of your own inventory, nothing goes to
    /// the network.
    /// </summary>
    internal static class RepairAllPatch
    {
        // CanRepair is private and concentrates the rules (repairable item, right
        // station, known recipe, world level). Reusing it avoids duplicating that
        // logic and getting some case wrong.
        private static readonly MethodInfo CanRepairMethod =
            AccessTools.Method(typeof(InventoryGui), "CanRepair", new[] { typeof(ItemDrop.ItemData) });

        private static readonly List<ItemDrop.ItemData> Buffer = new List<ItemDrop.ItemData>();

        /// <summary>Repairs everything it can. Returns how many items were repaired.</summary>
        private static int RepairAll(InventoryGui gui)
        {
            var player = Player.m_localPlayer;
            if (player == null || gui == null || CanRepairMethod == null) return 0;

            var station = player.GetCurrentCraftingStation();
            if (station == null && !player.NoCostCheat()) return 0;
            if (station != null && !station.CheckUsable(player, showMessage: false)) return 0;

            Buffer.Clear();
            player.GetInventory().GetWornItems(Buffer);

            int reparados = 0;
            foreach (var item in Buffer)
            {
                if (!(bool)CanRepairMethod.Invoke(gui, new object[] { item })) continue;

                // Same Crafting progression that a normal repair would give.
                player.RaiseSkill(Skills.SkillType.Crafting,
                    1f - item.m_durability / item.GetMaxDurability());
                item.m_durability = item.GetMaxDurability();
                reparados++;
            }

            if (reparados > 0 && station != null)
            {
                station.m_repairItemDoneEffects.Create(station.transform.position, Quaternion.identity);
            }

            return reparados;
        }

        private static void Avisar(int n)
        {
            if (n <= 0 || Player.m_localPlayer == null) return;
            Player.m_localPlayer.Message(MessageHud.MessageType.Center,
                n == 1
                    ? Lang.T("1 item repaired", "1 item reparado")
                    : string.Format(Lang.T("{0} items repaired", "{0} itens reparados"), n));
        }

        /// <summary>Repairs when opening the panel near a station.</summary>
        [HarmonyPatch(typeof(InventoryGui), nameof(InventoryGui.Show))]
        internal static class ShowHook
        {
            private static void Postfix(InventoryGui __instance)
            {
                if (!ModConfig.AutoRepairOnOpen.Value) return;
                Avisar(RepairAll(__instance));
            }
        }

        /// <summary>Makes the repair button fix everything instead of one item.</summary>
        [HarmonyPatch(typeof(InventoryGui), "RepairOneItem")]
        internal static class RepairOneItemHook
        {
            private static bool Prefix(InventoryGui __instance)
            {
                if (!ModConfig.RepairButtonRepairsAll.Value) return true;

                int n = RepairAll(__instance);
                if (n > 0) Avisar(n);
                else if (Player.m_localPlayer != null)
                {
                    // Keeps the game's return when there is nothing to do.
                    Player.m_localPlayer.Message(MessageHud.MessageType.Center,
                        Lang.T("Nothing to repair", "Nada para reparar"));
                }
                return false; // skips the original
            }
        }
    }
}
