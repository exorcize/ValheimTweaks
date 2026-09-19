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

        private static GameObject _panel;
        private static RectTransform _grid;
        private static TextMeshProUGUI _title;
        private static TextMeshProUGUI _footer;

        private static Container _current;
        private static long _seenRevision = -1;
        private static float _lostAimAt = -1f;

        // ------------------------------------------------------------------
        // Detection
        // ------------------------------------------------------------------
        private static Container AimedChest()
        {
            var player = Player.m_localPlayer;
            if (player == null || HoveringField == null) return null;

            var go = HoveringField.GetValue(player) as GameObject;
            if (go == null) return null;

            var chest = go.GetComponentInParent<Container>();
            if (chest == null) return null;

            float d = Vector3.Distance(player.transform.position, chest.transform.position);
            if (d > ModConfig.ChestPeekDistance.Value) return null;

            return chest;
        }

        internal static void Update()
        {
            if (!ModConfig.ChestPeekEnabled.Value) { Hide(); return; }
            if (InventoryGui.IsVisible() || Minimap.IsOpen()) { Hide(); return; }

            var chest = AimedChest();

            if (chest == null)
            {
                // Delay before hiding: a crosshair that grazes past would make the panel blink.
                if (_panel != null && _panel.activeSelf)
                {
                    if (_lostAimAt < 0f) _lostAimAt = Time.realtimeSinceStartup;
                    else if (Time.realtimeSinceStartup - _lostAimAt >= ModConfig.ChestPeekHideDelay.Value)
                        Hide();
                }
                return;
            }

            _lostAimAt = -1f;

            var nview = chest.GetComponent<ZNetView>();
            long revision = nview?.GetZDO() != null ? nview.GetZDO().DataRevision : 0;

            // Only rebuilds when the chest changes or the contents actually change.
            if (chest != _current || revision != _seenRevision)
            {
                _current = chest;
                _seenRevision = revision;
                Build(chest);
            }
        }

        private static void Hide()
        {
            if (_panel != null && _panel.activeSelf) _panel.SetActive(false);
            _current = null;
            _seenRevision = -1;
            _lostAimAt = -1f;
        }

        // ------------------------------------------------------------------
        // Grouped and sorted contents
        // ------------------------------------------------------------------
        private class Entry
        {
            internal Sprite Icon;
            internal int Total;
        }

        private static List<Entry> Group(Inventory inv)
        {
            var map = new Dictionary<string, Entry>();

            foreach (var item in inv.GetAllItems())
            {
                string key = item.m_shared.m_name;
                if (!map.TryGetValue(key, out var e))
                {
                    var icons = item.m_shared.m_icons;
                    e = new Entry
                    {
                        Icon = (icons != null && icons.Length > 0)
                            ? icons[Mathf.Clamp(item.m_variant, 0, icons.Length - 1)]
                            : null
                    };
                    map[key] = e;
                }
                e.Total += item.m_stack;
            }

            var list = new List<Entry>(map.Values);
            list.Sort((a, b) => b.Total.CompareTo(a.Total));
            return list;
        }

        // ------------------------------------------------------------------
        // UI
        // ------------------------------------------------------------------
        private static void Ensure()
        {
            if (_panel != null || Hud.instance == null || Hud.instance.m_rootObject == null) return;

            _panel = new GameObject("VT_EspiarBau", typeof(RectTransform));
            _panel.transform.SetParent(Hud.instance.m_rootObject.transform, worldPositionStays: false);

            var rt = _panel.GetComponent<RectTransform>();
            rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(1f, 0.5f);
            rt.anchoredPosition = new Vector2(-ModConfig.ChestPeekX.Value, 0f);

            // Same wood as the game's panels, instead of a plain rectangle of mine.
            GameStyle.Discover();
            var background = _panel.AddComponent<Image>();
            GameStyle.ApplyBackground(background);
            background.raycastTarget = false;

            var col = _panel.AddComponent<VerticalLayoutGroup>();
            // More padding than before: the wood frame has its own border, and
            // content pressed against it looks cramped.
            col.padding = new RectOffset(18, 18, 14, 16);
            col.spacing = 6f;
            col.childAlignment = TextAnchor.UpperLeft;
            col.childForceExpandWidth = false;
            col.childForceExpandHeight = false;
            col.childControlWidth = true;
            col.childControlHeight = true;

            var fit = _panel.AddComponent<ContentSizeFitter>();
            fit.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            fit.horizontalFit = ContentSizeFitter.FitMode.PreferredSize;

            _title = NewText(_panel.transform, 17f, "#E6D3A2");
            GameStyle.ApplyTitle(_title);
            _title.alignment = TextAlignmentOptions.Center;

            var gridGo = new GameObject("grade", typeof(RectTransform));
            gridGo.transform.SetParent(_panel.transform, worldPositionStays: false);
            _grid = gridGo.GetComponent<RectTransform>();
            var g = gridGo.AddComponent<GridLayoutGroup>();
            g.spacing = new Vector2(4f, 4f);
            var gf = gridGo.AddComponent<ContentSizeFitter>();
            gf.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            gf.horizontalFit = ContentSizeFitter.FitMode.PreferredSize;

            _footer = NewText(_panel.transform, 12f, "#9A8F70");
        }

        private static TextMeshProUGUI NewText(Transform parent, float size, string color)
        {
            var go = new GameObject("txt", typeof(RectTransform));
            go.transform.SetParent(parent, worldPositionStays: false);
            var t = go.AddComponent<TextMeshProUGUI>();
            StoreHudPatch.ApplyFont(t);
            t.fontSize = size;
            t.alignment = TextAlignmentOptions.MidlineLeft;
            t.enableWordWrapping = false;
            t.raycastTarget = false;
            t.color = ColorUtility.TryParseHtmlString(color, out var c) ? c : Color.white;
            return t;
        }

        private static void Build(Container chest)
        {
            Ensure();
            if (_panel == null) return;

            var inv = chest.GetInventory();
            if (inv == null) { Hide(); return; }

            var items = Group(inv);

            // Shows EVERYTHING, but shrinks the slot as the number of types grows
            // so it doesn't take over the screen. Columns increase along with it,
            // otherwise it would become one tall column.
            float side;
            int columns;
            if (items.Count <= 10) { side = 68f; columns = 5; }
            else if (items.Count <= 24) { side = 56f; columns = 6; }
            else if (items.Count <= 40) { side = 48f; columns = 8; }
            else { side = 40f; columns = 10; }

            var g = _grid.GetComponent<GridLayoutGroup>();
            g.cellSize = new Vector2(side, side);
            g.constraint = GridLayoutGroup.Constraint.FixedColumnCount;
            g.constraintCount = columns;

            for (int i = _grid.childCount - 1; i >= 0; i--)
                Object.Destroy(_grid.GetChild(i).gameObject);

            foreach (var e in items) NewSlot(side, e);

            string name = StoreHudPatch.ChestName(chest);
            _title.text = string.IsNullOrEmpty(name)
                ? Localization.instance.Localize(chest.m_name)
                : name;

            _footer.text = string.Format(
                Lang.T("{0} kinds · {1} / {2} slots", "{0} tipos · {1} / {2} espaços"),
                items.Count, inv.NrOfItems(), inv.GetWidth() * inv.GetHeight());

            _panel.SetActive(true);
        }

        /// <summary>
        /// Uses the inventory's own slot (InventoryGrid.m_elementPrefab) instead of
        /// drawing a square. That way the border, the background and the number's
        /// position are exactly the game's, and the panel stops looking like
        /// something foreign.
        /// </summary>
        private static void NewSlot(float side, Entry e)
        {
            var prefab = InventoryGui.instance?.m_playerGrid?.m_elementPrefab;

            if (prefab == null)
            {
                SimpleSlot(side, e);   // before the inventory exists
                return;
            }

            var slotGo = Object.Instantiate(prefab, _grid);
            slotGo.SetActive(true);

            var el = slotGo.GetComponent<InventoryElement>();
            el.m_icon.enabled = e.Icon != null;
            el.m_icon.sprite = e.Icon;
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

        private static void SimpleSlot(float side, Entry e)
        {
            var slot = new GameObject("slot", typeof(RectTransform));
            slot.transform.SetParent(_grid, worldPositionStays: false);

            var background = slot.AddComponent<Image>();
            background.color = new Color(0.10f, 0.10f, 0.09f, 0.95f);
            background.raycastTarget = false;

            if (e.Icon != null)
            {
                var icoGo = new GameObject("ico", typeof(RectTransform));
                icoGo.transform.SetParent(slot.transform, worldPositionStays: false);
                var img = icoGo.AddComponent<Image>();
                img.sprite = e.Icon;
                img.preserveAspect = true;
                img.raycastTarget = false;
                var r = icoGo.GetComponent<RectTransform>();
                r.anchorMin = Vector2.zero;
                r.anchorMax = Vector2.one;
                r.offsetMin = new Vector2(3f, 3f);
                r.offsetMax = new Vector2(-3f, -3f);
            }

            var amount = NewText(slot.transform, Mathf.Max(9f, side * 0.34f), "#FFFFFF");
            amount.text = e.Total.ToString();
            amount.alignment = TextAlignmentOptions.BottomRight;
            var qr = amount.GetComponent<RectTransform>();
            qr.anchorMin = Vector2.zero;
            qr.anchorMax = Vector2.one;
            qr.offsetMin = Vector2.zero;
            qr.offsetMax = new Vector2(-2f, 0f);
        }
    }
}
