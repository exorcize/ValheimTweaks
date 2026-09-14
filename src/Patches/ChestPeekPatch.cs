using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace ValheimTweaks.Patches
{
    /// <summary>
    /// Espiar o conteudo do bau mirado, sem abrir.
    ///
    /// Da para ler sem abrir porque Container.Update chama Load(), que desserializa
    /// ZDOVars.s_items do ZDO -- e so relê quando a DataRevision muda. Ou seja: o
    /// m_inventory de qualquer bau carregado ja esta em dia de graca.
    ///
    /// Vale para carroca e navio tambem: os tres sao Container no codigo.
    ///
    /// A mira sozinha nao basta -- exigimos proximidade. Na pratica o raycast de
    /// hover do jogo ja limita a m_maxInteractDistance, mas a checagem fica
    /// explicita para o caso de outro mod esticar esse alcance.
    ///
    /// Itens sao agrupados por tipo antes de ordenar: GetAllItems devolve por SLOT,
    /// entao tres pilhas de madeira viriam como tres entradas e a ordenacao sairia
    /// errada com madeira repetida.
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
        // Deteccao
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
                // Atraso antes de sumir: mira que passa raspando faria o painel piscar.
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

            // So remonta quando muda de bau ou quando o conteudo muda de verdade.
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
        // Conteudo agrupado e ordenado
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

            var fundo = _painel.AddComponent<Image>();
            fundo.color = new Color(0.08f, 0.08f, 0.07f, 0.82f);
            fundo.raycastTarget = false;

            var col = _painel.AddComponent<VerticalLayoutGroup>();
            col.padding = new RectOffset(9, 9, 8, 8);
            col.spacing = 6f;
            col.childAlignment = TextAnchor.UpperLeft;
            col.childForceExpandWidth = false;
            col.childForceExpandHeight = false;
            col.childControlWidth = true;
            col.childControlHeight = true;

            var fit = _painel.AddComponent<ContentSizeFitter>();
            fit.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            fit.horizontalFit = ContentSizeFitter.FitMode.PreferredSize;

            _titulo = NovoTexto(_painel.transform, 14f, "#D8C48A");

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

            // Mostra TUDO, mas encolhe o slot conforme a quantidade de tipos para
            // nao tomar a tela. Colunas sobem junto, senao viraria uma coluna alta.
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

            _rodape.text = $"{itens.Count} tipos · {inv.NrOfItems()} / {inv.GetWidth() * inv.GetHeight()} espaços";

            _painel.SetActive(true);
        }

        private static void NovoSlot(float lado, Entrada e)
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
