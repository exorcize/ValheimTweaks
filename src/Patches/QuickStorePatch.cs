using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using UnityEngine;

namespace ValheimTweaks.Patches
{
    /// <summary>
    /// Guardar itens nos baús próximos.
    ///
    /// Dois modos, ambos ativos:
    ///   - marcar um item (tecla sobre ele) e depois despejar todos os marcados
    ///   - guardar na hora o item sob o cursor
    ///
    /// ---- Por que reutiliza Container.StackAll em vez de mexer no inventário ----
    /// Escrever direto no inventário de um baú corrompe save em multijogador: outro
    /// jogador pode estar com ele aberto, o baú pode estar dentro de área protegida,
    /// e quem manda nos dados é o dono do ZDO, que pode ser outra máquina.
    ///
    /// O jogo já tem o aperto de mão para isso:
    ///
    ///     Container.StackAll()
    ///       -> RPC_RequestStack  (dono checa IsOwner / IsInUse / CheckAccess)
    ///            -> concede: ForceSendZDO + SetOwner(uid)
    ///                 -> RPC_StackResponse: m_inventory.StackAll(inv do player)
    ///
    /// Chamamos isso e deixamos o jogo cuidar de posse, baú em uso e guard stone.
    /// Como o RPC transfere a posse para nós antes do StackAll, mexer no inventário
    /// dentro dessa janela é legítimo -- é o que o próprio jogo faz.
    ///
    /// ---- O filtro ----
    /// Inventory.StackAll move tudo que o destino já contém. Para respeitar a
    /// marcação, um Prefix substitui o laço -- mas SÓ enquanto a nossa operação
    /// está em andamento (s_filtroAte). Fora dela, o quick-stack do jogo e de
    /// outros mods continua intacto. É assim que se evita conflito.
    ///
    /// Como o RPC é assíncrono, o filtro vale por uma janela de tempo em vez de
    /// uma única chamada: as respostas dos vários baús chegam em frames diferentes.
    /// </summary>
    internal static class QuickStorePatch
    {
        private const string ChaveMarca = "vt_store";

        private static readonly MethodInfo GetHoveredElementMethod =
            AccessTools.Method(typeof(InventoryGrid), "GetHoveredElement");

        // Inventory.Changed e privado; e ele que avisa a UI e marca o ZDO como sujo.
        private static readonly MethodInfo ChangedMethod =
            AccessTools.Method(typeof(Inventory), "Changed", new[] { typeof(bool), typeof(bool) });

        private static void Notificar(Inventory inv)
        {
            if (inv != null) ChangedMethod?.Invoke(inv, new object[] { false, false });
        }

        // Filtro ativo durante a operação. null = comportamento normal do jogo.
        private static System.Func<ItemDrop.ItemData, bool> s_filtro;
        private static float s_filtroAte;
        private static int s_movidos;

        private static bool FiltroAtivo => s_filtro != null && Time.realtimeSinceStartup < s_filtroAte;

        // ------------------------------------------------------------------
        // Marcação (persistida no item, sobrevive a save e a troca de inventário)
        // ------------------------------------------------------------------
        internal static bool EstaMarcado(ItemDrop.ItemData item)
            => item?.m_customData != null && item.m_customData.ContainsKey(ChaveMarca);

        private static void AlternarMarca(ItemDrop.ItemData item)
        {
            if (item?.m_customData == null) return;

            if (item.m_customData.Remove(ChaveMarca))
            {
                Aviso($"{item.m_shared.m_name} desmarcado");
            }
            else
            {
                item.m_customData[ChaveMarca] = "1";
                Aviso($"{item.m_shared.m_name} marcado para guardar");
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
        // Item sob o cursor
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
        // Despejo
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

                // Baú sem ZDO valido ainda esta carregando; pular evita RPC perdido.
                var nview = cont.GetComponent<ZNetView>();
                if (nview == null || !nview.IsValid()) continue;

                achados.Add(cont);
            }
            return achados;
        }

        private static void Despejar(System.Func<ItemDrop.ItemData, bool> filtro, string oQue)
        {
            var baus = BausProximos();
            if (baus.Count == 0)
            {
                Aviso("Nenhum bau por perto");
                return;
            }

            s_filtro = filtro;
            s_movidos = 0;
            // Janela generosa: cada bau responde no seu proprio frame.
            s_filtroAte = Time.realtimeSinceStartup + 3f;

            foreach (var bau in baus) bau.StackAll();

            Plugin.Log.LogInfo($"[GUARDAR] {oQue} -> {baus.Count} bau(s) em {ModConfig.StoreRadius.Value}m");
        }

        // ------------------------------------------------------------------
        // Substitui o laço do StackAll enquanto a nossa operação roda
        // ------------------------------------------------------------------
        [HarmonyPatch(typeof(Inventory), nameof(Inventory.StackAll))]
        internal static class StackAllHook
        {
            private static bool Prefix(Inventory __instance, Inventory fromInventory,
                                       ref int __result)
            {
                if (!FiltroAtivo) return true; // jogo normal, nao interferimos

                var player = Player.m_localPlayer;
                if (player == null) return true;

                int movidos = 0;
                var itens = new List<ItemDrop.ItemData>(fromInventory.GetAllItems());

                // 1a passada: so onde o bau JA tem o item (comportamento do jogo).
                foreach (var item in itens)
                {
                    if (!s_filtro(item) || player.IsItemEquiped(item)) continue;
                    if (!__instance.ContainsItemByName(item.m_shared.m_name)) continue;
                    if (__instance.AddItem(item)) { fromInventory.RemoveItem(item); movidos++; }
                }

                // 2a passada (opcional): qualquer bau com espaco.
                if (ModConfig.StoreFallbackAnyChest.Value)
                {
                    foreach (var item in new List<ItemDrop.ItemData>(fromInventory.GetAllItems()))
                    {
                        if (!s_filtro(item) || player.IsItemEquiped(item)) continue;
                        if (__instance.AddItem(item)) { fromInventory.RemoveItem(item); movidos++; }
                    }
                }

                if (movidos > 0)
                {
                    Notificar(__instance);
                    Notificar(fromInventory);
                    s_movidos += movidos;
                    Aviso($"{s_movidos} item(ns) guardado(s)");
                }

                __result = movidos;
                return false; // pula o original
            }
        }

        // ------------------------------------------------------------------
        // Marca visual: tinge o icone do slot marcado
        // ------------------------------------------------------------------
        [HarmonyPatch(typeof(InventoryGrid), "UpdateGui")]
        internal static class UpdateGuiHook
        {
            private static readonly Color Tom = new Color(0.55f, 0.85f, 1f, 1f);

            private static void Postfix(InventoryGrid __instance)
            {
                var inv = __instance.GetInventory();
                if (inv == null) return;

                foreach (var el in __instance.GetComponentsInChildren<InventoryElement>(true))
                {
                    if (el.m_icon == null || !el.m_icon.enabled) continue;

                    var item = inv.GetItemAt(el.Position.x, el.Position.y);
                    // Reescreve sempre, nos dois sentidos: o jogo reaproveita os
                    // elementos entre aberturas e um icone tingido ficaria preso.
                    el.m_icon.color = (item != null && EstaMarcado(item)) ? Tom : Color.white;
                }
            }
        }

        // ------------------------------------------------------------------
        // Teclas
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
                Despejar(EstaMarcado, "itens marcados");
            }
        }
    }
}
