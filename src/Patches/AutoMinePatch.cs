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
        private static float _avisoAte;
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

            // Tres formas diferentes de um alvo minerável carregar sua tabela de
            // drop. O estanho nao usa MineRock: e um Destructible com o componente
            // separado DropOnDestroyed, e por isso a versao anterior o ignorava.
            bool ehMinerio = false;

            var rock5 = go.GetComponentInParent<MineRock5>();
            if (rock5 != null) ehMinerio = SoltaMinerio(rock5.m_dropItems);

            if (!ehMinerio)
            {
                var rock = go.GetComponentInParent<MineRock>();
                if (rock != null) ehMinerio = SoltaMinerio(rock.m_dropItems);
            }

            if (!ehMinerio)
            {
                var drop = go.GetComponentInParent<DropOnDestroyed>();
                if (drop != null) ehMinerio = SoltaMinerio(drop.m_dropWhenDestroyed);
            }

            if (!ehMinerio)
            {
                Diagnosticar(go);
                return null;
            }

            // O collider mirado e o pedaco exato: MineRock5 e dividido em varias
            // areas, e usar o do objeto raiz mediria distancia errada.
            return go.GetComponent<Collider>() ?? go.GetComponentInParent<Collider>();
        }

        private static string _ultimoDiagnostico;

        /// <summary>
        /// Loga o que foi recusado e por que. Sem isso, "nao funciona no estanho"
        /// vira tentativa e erro; com isso o proprio log diz qual componente o
        /// alvo usa e o que ele solta.
        /// </summary>
        private static void Diagnosticar(GameObject go)
        {
            if (!ModConfig.AutoMineDebug.Value) return;

            string nome = go.name;
            if (nome == _ultimoDiagnostico) return; // nao repete todo frame
            _ultimoDiagnostico = nome;

            var partes = new System.Text.StringBuilder();
            partes.Append($"[AUTO-MINERAR] alvo recusado: {nome}");

            var r5 = go.GetComponentInParent<MineRock5>();
            var r = go.GetComponentInParent<MineRock>();
            var dd = go.GetComponentInParent<DropOnDestroyed>();
            var de = go.GetComponentInParent<Destructible>();

            partes.Append($" | MineRock5={r5 != null} MineRock={r != null} " +
                          $"DropOnDestroyed={dd != null} Destructible={de != null}");

            var tabela = r5?.m_dropItems ?? r?.m_dropItems ?? dd?.m_dropWhenDestroyed;
            if (tabela?.m_drops != null)
            {
                partes.Append(" | solta:");
                foreach (var d in tabela.m_drops)
                    if (d.m_item != null) partes.Append(' ').Append(d.m_item.name);
            }
            else partes.Append(" | sem tabela de drop");

            Plugin.Log.LogInfo(partes.ToString());
        }

        /// <summary>
        /// Distancia ate a SUPERFICIE do alvo.
        ///
        /// Collider.ClosestPoint so aceita box, esfera, capsula e mesh CONVEXA --
        /// e deposito de minerio e mesh nao convexa. Nesses casos o Unity nao so
        /// cospe um aviso por frame no log (milhares em poucos minutos de jogo)
        /// como devolve o proprio ponto consultado: a distancia saia zero e o
        /// limite de alcance nunca reprovava nada.
        ///
        /// Para collider nao suportado usamos a caixa envolvente. Menos preciso
        /// que a malha, mas funciona em qualquer tipo, nao polui o log e -- o que
        /// importa -- nao mente sobre a distancia.
        /// </summary>
        private static float DistanciaAteSuperficie(Collider col, Vector3 origem)
        {
            var mesh = col as MeshCollider;
            bool suportado = col is BoxCollider || col is SphereCollider
                             || col is CapsuleCollider || (mesh != null && mesh.convex);

            Vector3 ponto = suportado ? col.ClosestPoint(origem)
                                      : col.ClosestPointOnBounds(origem);
            return Vector3.Distance(origem, ponto);
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
                // De proposito NAO usa MessageHud: a duracao dele e do jogo e vale
                // para todas as mensagens. O proprio indicador da o retorno, com
                // tempo que a gente controla.
                _avisoAte = Time.realtimeSinceStartup + ModConfig.AutoMineToggleSeconds.Value;
            }

            // Fica visivel enquanto ligado; ao desligar, ainda aparece pelo tempo
            // do aviso para voce ver que desligou.
            MostrarAviso(_ligado || Time.realtimeSinceStartup < _avisoAte);

            if (!_ligado || QueuedAttackField == null) return;
            if (InventoryGui.IsVisible() || Minimap.IsOpen()) return;
            if (!ComPicareta(player)) return;

            var col = MinerioMirado(player);
            if (col == null) return;

            // Distancia ate a superficie do alvo, nao ate o centro.
            Vector3 origem = player.transform.position + Vector3.up * 1f;
            float d = DistanciaAteSuperficie(col, origem);
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
                _avisoTxt.fontSize = 15f;
                _avisoTxt.alignment = TextAlignmentOptions.Center;
                _avisoTxt.raycastTarget = false;
            }

            // Reaplica a cada exibicao: se a HUD ainda nao estava pronta quando o
            // objeto foi criado, a fonte teria ficado no fallback para sempre. E
            // sem fonte o TMP procura LiberationSans, que o Valheim nao inclui --
            // o rotulo sai sem fonte nenhuma. Melhor esconder e tentar no proximo
            // frame do que mostrar quebrado.
            if (!StoreHudPatch.AplicarFonte(_avisoTxt))
            {
                if (_aviso.activeSelf) _aviso.SetActive(false);
                return;
            }

            // Logo apos alternar, destaca o estado; depois volta ao rotulo discreto.
            // Sem emoji: a fonte do Valheim nao tem esses glifos e sai quadradinho.
            bool recemAlternado = Time.realtimeSinceStartup < _avisoAte;
            if (recemAlternado)
                _avisoTxt.text = _ligado
                    ? "<color=#B8DD97>Minerar automático LIGADO</color>"
                    : "<color=#C08080>Minerar automático desligado</color>";
            else
                _avisoTxt.text = "<color=#D8C48A>Minerar automático</color>";

            if (!_aviso.activeSelf) _aviso.SetActive(true);
        }
    }
}
