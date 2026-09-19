using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using UnityEngine;

namespace ValheimTweaks.Patches
{
    /// <summary>
    /// Reparar tudo de uma vez.
    ///
    /// O jogo repara um item por clique: InventoryGui.RepairOneItem percorre as
    /// peças gastas, conserta a primeira que puder e sai no return. Com equipamento
    /// completo isso vira cinco ou seis cliques toda vez que se passa na bancada.
    ///
    /// Reparo no Valheim não consome material -- só exige a estação certa. Então
    /// fazer tudo de uma vez é conveniência, não vantagem: o resultado é o mesmo
    /// que clicar até acabar.
    ///
    /// Tudo local: mexe na durabilidade do próprio inventário, nada vai para a rede.
    /// </summary>
    internal static class RepairAllPatch
    {
        // CanRepair é privado e concentra as regras (item reparável, estação certa,
        // receita conhecida, world level). Reusar evita duplicar essa lógica e
        // errar algum caso.
        private static readonly MethodInfo CanRepairMethod =
            AccessTools.Method(typeof(InventoryGui), "CanRepair", new[] { typeof(ItemDrop.ItemData) });

        private static readonly List<ItemDrop.ItemData> Buffer = new List<ItemDrop.ItemData>();

        /// <summary>Repara tudo que der. Devolve quantos itens foram consertados.</summary>
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

                // Mesma progressão de Artesanato que o reparo normal daria.
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

        /// <summary>Repara ao abrir o painel perto de uma estação.</summary>
        [HarmonyPatch(typeof(InventoryGui), nameof(InventoryGui.Show))]
        internal static class ShowHook
        {
            private static void Postfix(InventoryGui __instance)
            {
                if (!ModConfig.AutoRepairOnOpen.Value) return;
                Avisar(RepairAll(__instance));
            }
        }

        /// <summary>Faz o botão de reparo consertar tudo em vez de um item.</summary>
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
                    // Mantém o retorno do jogo quando não há nada a fazer.
                    Player.m_localPlayer.Message(MessageHud.MessageType.Center,
                        Lang.T("Nothing to repair", "Nada para reparar"));
                }
                return false; // pula o original
            }
        }
    }
}
