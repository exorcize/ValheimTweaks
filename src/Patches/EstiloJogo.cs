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
    internal static class EstiloJogo
    {
        internal static bool Pronto { get; private set; }

        internal static Sprite FundoSprite;
        internal static Image.Type FundoTipo = Image.Type.Sliced;
        internal static Material FundoMat;
        internal static Color FundoCor = Color.white;
        internal static float FundoPpu = 1f;

        internal static TMP_FontAsset FonteTitulo;
        internal static Material MatTitulo;
        internal static Color CorTitulo = new Color(0.90f, 0.83f, 0.64f, 1f);

        private static readonly Color FundoReserva = new Color(0.086f, 0.071f, 0.055f, 0.97f);

        internal static void Descobrir()
        {
            if (Pronto) return;

            var gui = InventoryGui.instance;
            var molde = gui != null ? gui.m_crafting : null;
            if (molde == null) return;

            Image melhorSliced = null, melhorQualquer = null;
            float areaSliced = 0f, areaQualquer = 0f;
            var candidatos = new System.Text.StringBuilder();

            foreach (var img in molde.GetComponentsInChildren<Image>(true))
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
                bool ehMascara = img.GetComponent<Mask>() != null
                              || img.sprite.name.IndexOf("mask", System.StringComparison.OrdinalIgnoreCase) >= 0;

                candidatos.Append($"\n    {img.sprite.name} ({img.type}) {r.width:0}x{r.height:0}"
                                + (ehMascara ? "  [mask, ignored]" : ""));
                if (ehMascara) continue;

                if (img.type == Image.Type.Sliced && area > areaSliced)
                {
                    areaSliced = area;
                    melhorSliced = img;
                }
                if (area > areaQualquer)
                {
                    areaQualquer = area;
                    melhorQualquer = img;
                }
            }
            if (ModConfig.ChestSearchDebug.Value)
                Plugin.Log.LogInfo($"[STYLE] background candidates:{candidatos}");

            var escolhido = melhorSliced ?? melhorQualquer;
            if (escolhido != null)
            {
                FundoSprite = escolhido.sprite;
                FundoTipo = escolhido.type;
                FundoMat = escolhido.material;
                FundoCor = escolhido.color;
                FundoPpu = escolhido.pixelsPerUnitMultiplier;
            }

            float maiorFonte = 0f;
            foreach (var t in molde.GetComponentsInChildren<TMP_Text>(true))
            {
                if (t.font == null || t.fontSize <= maiorFonte) continue;
                maiorFonte = t.fontSize;
                FonteTitulo = t.font;
                MatTitulo = t.fontSharedMaterial;
                CorTitulo = t.color;
            }

            Pronto = FundoSprite != null || FonteTitulo != null;

            Plugin.Log.LogInfo(
                $"[STYLE] background='{FundoSprite?.name ?? "-"}' ({FundoTipo}) "
              + $"| font='{FonteTitulo?.name ?? "-"}' {maiorFonte:0.#}px"
              + (melhorSliced == null ? "  (no 9-slice; using the largest)" : ""));
        }

        /// <summary>Paints the game's wood onto an Image. Without a sprite, it falls back to a flat background.</summary>
        internal static void AplicarFundo(Image alvo, float opacidade = 1f)
        {
            if (alvo == null) return;

            if (FundoSprite == null)
            {
                var c = FundoReserva;
                alvo.color = new Color(c.r, c.g, c.b, c.a * opacidade);
                return;
            }

            alvo.sprite = FundoSprite;
            alvo.type = FundoTipo;
            alvo.material = FundoMat;
            alvo.pixelsPerUnitMultiplier = FundoPpu;
            alvo.color = new Color(FundoCor.r, FundoCor.g, FundoCor.b, FundoCor.a * opacidade);
        }

        internal static void AplicarTitulo(TMP_Text alvo)
        {
            if (alvo == null || FonteTitulo == null) return;
            alvo.font = FonteTitulo;
            if (MatTitulo != null) alvo.fontSharedMaterial = MatTitulo;
            alvo.color = CorTitulo;
        }
    }
}
