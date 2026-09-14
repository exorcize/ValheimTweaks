using System.Reflection;
using HarmonyLib;
using TMPro;
using UnityEngine;

namespace ValheimTweaks.Patches
{
    /// <summary>
    /// Minerar sozinho enquanto a mira estiver no minério.
    ///
    /// ---- Só minério, não pedra ----
    /// MineRock5 e MineRock servem tanto para depósito de cobre quanto para pedra
    /// comum, então o tipo não distingue. O que distingue é o que o alvo SOLTA:
    /// ambos expõem m_dropItems (DropTable) com m_drops[].m_item, e basta olhar se
    /// algum drop bate com a lista de minérios. Pedra solta Stone, que não está na
    /// lista -- e assim a regra vale para minério modado também, bastando
    /// acrescentar o nome na config.
    ///
    /// ---- Alcance de verdade ----
    /// Mirar não basta: a distância é medida até o PONTO MAIS PRÓXIMO do collider
    /// (Collider.ClosestPoint), não até o transform. Depósito é grande e o centro
    /// dele pode estar metros além da superfície -- medir pelo centro deixava o
    /// personagem batendo no que estivesse na frente. E o limite é o alcance real
    /// da arma equipada (m_attack.m_attackRange), não um número fixo.
    ///
    /// ---- Como para ----
    /// As condições são checadas todo frame e a fila de ataque só é alimentada
    /// enquanto todas passam. Quebrou o minério, desviou a mira, guardou a
    /// picareta ou afastou: o golpe em curso termina e o personagem para. Não
    /// existe detecção separada de "quebrou".
    /// </summary>
    internal static class AutoMinePatch
    {
        private static readonly FieldInfo HoveringField =
            AccessTools.Field(typeof(Player), "m_hovering");

        private static readonly FieldInfo QueuedAttackField =
            AccessTools.Field(typeof(Player), "m_queuedAttackTimer");

        private static bool _ligado;
        private static GameObject _aviso;
        private static TextMeshProUGUI _avisoTxt;

        internal static bool Ligado => _ligado;

        // ------------------------------------------------------------------
        // O alvo solta minerio?
        // ------------------------------------------------------------------
        private static bool SoltaMinerio(DropTable tabela)
        {
            if (tabela?.m_drops == null) return false;

            string lista = ModConfig.AutoMineOres.Value ?? "";
            if (lista.Length == 0) return false;

            foreach (var d in tabela.m_drops)
            {
                if (d.m_item == null) continue;
                string nome = d.m_item.name.ToLowerInvariant();

                foreach (var frag in lista.Split(','))
                {
                    var f = frag.Trim().ToLowerInvariant();
                    if (f.Length > 0 && nome.Contains(f)) return true;
                }
            }
            return false;
        }

        /// <summary>Collider do minerio sob a mira, ou null.</summary>
        private static Collider MinerioMirado(Player player)
        {
            if (HoveringField == null) return null;

            var go = HoveringField.GetValue(player) as GameObject;
            if (go == null) return null;

            var rock5 = go.GetComponentInParent<MineRock5>();
            if (rock5 != null && !SoltaMinerio(rock5.m_dropItems)) return null;

            if (rock5 == null)
            {
                var rock = go.GetComponentInParent<MineRock>();
                if (rock == null) return null;                      // nem minerável
                if (!SoltaMinerio(rock.m_dropItems)) return null;   // minerável, mas é pedra
            }

            // O collider mirado e o pedaco exato: MineRock5 e dividido em varias
            // areas, e usar o do objeto raiz mediria distancia errada.
            return go.GetComponent<Collider>() ?? go.GetComponentInParent<Collider>();
        }

        private static float AlcanceDaArma(Player player)
        {
            var arma = player.GetCurrentWeapon();
            var atk = arma?.m_shared?.m_attack;
            float baseRange = (atk != null && atk.m_attackRange > 0f) ? atk.m_attackRange : 2f;
            return baseRange * ModConfig.AutoMineRangeScale.Value;
        }

        private static bool ComPicareta(Player player)
        {
            var arma = player.GetCurrentWeapon();
            return arma != null && arma.m_shared.m_skillType == Skills.SkillType.Pickaxes;
        }

        // ------------------------------------------------------------------
        internal static void Update()
        {
            var player = Player.m_localPlayer;
            if (player == null) { MostrarAviso(false); return; }

            bool digitando = (Chat.instance != null && Chat.instance.HasFocus())
                             || Console.IsVisible() || TextInput.IsVisible();

            if (!digitando && ModConfig.AutoMineKey.Value.IsDown())
            {
                _ligado = !_ligado;
                player.Message(MessageHud.MessageType.Center,
                    _ligado ? "Minerar automático LIGADO" : "Minerar automático desligado");
            }

            MostrarAviso(_ligado);

            if (!_ligado || QueuedAttackField == null) return;
            if (InventoryGui.IsVisible() || Minimap.IsOpen()) return;
            if (!ComPicareta(player)) return;

            var col = MinerioMirado(player);
            if (col == null) return;

            // Ponto mais proximo do collider, nao o centro do objeto.
            Vector3 origem = player.transform.position + Vector3.up * 1f;
            float d = Vector3.Distance(origem, col.ClosestPoint(origem));
            if (d > AlcanceDaArma(player)) return;

            // Mesmo valor que o jogo escreve ao apertar o botao de ataque.
            QueuedAttackField.SetValue(player, 0.5f);
        }

        // ------------------------------------------------------------------
        // Indicador permanente enquanto ligado
        // ------------------------------------------------------------------
        private static void MostrarAviso(bool visivel)
        {
            if (!visivel)
            {
                if (_aviso != null && _aviso.activeSelf) _aviso.SetActive(false);
                return;
            }

            if (_aviso == null)
            {
                if (Hud.instance == null || Hud.instance.m_rootObject == null) return;

                _aviso = new GameObject("VT_AutoMinerar", typeof(RectTransform));
                _aviso.transform.SetParent(Hud.instance.m_rootObject.transform, worldPositionStays: false);

                var rt = _aviso.GetComponent<RectTransform>();
                rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0.5f, 0f);
                rt.anchoredPosition = new Vector2(0f, ModConfig.AutoMineHudY.Value);
                rt.sizeDelta = new Vector2(400f, 26f);

                _avisoTxt = _aviso.AddComponent<TextMeshProUGUI>();
                StoreHudPatch.AplicarFonte(_avisoTxt);
                _avisoTxt.fontSize = 15f;
                _avisoTxt.alignment = TextAlignmentOptions.Center;
                _avisoTxt.raycastTarget = false;
                _avisoTxt.text = "<color=#D8C48A>⛏ minerar automático</color>";
            }

            if (!_aviso.activeSelf) _aviso.SetActive(true);
        }
    }
}
