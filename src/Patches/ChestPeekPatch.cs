using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace ValheimTweaks.Patches
{
    /// <summary>
    /// Peek at the contents of the targeted chest, without opening it.
    ///
    /// You can read without opening because Container.Update calls Load(), which
    /// deserializes ZDOVars.s_items from the ZDO -- and it only rereads when
    /// DataRevision changes. In other words: the m_inventory of any loaded chest
    /// is already up to date for free.
    ///
    /// Applies to carts and ships too: all three are Container in the code.
    ///
    /// Aiming alone isn't enough -- we require proximity. In practice the game's
    /// hover raycast already limits to m_maxInteractDistance, but the check stays
    /// explicit in case another mod stretches that reach.
    ///
    /// Items are grouped by type before sorting: GetAllItems returns per SLOT,
    /// so three stacks of wood would come as three entries and the sorting would
    /// come out wrong with wood repeated.
    /// </summary>
    internal static class ChestPeekPatch
    {
        private static readonly FieldInfo HoveringField =
            AccessTools.Field(typeof(Player), "m_hovering");

        private static GameObject _painel;
        private static RectTransform _grade;
        private static TextMeshProUGUI _titulo;
        private static TextMeshProUGUI _rodape;

        private static Container _atual;
        private static long _revisaoVista = -1;
        private static float _perdeuMiraEm = -1f;

        // ------------------------------------------------------------------
        // Detection
        // ------------------------------------------------------------------
        private static Container BauMirado()
        {
            var player = Player.m_localPlayer;
            if (player == null || HoveringField == null) return null;

            var go = HoveringField.GetValue(player) as GameObject;
            if (go == null) return null;

            var bau = go.GetComponentInParent<Container>();
            if (bau == null) return null;

            float d = Vector3.Distance(player.transform.position, bau.transform.position);
            if (d > ModConfig.ChestPeekDistance.Value) return null;

            return bau;
        }

        internal static void Update()
        {
            if (!ModConfig.ChestPeekEnabled.Value) { Esconder(); return; }
            if (InventoryGui.IsVisible() || Minimap.IsOpen()) { Esconder(); return; }

            var bau = BauMirado();

            if (bau == null)
            {
                // Delay before hiding: a crosshair that grazes past would make the panel blink.
                if (_painel != null && _painel.activeSelf)
                {
                    if (_perdeuMiraEm < 0f) _perdeuMiraEm = Time.realtimeSinceStartup;
                    else if (Time.realtimeSinceStartup - _perdeuMiraEm >= ModConfig.ChestPeekHideDelay.Value)
                        Esconder();
                }
                return;
            }

            _perdeuMiraEm = -1f;

            var nview = bau.GetComponent<ZNetView>();
            long revisao = nview?.GetZDO() != null ? nview.GetZDO().DataRevision : 0;

            // Only rebuilds when the chest changes or the contents actually change.
            if (bau != _atual || revisao != _revisaoVista)
            {
                _atual = bau;
                _revisaoVista = revisao;
                Montar(bau);
            }
        }

        private static void Esconder()
        {
            if (_painel != null && _painel.activeSelf) _painel.SetActive(false);
            _atual = null;
            _revisaoVista = -1;
            _perdeuMiraEm = -1f;
        }

        // ------------------------------------------------------------------
        // Grouped and sorted contents
        // ------------------------------------------------------------------
        private class Entrada
        {
            internal Sprite Icone;
            internal int Total;
        }

        private static List<Entrada> Agrupar(Inventory inv)
        {
            var mapa = new Dictionary<string, Entrada>();

            foreach (var item in inv.GetAllItems())
            {
                string chave = item.m_shared.m_name;
                if (!mapa.TryGetValue(chave, out var e))
                {
                    var icones = item.m_shared.m_icons;
                    e = new Entrada
                    {
                        Icone = (icones != null && icones.Length > 0)
                            ? icones[Mathf.Clamp(item.m_variant, 0, icones.Length - 1)]
                            : null
                    };
                    mapa[chave] = e;
                }
                e.Total += item.m_stack;
            }

            var lista = new List<Entrada>(mapa.Values);
            lista.Sort((a, b) => b.Total.CompareTo(a.Total));
            return lista;
        }

        // ------------------------------------------------------------------
        // UI
        // ------------------------------------------------------------------
        private static void Garantir()
        {
            if (_painel != null || Hud.instance == null || Hud.instance.m_rootObject == null) return;

            _painel = new GameObject("VT_EspiarBau", typeof(RectTransform));
            _painel.transform.SetParent(Hud.instance.m_rootObject.transform, worldPositionStays: false);

            var rt = _painel.GetComponent<RectTransform>();
            rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(1f, 0.5f);
            rt.anchoredPosition = new Vector2(-ModConfig.ChestPeekX.Value, 0f);

            // Same wood as the game's panels, instead of a plain rectangle of mine.
            EstiloJogo.Descobrir();
            var fundo = _painel.AddComponent<Image>();
            EstiloJogo.AplicarFundo(fundo);
            fundo.raycastTarget = false;

            var col = _painel.AddComponent<VerticalLayoutGroup>();
            // More padding than before: the wood frame has its own border, and
            // content pressed against it looks cramped.
            col.padding = new RectOffset(18, 18, 14, 16);
            col.spacing = 6f;
            col.childAlignment = TextAnchor.UpperLeft;
            col.childForceExpandWidth = false;
            col.childForceExpandHeight = false;
            col.childControlWidth = true;
            col.childControlHeight = true;

            var fit = _painel.AddComponent<ContentSizeFitter>();
            fit.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            fit.horizontalFit = ContentSizeFitter.FitMode.PreferredSize;

            _titulo = NovoTexto(_painel.transform, 17f, "#E6D3A2");
            EstiloJogo.AplicarTitulo(_titulo);
            _titulo.alignment = TextAlignmentOptions.Center;

            var gradeGo = new GameObject("grade", typeof(RectTransform));
            gradeGo.transform.SetParent(_painel.transform, worldPositionStays: false);
            _grade = gradeGo.GetComponent<RectTransform>();
            var g = gradeGo.AddComponent<GridLayoutGroup>();
            g.spacing = new Vector2(4f, 4f);
            var gf = gradeGo.AddComponent<ContentSizeFitter>();
            gf.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            gf.horizontalFit = ContentSizeFitter.FitMode.PreferredSize;

            _rodape = NovoTexto(_painel.transform, 12f, "#9A8F70");
        }

        private static TextMeshProUGUI NovoTexto(Transform pai, float tamanho, string cor)
        {
            var go = new GameObject("txt", typeof(RectTransform));
            go.transform.SetParent(pai, worldPositionStays: false);
            var t = go.AddComponent<TextMeshProUGUI>();
            StoreHudPatch.AplicarFonte(t);
            t.fontSize = tamanho;
            t.alignment = TextAlignmentOptions.MidlineLeft;
            t.enableWordWrapping = false;
            t.raycastTarget = false;
            t.color = ColorUtility.TryParseHtmlString(cor, out var c) ? c : Color.white;
            return t;
        }

        private static void Montar(Container bau)
        {
            Garantir();
            if (_painel == null) return;

            var inv = bau.GetInventory();
            if (inv == null) { Esconder(); return; }

            var itens = Agrupar(inv);

            // Shows EVERYTHING, but shrinks the slot as the number of types grows
            // so it doesn't take over the screen. Columns increase along with it,
            // otherwise it would become one tall column.
            float lado;
            int colunas;
            if (itens.Count <= 10) { lado = 68f; colunas = 5; }
            else if (itens.Count <= 24) { lado = 56f; colunas = 6; }
            else if (itens.Count <= 40) { lado = 48f; colunas = 8; }
            else { lado = 40f; colunas = 10; }

            var g = _grade.GetComponent<GridLayoutGroup>();
            g.cellSize = new Vector2(lado, lado);
            g.constraint = GridLayoutGroup.Constraint.FixedColumnCount;
            g.constraintCount = colunas;

            for (int i = _grade.childCount - 1; i >= 0; i--)
                Object.Destroy(_grade.GetChild(i).gameObject);

            foreach (var e in itens) NovoSlot(lado, e);

            string nome = StoreHudPatch.NomeDoBau(bau);
            _titulo.text = string.IsNullOrEmpty(nome)
                ? Localization.instance.Localize(bau.m_name)
                : nome;

            _rodape.text = string.Format(
                Lang.T("{0} kinds · {1} / {2} slots", "{0} tipos · {1} / {2} espaços"),
                itens.Count, inv.NrOfItems(), inv.GetWidth() * inv.GetHeight());

            _painel.SetActive(true);
        }

        /// <summary>
        /// Uses the inventory's own slot (InventoryGrid.m_elementPrefab) instead of
        /// drawing a square. That way the border, the background and the number's
        /// position are exactly the game's, and the panel stops looking like
        /// something foreign.
        /// </summary>
        private static void NovoSlot(float lado, Entrada e)
        {
            var prefab = InventoryGui.instance?.m_playerGrid?.m_elementPrefab;

            if (prefab == null)
            {
                SlotSimples(lado, e);   // before the inventory exists
                return;
            }

            var slotGo = Object.Instantiate(prefab, _grade);
            slotGo.SetActive(true);

            var el = slotGo.GetComponent<InventoryElement>();
            el.m_icon.enabled = e.Icone != null;
            el.m_icon.sprite = e.Icone;
            el.m_icon.color = Color.white;

            el.m_amount.enabled = true;
            el.m_amount.text = e.Total.ToString();

            el.m_durability.gameObject.SetActive(false);
            el.m_equiped.enabled = false;
            el.m_queued.enabled = false;
            el.m_noteleport.enabled = false;
            el.m_food.enabled = false;
            el.m_quality.enabled = false;
            if (el.m_selected != null) el.m_selected.SetActive(false);

            // No tooltip or click: this is just peeking, and the mouse is on the chest.
            if (el.m_tooltip != null) el.m_tooltip.enabled = false;
            foreach (var h in slotGo.GetComponentsInChildren<UIInputHandler>(true)) h.enabled = false;
            foreach (var d in slotGo.GetComponentsInChildren<UIDragHandler>(true)) d.enabled = false;

            var bind = slotGo.transform.Find("binding");
            if (bind != null)
            {
                var bt = bind.GetComponent<TMP_Text>();
                if (bt != null) bt.enabled = false;
            }
        }

        private static void SlotSimples(float lado, Entrada e)
        {
            var slot = new GameObject("slot", typeof(RectTransform));
            slot.transform.SetParent(_grade, worldPositionStays: false);

            var fundo = slot.AddComponent<Image>();
            fundo.color = new Color(0.10f, 0.10f, 0.09f, 0.95f);
            fundo.raycastTarget = false;

            if (e.Icone != null)
            {
                var icoGo = new GameObject("ico", typeof(RectTransform));
                icoGo.transform.SetParent(slot.transform, worldPositionStays: false);
                var img = icoGo.AddComponent<Image>();
                img.sprite = e.Icone;
                img.preserveAspect = true;
                img.raycastTarget = false;
                var r = icoGo.GetComponent<RectTransform>();
                r.anchorMin = Vector2.zero;
                r.anchorMax = Vector2.one;
                r.offsetMin = new Vector2(3f, 3f);
                r.offsetMax = new Vector2(-3f, -3f);
            }

            var qtd = NovoTexto(slot.transform, Mathf.Max(9f, lado * 0.34f), "#FFFFFF");
            qtd.text = e.Total.ToString();
            qtd.alignment = TextAlignmentOptions.BottomRight;
            var qr = qtd.GetComponent<RectTransform>();
            qr.anchorMin = Vector2.zero;
            qr.anchorMax = Vector2.one;
            qr.offsetMin = Vector2.zero;
            qr.offsetMax = new Vector2(-2f, 0f);
        }
    }
}
