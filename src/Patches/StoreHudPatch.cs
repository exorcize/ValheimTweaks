using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace ValheimTweaks.Patches
{
    /// <summary>
    /// Lista dos itens guardados, no canto inferior esquerdo.
    ///
    /// Fica ACIMA da HUD de vida e comida, que ja ocupa o pe da tela. A altura e
    /// configuravel porque a posicao exata daquela HUD vem de prefab, nao de
    /// codigo -- nao da para calcular por leitura, entao o valor e ajustavel.
    ///
    /// Uma linha por item (nao por bau): se madeira for para dois baus, saem duas
    /// linhas. As mais antigas sobem, esmaecem e somem.
    ///
    /// O nome do bau vem de Container.GetHoverName(), que devolve m_name. Como
    /// bau sem nome devolve o nome padrao da peca em vez de vazio, comparamos com
    /// o m_name do PREFAB original: se for igual, nao ha nome proprio e a linha
    /// sai sem seta. Isso funciona com qualquer mod que renomeie bau, nao so com
    /// um especifico.
    /// </summary>
    internal static class StoreHudPatch
    {
        private class Linha
        {
            internal GameObject Go;
            internal CanvasGroup Fade;
            internal float Nasceu;
        }

        private static GameObject _raiz;
        private static readonly List<Linha> Linhas = new List<Linha>();
        private static TMP_FontAsset _fonte;

        // ------------------------------------------------------------------
        // Nome proprio do bau, ou null
        // ------------------------------------------------------------------
        internal static string NomeDoBau(Container bau)
        {
            if (bau == null) return null;

            string atual = bau.GetHoverName();
            if (string.IsNullOrEmpty(atual)) return null;

            var nview = bau.GetComponent<ZNetView>();
            var zdo = nview != null ? nview.GetZDO() : null;
            if (zdo == null || ZNetScene.instance == null) return null;

            var prefab = ZNetScene.instance.GetPrefab(zdo.GetPrefab());
            var original = prefab != null ? prefab.GetComponent<Container>() : null;
            if (original == null) return null;

            // Igual ao padrao da peca = ninguem renomeou.
            return atual == original.m_name ? null : atual;
        }

        // ------------------------------------------------------------------
        // Construcao da UI
        // ------------------------------------------------------------------
        private static void Garantir()
        {
            if (_raiz != null || Hud.instance == null || Hud.instance.m_rootObject == null) return;

            // Reaproveita a fonte de algum texto ja existente na HUD: criar TMP sem
            // font asset valido renderiza em branco.
            if (_fonte == null)
            {
                var qualquer = Hud.instance.GetComponentInChildren<TMP_Text>(true);
                if (qualquer != null) _fonte = qualquer.font;
            }

            _raiz = new GameObject("VT_ItensGuardados", typeof(RectTransform));
            _raiz.transform.SetParent(Hud.instance.m_rootObject.transform, worldPositionStays: false);

            var rt = _raiz.GetComponent<RectTransform>();
            rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0f, 0f);
            rt.anchoredPosition = new Vector2(
                ModConfig.StoreHudX.Value, ModConfig.StoreHudY.Value);
            rt.sizeDelta = new Vector2(400f, 0f);

            var layout = _raiz.AddComponent<VerticalLayoutGroup>();
            layout.childAlignment = TextAnchor.LowerLeft;
            layout.spacing = 3f;
            layout.childForceExpandWidth = false;
            layout.childForceExpandHeight = false;
            layout.childControlWidth = true;
            layout.childControlHeight = true;

            var fitter = _raiz.AddComponent<ContentSizeFitter>();
            fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            fitter.horizontalFit = ContentSizeFitter.FitMode.PreferredSize;
        }

        internal static void Adicionar(ItemDrop.ItemData item, int quantidade, string nomeBau)
        {
            if (!ModConfig.StoreHudEnabled.Value || item == null) return;
            Garantir();
            if (_raiz == null) return;

            var go = new GameObject("linha", typeof(RectTransform));
            go.transform.SetParent(_raiz.transform, worldPositionStays: false);

            var fade = go.AddComponent<CanvasGroup>();
            var h = go.AddComponent<HorizontalLayoutGroup>();
            h.spacing = 7f;
            h.childAlignment = TextAnchor.MiddleLeft;
            h.childForceExpandWidth = false;
            h.childForceExpandHeight = false;
            h.childControlWidth = true;
            h.childControlHeight = true;

            // Icone
            var icones = item.m_shared.m_icons;
            if (icones != null && icones.Length > 0)
            {
                var icoGo = new GameObject("ico", typeof(RectTransform));
                icoGo.transform.SetParent(go.transform, worldPositionStays: false);
                var img = icoGo.AddComponent<Image>();
                img.sprite = icones[Mathf.Clamp(item.m_variant, 0, icones.Length - 1)];
                img.preserveAspect = true;
                var le = icoGo.AddComponent<LayoutElement>();
                le.preferredWidth = le.preferredHeight = 20f;
            }

            // Texto
            var txtGo = new GameObject("txt", typeof(RectTransform));
            txtGo.transform.SetParent(go.transform, worldPositionStays: false);
            var txt = txtGo.AddComponent<TextMeshProUGUI>();
            if (_fonte != null) txt.font = _fonte;
            txt.fontSize = ModConfig.StoreHudFontSize.Value;
            txt.alignment = TextAlignmentOptions.MidlineLeft;
            txt.enableWordWrapping = false;
            txt.raycastTarget = false;

            string nome = item.m_shared.m_name;
            txt.text = string.IsNullOrEmpty(nomeBau)
                ? $"<color=#E8DDC4>{nome} x{quantidade}</color>"
                : $"<color=#E8DDC4>{nome} x{quantidade}</color> <color=#A8996F>→</color> <color=#D8C48A>{nomeBau}</color>";

            Linhas.Add(new Linha { Go = go, Fade = fade, Nasceu = Time.realtimeSinceStartup });

            // Teto de linhas: despejo grande viraria parede de texto.
            while (Linhas.Count > ModConfig.StoreHudMaxLines.Value) Remover(0);
        }

        private static void Remover(int i)
        {
            if (i < 0 || i >= Linhas.Count) return;
            if (Linhas[i].Go != null) Object.Destroy(Linhas[i].Go);
            Linhas.RemoveAt(i);
        }

        // ------------------------------------------------------------------
        // Expiracao
        // ------------------------------------------------------------------
        internal static void Update()
        {
            if (Linhas.Count == 0) return;

            float agora = Time.realtimeSinceStartup;
            float vida = ModConfig.StoreHudSeconds.Value;
            const float Fade = 1f;

            for (int i = Linhas.Count - 1; i >= 0; i--)
            {
                var l = Linhas[i];
                if (l.Go == null) { Linhas.RemoveAt(i); continue; }

                float idade = agora - l.Nasceu;
                if (idade >= vida) { Remover(i); continue; }

                l.Fade.alpha = idade > vida - Fade
                    ? Mathf.Clamp01((vida - idade) / Fade)
                    : 1f;
            }
        }
    }
}
