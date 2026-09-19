using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace ValheimTweaks.Patches
{
    /// <summary>
    /// List of stored items, in the lower left corner.
    ///
    /// It sits ABOVE the health and food HUD, which already occupies the bottom
    /// of the screen. The height is configurable because the exact position of
    /// that HUD comes from a prefab, not from code -- it can't be calculated by
    /// reading, so the value is adjustable.
    ///
    /// One line per item (not per chest): if wood goes to two chests, two lines
    /// appear. The older ones rise, fade and disappear.
    ///
    /// The chest name comes from Container.GetHoverName(), which returns m_name.
    /// Since an unnamed chest returns the piece's default name instead of empty,
    /// we compare it with the m_name of the original PREFAB: if equal, there is
    /// no custom name and the line comes out without an arrow. This works with
    /// any mod that renames a chest, not just a specific one.
    /// </summary>
    internal static class StoreHudPatch
    {
        private class Line
        {
            internal GameObject Go;
            internal CanvasGroup Fade;
            internal float BornAt;
        }

        private static GameObject _root;
        private static readonly List<Line> Lines = new List<Line>();
        private static TMP_FontAsset _font;
        private static Material _material;

        /// <summary>Bumps when the font is first resolved; panels reapply it on change.</summary>
        internal static int FontVersion { get; private set; }

        /// <summary>
        /// Font and material from a KNOWN HUD text. Grabbing the first TMP_Text
        /// that appeared brought some random font, and without the
        /// fontSharedMaterial TextMeshPro falls back -- that's what made the text
        /// look like a console on the first attempt.
        /// </summary>
        internal static bool ApplyFont(TMP_Text target)
        {
            if (target == null) return false;
            EnsureFont();
            if (_font == null) return false;   // HUD not ready yet: try again later

            target.font = _font;
            if (_material != null) target.fontSharedMaterial = _material;
            return true;
        }

        /// <summary>Applies the font to every TMP text under <paramref name="root"/>.</summary>
        internal static void ApplyFontToAll(Transform root)
        {
            if (root == null) return;
            EnsureFont();
            if (_font == null) return;

            foreach (var t in root.GetComponentsInChildren<TMP_Text>(true))
            {
                t.font = _font;
                if (_material != null) t.fontSharedMaterial = _material;
            }
        }

        private static void EnsureFont()
        {
            if (_font != null) return;

            // 1) A known HUD text.
            if (Hud.instance != null)
            {
                TMP_Text template = Hud.instance.m_healthText
                              ?? Hud.instance.m_staminaText
                              ?? Hud.instance.m_actionName;
                if (template != null)
                {
                    _font = template.font;
                    _material = template.fontSharedMaterial;
                }
            }

            if (_font == null && MessageHud.instance != null)
            {
                var template = MessageHud.instance.m_messageCenterText;
                if (template != null)
                {
                    _font = template.font;
                    _material = template.fontSharedMaterial;
                }
            }

            // 2) Fallback: the font the chest panels already discovered from the
            // crafting panel. The HUD source above can come back null depending on
            // load order, and a text with no font makes TMP spam the log every frame
            // ("no Font Asset assigned").
            if (_font == null && GameStyle.TitleFont != null)
            {
                _font = GameStyle.TitleFont;
                _material = GameStyle.TitleMaterial;
            }

            if (_font != null)
            {
                FontVersion++;
                Plugin.Log.LogInfo($"[STYLE] body font = '{_font.name}'"
                    + (Hud.instance != null && Hud.instance.m_healthText != null ? " (HUD)" : " (fallback)"));
            }
        }

        // ------------------------------------------------------------------
        // Chest's custom name, or null
        // ------------------------------------------------------------------
        internal static string ChestName(Container chest)
        {
            if (chest == null) return null;

            string current = chest.GetHoverName();
            if (string.IsNullOrEmpty(current)) return null;

            var nview = chest.GetComponent<ZNetView>();
            var zdo = nview != null ? nview.GetZDO() : null;
            if (zdo == null || ZNetScene.instance == null) return null;

            var prefab = ZNetScene.instance.GetPrefab(zdo.GetPrefab());
            var original = prefab != null ? prefab.GetComponent<Container>() : null;
            if (original == null) return null;

            // Same as the piece's default = no one renamed it.
            return current == original.m_name ? null : current;
        }

        // ------------------------------------------------------------------
        // UI construction
        // ------------------------------------------------------------------
        private static void Ensure()
        {
            if (_root != null || Hud.instance == null || Hud.instance.m_rootObject == null) return;

            _root = new GameObject("VT_ItensGuardados", typeof(RectTransform));
            _root.transform.SetParent(Hud.instance.m_rootObject.transform, worldPositionStays: false);

            var rt = _root.GetComponent<RectTransform>();
            rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0f, 0f);
            rt.anchoredPosition = new Vector2(
                ModConfig.StoreHudX.Value, ModConfig.StoreHudY.Value);
            rt.sizeDelta = new Vector2(400f, 0f);

            var layout = _root.AddComponent<VerticalLayoutGroup>();
            layout.childAlignment = TextAnchor.LowerLeft;
            layout.spacing = 3f;
            layout.childForceExpandWidth = false;
            layout.childForceExpandHeight = false;
            layout.childControlWidth = true;
            layout.childControlHeight = true;

            var fitter = _root.AddComponent<ContentSizeFitter>();
            fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            fitter.horizontalFit = ContentSizeFitter.FitMode.PreferredSize;
        }

        internal static void Add(ItemDrop.ItemData item, int amount, string chestName)
        {
            if (!ModConfig.StoreHudEnabled.Value || item == null) return;
            Ensure();
            if (_root == null) return;

            var go = new GameObject("linha", typeof(RectTransform));
            go.transform.SetParent(_root.transform, worldPositionStays: false);

            var fade = go.AddComponent<CanvasGroup>();
            var h = go.AddComponent<HorizontalLayoutGroup>();
            h.spacing = 7f;
            h.childAlignment = TextAnchor.MiddleLeft;
            h.childForceExpandWidth = false;
            h.childForceExpandHeight = false;
            h.childControlWidth = true;
            h.childControlHeight = true;

            // Icon
            var icons = item.m_shared.m_icons;
            if (icons != null && icons.Length > 0)
            {
                var icoGo = new GameObject("ico", typeof(RectTransform));
                icoGo.transform.SetParent(go.transform, worldPositionStays: false);
                var img = icoGo.AddComponent<Image>();
                img.sprite = icons[Mathf.Clamp(item.m_variant, 0, icons.Length - 1)];
                img.preserveAspect = true;
                var le = icoGo.AddComponent<LayoutElement>();
                le.preferredWidth = le.preferredHeight = 20f;
            }

            // Text
            var txtGo = new GameObject("txt", typeof(RectTransform));
            txtGo.transform.SetParent(go.transform, worldPositionStays: false);
            var txt = txtGo.AddComponent<TextMeshProUGUI>();
            ApplyFont(txt);
            txt.fontSize = ModConfig.StoreHudFontSize.Value;
            txt.alignment = TextAlignmentOptions.MidlineLeft;
            txt.enableWordWrapping = false;
            txt.raycastTarget = false;

            // m_name is a localization token ("$item_feathers"); without Localize
            // the raw token appears on screen.
            string name = Localization.instance.Localize(item.m_shared.m_name);
            txt.text = string.IsNullOrEmpty(chestName)
                ? $"<color=#E8DDC4>{name} x{amount}</color>"
                : $"<color=#E8DDC4>{name} x{amount}</color> <color=#A8996F>→</color> <color=#D8C48A>{chestName}</color>";

            Lines.Add(new Line { Go = go, Fade = fade, BornAt = Time.realtimeSinceStartup });

            // Line cap: a big dump would become a wall of text.
            while (Lines.Count > ModConfig.StoreHudMaxLines.Value) Remove(0);
        }

        private static void Remove(int i)
        {
            if (i < 0 || i >= Lines.Count) return;
            if (Lines[i].Go != null) Object.Destroy(Lines[i].Go);
            Lines.RemoveAt(i);
        }

        // ------------------------------------------------------------------
        // Expiration
        // ------------------------------------------------------------------
        internal static void Update()
        {
            if (Lines.Count == 0) return;

            float now = Time.realtimeSinceStartup;
            float life = ModConfig.StoreHudSeconds.Value;
            const float Fade = 1f;

            for (int i = Lines.Count - 1; i >= 0; i--)
            {
                var l = Lines[i];
                if (l.Go == null) { Lines.RemoveAt(i); continue; }

                float age = now - l.BornAt;
                if (age >= life) { Remove(i); continue; }

                l.Fade.alpha = age > life - Fade
                    ? Mathf.Clamp01((life - age) / Fade)
                    : 1f;
            }
        }
    }
}
