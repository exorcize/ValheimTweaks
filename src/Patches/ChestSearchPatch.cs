using System.Collections.Generic;
using System.Reflection;
using System.Text;
using HarmonyLib;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace ValheimTweaks.Patches
{
    /// <summary>
    /// Painel de busca nos baús próximos, no lugar do painel de produção.
    ///
    /// ---- Por que ele vive junto do inventário ----
    /// Player.TakeInput e GameCamera.UpdateMouseCapture decidem se você anda e se o
    /// cursor aparece consultando uma LISTA FIXA de telas do jogo -- não há ponto de
    /// extensão. Um painel solto exigiria remendar os dois, senão o personagem anda
    /// enquanto você digita. Abrindo junto do inventário, InventoryGui.IsVisible() já
    /// é true: cursor solto, personagem parado, ESC fechando. Zero patch de input.
    ///
    /// ---- Por que ocupa o lugar da produção ----
    /// O lado direito da tela de inventário JÁ é do painel de produção. Desenhar por
    /// cima deixa os dois aparecendo um through the other. Em vez disso escondemos a
    /// produção enquanto o painel está aberto e copiamos o RectTransform dela --
    /// mesma posição, mesmo tamanho, encaixe exato. Fechou, a produção volta.
    ///
    /// ---- Layout na mão, de propósito ----
    /// A primeira versão usava VerticalLayoutGroup/GridLayoutGroup e saiu torta: os
    /// botões de ordenação viraram caixas de 100px. Layout automático do uGUI depende
    /// de rebuild na ordem certa, e num painel criado em runtime dentro de outro
    /// canvas isso não é confiável. Painel de tamanho fixo não ganha nada com layout
    /// automático -- posicionar na mão é determinístico e dá para conferir na tela.
    ///
    /// ---- Ler baú fechado é de graça ----
    /// Container.Awake registra InvokeRepeating("CheckForChanges", 0f, 1f), e o Load()
    /// sai na hora quando a DataRevision não mudou. O inventário de todo baú carregado
    /// já está em memória e no máximo 1s atrasado.
    ///
    /// ---- Pegar item ----
    /// Quem manda nos dados de um baú é o dono do ZDO. Dois caminhos:
    ///
    ///   já sou o dono  -> movo direto, sem RPC, instantâneo
    ///   não sou        -> Container.TakeAll() e o aperto de mão do jogo
    ///
    /// A distinção importa: RPC_RequestTakeAll recusa dois pedidos ao mesmo baú
    /// dentro de 2s (m_lastTakeAllTime). Pelo caminho do dono esse limite não existe
    /// -- e perto da própria base você é o dono de tudo, que era a lentidão relatada.
    /// </summary>
    internal static class ChestSearchPatch
    {
        // ==================================================================
        // Modelo
        // ==================================================================
        private class Onde
        {
            internal Container Bau;
            internal string Nome;
            internal int Qtd;
        }

        private class Agregado
        {
            internal string Chave;
            internal ItemDrop.ItemData Amostra;
            internal string NomeLocal;
            internal int Total;
            internal readonly List<Onde> Ondes = new List<Onde>();
        }

        /// <summary>Capacidade de um baú, em slots (cada pilha ocupa um).</summary>
        private class InfoBau
        {
            internal Container Bau;
            internal string Nome;
            internal int Usados;
            internal int Totais;
            internal int Livres => Totais - Usados;
        }

        // ==================================================================
        // Estado
        // ==================================================================
        private static bool _aberto;
        private static readonly List<Agregado> _tudo = new List<Agregado>();
        private static readonly List<InfoBau> _baus = new List<InfoBau>();
        private static int _slotsUsados, _slotsTotais;
        private static float _proximaVarredura;
        private static bool _porBau;
        private static bool _ordemPorNome;
        private static Agregado _focado;
        private static int _bausVistos;
        private static string _assinatura;
        private static bool _buscaFocada;

        // UI
        private static GameObject _painel;
        private static RectTransform _rolo, _conteudo;
        private static TMP_Text _resumo, _rodNome, _rodTot, _rodOndes, _rodAcao;
        private static TMP_Text _ocupTxt;
        private static Image _ocupFill;
        private static TMP_InputField _busca;
        private static Button _botao;
        private static TMP_Text _botaoTxt;
        private static readonly Image[] _chip = new Image[3];
        private static readonly TMP_Text[] _chipTxt = new TMP_Text[3];
        private static readonly List<GameObject> _descartar = new List<GameObject>();

        // ---- paleta
        private static readonly Color Ouro = Cor("#E6D3A2");
        private static readonly Color Texto = Cor("#A2947C");
        private static readonly Color Apagado = Cor("#6F6353");
        private static readonly Color Linha = new Color(0.745f, 0.627f, 0.431f, 0.18f);
        private static readonly Color Borda = Cor("#4A3D2C");
        private static readonly Color ChipFundo = Cor("#171208");

        private static Color Cor(string hex)
            => ColorUtility.TryParseHtmlString(hex, out var c) ? c : Color.white;


        // ---- medidas do painel (tudo em pixels de canvas, do topo para baixo)
        private const float Pad = 12f;
        private const float AltTitulo = 17f;
        private const float AltResumo = 14f;
        private const float AltBusca = 26f;
        private const float AltChip = 26f;
        private const float AltBarra = 12f;
        private const float AltRodape = 92f;
        private const float GapCel = 5f;
        private const float AltRotulo = 15f;

        // ==================================================================
        // Ciclo
        // ==================================================================
        internal static void Update()
        {
            ProcessarFila();

            if (!ModConfig.ChestSearchEnabled.Value)
            {
                // Desligar no F1 tem que sumir com o botão também. Antes ele
                // continuava na tela e clicável, dando a impressão de que a opção
                // não fazia nada.
                Fechar();
                if (_botao != null) { Object.Destroy(_botao.gameObject); _botao = null; _botaoTxt = null; }
                return;
            }

            // Tecla opcional: se o inventário estiver fechado, abre os dois de uma
            // vez. Fica fora do bloco de IsVisible por isso.
            if (TeclaDeAbrirFoiApertada())
            {
                if (!InventoryGui.IsVisible())
                {
                    InventoryGui.instance?.Show(null);
                    _abrirAoMostrar = true;   // o painel só existe depois da tela montar
                }
                else Alternar();
            }

            if (!InventoryGui.IsVisible())
            {
                Fechar();
                return;
            }

            GarantirBotao();

            if (_abrirAoMostrar)
            {
                _abrirAoMostrar = false;
                if (!_aberto) Alternar();
            }

            // Lido por um Prefix que roda dezenas de vezes por frame: calculamos uma
            // vez aqui para que la seja so a leitura de um bool.
            bool antes = _buscaFocada;
            _buscaFocada = _aberto && BuscaTemFoco();
            if (antes != _buscaFocada && ModConfig.ChestSearchDebug.Value)
                Plugin.Log.LogInfo($"[BAUS] foco na busca: {_buscaFocada}");

            if (!_aberto) return;

            // InventoryGui.Show reativa a producao. Se a tela foi reaberta por baixo
            // de nos, ela voltaria por cima do painel -- reforcamos todo frame.
            MostrarProducao(false);

            // Posicao e tamanho vem da config a cada frame, entao da para acertar o
            // encaixe pelo F1 sem reiniciar o jogo.
            PosicionarPainel();

            AtualizarDeposito();

            // Redimensionar so muda a moldura: o miolo foi posicionado na mao para o
            // tamanho antigo e ficaria encolhido num canto. Remonta quando mudar.
            var tam = _painel.GetComponent<RectTransform>().rect.size;
            if ((tam - _tamanhoMontado).sqrMagnitude > 4f) Reconstruir();

            if (Time.realtimeSinceStartup >= _proximaVarredura)
            {
                _proximaVarredura = Time.realtimeSinceStartup + ModConfig.ChestSearchRefresh.Value;
                Varrer();

                // Remontar a grade duas vezes por segundo destruiria e recriaria
                // dezenas de slots à toa, piscando o tooltip e perdendo o item sob o
                // mouse. Só remonta quando o conteúdo dos baús mudou de verdade.
                string agora = Assinatura();
                if (agora != _assinatura) { _assinatura = agora; Montar(); }
            }
        }

        private static Vector2 _tamanhoMontado;

        /// <summary>
        /// Refaz o painel inteiro preservando o que você já digitou. Só acontece
        /// quando o tamanho muda (ajuste no F1, troca de resolução), então o custo
        /// de recriar não importa.
        /// </summary>
        private static void Reconstruir()
        {
            string texto = _busca != null ? _busca.text : "";

            _painel.transform.SetParent(null, false);
            Object.Destroy(_painel);
            // Tudo abaixo era filho do painel e morreu junto. Zerar as referências
            // evita reusar objeto destruído depois do rebuild.
            _painel = null;
            _busca = null;
            _rolo = null;
            _conteudo = null;
            _ocupFill = null;
            _ocupTxt = null;
            _linhaQtd = null;
            _campoQtd = null;
            _deposito = null;
            _depTitulo = null;
            _depPlano = null;
            _depAssinatura = null;
            _rotulosAtalho.Clear();
            _descartar.Clear();

            GarantirPainel();
            if (_painel == null) return;

            _painel.SetActive(true);
            if (_busca != null) _busca.text = texto;
            _assinatura = null;
        }

        /// <summary>
        /// TMP_InputField.isFocused sozinho não bastou na prática -- o campo é um
        /// GuiInputField (subclasse do jogo) e o estado dele nem sempre bate. O
        /// EventSystem é a fonte da verdade sobre quem está recebendo o teclado.
        /// </summary>
        private static bool BuscaTemFoco()
        {
            // Vale para os DOIS campos: digitar a quantidade tambem nao pode virar
            // comando de movimento.
            if (_campoQtd != null && _campoQtd.isFocused) return true;
            if (_busca == null) return false;
            if (_busca.isFocused) return true;

            var es = UnityEngine.EventSystems.EventSystem.current;
            if (es == null) return false;

            var sel = es.currentSelectedGameObject;
            return sel != null && (sel == _busca.gameObject || sel.transform.IsChildOf(_busca.transform));
        }

        private static bool _abrirAoMostrar;

        /// <summary>
        /// A tecla não pode disparar enquanto você digita na busca nem no chat --
        /// senão a letra dela fecharia o painel no meio da pesquisa.
        /// </summary>
        private static bool TeclaDeAbrirFoiApertada()
        {
            var atalho = ModConfig.ChestSearchKey.Value;
            if (atalho.MainKey == KeyCode.None) return false;
            if (_buscaFocada) return false;
            if (Chat.instance != null && Chat.instance.HasFocus()) return false;
            if (Console.IsVisible() || TextInput.IsVisible() || Minimap.IsOpen()) return false;
            return atalho.IsDown();
        }

        private static void Fechar()
        {
            if (!_aberto && _painel == null) return;

            // Fechar com o diálogo de dividir aberto deixaria os nossos ouvintes
            // pendurados no objeto compartilhado do jogo.
            if (_meuSplit) { InventoryGui.instance?.m_splitDialog?.SetActive(false); LimparSplit(); }

            if (_painel != null && _painel.activeSelf) _painel.SetActive(false);
            MostrarProducao(true);
            _aberto = false;
            _focado = null;
            _buscaFocada = false;
            AtualizarBotao();
        }

        private static void Alternar()
        {
            if (_aberto) { Fechar(); return; }

            GarantirPainel();
            if (_painel == null) return;

            _aberto = true;
            _assinatura = null;
            _proximaVarredura = 0f;
            MostrarProducao(false);
            _painel.SetActive(true);
            if (_busca != null) _busca.ActivateInputField();
            AtualizarBotao();
        }

        /// <summary>
        /// O painel ocupa o lugar da produção em vez de desenhar por cima dela.
        /// </summary>
        private static void MostrarProducao(bool visivel)
        {
            var gui = InventoryGui.instance;
            if (gui == null || gui.m_crafting == null) return;
            if (gui.m_crafting.gameObject.activeSelf != visivel)
                gui.m_crafting.gameObject.SetActive(visivel);
        }

        // ==================================================================
        // Varredura
        // ==================================================================
        private static string Chave(ItemDrop.ItemData item)
        {
            // Qualidade entra na chave só quando o item tem níveis -- senão duas
            // espadas de qualidade diferente virariam uma pilha só na tela.
            return item.m_shared.m_maxQuality > 1
                ? item.m_shared.m_name + "#" + item.m_quality
                : item.m_shared.m_name;
        }

        private static void Varrer()
        {
            _tudo.Clear();
            _baus.Clear();
            _slotsUsados = 0;
            _slotsTotais = 0;
            var mapa = new Dictionary<string, Agregado>();

            var baus = Baus.Proximos(ModConfig.ChestSearchRadius.Value);
            _bausVistos = baus.Count;

            foreach (var bau in baus)
            {
                var inv = bau.GetInventory();
                if (inv == null) continue;

                string nomeBau = Baus.NomeVisivel(bau);

                // Slot é por PILHA, não por unidade: NrOfItems devolve m_inventory.Count,
                // que é o número de pilhas. 120 de madeira com pilha de 50 ocupa 3.
                var info = new InfoBau
                {
                    Bau = bau,
                    Nome = nomeBau,
                    Usados = inv.NrOfItems(),
                    Totais = inv.GetWidth() * inv.GetHeight(),
                };
                _baus.Add(info);
                _slotsUsados += info.Usados;
                _slotsTotais += info.Totais;

                foreach (var item in inv.GetAllItems())
                {
                    string chave = Chave(item);
                    if (!mapa.TryGetValue(chave, out var ag))
                    {
                        ag = new Agregado
                        {
                            Chave = chave,
                            Amostra = item,
                            NomeLocal = Localization.instance.Localize(item.m_shared.m_name),
                        };
                        mapa[chave] = ag;
                        _tudo.Add(ag);
                    }

                    ag.Total += item.m_stack;

                    var onde = ag.Ondes.Find(o => o.Bau == bau);
                    if (onde == null) ag.Ondes.Add(new Onde { Bau = bau, Nome = nomeBau, Qtd = item.m_stack });
                    else onde.Qtd += item.m_stack;
                }
            }

            if (_focado != null && !_tudo.Contains(_focado))
            {
                string chave = _focado.Chave;
                _focado = _tudo.Find(a => a.Chave == chave);
            }
        }

        private static string Assinatura()
        {
            var sb = new StringBuilder();
            sb.Append(_bausVistos).Append('/').Append(_slotsUsados)
              .Append('/').Append(_slotsTotais).Append('|');
            foreach (var a in _tudo) sb.Append(a.Chave).Append(':').Append(a.Total).Append(';');
            return sb.ToString();
        }

        // Buscar "carvao" tem que achar "Carvão", e "carvão" também. Tabela
        // explícita em vez de String.Normalize(FormD): a decomposição Unicode
        // depende da ICU, que no Mono do Unity nem sempre está completa -- e uma
        // busca que falha calada é pior do que não ter busca.
        private const string ComAcento = "áàâãäéèêëíìîïóòôõöúùûüçñýÿ";
        private const string SemAcento = "aaaaaeeeeiiiiooooouuuucnyy";

        private static string Normalizar(string s)
        {
            if (string.IsNullOrEmpty(s)) return "";

            // As duas tabelas andam em par pelo índice: se alguém editar uma e
            // esquecer a outra, o erro seria silencioso e trocaria letra por letra.
            if (ComAcento.Length != SemAcento.Length) return s.ToLowerInvariant();

            var sb = new StringBuilder(s.Length);
            foreach (char bruto in s)
            {
                char c = char.ToLowerInvariant(bruto);
                int i = ComAcento.IndexOf(c);
                sb.Append(i >= 0 ? SemAcento[i] : c);
            }
            return sb.ToString();
        }

        private static List<Agregado> Filtrados()
        {
            string q = Normalizar(_busca != null ? _busca.text.Trim() : "");

            var l = new List<Agregado>();
            foreach (var a in _tudo)
                if (q.Length == 0 || Normalizar(a.NomeLocal).Contains(q))
                    l.Add(a);

            if (_ordemPorNome)
                l.Sort((x, y) => string.Compare(x.NomeLocal, y.NomeLocal, System.StringComparison.CurrentCulture));
            else
                l.Sort((x, y) =>
                {
                    int d = y.Total.CompareTo(x.Total);
                    return d != 0 ? d : string.Compare(x.NomeLocal, y.NomeLocal, System.StringComparison.CurrentCulture);
                });

            return l;
        }

        // ==================================================================
        // Pegar
        // ==================================================================
        private class Pedido
        {
            internal Container Bau;
            internal string Chave;
            internal int Qtd;
            internal string Nome;
            /// <summary>true = mochila -> baú (guardar); false = baú -> mochila (pegar).</summary>
            internal bool Guardando;
        }

        private static readonly List<Pedido> _fila = new List<Pedido>();
        private static readonly Dictionary<Container, float> _ultimoPedido =
            new Dictionary<Container, float>();

        private static Pedido _emVoo;
        private static float _emVooAte;
        private static int _levados;
        private static string _nomeRodada;

        private static bool FiltroAtivo => _emVoo != null && Time.realtimeSinceStartup < _emVooAte;

        /// <summary>
        /// Já sou dono do ZDO? Então posso mexer no inventário direto -- é a mesma
        /// condição que o próprio jogo exige antes de conceder o pedido.
        /// </summary>
        private static bool SouDono(Container bau)
        {
            var nview = bau.GetComponent<ZNetView>();
            if (nview == null || !nview.IsValid() || !nview.IsOwner()) return false;
            if (bau.IsInUse()) return false;
            if (bau.m_checkGuardStone && !PrivateArea.CheckAccess(bau.transform.position, 0f, false))
                return false;
            return true;
        }

        /// <summary>
        /// Tira <paramref name="quantidade"/> unidades do item, juntando de quantos
        /// baús for preciso. Passe int.MaxValue para "tudo".
        /// </summary>
        private static void Pegar(Agregado ag, Container soDeste, int quantidade)
        {
            if (ag == null || Player.m_localPlayer == null) return;

            int querido = Mathf.Clamp(quantidade, 1, ag.Total);

            // Do baú com mais primeiro: menos pedidos para a mesma quantidade.
            var ondes = new List<Onde>(ag.Ondes);
            ondes.Sort((a, b) => b.Qtd.CompareTo(a.Qtd));

            _levados = 0;
            _nomeRodada = ag.NomeLocal;

            var destino = Player.m_localPlayer.GetInventory();

            foreach (var o in ondes)
            {
                if (querido <= 0) break;
                if (o.Qtd <= 0 || o.Bau == null) continue;
                if (soDeste != null && o.Bau != soDeste) continue;

                int n = Mathf.Min(o.Qtd, querido);
                querido -= n;

                if (SouDono(o.Bau))
                {
                    // Caminho rápido: sem RPC, sem espera, sem limite de 2s.
                    _levados += Mover(destino, o.Bau.GetInventory(), ag.Chave, n);
                }
                else
                {
                    _fila.Add(new Pedido { Bau = o.Bau, Chave = ag.Chave, Qtd = n, Nome = ag.NomeLocal });
                }
            }

            Concluir();
        }

        /// <summary>Move até <paramref name="qtd"/> do item, respeitando espaço.</summary>
        private static readonly MethodInfo MetodoChanged =
            AccessTools.Method(typeof(Inventory), "Changed", new[] { typeof(bool), typeof(bool) });

        private static void Notificar(Inventory inv)
            => MetodoChanged?.Invoke(inv, new object[] { true, false });

        /// <summary>Quantas unidades deste item existem no inventário.</summary>
        private static int Contar(Inventory inv, string chave)
        {
            if (inv == null) return 0;
            int n = 0;
            foreach (var it in inv.GetAllItems())
                if (Chave(it) == chave) n += it.m_stack;
            return n;
        }

        /// <summary>
        /// Move até <paramref name="qtd"/> unidades entre dois inventários.
        ///
        /// ---- Por que tira da origem ANTES de pôr no destino ----
        /// Inventory.AddItem não é tudo-ou-nada. Para item empilhável ele vai
        /// distribuindo unidade a unidade nas pilhas que já existem e, quando
        /// precisa de um slot novo e o inventário está cheio, devolve false --
        /// deixando lá as unidades que já tinham entrado:
        ///
        ///     itemData.m_stack++;  ... continue;      // ja entrou
        ///     ...
        ///     else { flag = false; ZLog.LogError(...); }   // e falha depois
        ///
        /// A versão anterior fazia "se AddItem deu certo, remove da origem". No
        /// caminho acima o AddItem falha, a origem NÃO é debitada, e as unidades que
        /// entraram viram item DUPLICADO. O log deste mundo tem três
        /// "Trying to add item to occupied slot -1, -1", ou seja: aconteceu.
        ///
        /// Agora debitamos primeiro e devolvemos o que não coube. Duplicar é pior que
        /// falhar, e sumir é pior que os dois -- por isso a conferência abaixo.
        /// </summary>
        private static int Mover(Inventory destino, Inventory origem, string chave, int qtd)
        {
            if (destino == null || origem == null || qtd <= 0) return 0;

            int antesOrigem = Contar(origem, chave);
            int antesDestino = Contar(destino, chave);

            int restante = qtd;

            foreach (var item in new List<ItemDrop.ItemData>(origem.GetAllItems()))
            {
                if (restante <= 0) break;
                if (Chave(item) != chave) continue;

                int n = Mathf.Min(item.m_stack, restante);

                // Destaca o pedaço e já debita a origem.
                var parte = item.Clone();
                parte.m_stack = n;
                origem.RemoveItem(item, n);

                // AddItem reduz parte.m_stack conforme coloca; o que sobrar não entrou.
                if (!destino.AddItem(parte) && parte.m_stack > 0)
                    origem.AddItem(parte);   // devolve o que não coube

                restante -= n;
            }

            Notificar(origem);
            Notificar(destino);

            int saiu = antesOrigem - Contar(origem, chave);
            int entrou = Contar(destino, chave) - antesDestino;

            // Rede de segurança: se as duas pontas não baterem, alguém ganhou ou
            // perdeu item. Não dá para desfazer com segurança aqui, mas gritar no log
            // transforma "sumiu não sei como" em algo investigável.
            if (saiu != entrou)
                Plugin.Log.LogError(
                    $"[BAUS] DESEQUILIBRIO em '{chave}': saiu {saiu} da origem mas "
                  + $"entrou {entrou} no destino (pedido {qtd}). Avise o autor do mod.");
            else if (entrou > 0 && entrou < qtd)
                Plugin.Log.LogInfo($"[BAUS] moveu {entrou} de {qtd} (destino cheio?)");

            return entrou;
        }

        /// <summary>
        /// Um pedido por vez: o RPC é assíncrono e o filtro é global, então dois
        /// baús em voo ao mesmo tempo misturariam as respostas.
        /// </summary>
        private static void ProcessarFila()
        {
            if (_emVoo != null)
            {
                if (Time.realtimeSinceStartup < _emVooAte) return;
                Plugin.Log.LogInfo($"[BAUS] sem resposta de '{Baus.NomeVisivel(_emVoo.Bau)}'");
                _emVoo = null;
                Concluir();
            }

            if (_fila.Count == 0) return;

            var p = _fila[0];
            if (p.Bau == null) { _fila.RemoveAt(0); return; }

            // O jogo recusa dois TakeAll no mesmo baú dentro de 2s
            // (Container.RPC_RequestTakeAll, m_lastTakeAllTime). Esperar é melhor do
            // que perder o pedido calado. O RPC de guardar (RPC_RequestStack) não tem
            // esse limite, então ele não espera.
            if (!p.Guardando
                && _ultimoPedido.TryGetValue(p.Bau, out float t)
                && Time.realtimeSinceStartup - t < 2.05f) return;

            _fila.RemoveAt(0);
            if (!p.Guardando) _ultimoPedido[p.Bau] = Time.realtimeSinceStartup;

            _emVoo = p;
            _emVooAte = Time.realtimeSinceStartup + 3f;

            if (p.Guardando)
            {
                // Mesmo aperto de mão do "pegar", ao contrário: o dono concede posse
                // (ForceSendZDO + SetOwner) antes de o inventário ser tocado.
                p.Bau.StackAll();
            }
            else if (!p.Bau.TakeAll(Player.m_localPlayer))
            {
                _emVoo = null;   // recusado na hora; o jogo já avisou
                Concluir();
            }
        }

        private static void Concluir()
        {
            if (_emVoo != null || _fila.Count > 0) return;
            if (_levados <= 0) return;

            Player.m_localPlayer?.Message(MessageHud.MessageType.TopLeft,
                $"{_levados} {_nomeRodada}");

            _levados = 0;
            _assinatura = null;     // força remontar
            _proximaVarredura = 0f;
        }

        /// <summary>
        /// Só entra em ação quando o pedido saiu pelo caminho do RPC (baú de outro
        /// jogador). Fora da janela, o "pegar tudo" do jogo segue intacto.
        /// </summary>
        [HarmonyPatch(typeof(Inventory), nameof(Inventory.MoveAll))]
        internal static class MoveAllHook
        {
            private static bool Prefix(Inventory __instance, Inventory fromInventory)
            {
                if (!FiltroAtivo) return true;

                var p = _emVoo;

                // Confere que é a resposta do NOSSO baú: se o jogador apertou o
                // "pegar tudo" do próprio jogo dentro da janela, não sequestramos.
                if (p.Bau == null || fromInventory != p.Bau.GetInventory()) return true;

                _emVoo = null;
                _levados += Mover(__instance, fromInventory, p.Chave, p.Qtd);
                Concluir();
                return false;
            }
        }

        /// <summary>
        /// Guardar num baú de outro jogador passa pelo RPC_RequestStack, cuja resposta
        /// chama Inventory.StackAll(mochila). Trocamos esse laço pelo movimento exato
        /// que foi planejado -- senão ele despejaria tudo que o baú já contém.
        ///
        /// O QuickStorePatch também prefixa este método, com a janela dele. Os dois
        /// convivem porque cada um só age dentro da própria janela, e as duas nunca
        /// estão abertas ao mesmo tempo (partem de ações diferentes do jogador).
        /// </summary>
        [HarmonyPatch(typeof(Inventory), nameof(Inventory.StackAll))]
        internal static class StackAllHook
        {
            private static bool Prefix(Inventory __instance, Inventory fromInventory, ref int __result)
            {
                if (!FiltroAtivo) return true;

                var p = _emVoo;
                if (p == null || !p.Guardando) return true;
                if (p.Bau == null || __instance != p.Bau.GetInventory()) return true;

                _emVoo = null;
                int n = Mover(__instance, fromInventory, p.Chave, p.Qtd);
                _levados += n;
                __result = n;
                Concluir();
                return false;
            }
        }

        // ==================================================================
        // Guardar: planejamento
        // ==================================================================
        private class Destino
        {
            internal Container Bau;
            internal string Nome;
            internal int Qtd;
            internal string Motivo;
        }

        /// <summary>
        /// Decide para onde vai cada unidade, em três passadas:
        ///
        ///   1. sobra das pilhas que JÁ existem  -> não gasta slot nenhum
        ///   2. baú que já tem o item, em slot novo -> mantém sua organização
        ///   3. qualquer baú com espaço
        ///
        /// A ordem importa: começar pelo passo 2 encheria de slots novos baús que
        /// tinham pilha pela metade, e o armazém ficaria cheio antes da hora.
        ///
        /// Só planeja. Quem executa é Guardar(), e o painel mostra o plano antes de
        /// você confirmar -- guardar sem saber onde foi parar é pior que não guardar.
        /// </summary>
        private static List<Destino> PlanejarDeposito(ItemDrop.ItemData item, int quantidade, out int sobra)
        {
            var plano = new List<Destino>();
            sobra = quantidade;
            if (item == null || quantidade <= 0) return plano;

            // C# nao deixa uma funcao local tocar em parametro out, entao o saldo
            // corre numa variavel normal e volta para 'sobra' no fim.
            int resta = quantidade;

            string nome = item.m_shared.m_name;
            int pilha = Mathf.Max(1, item.m_shared.m_maxStackSize);
            float nivel = item.m_worldLevel;

            // Slots livres vão sendo consumidos ao longo do plano, senão dois passos
            // reservariam o mesmo slot e a conta do "não coube" sairia otimista.
            var livres = new Dictionary<Container, int>();
            foreach (var b in _baus) livres[b.Bau] = b.Livres;

            void Poe(Container bau, string nomeBau, int n, string motivo)
            {
                if (n <= 0) return;
                var j = plano.Find(p => p.Bau == bau && p.Motivo == motivo);
                if (j != null) j.Qtd += n;
                else plano.Add(new Destino { Bau = bau, Nome = nomeBau, Qtd = n, Motivo = motivo });
                resta -= n;
            }

            bool TemOItem(Container bau)
            {
                var inv = bau.GetInventory();
                return inv != null && inv.ContainsItemByName(nome);
            }

            // 1) completar pilhas existentes
            foreach (var b in _baus)
            {
                if (resta <= 0) break;
                var inv = b.Bau.GetInventory();
                if (inv == null) continue;
                int cabe = inv.FindFreeStackSpace(nome, nivel);
                Poe(b.Bau, b.Nome, Mathf.Min(cabe, resta), Lang.T("tops off stack", "completa pilha"));
            }

            // 2) baús que já têm o item
            foreach (var b in _baus)
            {
                if (resta <= 0) break;
                if (livres[b.Bau] <= 0 || !TemOItem(b.Bau)) continue;
                int cabe = Mathf.Min(livres[b.Bau] * pilha, resta);
                livres[b.Bau] -= Mathf.CeilToInt(cabe / (float)pilha);
                Poe(b.Bau, b.Nome, cabe, Lang.T("with the rest", "junto do resto"));
            }

            // 3) qualquer um com espaço
            foreach (var b in _baus)
            {
                if (resta <= 0) break;
                if (livres[b.Bau] <= 0) continue;
                int cabe = Mathf.Min(livres[b.Bau] * pilha, resta);
                livres[b.Bau] -= Mathf.CeilToInt(cabe / (float)pilha);
                Poe(b.Bau, b.Nome, cabe, Lang.T("free space", "espaço livre"));
            }

            sobra = resta;
            return plano;
        }

        // ==================================================================
        // Diálogo de dividir pilha (o do próprio jogo)
        // ==================================================================
        private static bool _meuSplit;
        private static Agregado _splitAg;
        private static Container _splitBau;

        /// <summary>
        /// Reaproveita o SplitDialog do jogo, que é a telinha que todo mundo já
        /// conhece. Ele é COMPARTILHADO, e isso exige três cuidados:
        ///
        ///   - o botão OK dispara o evento SplitAccepted, que só tem ouvinte quando
        ///     foi o jogo que abriu. Por isso assinamos o nosso.
        ///   - InventoryGui.UpdateSplitDialog roda todo frame e no Enter chama
        ///     OnSplitOk() direto, sem passar pelo evento. Por isso o prefixo abaixo.
        ///   - Escape faz o jogo chamar HideSplitDialog, que não conhece os nossos
        ///     ouvintes. Por isso soltamos no postfix dele.
        ///
        /// Sem os três, ou o OK não faz nada, ou o Enter inicia um arrasto com
        /// m_splitItem nulo.
        /// </summary>
        private static void AbrirSplit(Agregado ag, Container soDeste)
        {
            var gui = InventoryGui.instance;
            if (gui == null || gui.m_splitDialog == null || ag.Total <= 1)
            {
                // Sem diálogo (ou item único) o rodapé já resolve.
                DesenharRodape();
                return;
            }

            _meuSplit = true;
            _splitAg = ag;
            _splitBau = soDeste;

            gui.m_splitDialog.UpdateLimits(ag.Total, altMode: false);
            gui.m_splitDialog.UpdateIcon(ag.Amostra.GetIcon(), ag.NomeLocal);
            gui.m_splitDialog.SplitAccepted += SplitConfirmado;
            gui.m_splitDialog.SplitCanceled += SplitCancelado;
            gui.m_splitDialog.SetActive(active: true);
        }

        private static void LimparSplit()
        {
            var gui = InventoryGui.instance;
            if (gui != null && gui.m_splitDialog != null)
            {
                gui.m_splitDialog.SplitAccepted -= SplitConfirmado;
                gui.m_splitDialog.SplitCanceled -= SplitCancelado;
            }
            _meuSplit = false;
            _splitAg = null;
            _splitBau = null;
        }

        private static void SplitConfirmado()
        {
            var gui = InventoryGui.instance;
            if (gui == null || _splitAg == null) { LimparSplit(); return; }

            int n = Mathf.RoundToInt(gui.m_splitDialog.SliderValue);
            var ag = _splitAg;
            var bau = _splitBau;

            gui.m_splitDialog.SetActive(active: false);
            LimparSplit();

            Pegar(ag, bau, n);
        }

        private static void SplitCancelado()
        {
            InventoryGui.instance?.m_splitDialog?.SetActive(active: false);
            LimparSplit();
        }

        /// <summary>Enter no diálogo chama isto direto; desviamos quando é o nosso.</summary>
        [HarmonyPatch(typeof(InventoryGui), "OnSplitOk")]
        internal static class SplitOkHook
        {
            private static bool Prefix()
            {
                if (!_meuSplit) return true;
                SplitConfirmado();
                return false;
            }
        }

        [HarmonyPatch(typeof(InventoryGui), "OnSplitCancel")]
        internal static class SplitCancelHook
        {
            private static bool Prefix()
            {
                if (!_meuSplit) return true;
                SplitCancelado();
                return false;
            }
        }

        /// <summary>Escape passa por aqui; soltamos os nossos ouvintes junto.</summary>
        [HarmonyPatch(typeof(InventoryGui), "HideSplitDialog")]
        internal static class SplitHideHook
        {
            private static void Postfix()
            {
                if (_meuSplit) LimparSplit();
            }
        }

        // ==================================================================
        // Guardar: a área de soltar
        // ==================================================================
        private static GameObject _deposito;
        private static TMP_Text _depTitulo, _depPlano;
        private static string _depAssinatura;

        /// <summary>
        /// Cobre o painel inteiro enquanto você está com um item na mão. Cobrir tudo
        /// é de propósito: se só uma faixa aceitasse o item, você erraria a mira e o
        /// clique cairia no slot de baixo, pegando outra coisa.
        /// </summary>
        private static void MontarDeposito(float larg, float alt)
        {
            _deposito = new GameObject("VT_Deposito", typeof(RectTransform));
            _deposito.transform.SetParent(_painel.transform, false);
            Por(_deposito, 0f, 0f, larg, alt);

            var fundo = _deposito.AddComponent<Image>();
            fundo.color = new Color(0.047f, 0.035f, 0.02f, 0.93f);

            var bt = _deposito.AddComponent<Button>();
            bt.targetGraphic = fundo;
            bt.onClick.AddListener(SoltarNoPainel);

            var t = NovoTexto(_deposito.transform, 17f, Cor("#8FD0E8"));
            EstiloJogo.AplicarTitulo(t);
            t.color = Cor("#8FD0E8");
            t.alignment = TextAlignmentOptions.Center;
            t.enableWordWrapping = true;
            Por(t.gameObject, Pad, alt * 0.22f, larg - Pad * 2f, 50f);
            _depTitulo = t;

            var p = NovoTexto(_deposito.transform, 12f, Texto);
            p.alignment = TextAlignmentOptions.Top;
            p.enableWordWrapping = true;
            Por(p.gameObject, Pad + 14f, alt * 0.22f + 56f, larg - Pad * 2f - 28f, alt * 0.5f);
            _depPlano = p;

            _deposito.SetActive(false);
        }

        /// <summary>Mostra/esconde a área e mantém o plano em dia. Chamado todo frame.</summary>
        private static void AtualizarDeposito()
        {
            if (_deposito == null) return;

            var item = ItemNaMao(out _, out int qtd);
            bool mostrar = item != null && _aberto;

            if (_deposito.activeSelf != mostrar) _deposito.SetActive(mostrar);
            if (!mostrar) { _depAssinatura = null; return; }

            // Recalcular o plano todo frame seria desperdício: ele só muda se o item,
            // a quantidade ou o conteúdo dos baús mudarem.
            string assin = item.m_shared.m_name + "#" + qtd + "#" + _assinatura;
            if (assin == _depAssinatura) return;
            _depAssinatura = assin;

            string nome = Localization.instance.Localize(item.m_shared.m_name);
            var plano = PlanejarDeposito(item, qtd, out int sobra);

            _depTitulo.text = plano.Count > 0
                ? string.Format(Lang.T("Store {0} {1}", "Guardar {0} {1}"), qtd, nome)
                : string.Format(Lang.T("No room for {0}", "Sem espaço para {0}"), nome);

            var sb = new StringBuilder();
            if (plano.Count == 0)
            {
                sb.Append("<color=#C08080>")
                  .Append(string.Format(
                      Lang.T("The chests within {0} m are full.",
                             "Os baús a {0} m estão cheios."),
                      ModConfig.ChestSearchRadius.Value.ToString("0")))
                  .Append("</color>");
            }
            else
            {
                foreach (var d in plano)
                    sb.Append($"<color=#DCCFB2>{d.Qtd}</color>  {d.Nome}")
                      .Append($"   <color=#6F6353>{d.Motivo}</color>\n");

                if (sobra > 0)
                    sb.Append("\n<color=#C08080>"
                            + string.Format(Lang.T("{0} won't fit", "{0} não cabe"), sobra)
                            + "</color>");
                else
                    sb.Append("\n<color=#6F6353><i>"
                            + Lang.T("click to confirm", "clique para confirmar")
                            + "</i></color>");
            }
            _depPlano.text = sb.ToString();
        }

        private static void SoltarNoPainel()
        {
            var item = ItemNaMao(out var de, out int qtd);
            if (item == null) return;

            // O item pode ter saído do inventário entre pegar e soltar (outro mod,
            // outro jogador). Conferir evita duplicar item a partir de referência velha.
            if (de == null || !de.ContainsItem(item)) { SoltarArrasto(); return; }

            Guardar(item, qtd);
            SoltarArrasto();
            _assinatura = null;       // força remontar a grade com o novo conteúdo
            _proximaVarredura = 0f;
        }

        // ==================================================================
        // Guardar: execução
        // ==================================================================
        private static void Guardar(ItemDrop.ItemData item, int quantidade)
        {
            var player = Player.m_localPlayer;
            if (item == null || player == null) return;

            // Item de missão não sai do inventário: é a mesma recusa que o jogo faz
            // em OnSelectedItem, e sem ela dava para perder um item de quest num baú.
            if (item.m_shared.m_questItem)
            {
                player.Message(MessageHud.MessageType.Center, "$msg_cantmove");
                return;
            }

            // Guardar peça equipada tem que desequipar antes, senão o personagem fica
            // com o bônus de uma armadura que já está dentro do baú.
            if (player.IsItemEquiped(item))
            {
                player.RemoveEquipAction(item);
                player.UnequipItem(item, triggerEquipEffects: false);
            }

            var plano = PlanejarDeposito(item, quantidade, out int sobra);
            if (plano.Count == 0)
            {
                player.Message(MessageHud.MessageType.Center,
                    Lang.T("No room in nearby chests", "Sem espaço nos baús por perto"));
                return;
            }

            _levados = 0;
            _nomeRodada = Localization.instance.Localize(item.m_shared.m_name);

            var mochila = player.GetInventory();
            string chave = Chave(item);

            foreach (var d in plano)
            {
                if (SouDono(d.Bau))
                    _levados += Mover(d.Bau.GetInventory(), mochila, chave, d.Qtd);
                else
                    _fila.Add(new Pedido
                    {
                        Bau = d.Bau, Chave = chave, Qtd = d.Qtd,
                        Nome = _nomeRodada, Guardando = true,
                    });
            }

            if (sobra > 0)
                Plugin.Log.LogInfo($"[BAUS] {_nomeRodada}: {sobra} nao coube nos baus por perto");

            Concluir();
        }

        // ---- leitura do arrasto do jogo -----------------------------------
        private static readonly FieldInfo CampoDragItem =
            AccessTools.Field(typeof(InventoryGui), "m_dragItem");
        private static readonly FieldInfo CampoDragInv =
            AccessTools.Field(typeof(InventoryGui), "m_dragInventory");
        private static readonly FieldInfo CampoDragQtd =
            AccessTools.Field(typeof(InventoryGui), "m_dragAmount");
        private static readonly MethodInfo MetodoSetupDrag =
            AccessTools.Method(typeof(InventoryGui), "SetupDragItem");

        private static ItemDrop.ItemData ItemNaMao(out Inventory de, out int qtd)
        {
            de = null; qtd = 0;
            var gui = InventoryGui.instance;
            if (gui == null || CampoDragItem == null) return null;

            var item = CampoDragItem.GetValue(gui) as ItemDrop.ItemData;
            if (item == null) return null;

            de = CampoDragInv?.GetValue(gui) as Inventory;
            qtd = CampoDragQtd != null ? (int)CampoDragQtd.GetValue(gui) : item.m_stack;
            return item;
        }

        /// <summary>Encerra o arrasto do jeito que o próprio jogo encerra.</summary>
        private static void SoltarArrasto()
        {
            var gui = InventoryGui.instance;
            if (gui != null) MetodoSetupDrag?.Invoke(gui, new object[] { null, null, 1 });
        }

        // ==================================================================
        // Botão
        // ==================================================================
        private static void GarantirBotao()
        {
            if (_botao != null) return;

            var gui = InventoryGui.instance;
            if (gui == null || gui.m_player == null)
            {
                Reclamar("InventoryGui ou o painel do inventario ainda nao existem");
                return;
            }

            GameObject go;

            if (gui.m_takeAllButton != null)
            {
                // Clonar um botão do próprio jogo traz arte, fonte e som de clique.
                go = Object.Instantiate(gui.m_takeAllButton.gameObject, gui.m_player);
                _botao = go.GetComponent<Button>();
                _botaoTxt = go.GetComponentInChildren<TMP_Text>(true);
            }
            else
            {
                // Sem o molde, um botão simples -- feio é melhor que ausente. Antes
                // isto era um `return` calado, e "o botão não aparece" virava um
                // mistério sem nenhuma pista no log.
                Reclamar("m_takeAllButton nao encontrado; usando um botao simples");

                go = new GameObject("VT_BotaoBaus", typeof(RectTransform));
                go.transform.SetParent(gui.m_player, false);

                var img = go.AddComponent<Image>();
                img.color = ChipFundo;
                _botao = go.AddComponent<Button>();
                _botao.targetGraphic = img;

                _botaoTxt = NovoTexto(go.transform, 13f, Ouro);
                _botaoTxt.alignment = TextAlignmentOptions.Center;
                Esticar(_botaoTxt.rectTransform);
            }

            go.name = "VT_BotaoBaus";
            go.SetActive(true);

            _botao.onClick.RemoveAllListeners();
            _botao.onClick.AddListener(Alternar);
            _botao.interactable = true;

            var rt = go.GetComponent<RectTransform>();
            rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(1f, 0f);
            rt.sizeDelta = new Vector2(150f, 30f);
            rt.anchoredPosition = new Vector2(ModConfig.ChestSearchButtonX.Value,
                                              ModConfig.ChestSearchButtonY.Value);

            Plugin.Log.LogInfo($"[BAUS] botao criado em {rt.anchoredPosition} "
                             + $"(painel do inventario {gui.m_player.rect.width:0}x{gui.m_player.rect.height:0})");
            AtualizarBotao();
        }

        private static float _proximaReclamacao;

        /// <summary>Avisa no log, no máximo uma vez a cada 5s, por que o botão não veio.</summary>
        private static void Reclamar(string motivo)
        {
            if (Time.realtimeSinceStartup < _proximaReclamacao) return;
            _proximaReclamacao = Time.realtimeSinceStartup + 5f;
            Plugin.Log.LogWarning($"[BAUS] botao nao criado: {motivo}");
        }

        private static void AtualizarBotao()
        {
            if (_botaoTxt != null)
                _botaoTxt.text = _aberto
                    ? Lang.T("Close chests", "Fechar baús")
                    : Lang.T("Nearby chests", "Baús próximos");
        }

        // ==================================================================
        // Painel -- posicionamento manual
        // ==================================================================
        /// <summary>Ancora no canto superior esquerdo do painel e posiciona por (x, y de cima).</summary>
        private static RectTransform Por(GameObject go, float x, float yDeCima, float larg, float alt)
        {
            var rt = go.GetComponent<RectTransform>() ?? go.AddComponent<RectTransform>();
            rt.anchorMin = rt.anchorMax = new Vector2(0f, 1f);
            rt.pivot = new Vector2(0f, 1f);
            rt.anchoredPosition = new Vector2(x, -yDeCima);
            rt.sizeDelta = new Vector2(larg, alt);
            return rt;
        }

        /// <summary>
        /// Encaixa o painel no retângulo do painel de produção. Largura e altura em
        /// 0 quer dizer copiar o da produção, que é o que alinha certo em qualquer
        /// resolução; valor diferente de 0 manda.
        /// </summary>
        private static void PosicionarPainel()
        {
            var gui = InventoryGui.instance;
            if (_painel == null || gui == null || gui.m_crafting == null) return;

            var c = gui.m_crafting;
            var prt = _painel.GetComponent<RectTransform>();

            prt.anchoredPosition = c.anchoredPosition
                + new Vector2(ModConfig.ChestSearchX.Value, ModConfig.ChestSearchY.Value);

            float w = ModConfig.ChestSearchWidth.Value;
            float h = ModConfig.ChestSearchHeight.Value;
            prt.sizeDelta = new Vector2(w > 0f ? w : c.sizeDelta.x,
                                        h > 0f ? h : c.sizeDelta.y);
        }

        private static void GarantirPainel()
        {
            if (_painel != null) return;

            var gui = InventoryGui.instance;
            if (gui == null || gui.m_crafting == null) return;

            var pai = gui.m_crafting.parent as RectTransform;
            if (pai == null) return;

            // ---- casca, copiando exatamente o retângulo do painel de produção
            _painel = new GameObject("VT_PainelBaus", typeof(RectTransform));
            _painel.transform.SetParent(pai, false);
            _painel.transform.SetSiblingIndex(gui.m_crafting.GetSiblingIndex());

            var c = gui.m_crafting;
            var prt = _painel.GetComponent<RectTransform>();
            prt.anchorMin = c.anchorMin;
            prt.anchorMax = c.anchorMax;
            prt.pivot = c.pivot;
            PosicionarPainel();

            // A madeira do painel de produção, com a mesma borda em 9-slice. Nada de
            // desenhar borda na mão: o sprite do jogo já traz a dele.
            EstiloJogo.Descobrir();

            var img = _painel.AddComponent<Image>();
            EstiloJogo.AplicarFundo(img);

            LayoutRebuilder.ForceRebuildLayoutImmediate(prt);

            // Se o retangulo ainda nao resolveu (ancora esticada num pai que nao
            // passou pelo layout), medir daria zero e o painel sairia sem nada.
            float larg = prt.rect.width  > 60f ? prt.rect.width  : 340f;
            float alt  = prt.rect.height > 80f ? prt.rect.height : 520f;
            if (prt.rect.width <= 60f || prt.rect.height <= 80f)
            {
                prt.sizeDelta = new Vector2(larg, alt);
                Plugin.Log.LogInfo($"[BAUS] retangulo da producao nao resolveu; usando {larg}x{alt}");
            }
            float dentroLarg = larg - Pad * 2f;

            // ---- cabeçalho
            float y = Pad;

            var titulo = NovoTexto(_painel.transform, 18f, Ouro);
            titulo.text = Lang.T("Nearby chests", "Baús próximos");
            EstiloJogo.AplicarTitulo(titulo);
            titulo.alignment = TextAlignmentOptions.Center;
            Por(titulo.gameObject, Pad, y, dentroLarg, AltTitulo);
            y += AltTitulo + 4f;

            _resumo = NovoTexto(_painel.transform, 11.5f, Texto);
            _resumo.alignment = TextAlignmentOptions.Center;
            Por(_resumo.gameObject, Pad, y, dentroLarg, AltResumo);
            y += AltResumo + 5f;

            // ---- barra de ocupação: quanto dos baús ao redor ainda está livre
            float largTxt = 112f;
            var trilho = new GameObject("trilho", typeof(RectTransform));
            trilho.transform.SetParent(_painel.transform, false);
            Por(trilho, Pad, y + 2f, dentroLarg - largTxt - 8f, AltBarra);
            var bg = trilho.AddComponent<Image>();
            bg.color = new Color(0.07f, 0.055f, 0.03f, 1f);
            bg.raycastTarget = false;

            var fillGo = new GameObject("fill", typeof(RectTransform));
            fillGo.transform.SetParent(trilho.transform, false);
            _ocupFill = fillGo.AddComponent<Image>();
            _ocupFill.raycastTarget = false;
            var frt = _ocupFill.rectTransform;
            frt.anchorMin = new Vector2(0f, 0f);
            frt.anchorMax = new Vector2(0f, 1f);   // largura vem do sizeDelta.x
            frt.pivot = new Vector2(0f, 0.5f);
            frt.offsetMin = new Vector2(1f, 1f);
            frt.offsetMax = new Vector2(1f, -1f);

            _ocupTxt = NovoTexto(_painel.transform, 11f, Texto);
            _ocupTxt.alignment = TextAlignmentOptions.MidlineRight;
            Por(_ocupTxt.gameObject, Pad + dentroLarg - largTxt, y, largTxt, AltBarra + 4f);
            y += AltBarra + 8f;

            Divisor(Pad, y, dentroLarg);
            y += 7f;

            // ---- busca
            CriarBusca(Pad, y, dentroLarg);
            y += AltBusca + 7f;

            // ---- ordenação
            float x = Pad;
            x += Chip(0, Lang.T("Amount", "Quantidade"), x, y, () => { _ordemPorNome = false; Montar(); });
            x += Chip(1, Lang.T("Name", "Nome"), x, y, () => { _ordemPorNome = true; Montar(); });
            Chip(2, Lang.T("By chest", "Por baú"), Pad + dentroLarg - 62f, y, () => { _porBau = !_porBau; Montar(); }, 62f);
            y += AltChip + 7f;

            Divisor(Pad, y, dentroLarg);
            y += 8f;

            // ---- área rolável
            float alturaRolo = alt - y - AltRodape - Pad - 8f;

            var roloGo = new GameObject("rolo", typeof(RectTransform));
            roloGo.transform.SetParent(_painel.transform, false);
            _rolo = Por(roloGo, Pad, y, dentroLarg, alturaRolo);
            roloGo.AddComponent<RectMask2D>();

            var sr = roloGo.AddComponent<ScrollRect>();
            sr.horizontal = false;
            sr.scrollSensitivity = 28f;
            sr.movementType = ScrollRect.MovementType.Clamped;

            var conteudoGo = new GameObject("conteudo", typeof(RectTransform));
            conteudoGo.transform.SetParent(roloGo.transform, false);
            _conteudo = conteudoGo.GetComponent<RectTransform>();
            _conteudo.anchorMin = new Vector2(0f, 1f);
            _conteudo.anchorMax = new Vector2(1f, 1f);
            _conteudo.pivot = new Vector2(0.5f, 1f);
            _conteudo.anchoredPosition = Vector2.zero;
            _conteudo.sizeDelta = new Vector2(0f, 0f);
            sr.content = _conteudo;
            sr.viewport = _rolo;

            // ---- rodapé
            float yr = alt - AltRodape - Pad + 4f;
            Divisor(Pad, yr - 7f, dentroLarg);

            _rodNome = NovoTexto(_painel.transform, 12.5f, Ouro);
            Por(_rodNome.gameObject, Pad, yr, dentroLarg - 110f, 15f);

            _rodTot = NovoTexto(_painel.transform, 11.5f, Texto);
            _rodTot.alignment = TextAlignmentOptions.MidlineRight;
            Por(_rodTot.gameObject, Pad + dentroLarg - 110f, yr, 110f, 15f);

            _rodOndes = NovoTexto(_painel.transform, 11f, Texto);
            _rodOndes.enableWordWrapping = true;
            _rodOndes.alignment = TextAlignmentOptions.TopLeft;
            Por(_rodOndes.gameObject, Pad, yr + 17f, dentroLarg, 26f);

            _rodAcao = NovoTexto(_painel.transform, 11f, Apagado);
            _rodAcao.alignment = TextAlignmentOptions.TopLeft;
            Por(_rodAcao.gameObject, Pad, yr + 44f, dentroLarg, 15f);

            MontarLinhaQuantidade(Pad, yr + 42f, dentroLarg);
            MontarDeposito(larg, alt);

            AtualizarChips();
            _tamanhoMontado = new Vector2(larg, alt);
        }

        /// <summary>
        /// Campo próprio em vez de clonar o do menu de construção.
        ///
        /// O clone parecia a escolha óbvia -- arte do jogo de graça -- mas a arte
        /// dele é uma pílula clara, desenhada para o fundo claro do menu de
        /// construção, e num painel escuro ela grita. Junto vinham um selo de tecla,
        /// placeholder em maiúsculas e o comportamento de foco do GuiInputField, que
        /// é subclasse e faz coisas próprias. Um TMP_InputField simples e escuro
        /// combina com o painel e não tem nada escondido.
        /// </summary>
        private static void CriarBusca(float x, float y, float larg)
        {
            var caixa = new GameObject("VT_BuscaSimples", typeof(RectTransform));
            caixa.transform.SetParent(_painel.transform, false);
            Por(caixa, x, y, larg, AltBusca);
            // Caixa escura sobre a madeira, para o texto digitado ter contraste.
            var borda = caixa.AddComponent<Image>();
            borda.color = Borda;

            var dentro = new GameObject("fundo", typeof(RectTransform));
            dentro.transform.SetParent(caixa.transform, false);
            var drt = dentro.GetComponent<RectTransform>();
            drt.anchorMin = Vector2.zero; drt.anchorMax = Vector2.one;
            drt.offsetMin = new Vector2(1f, 1f); drt.offsetMax = new Vector2(-1f, -1f);
            var dimg = dentro.AddComponent<Image>();
            dimg.color = new Color(0.05f, 0.04f, 0.03f, 0.85f);
            dimg.raycastTarget = false;

            var area = new GameObject("area", typeof(RectTransform));
            area.transform.SetParent(caixa.transform, false);
            var art = area.GetComponent<RectTransform>();
            art.anchorMin = Vector2.zero; art.anchorMax = Vector2.one;
            art.offsetMin = new Vector2(8f, 2f); art.offsetMax = new Vector2(-8f, -2f);
            area.AddComponent<RectMask2D>();

            var txt = NovoTexto(area.transform, 12.5f, Cor("#E8DCC2"));
            Esticar(txt.rectTransform);
            var ph = NovoTexto(area.transform, 12.5f, Apagado);
            Esticar(ph.rectTransform);
            ph.text = Lang.T("search item...", "buscar item...");
            ph.fontStyle = FontStyles.Italic;

            var campo = caixa.AddComponent<TMP_InputField>();
            campo.textViewport = art;
            campo.textComponent = txt;
            campo.placeholder = ph;
            campo.lineType = TMP_InputField.LineType.SingleLine;
            campo.caretColor = Ouro;
            campo.customCaretColor = true;
            campo.caretWidth = 2;
            campo.selectionColor = new Color(0.72f, 0.63f, 0.45f, 0.35f);
            campo.onValueChanged.AddListener(_ => { _focado = null; Montar(); });

            _busca = campo;
        }

        // ==================================================================
        // Grade
        // ==================================================================
        private static void Montar()
        {
            if (_painel == null || _conteudo == null) return;

            int soma = 0;
            foreach (var a in _tudo) soma += a.Total;
            _resumo.text = string.Format(
                Lang.T("{0} chests · {1} kinds · {2} items", "{0} baús · {1} tipos · {2} itens"),
                _bausVistos, _tudo.Count, soma)
                         + $"   <color=#6F6353>{ModConfig.ChestSearchRadius.Value:0} m</color>";

            AtualizarOcupacao();

            AtualizarChips();
            Limpar();

            var lista = Filtrados();

            float lado = ModConfig.ChestSearchSlotSize.Value;
            float cel = lado + AltRotulo;
            float larg = _rolo.rect.width;

            // Colunas pela largura real: o painel muda de tamanho com a resolução.
            int cols = ModConfig.ChestSearchColumns.Value > 0
                ? ModConfig.ChestSearchColumns.Value
                : Mathf.Max(1, Mathf.FloorToInt((larg + GapCel) / (lado + GapCel)));

            float y = 0f;

            if (lista.Count == 0)
            {
                var vazio = NovoTexto(_conteudo, 12f, Apagado);
                vazio.fontStyle = FontStyles.Italic;
                vazio.alignment = TextAlignmentOptions.Top;
                vazio.enableWordWrapping = true;
                vazio.text = _tudo.Count == 0
                    ? string.Format(Lang.T("No chest within {0} m.", "Nenhum baú a {0} m."),
                                    ModConfig.ChestSearchRadius.Value.ToString("0"))
                    : Lang.T("Nothing with that name in nearby chests.",
                             "Nada com esse nome nos baús por perto.");
                Por(vazio.gameObject, 0f, 22f, larg, 40f);
                _descartar.Add(vazio.gameObject);
                y = 70f;
            }
            else if (!_porBau)
            {
                y = Grade(lista, null, 0f, cols, lado, cel, larg);
            }
            else
            {
                var ordem = new List<Container>();
                foreach (var a in lista)
                    foreach (var o in a.Ondes)
                        if (o.Qtd > 0 && !ordem.Contains(o.Bau)) ordem.Add(o.Bau);

                foreach (var bau in ordem)
                {
                    var doBau = new List<Agregado>();
                    int somaBau = 0;
                    foreach (var a in lista)
                    {
                        var o = a.Ondes.Find(z => z.Bau == bau);
                        if (o != null && o.Qtd > 0) { doBau.Add(a); somaBau += o.Qtd; }
                    }
                    if (doBau.Count == 0) continue;

                    var cab = NovoTexto(_conteudo, 11.5f, Cor("#D0BB8A"));
                    var info = _baus.Find(x => x.Bau == bau);
                    string cap = info != null
                        ? $" · {info.Usados}/{info.Totais} slots"
                          + (info.Livres > 0
                                ? " · <color=#7E9C50>"
                                  + string.Format(Lang.T("{0} free", "{0} livres"), info.Livres)
                                  + "</color>"
                                : " · <color=#B0603F>" + Lang.T("full", "cheio") + "</color>")
                        : "";
                    cab.text = $"{Baus.NomeVisivel(bau)}   <color=#6F6353>"
                             + string.Format(Lang.T("{0} kinds", "{0} tipos"), doBau.Count)
                             + $"{cap}</color>";
                    Por(cab.gameObject, 0f, y, larg, 15f);
                    _descartar.Add(cab.gameObject);
                    y += 18f;

                    y = Grade(doBau, bau, y, cols, lado, cel, larg) + 8f;
                }
            }

            _conteudo.sizeDelta = new Vector2(0f, Mathf.Max(y, _rolo.rect.height));
            _conteudo.anchoredPosition = Vector2.zero;
            DesenharRodape();
        }

        private static float Grade(List<Agregado> lista, Container soDeste, float y0,
                                   int cols, float lado, float cel, float larg)
        {
            float passo = (larg - cols * lado) / Mathf.Max(1, cols - 1);
            if (cols == 1) passo = 0f;
            passo = Mathf.Min(passo, GapCel * 3f);

            for (int i = 0; i < lista.Count; i++)
            {
                int col = i % cols;
                int row = i / cols;
                float x = col * (lado + passo);
                float y = y0 + row * (cel + GapCel);
                NovaCelula(lista[i], soDeste, x, y, lado);
            }

            int linhas = Mathf.CeilToInt(lista.Count / (float)cols);
            return y0 + linhas * (cel + GapCel);
        }

        private static void Limpar()
        {
            // Destroy só acontece no fim do frame; sem soltar do pai agora, os
            // filhos velhos ainda apareceriam sobre os novos por um frame.
            foreach (var g in _descartar)
                if (g != null) { g.transform.SetParent(null, false); Object.Destroy(g); }
            _descartar.Clear();

            for (int i = _conteudo.childCount - 1; i >= 0; i--)
            {
                var filho = _conteudo.GetChild(i).gameObject;
                filho.transform.SetParent(null, false);
                Object.Destroy(filho);
            }
        }

        private static void NovaCelula(Agregado ag, Container soDeste, float x, float y, float lado)
        {
            var gui = InventoryGui.instance;
            if (gui?.m_playerGrid?.m_elementPrefab == null) return;

            var raiz = new GameObject("cel", typeof(RectTransform));
            raiz.transform.SetParent(_conteudo, false);
            Por(raiz, x, y, lado, lado + AltRotulo);
            _descartar.Add(raiz);

            // O slot do jogo: arte, borda e tooltip nativos.
            var slotGo = Object.Instantiate(gui.m_playerGrid.m_elementPrefab, raiz.transform);
            slotGo.SetActive(true);
            Por(slotGo, 0f, 0f, lado, lado);

            var el = slotGo.GetComponent<InventoryElement>();
            var item = ag.Amostra;

            int qtd = ag.Total;
            if (soDeste != null)
            {
                var o = ag.Ondes.Find(z => z.Bau == soDeste);
                qtd = o != null ? o.Qtd : 0;
            }

            el.m_icon.enabled = true;
            el.m_icon.sprite = item.GetIcon();
            el.m_icon.color = Color.white;

            // O UpdateGui do jogo escreveria "347/50" aqui -- por isso usamos só o
            // slot, e não o InventoryGrid inteiro.
            el.m_amount.enabled = true;
            el.m_amount.text = qtd.ToString();

            el.m_durability.gameObject.SetActive(false);
            el.m_equiped.enabled = false;
            el.m_queued.enabled = false;
            el.m_noteleport.enabled = false;
            el.m_food.enabled = false;
            el.m_quality.enabled = item.m_shared.m_maxQuality > 1;
            if (el.m_quality.enabled) el.m_quality.text = item.m_quality.ToString();
            if (el.m_selected != null) el.m_selected.SetActive(false);

            var bind = slotGo.transform.Find("binding");
            if (bind != null)
            {
                var bt = bind.GetComponent<TMP_Text>();
                if (bt != null) bt.enabled = false;
            }

            if (el.m_tooltip != null)
                el.m_tooltip.Set(item.m_shared.m_name, item.GetTooltip(), gui.m_playerGrid.m_tooltipAnchor);

            var rotulo = NovoTexto(raiz.transform, ModConfig.ChestSearchLabelSize.Value, Texto);
            rotulo.text = ag.NomeLocal;
            rotulo.alignment = TextAlignmentOptions.Top;
            rotulo.enableWordWrapping = false;
            rotulo.overflowMode = TextOverflowModes.Ellipsis;
            Por(rotulo.gameObject, -2f, lado + 1f, lado + 4f, AltRotulo);

            var handler = slotGo.GetComponentInChildren<UIInputHandler>();
            if (handler != null)
            {
                // Passar o mouse não mexe mais no rodapé: com a linha de quantidade
                // ali, mover o cursor até o botão "10" trocaria o item embaixo dele.
                // O tooltip nativo do slot já cobre a curiosidade rápida.
                // Mesmos modificadores que o jogo usa nas grades dele
                // (InventoryGrid.OnLeftDown): Shift = dividir, Ctrl = mover.
                handler.m_onLeftClick = _ =>
                {
                    bool shift = ZInput.GetKey(KeyCode.LeftShift) || ZInput.GetKey(KeyCode.RightShift);
                    bool ctrl = ZInput.GetKey(KeyCode.LeftControl) || ZInput.GetKey(KeyCode.RightControl);

                    _focado = ag;
                    _qtdEscolhida = 0;

                    if (shift) { AbrirSplit(ag, soDeste); return; }
                    if (ctrl) { Pegar(ag, soDeste, ag.Total); return; }

                    // Clique simples leva uma pilha, igual a pegar de um baú aberto.
                    Pegar(ag, soDeste, Mathf.Max(1, ag.Amostra.m_shared.m_maxStackSize));
                };
            }
        }

        // ==================================================================
        // Linha de quantidade
        // ==================================================================
        private static GameObject _linhaQtd;
        private static TMP_InputField _campoQtd;
        private static readonly List<TMP_Text> _rotulosAtalho = new List<TMP_Text>();
        private static int _qtdEscolhida;

        /// <summary>
        /// Constrói a linha "PEGAR [1] [10] [pilha] [Tudo]  [-] [n] [+]  [Pegar]".
        ///
        /// Ela é montada UMA vez e depois só tem rótulo e visibilidade atualizados.
        /// Recriar botão a cada seleção destruiria o objeto sob o mouse no meio do
        /// clique, o que no uGUI engole o evento.
        /// </summary>
        private static void MontarLinhaQuantidade(float x, float y, float larg)
        {
            _linhaQtd = new GameObject("VT_LinhaQtd", typeof(RectTransform));
            _linhaQtd.transform.SetParent(_painel.transform, false);
            Por(_linhaQtd, x, y, larg, 24f);
            _rotulosAtalho.Clear();

            var rot = NovoTexto(_linhaQtd.transform, 10.5f, Apagado);
            rot.text = Lang.T("TAKE", "PEGAR");
            rot.characterSpacing = 6f;
            Por(rot.gameObject, 0f, 6f, 42f, 14f);

            float cx = 46f;

            // Quatro atalhos: 1, 10, uma pilha, tudo. O texto de cada um muda com o
            // item selecionado (a pilha do minério é 30, a da madeira 50).
            for (int i = 0; i < 4; i++)
            {
                int idx = i;
                var b = MiniBotao(_linhaQtd.transform, "", cx, 0f, 38f, 22f,
                                  () => AtalhoQuantidade(idx));
                _rotulosAtalho.Add(b);
                cx += 41f;
            }

            cx += 6f;
            MiniBotao(_linhaQtd.transform, "−", cx, 0f, 24f, 22f, () => AjustarQtd(-1));
            cx += 26f;

            _campoQtd = CampoNumero(_linhaQtd.transform, cx, 0f, 54f, 22f);
            cx += 58f;

            MiniBotao(_linhaQtd.transform, "+", cx, 0f, 24f, 22f, () => AjustarQtd(+1));
            cx += 30f;

            var pegar = MiniBotao(_linhaQtd.transform, "Pegar", cx, 0f, larg - cx, 22f,
                                  () => PegarEscolhido());
            pegar.color = Ouro;

            _linhaQtd.SetActive(false);
        }

        private static void AtalhoQuantidade(int i)
        {
            if (_focado == null) return;
            int pilha = Mathf.Max(1, _focado.Amostra.m_shared.m_maxStackSize);
            int[] v = { 1, 10, pilha, _focado.Total };
            _qtdEscolhida = Mathf.Clamp(v[i], 1, _focado.Total);
            AtualizarLinhaQuantidade();
        }

        private static void AjustarQtd(int d)
        {
            if (_focado == null) return;
            _qtdEscolhida = Mathf.Clamp(_qtdEscolhida + d, 1, _focado.Total);
            AtualizarLinhaQuantidade();
        }

        private static void PegarEscolhido()
        {
            if (_focado == null) return;
            Pegar(_focado, null, Mathf.Clamp(_qtdEscolhida, 1, _focado.Total));
        }

        private static void AtualizarLinhaQuantidade()
        {
            if (_linhaQtd == null) return;

            bool tem = _focado != null && _focado.Total > 0;
            if (_linhaQtd.activeSelf != tem) _linhaQtd.SetActive(tem);
            if (!tem) return;

            int pilha = Mathf.Max(1, _focado.Amostra.m_shared.m_maxStackSize);
            if (_qtdEscolhida < 1 || _qtdEscolhida > _focado.Total)
                _qtdEscolhida = Mathf.Min(pilha, _focado.Total);

            string[] textos = { "1", "10", pilha.ToString(), Lang.T("All", "Tudo") };
            int[] valores = { 1, 10, pilha, _focado.Total };

            for (int i = 0; i < _rotulosAtalho.Count && i < 4; i++)
            {
                var t = _rotulosAtalho[i];
                if (t == null) continue;
                t.text = textos[i];
                // Atalho que não faz sentido para este item fica apagado em vez de
                // sumir: botão que dança de lugar é pior do que botão inerte.
                bool util = valores[i] <= _focado.Total;
                t.color = util ? Texto : new Color(Apagado.r, Apagado.g, Apagado.b, 0.4f);
            }

            if (_campoQtd != null && !_campoQtd.isFocused)
                _campoQtd.SetTextWithoutNotify(_qtdEscolhida.ToString());
        }

        /// <summary>
        /// Barra de ocupação dos baús ao redor. O número que importa é "quantos slots
        /// ainda dá para encher", então ele é que vai em destaque, não a porcentagem.
        /// </summary>
        private static void AtualizarOcupacao()
        {
            if (_ocupFill == null || _ocupTxt == null) return;

            int livres = _slotsTotais - _slotsUsados;
            float frac = _slotsTotais > 0 ? (float)_slotsUsados / _slotsTotais : 0f;

            var trilho = _ocupFill.rectTransform.parent as RectTransform;
            float largura = trilho != null ? trilho.rect.width - 2f : 0f;
            _ocupFill.rectTransform.sizeDelta =
                new Vector2(Mathf.Max(0f, largura * Mathf.Clamp01(frac)), 0f);

            // Verde enquanto sobra espaço; avermelha quando está apertado, para a
            // barra dizer algo de relance em vez de ser só decoração.
            _ocupFill.color = frac >= 0.85f ? Cor("#B0603F")
                            : frac >= 0.65f ? Cor("#B09A45")
                                            : Cor("#7E9C50");

            _ocupTxt.text = _slotsTotais > 0
                ? $"<color=#E6D3A2>{livres}</color> " + Lang.T("free slots", "slots livres")
                : "";
        }

        private static void DesenharRodape()
        {
            if (_rodNome == null) return;

            if (_focado == null || _focado.Total <= 0)
            {
                _rodNome.text = "";
                _rodTot.text = "";
                _rodOndes.text = "";
                _rodAcao.text = "<i>"
                    + Lang.T("Click an item to choose how many to take.",
                             "Clique num item para escolher quanto pegar.")
                    + "</i>";
                AtualizarLinhaQuantidade();
                return;
            }

            var ondes = new List<Onde>(_focado.Ondes);
            ondes.Sort((a, b) => b.Qtd.CompareTo(a.Qtd));

            _rodNome.text = _focado.NomeLocal;
            _rodTot.text = string.Format(
                Lang.T("{0} in {1} chest(s)", "{0} em {1} baú(s)"),
                _focado.Total, ondes.Count);

            var sb = new StringBuilder();
            for (int i = 0; i < ondes.Count && i < 5; i++)
            {
                if (i > 0) sb.Append("   ");
                sb.Append($"{ondes[i].Nome} <color=#DCCFB2>×{ondes[i].Qtd}</color>");
            }
            if (ondes.Count > 5) sb.Append($"   +{ondes.Count - 5}");
            _rodOndes.text = sb.ToString();

            _rodAcao.text = "";
            AtualizarLinhaQuantidade();
        }

        // ==================================================================
        // Peças
        // ==================================================================
        private static float Chip(int i, string texto, float x, float y,
                                  UnityEngine.Events.UnityAction ao, float larg = 0f)
        {
            if (larg <= 0f) larg = texto.Length * 7f + 22f;

            var gui = InventoryGui.instance;
            GameObject go;
            Image img;
            TMP_Text txt;

            // Mesmo botão do jogo dos outros, só menor: arte, fonte e som de clique
            // vêm juntos, e fica coerente com o botão que abre o painel.
            if (gui != null && gui.m_takeAllButton != null)
            {
                go = Object.Instantiate(gui.m_takeAllButton.gameObject, _painel.transform);
                go.name = "VT_chip";
                go.SetActive(true);

                var b = go.GetComponent<Button>();
                b.onClick.RemoveAllListeners();
                b.onClick.AddListener(ao);
                b.interactable = true;

                img = go.GetComponent<Image>();
                txt = go.GetComponentInChildren<TMP_Text>(true);
                if (txt != null) { txt.text = texto; txt.fontSize = 12f; txt.enabled = true; }
            }
            else
            {
                go = new GameObject("chip", typeof(RectTransform));
                go.transform.SetParent(_painel.transform, false);
                img = go.AddComponent<Image>();
                img.color = ChipFundo;
                txt = NovoTexto(go.transform, 12f, Texto);
                txt.alignment = TextAlignmentOptions.Center;
                txt.text = texto;
                Esticar(txt.rectTransform);
                var b = go.AddComponent<Button>();
                b.targetGraphic = img;
                b.onClick.AddListener(ao);
            }

            Por(go, x, y, larg, AltChip);

            _chip[i] = img;
            _chipTxt[i] = txt;
            _chipCorBase[i] = img != null ? img.color : Color.white;
            return larg + 5f;
        }

        private static void AtualizarChips()
        {
            Pintar(0, !_ordemPorNome);
            Pintar(1, _ordemPorNome);
            Pintar(2, _porBau);
        }

        private static readonly Color[] _chipCorBase = new Color[3];

        private static void Pintar(int i, bool ligado)
        {
            if (_chip[i] == null) return;

            // Tingir em cima da cor original: assim funciona tanto com a arte do
            // botão do jogo quanto com o retângulo do plano B.
            var b = _chipCorBase[i];
            _chip[i].color = ligado ? b : new Color(b.r, b.g, b.b, b.a * 0.5f);
            if (_chipTxt[i] != null) _chipTxt[i].color = ligado ? Ouro : Apagado;
        }

        /// <summary>
        /// Botão pequeno com a arte do jogo. Devolve o TMP do rótulo, que é o que
        /// os chamadores precisam atualizar depois.
        /// </summary>
        private static TMP_Text MiniBotao(Transform pai, string texto, float x, float y,
                                          float larg, float alt, UnityEngine.Events.UnityAction ao)
        {
            var gui = InventoryGui.instance;
            GameObject go;
            TMP_Text txt;

            if (gui != null && gui.m_takeAllButton != null)
            {
                go = Object.Instantiate(gui.m_takeAllButton.gameObject, pai);
                go.SetActive(true);
                var b = go.GetComponent<Button>();
                b.onClick.RemoveAllListeners();
                b.onClick.AddListener(ao);
                b.interactable = true;
                txt = go.GetComponentInChildren<TMP_Text>(true);
                if (txt != null) { txt.enabled = true; txt.fontSize = 11.5f; }
            }
            else
            {
                go = new GameObject("mini", typeof(RectTransform));
                go.transform.SetParent(pai, false);
                var img = go.AddComponent<Image>();
                img.color = ChipFundo;
                txt = NovoTexto(go.transform, 11.5f, Texto);
                txt.alignment = TextAlignmentOptions.Center;
                Esticar(txt.rectTransform);
                var b = go.AddComponent<Button>();
                b.targetGraphic = img;
                b.onClick.AddListener(ao);
            }

            go.name = "mini";
            if (txt != null) { txt.text = texto; txt.alignment = TextAlignmentOptions.Center; }
            Por(go, x, y, larg, alt);
            return txt;
        }

        /// <summary>Campo só de número, para digitar a quantidade exata.</summary>
        private static TMP_InputField CampoNumero(Transform pai, float x, float y, float larg, float alt)
        {
            var caixa = new GameObject("VT_Qtd", typeof(RectTransform));
            caixa.transform.SetParent(pai, false);
            Por(caixa, x, y, larg, alt);

            var borda = caixa.AddComponent<Image>();
            borda.color = Borda;

            var dentro = new GameObject("fundo", typeof(RectTransform));
            dentro.transform.SetParent(caixa.transform, false);
            var drt = dentro.GetComponent<RectTransform>();
            drt.anchorMin = Vector2.zero; drt.anchorMax = Vector2.one;
            drt.offsetMin = new Vector2(1f, 1f); drt.offsetMax = new Vector2(-1f, -1f);
            var di = dentro.AddComponent<Image>();
            di.color = new Color(0.05f, 0.04f, 0.03f, 0.9f);
            di.raycastTarget = false;

            var area = new GameObject("area", typeof(RectTransform));
            area.transform.SetParent(caixa.transform, false);
            var art = area.GetComponent<RectTransform>();
            art.anchorMin = Vector2.zero; art.anchorMax = Vector2.one;
            art.offsetMin = new Vector2(4f, 1f); art.offsetMax = new Vector2(-4f, -1f);
            area.AddComponent<RectMask2D>();

            var txt = NovoTexto(area.transform, 12f, Cor("#F0E4C6"));
            txt.alignment = TextAlignmentOptions.Center;
            Esticar(txt.rectTransform);

            var campo = caixa.AddComponent<TMP_InputField>();
            campo.textViewport = art;
            campo.textComponent = txt;
            campo.lineType = TMP_InputField.LineType.SingleLine;
            campo.characterValidation = TMP_InputField.CharacterValidation.Integer;
            campo.characterLimit = 6;
            campo.caretColor = Ouro;
            campo.customCaretColor = true;
            campo.caretWidth = 2;
            campo.selectionColor = new Color(0.72f, 0.63f, 0.45f, 0.35f);

            campo.onValueChanged.AddListener(s =>
            {
                if (_focado == null) return;
                if (int.TryParse(s, out int v))
                    _qtdEscolhida = Mathf.Clamp(v, 1, _focado.Total);
            });
            // Enter pega direto, como no diálogo de dividir pilha do jogo.
            campo.onSubmit.AddListener(_ => PegarEscolhido());

            return campo;
        }

        private static void Divisor(float x, float y, float larg)
        {
            var go = new GameObject("div", typeof(RectTransform));
            go.transform.SetParent(_painel.transform, false);
            Por(go, x, y, larg, 1f);
            var img = go.AddComponent<Image>();
            img.color = Linha;
            img.raycastTarget = false;
        }

        private static TMP_Text NovoTexto(Transform pai, float tamanho, Color cor)
        {
            var go = new GameObject("txt", typeof(RectTransform));
            go.transform.SetParent(pai, false);
            var t = go.AddComponent<TextMeshProUGUI>();
            StoreHudPatch.AplicarFonte(t);
            t.fontSize = tamanho;
            t.color = cor;
            t.alignment = TextAlignmentOptions.MidlineLeft;
            t.enableWordWrapping = false;
            t.raycastTarget = false;
            return t;
        }

        private static void Esticar(RectTransform rt)
        {
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = Vector2.zero;
        }

        // ==================================================================
        // Ctrl+clique no inventário guarda nos baús
        // ==================================================================
        /// <summary>
        /// Ctrl+clique num item chega aqui como Modifier.Move. Com um baú aberto, o
        /// jogo move para ele; sem baú aberto, ele faz isto:
        ///
        ///     else if (Player.m_localPlayer.DropItem(grid.GetInventory(), item, item.m_stack))
        ///
        /// ou seja, LARGA NO CHÃO. Com o painel aberto isso é quase sempre um
        /// acidente -- você queria guardar. Então assumimos o comando: distribui nos
        /// baús próximos, exatamente como soltar o item em cima do painel.
        ///
        /// Só interfere quando o painel está aberto e não há baú aberto. Fora disso,
        /// o comportamento do jogo fica intacto.
        /// </summary>
        [HarmonyPatch(typeof(InventoryGui), "OnSelectedItem")]
        internal static class CtrlGuardaHook
        {
            private static bool Prefix(InventoryGui __instance, InventoryGrid grid,
                                       ItemDrop.ItemData item, InventoryGrid.Modifier mod)
            {
                if (!_aberto || item == null) return true;
                if (mod != InventoryGrid.Modifier.Move) return true;
                if (__instance.IsContainerOpen()) return true;

                // Só a grade do jogador: a do baú aberto é assunto do jogo.
                if (grid == null || grid != __instance.m_playerGrid) return true;

                // Se já existe algo na mão, o clique é um "soltar" -- não é nosso.
                if (ItemNaMao(out _, out _) != null) return true;

                Guardar(item, item.m_stack);
                _assinatura = null;
                _proximaVarredura = 0f;
                return false;
            }
        }

        // ==================================================================
        // Digitar não pode virar comando
        // ==================================================================
        /// <summary>
        /// InventoryGui.Update fecha a tela com isto:
        ///
        ///     bool flag = ZInput.GetButtonDown("Inventory") || ... || ZInput.GetButtonDown("Use");
        ///     if (m_shownFrames > 1 &amp;&amp; flag) { ...; Hide(); }
        ///
        /// Sem nenhuma checagem de campo de texto -- o jogo nunca teve um campo de
        /// busca dentro do inventário. Então digitar "resina" no nosso campo manda um
        /// "Use" (E) e a tela fecha; aí o personagem volta a andar com as próximas
        /// letras, que é exatamente o sintoma relatado.
        ///
        /// Silenciamos só os botões que fecham a tela, e só enquanto o campo tem foco.
        /// Escape fica de fora de propósito: é por GetKeyDown, outro método, então
        /// continua funcionando e você nunca fica preso.
        /// </summary>
        [HarmonyPatch(typeof(ZInput), nameof(ZInput.GetButtonDown))]
        internal static class BotaoHook
        {
            private static bool Prefix(string name, ref bool __result)
            {
                if (!_aberto) return true;

                // "Use" (E) morre enquanto o painel está aberto, com ou sem foco no
                // campo: com o painel na tela o E não tem outro significado, e assim
                // a correção não fica dependendo de acertar a detecção de foco.
                if (name == "Use") { __result = false; return false; }

                if (EhMovimento(name)) { __result = false; return false; }

                // Os outros só com o campo em foco, senão o TAB deixaria de fechar
                // o inventário normalmente.
                if (!_buscaFocada) return true;

                switch (name)
                {
                    case "Inventory":
                    case "JoyButtonB":
                    case "JoyButtonY":
                        __result = false;
                        return false;
                    default:
                        return true;
                }
            }
        }

        /// <summary>
        /// Quem move o personagem é PlayerController.TakeInput -- um método
        /// diferente do Player.TakeInput, com uma lista própria. E nela o inventário
        /// só conta quando há controle conectado:
        ///
        ///     (!ZInput.IsGamepadActive() || !InventoryGui.IsVisible())
        ///
        /// No teclado e mouse isso é sempre verdadeiro, ou seja: andar com o
        /// inventário aberto é comportamento normal do Valheim. Por isso o bloqueio
        /// que eu tinha posto em Player.TakeInput não surtiu efeito nenhum -- o
        /// movimento nunca passou por lá.
        ///
        /// Na mesma condição está a solução do próprio jogo para o campo de busca do
        /// menu de construção (!Hud.instance.m_buildUi.SearchFieldFocused). Fazemos
        /// o equivalente para o nosso.
        /// </summary>
        [HarmonyPatch(typeof(PlayerController), "TakeInput")]
        internal static class ControleHook
        {
            private static void Postfix(ref bool __result)
            {
                if (_buscaFocada) __result = false;
            }
        }

        /// <summary>Cinto e suspensório: cobre ações que passam pelo Player.</summary>
        [HarmonyPatch(typeof(Player), "TakeInput")]
        internal static class TakeInputHook
        {
            private static void Postfix(ref bool __result)
            {
                if (_buscaFocada) __result = false;
            }
        }

        private static bool EhMovimento(string nome)
        {
            switch (nome)
            {
                case "Forward":
                case "Backward":
                case "Left":
                case "Right":
                case "Jump":
                case "Run":
                case "Crouch":
                case "AutoRun":
                    return true;
                default:
                    return false;
            }
        }

        /// <summary>
        /// Bloqueio na origem, e não num portão.
        ///
        /// PlayerController.FixedUpdate lê o andar assim:
        ///
        ///     if (ZInput.GetButton("Forward"))  zero.z += 1f;
        ///
        /// Note GetButton, não GetButtonDown: andar é tecla SEGURADA, e são métodos
        /// diferentes. Remendar só o GetButtonDown não pararia o movimento.
        ///
        /// Isto é reforço, não a defesa principal: ZInput.GetButton é um wrapper de
        /// uma linha (`m_instance?.TryGetButtonState(...) ?? false`), do tamanho que
        /// o JIT do Mono gosta de inlinar -- e método inlinado não passa pelo detour
        /// do Harmony. Por isso a trava de verdade está no FixedUpdate abaixo.
        /// </summary>
        [HarmonyPatch(typeof(ZInput), nameof(ZInput.GetButton))]
        internal static class BotaoSeguradoHook
        {
            private static bool Prefix(string name, ref bool __result)
            {
                if (!_aberto || !EhMovimento(name)) return true;
                __result = false;
                return false;
            }
        }

        private static readonly FieldInfo CampoPersonagem =
            AccessTools.Field(typeof(PlayerController), "m_character");

        private static int _logMov;

        /// <summary>
        /// A trava que não tem como falhar.
        ///
        /// Já tentei Player.TakeInput, PlayerController.TakeInput e ZInput.GetButton;
        /// os três aparecem na lista de patches aplicados e o personagem continuou
        /// andando. O que os três têm em comum é serem métodos pequenos, candidatos a
        /// inlining -- quando o Mono cola o corpo deles dentro de FixedUpdate, o
        /// detour do Harmony fica órfão e o patch vira decoração.
        ///
        /// FixedUpdate não corre esse risco: é mensagem de MonoBehaviour, chamada pela
        /// engine por ponteiro, nunca inlinada. Pulamos ele inteiro e zeramos os
        /// controles do mesmo jeito que o próprio jogo faz quando recusa input --
        /// sem isso, um movimento já em andamento continuaria para sempre.
        /// </summary>
        [HarmonyPatch(typeof(PlayerController), "FixedUpdate")]
        internal static class MovimentoHook
        {
            private static bool Prefix(PlayerController __instance)
            {
                // Critério é o PAINEL ABERTO, não o foco no campo.
                //
                // Quatro versões seguidas travaram o movimento "enquanto o campo tem
                // foco", e o log mostrou o bloqueio rodando -- só que por pouquíssimos
                // frames. Detectar foco de um InputField criado em runtime é
                // escorregadio: clicar num slot, mover o mouse ou remontar a grade
                // tiram o foco sem aviso, e nesses buracos o WASD volta a andar.
                //
                // Com o painel aberto você está mexendo em baú, não caminhando. Some
                // a dependência inteira de um sinal que não se provou confiável.
                if (!_aberto || !ModConfig.ChestSearchBlockMove.Value) return true;

                var p = CampoPersonagem?.GetValue(__instance) as Player;
                if (p != null)
                    p.SetControls(Vector3.zero, false, false, false, false,
                                  false, false, false, false, false, false);

                if (ModConfig.ChestSearchDebug.Value && ++_logMov % 200 == 1)
                    Plugin.Log.LogInfo("[BAUS] movimento travado (painel aberto)");

                return false;
            }
        }

        // ==================================================================
        // A causa real: o "E" digitado fechava o inventário
        // ==================================================================
        /// <summary>
        /// Era isto o tempo todo, e estava no primeiro relato: "o boneco anda E EU SAIO
        /// DO INVENTÁRIO". A ordem dos fatos:
        ///
        ///   1. você digita "resina"
        ///   2. o "e" dispara ZInput.GetButtonDown("Use") dentro de InventoryGui.Update
        ///   3. o inventário fecha  ->  o painel fecha  ->  _aberto vira false
        ///   4. sem painel aberto, o bloqueio de movimento sai de cena
        ///   5. as letras seguintes (a, s, d, w) andam com o personagem
        ///
        /// Eu vinha tratando o passo 5 e o problema estava no 2. Pior: o silenciador
        /// que pus no GetButtonDown nunca teve chance, porque ele é um wrapper de uma
        /// linha e o Mono inlina -- o mesmo motivo do GetButton.
        ///
        /// Hide() não corre esse risco: tem quase trinta linhas e é chamado de vários
        /// lugares, então o detour do Harmony vale. Enquanto você digita, ele
        /// simplesmente não fecha.
        ///
        /// Escape continua fechando de propósito: ele não passa por "Use" nem é
        /// zerado pelo ResetButtonStatus antes do Hide, então dá para distinguir a
        /// saída deliberada da letra digitada. Ninguém fica preso.
        /// </summary>
        [HarmonyPatch(typeof(InventoryGui), nameof(InventoryGui.Hide))]
        internal static class HideHook
        {
            private static bool Prefix()
            {
                if (!_aberto || !_buscaFocada) return true;
                if (ZInput.GetKeyDown(KeyCode.Escape)) return true;

                if (ModConfig.ChestSearchDebug.Value && ++_logFechar % 50 == 1)
                    Plugin.Log.LogInfo("[BAUS] ignorei um fechar de inventario (voce estava digitando)");
                return false;
            }

            private static void Postfix(bool __runOriginal)
            {
                if (__runOriginal) Fechar();
            }
        }

        private static int _logFechar;
    }
}
