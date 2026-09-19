using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace ValheimTweaks.Patches
{
    /// <summary>
    /// Appearance pieces borrowed from the game's own panels.
    ///
    /// Imitating Valheim with an eyeballed hex never comes out the same -- the
    /// wood has texture and the border is 9-slice. Instead we copy the sprite,
    /// the material and the font the crafting panel already uses.
    ///
    /// ---- Why search by shape and not by name ----
    /// GameObject names change between game versions and would break silently.
    /// The rules here are structural: a panel's background is 9-slice (Sliced)
    /// and covers the largest area; the title is the largest TMP_Text. That
    /// still holds after a patch.
    ///
    /// The first version simply grabbed the largest Image and picked
    /// 'darken_blob', a dark veil covering the whole panel -- larger than the
    /// frame, and with no look at all. Hence the explicit preference for
    /// Sliced.
    /// </summary>
    internal static class GameStyle
    {
        internal static bool Ready { get; private set; }

        internal static Sprite BackgroundSprite;
        internal static Image.Type BackgroundType = Image.Type.Sliced;
        internal static Material BackgroundMaterial;
        internal static Color BackgroundColor = Color.white;
        internal static float BackgroundPixelsPerUnit = 1f;

        internal static TMP_FontAsset TitleFont;
        internal static Material TitleMaterial;
        internal static Color TitleColor = new Color(0.90f, 0.83f, 0.64f, 1f);

        private static readonly Color FallbackBackground = new Color(0.086f, 0.071f, 0.055f, 0.97f);

        internal static void Discover()
        {
            if (Ready) return;

            var gui = InventoryGui.instance;
            var template = gui != null ? gui.m_crafting : null;
            if (template == null) return;

            Image bestSliced = null, bestAny = null;
            float slicedArea = 0f, anyArea = 0f;
            var candidates = new System.Text.StringBuilder();

            foreach (var img in template.GetComponentsInChildren<Image>(true))
            {
                if (img.sprite == null) continue;

                var r = img.rectTransform.rect;
                float area = r.width * r.height;
                if (area <= 0f) continue;

                // A mask sprite is a flat silhouette: drawn as a background it
                // becomes a blob of solid color. That's what happened with
                // 'woodpanel_crafting_240_mask', which won by area and painted
                // everything yellow. A mask has a Mask component alongside --
                // that's how we can tell.
                bool isMask = img.GetComponent<Mask>() != null
                              || img.sprite.name.IndexOf("mask", System.StringComparison.OrdinalIgnoreCase) >= 0;

                candidates.Append($"\n    {img.sprite.name} ({img.type}) {r.width:0}x{r.height:0}"
                                + (isMask ? "  [mask, ignored]" : ""));
                if (isMask) continue;

                if (img.type == Image.Type.Sliced && area > slicedArea)
                {
                    slicedArea = area;
                    bestSliced = img;
                }
                if (area > anyArea)
                {
                    anyArea = area;
                    bestAny = img;
                }
            }
            if (ModConfig.ChestSearchDebug.Value)
                Plugin.Log.LogInfo($"[STYLE] background candidates:{candidates}");

            var chosen = bestSliced ?? bestAny;
            if (chosen != null)
            {
                BackgroundSprite = chosen.sprite;
                BackgroundType = chosen.type;
                BackgroundMaterial = chosen.material;
                BackgroundColor = chosen.color;
                BackgroundPixelsPerUnit = chosen.pixelsPerUnitMultiplier;
            }

            float largestFont = 0f;
            foreach (var t in template.GetComponentsInChildren<TMP_Text>(true))
            {
                if (t.font == null || t.fontSize <= largestFont) continue;
                largestFont = t.fontSize;
                TitleFont = t.font;
                TitleMaterial = t.fontSharedMaterial;
                TitleColor = t.color;
            }

            Ready = BackgroundSprite != null || TitleFont != null;

            Plugin.Log.LogInfo(
                $"[STYLE] background='{BackgroundSprite?.name ?? "-"}' ({BackgroundType}) "
              + $"| font='{TitleFont?.name ?? "-"}' {largestFont:0.#}px"
              + (bestSliced == null ? "  (no 9-slice; using the largest)" : ""));
        }

        /// <summary>Paints the game's wood onto an Image. Without a sprite, it falls back to a flat background.</summary>
        internal static void ApplyBackground(Image target, float opacity = 1f)
        {
            if (target == null) return;

            if (BackgroundSprite == null)
            {
                var c = FallbackBackground;
                target.color = new Color(c.r, c.g, c.b, c.a * opacity);
                return;
            }

            target.sprite = BackgroundSprite;
            target.type = BackgroundType;
            target.material = BackgroundMaterial;
            target.pixelsPerUnitMultiplier = BackgroundPixelsPerUnit;
            target.color = new Color(BackgroundColor.r, BackgroundColor.g, BackgroundColor.b, BackgroundColor.a * opacity);
        }

        internal static void ApplyTitle(TMP_Text target)
        {
            if (target == null || TitleFont == null) return;
            target.font = TitleFont;
            if (TitleMaterial != null) target.fontSharedMaterial = TitleMaterial;
            target.color = TitleColor;
        }
    }
}
