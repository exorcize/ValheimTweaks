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
        private class Linha
        {
            internal GameObject Go;
            internal CanvasGroup Fade;
            internal float Nasceu;
        }

        private static GameObject _raiz;
        private static readonly List<Linha> Linhas = new List<Linha>();
        private static TMP_FontAsset _fonte;
        private static Material _material;

        /// <summary>
        /// Font and material from a KNOWN HUD text. Grabbing the first TMP_Text
        /// that appeared brought some random font, and without the
        /// fontSharedMaterial TextMeshPro falls back -- that's what made the text
        /// look like a console on the first attempt.
        /// </summary>
        internal static bool AplicarFonte(TMP_Text alvo)
        {
            if (alvo == null) return false;

            if (_fonte == null && Hud.instance != null)
            {
                TMP_Text molde = Hud.instance.m_healthText
                              ?? Hud.instance.m_staminaText
                              ?? Hud.instance.m_actionName;

                if (molde == null && MessageHud.instance != null)
                    molde = MessageHud.instance.m_messageCenterText;

                if (molde != null)
                {
                    _fonte = molde.font;
                    _material = molde.fontSharedMaterial;
                }
            }

            if (_fonte == null) return false;   // HUD not ready yet: try again later

            alvo.font = _fonte;
            if (_material != null) alvo.fontSharedMaterial = _material;
            return true;
        }

        // ------------------------------------------------------------------
        // Chest's custom name, or null
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

            // Same as the piece's default = no one renamed it.
            return atual == original.m_name ? null : atual;
        }

        // ------------------------------------------------------------------
        // UI construction
        // ------------------------------------------------------------------
        private static void Garantir()
        {
            if (_raiz != null || Hud.instance == null || Hud.instance.m_rootObject == null) return;

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

            // Icon
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

            // Text
            var txtGo = new GameObject("txt", typeof(RectTransform));
            txtGo.transform.SetParent(go.transform, worldPositionStays: false);
            var txt = txtGo.AddComponent<TextMeshProUGUI>();
            AplicarFonte(txt);
            txt.fontSize = ModConfig.StoreHudFontSize.Value;
            txt.alignment = TextAlignmentOptions.MidlineLeft;
            txt.enableWordWrapping = false;
            txt.raycastTarget = false;

            // m_name is a localization token ("$item_feathers"); without Localize
            // the raw token appears on screen.
            string nome = Localization.instance.Localize(item.m_shared.m_name);
            txt.text = string.IsNullOrEmpty(nomeBau)
                ? $"<color=#E8DDC4>{nome} x{quantidade}</color>"
                : $"<color=#E8DDC4>{nome} x{quantidade}</color> <color=#A8996F>→</color> <color=#D8C48A>{nomeBau}</color>";

            Linhas.Add(new Linha { Go = go, Fade = fade, Nasceu = Time.realtimeSinceStartup });

            // Line cap: a big dump would become a wall of text.
            while (Linhas.Count > ModConfig.StoreHudMaxLines.Value) Remover(0);
        }

        private static void Remover(int i)
        {
            if (i < 0 || i >= Linhas.Count) return;
            if (Linhas[i].Go != null) Object.Destroy(Linhas[i].Go);
            Linhas.RemoveAt(i);
        }

        // ------------------------------------------------------------------
        // Expiration
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
