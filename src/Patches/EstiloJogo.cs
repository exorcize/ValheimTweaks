using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace ValheimTweaks.Patches
{
    /// <summary>
    /// Peças de aparência emprestadas dos painéis do próprio jogo.
    ///
    /// Imitar o Valheim com hex escolhido a olho nunca fica igual -- a madeira tem
    /// textura e a borda é 9-slice. Em vez disso copiamos o sprite, o material e a
    /// fonte que o painel de produção já usa.
    ///
    /// ---- Por que procurar por forma e não por nome ----
    /// Nome de GameObject muda entre versões do jogo e quebraria calado. As regras
    /// aqui são estruturais: fundo de painel é 9-slice (Sliced) e cobre a maior
    /// área; título é o maior TMP_Text. Isso continua valendo depois de um patch.
    ///
    /// A primeira versão pegava simplesmente a maior Image e escolheu 'darken_blob',
    /// um véu escuro que cobre o painel inteiro -- maior que a moldura, e sem
    /// nenhuma aparência. Daí a preferência explícita por Sliced.
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

                // Um sprite de máscara é uma silhueta chapada: desenhado como fundo
                // vira um borrão de cor sólida. Foi o que aconteceu com
                // 'woodpanel_crafting_240_mask', que venceu por área e pintou tudo
                // de amarelo. Máscara tem componente Mask junto -- dá para saber.
                bool ehMascara = img.GetComponent<Mask>() != null
                              || img.sprite.name.IndexOf("mask", System.StringComparison.OrdinalIgnoreCase) >= 0;

                candidatos.Append($"\n    {img.sprite.name} ({img.type}) {r.width:0}x{r.height:0}"
                                + (ehMascara ? "  [mascara, ignorado]" : ""));
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
                Plugin.Log.LogInfo($"[ESTILO] candidatos a fundo:{candidatos}");

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
                $"[ESTILO] fundo='{FundoSprite?.name ?? "-"}' ({FundoTipo}) "
              + $"| fonte='{FonteTitulo?.name ?? "-"}' {maiorFonte:0.#}px"
              + (melhorSliced == null ? "  (nenhum 9-slice; usando o maior)" : ""));
        }

        /// <summary>Pinta a madeira do jogo num Image. Sem sprite, cai num fundo liso.</summary>
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
