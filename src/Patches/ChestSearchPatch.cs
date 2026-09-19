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
    /// Search panel for nearby chests, in place of the crafting panel.
    ///
    /// ---- Why it lives alongside the inventory ----
    /// Player.TakeInput and GameCamera.UpdateMouseCapture decide whether you walk and
    /// whether the cursor appears by consulting a FIXED LIST of game screens -- there
    /// is no extension point. A standalone panel would require patching both, otherwise
    /// the character walks while you type. Opening alongside the inventory,
    /// InventoryGui.IsVisible() is already true: cursor free, character stopped, ESC
    /// closing. Zero input patching.
    ///
    /// ---- Why it takes the crafting panel's place ----
    /// The right side of the inventory screen ALREADY belongs to the crafting panel.
    /// Drawing on top leaves the two showing through each other. Instead we hide
    /// crafting while the panel is open and copy its RectTransform -- same position,
    /// same size, exact fit. Closed, crafting comes back.
    ///
    /// ---- Hand-rolled layout, on purpose ----
    /// The first version used VerticalLayoutGroup/GridLayoutGroup and came out crooked:
    /// the sort buttons became 100px boxes. uGUI's automatic layout depends on
    /// rebuilding in the right order, and in a panel created at runtime inside another
    /// canvas that isn't reliable. A fixed-size panel gains nothing from automatic
    /// layout -- positioning by hand is deterministic and you can check it on screen.
    ///
    /// ---- Reading a closed chest is free ----
    /// Container.Awake registers InvokeRepeating("CheckForChanges", 0f, 1f), and Load()
    /// bails immediately when the DataRevision hasn't changed. Every loaded chest's
    /// inventory is already in memory and at most 1s stale.
    ///
    /// ---- Taking an item ----
    /// The ZDO owner is who calls the shots on a chest's data. Two paths:
    ///
    ///   already the owner -> move directly, no RPC, instant
    ///   not the owner     -> Container.TakeAll() and the game's handshake
    ///
    /// The distinction matters: RPC_RequestTakeAll refuses two requests to the same
    /// chest within 2s (m_lastTakeAllTime). On the owner path that limit doesn't exist
    /// -- and near your own base you own everything, which was the reported slowness.
    /// </summary>
    internal static class ChestSearchPatch
    {
        // ==================================================================
        // Model
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

        /// <summary>Capacity of a chest, in slots (each stack takes one).</summary>
        private class InfoBau
        {
            internal Container Bau;
            internal string Nome;
            internal int Usados;
            internal int Totais;
            internal int Livres => Totais - Usados;
        }

        // ==================================================================
        // State
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

        // ---- palette
        private static readonly Color Ouro = Cor("#E6D3A2");
        private static readonly Color Texto = Cor("#A2947C");
        private static readonly Color Apagado = Cor("#6F6353");
        private static readonly Color Linha = new Color(0.745f, 0.627f, 0.431f, 0.18f);
        private static readonly Color Borda = Cor("#4A3D2C");
        private static readonly Color ChipFundo = Cor("#171208");

        private static Color Cor(string hex)
            => ColorUtility.TryParseHtmlString(hex, out var c) ? c : Color.white;


        // ---- panel measurements (all in canvas pixels, top down)
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
        // Cycle
        // ==================================================================
        internal static void Update()
        {
            ProcessarFila();

            if (!ModConfig.ChestSearchEnabled.Value)
            {
                // Turning it off in F1 has to make the button disappear too. Before,
                // it stayed on screen and clickable, giving the impression that the
                // option did nothing.
                Fechar();
                if (_botao != null) { Object.Destroy(_botao.gameObject); _botao = null; _botaoTxt = null; }
                return;
            }

            // Optional key: if the inventory is closed, opens both at once. It stays
            // outside the IsVisible block for that reason.
            if (TeclaDeAbrirFoiApertada())
            {
                if (!InventoryGui.IsVisible())
                {
                    InventoryGui.instance?.Show(null);
                    _abrirAoMostrar = true;   // the panel only exists after the screen is built
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

            // Read by a Prefix that runs dozens of times per frame: we compute it
            // once here so that over there it's just a bool read.
            bool antes = _buscaFocada;
            _buscaFocada = _aberto && BuscaTemFoco();
            if (antes != _buscaFocada && ModConfig.ChestSearchDebug.Value)
                Plugin.Log.LogInfo($"[CHESTS] search focus: {_buscaFocada}");

            if (!_aberto) return;

            // InventoryGui.Show reactivates crafting. If the screen was reopened
            // underneath us, it would come back on top of the panel -- we reassert
            // every frame.
            MostrarProducao(false);

            // Position and size come from config every frame, so you can fine-tune
            // the fit through F1 without restarting the game.
            PosicionarPainel();

            AtualizarDeposito();

            // Resizing only changes the frame: the innards were positioned by hand for
            // the old size and would be shrunk into a corner. Rebuild when it changes.
            var tam = _painel.GetComponent<RectTransform>().rect.size;
            if ((tam - _tamanhoMontado).sqrMagnitude > 4f) Reconstruir();

            if (Time.realtimeSinceStartup >= _proximaVarredura)
            {
                _proximaVarredura = Time.realtimeSinceStartup + ModConfig.ChestSearchRefresh.Value;
                Varrer();

                // Rebuilding the grid twice a second would destroy and recreate
                // dozens of slots for nothing, flickering the tooltip and losing the
                // item under the mouse. Only rebuild when chest contents truly changed.
                string agora = Assinatura();
                if (agora != _assinatura) { _assinatura = agora; Montar(); }
            }
        }

        private static Vector2 _tamanhoMontado;

        /// <summary>
        /// Rebuilds the whole panel while preserving what you already typed. It only
        /// happens when the size changes (F1 tweak, resolution change), so the cost of
        /// recreating doesn't matter.
        /// </summary>
        private static void Reconstruir()
        {
            string texto = _busca != null ? _busca.text : "";

            _painel.transform.SetParent(null, false);
            Object.Destroy(_painel);
            // Everything below was a child of the panel and died with it. Clearing the
            // references avoids reusing a destroyed object after the rebuild.
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
        /// TMP_InputField.isFocused alone wasn't enough in practice -- the field is a
        /// GuiInputField (a game subclass) and its state doesn't always match. The
        /// EventSystem is the source of truth about who's receiving the keyboard.
        /// </summary>
        private static bool BuscaTemFoco()
        {
            // Applies to BOTH fields: typing the quantity also can't turn into a
            // movement command.
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
        /// The key can't fire while you're typing in the search or in chat --
        /// otherwise its letter would close the panel mid-search.
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

            // Closing with the split dialog open would leave our listeners hanging on
            // the game's shared object.
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
        /// The panel takes the crafting panel's place instead of drawing on top of it.
        /// </summary>
        private static void MostrarProducao(bool visivel)
        {
            var gui = InventoryGui.instance;
            if (gui == null || gui.m_crafting == null) return;
            if (gui.m_crafting.gameObject.activeSelf != visivel)
                gui.m_crafting.gameObject.SetActive(visivel);
        }

        // ==================================================================
        // Scan
        // ==================================================================
        private static string Chave(ItemDrop.ItemData item)
        {
            // Quality only enters the key when the item has levels -- otherwise two
            // swords of different quality would become a single stack on screen.
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

                // A slot is per STACK, not per unit: NrOfItems returns m_inventory.Count,
                // which is the number of stacks. 120 wood with a stack of 50 takes 3.
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

        // Searching "carvao" has to find "Carvão", and "carvão" too. An explicit
        // table instead of String.Normalize(FormD): Unicode decomposition
        // depends on ICU, which in Unity's Mono isn't always complete -- and a
        // search that fails silently is worse than having no search.
        private const string ComAcento = "áàâãäéèêëíìîïóòôõöúùûüçñýÿ";
        private const string SemAcento = "aaaaaeeeeiiiiooooouuuucnyy";

        private static string Normalizar(string s)
        {
            if (string.IsNullOrEmpty(s)) return "";

            // The two tables walk in pairs by index: if someone edits one and
            // forgets the other, the error would be silent and swap letter by letter.
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
        // Take
        // ==================================================================
        private class Pedido
        {
            internal Container Bau;
            internal string Chave;
            internal int Qtd;
            internal string Nome;
            /// <summary>true = backpack -> chest (store); false = chest -> backpack (take).</summary>
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
        /// Am I already the ZDO owner? Then I can touch the inventory directly -- it's
        /// the same condition the game itself requires before granting the request.
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
        /// Takes <paramref name="quantidade"/> units of the item, gathering from as
        /// many chests as needed. Pass int.MaxValue for "all".
        /// </summary>
        private static void Pegar(Agregado ag, Container soDeste, int quantidade)
        {
            if (ag == null || Player.m_localPlayer == null) return;

            int querido = Mathf.Clamp(quantidade, 1, ag.Total);

            // From the fullest chest first: fewer requests for the same amount.
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
                    // Fast path: no RPC, no wait, no 2s limit.
                    _levados += Mover(destino, o.Bau.GetInventory(), ag.Chave, n);
                }
                else
                {
                    _fila.Add(new Pedido { Bau = o.Bau, Chave = ag.Chave, Qtd = n, Nome = ag.NomeLocal });
                }
            }

            Concluir();
        }

        /// <summary>Moves up to <paramref name="qtd"/> of the item, respecting space.</summary>
        private static readonly MethodInfo MetodoChanged =
            AccessTools.Method(typeof(Inventory), "Changed", new[] { typeof(bool), typeof(bool) });

        private static void Notificar(Inventory inv)
            => MetodoChanged?.Invoke(inv, new object[] { true, false });

        /// <summary>How many units of this item exist in the inventory.</summary>
        private static int Contar(Inventory inv, string chave)
        {
            if (inv == null) return 0;
            int n = 0;
            foreach (var it in inv.GetAllItems())
                if (Chave(it) == chave) n += it.m_stack;
            return n;
        }

        /// <summary>
        /// Moves up to <paramref name="qtd"/> units between two inventories.
        ///
        /// ---- Why remove from the source BEFORE putting into the destination ----
        /// Inventory.AddItem isn't all-or-nothing. For a stackable item it distributes
        /// unit by unit into the stacks that already exist and, when it needs a new
        /// slot and the inventory is full, returns false -- leaving behind the units
        /// that already went in:
        ///
        ///     itemData.m_stack++;  ... continue;      // already went in
        ///     ...
        ///     else { flag = false; ZLog.LogError(...); }   // and fails afterwards
        ///
        /// The previous version did "if AddItem succeeded, remove from the source". On
        /// the path above AddItem fails, the source is NOT debited, and the units that
        /// went in become a DUPLICATED item. This world's log has three
        /// "Trying to add item to occupied slot -1, -1", that is: it happened.
        ///
        /// Now we debit first and give back what didn't fit. Duplicating is worse than
        /// failing, and vanishing is worse than both -- hence the check below.
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

                // Detach the piece and debit the source right away.
                var parte = item.Clone();
                parte.m_stack = n;
                origem.RemoveItem(item, n);

                // AddItem reduces parte.m_stack as it places; whatever is left didn't go in.
                if (!destino.AddItem(parte) && parte.m_stack > 0)
                    origem.AddItem(parte);   // give back what didn't fit

                restante -= n;
            }

            Notificar(origem);
            Notificar(destino);

            int saiu = antesOrigem - Contar(origem, chave);
            int entrou = Contar(destino, chave) - antesDestino;

            // Safety net: if the two ends don't match, someone gained or
            // lost an item. There's no safe way to undo it here, but yelling in the log
            // turns "it vanished, I don't know how" into something investigable.
            if (saiu != entrou)
                Plugin.Log.LogError(
                    $"[CHESTS] IMBALANCE in '{chave}': {saiu} left the source but "
                  + $"{entrou} entered the destination (request {qtd}). Tell the mod author.");
            else if (entrou > 0 && entrou < qtd)
                Plugin.Log.LogInfo($"[CHESTS] moved {entrou} of {qtd} (destination full?)");

            return entrou;
        }

        /// <summary>
        /// One request at a time: the RPC is asynchronous and the filter is global, so
        /// two chests in flight at once would mix up the responses.
        /// </summary>
        private static void ProcessarFila()
        {
            if (_emVoo != null)
            {
                if (Time.realtimeSinceStartup < _emVooAte) return;
                Plugin.Log.LogInfo($"[CHESTS] no response from '{Baus.NomeVisivel(_emVoo.Bau)}'");
                _emVoo = null;
                Concluir();
            }

            if (_fila.Count == 0) return;

            var p = _fila[0];
            if (p.Bau == null) { _fila.RemoveAt(0); return; }

            // The game refuses two TakeAlls on the same chest within 2s
            // (Container.RPC_RequestTakeAll, m_lastTakeAllTime). Waiting is better than
            // losing the request silently. The store RPC (RPC_RequestStack) has no
            // such limit, so it doesn't wait.
            if (!p.Guardando
                && _ultimoPedido.TryGetValue(p.Bau, out float t)
                && Time.realtimeSinceStartup - t < 2.05f) return;

            _fila.RemoveAt(0);
            if (!p.Guardando) _ultimoPedido[p.Bau] = Time.realtimeSinceStartup;

            _emVoo = p;
            _emVooAte = Time.realtimeSinceStartup + 3f;

            if (p.Guardando)
            {
                // Same handshake as "take", in reverse: the owner grants ownership
                // (ForceSendZDO + SetOwner) before the inventory is touched.
                p.Bau.StackAll();
            }
            else if (!p.Bau.TakeAll(Player.m_localPlayer))
            {
                _emVoo = null;   // refused on the spot; the game already warned
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
            _assinatura = null;     // force a rebuild
            _proximaVarredura = 0f;
        }

        /// <summary>
        /// Only kicks in when the request went out through the RPC path (another
        /// player's chest). Outside the window, the game's "take all" stays intact.
        /// </summary>
        [HarmonyPatch(typeof(Inventory), nameof(Inventory.MoveAll))]
        internal static class MoveAllHook
        {
            private static bool Prefix(Inventory __instance, Inventory fromInventory)
            {
                if (!FiltroAtivo) return true;

                var p = _emVoo;

                // Check that it's the response for OUR chest: if the player pressed
                // the game's own "take all" within the window, we don't hijack it.
                if (p.Bau == null || fromInventory != p.Bau.GetInventory()) return true;

                _emVoo = null;
                _levados += Mover(__instance, fromInventory, p.Chave, p.Qtd);
                Concluir();
                return false;
            }
        }

        /// <summary>
        /// Storing into another player's chest goes through RPC_RequestStack, whose
        /// response calls Inventory.StackAll(backpack). We swap that loop for the exact
        /// move that was planned -- otherwise it would dump everything the chest already
        /// holds.
        ///
        /// QuickStorePatch also prefixes this method, with its own window. The two
        /// coexist because each only acts within its own window, and the two are never
        /// open at the same time (they start from different player actions).
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
        // Store: planning
        // ==================================================================
        private class Destino
        {
            internal Container Bau;
            internal string Nome;
            internal int Qtd;
            internal string Motivo;
        }

        /// <summary>
        /// Decides where each unit goes, in three passes:
        ///
        ///   1. top off the stacks that ALREADY exist  -> uses no slot at all
        ///   2. chest that already has the item, in a new slot -> keeps your organization
        ///   3. any chest with space
        ///
        /// The order matters: starting with step 2 would fill chests that had a
        /// half-full stack with new slots, and the store would fill up before its time.
        ///
        /// It only plans. Guardar() executes, and the panel shows the plan before you
        /// confirm -- storing without knowing where it ended up is worse than not storing.
        /// </summary>
        private static List<Destino> PlanejarDeposito(ItemDrop.ItemData item, int quantidade, out int sobra)
        {
            var plano = new List<Destino>();
            sobra = quantidade;
            if (item == null || quantidade <= 0) return plano;

            // C# doesn't let a local function touch an out parameter, so the balance
            // runs in a normal variable and returns to 'sobra' at the end.
            int resta = quantidade;

            string nome = item.m_shared.m_name;
            int pilha = Mathf.Max(1, item.m_shared.m_maxStackSize);
            float nivel = item.m_worldLevel;

            // Free slots get consumed over the course of the plan, otherwise two passes
            // would reserve the same slot and the "didn't fit" count would come out optimistic.
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

            // 1) top off existing stacks
            foreach (var b in _baus)
            {
                if (resta <= 0) break;
                var inv = b.Bau.GetInventory();
                if (inv == null) continue;
                int cabe = inv.FindFreeStackSpace(nome, nivel);
                Poe(b.Bau, b.Nome, Mathf.Min(cabe, resta), Lang.T("tops off stack", "completa pilha"));
            }

            // 2) chests that already have the item
            foreach (var b in _baus)
            {
                if (resta <= 0) break;
                if (livres[b.Bau] <= 0 || !TemOItem(b.Bau)) continue;
                int cabe = Mathf.Min(livres[b.Bau] * pilha, resta);
                livres[b.Bau] -= Mathf.CeilToInt(cabe / (float)pilha);
                Poe(b.Bau, b.Nome, cabe, Lang.T("with the rest", "junto do resto"));
            }

            // 3) anyone with space
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
        // Stack-split dialog (the game's own)
        // ==================================================================
        private static bool _meuSplit;
        private static Agregado _splitAg;
        private static Container _splitBau;

        /// <summary>
        /// Reuses the game's SplitDialog, which is the little screen everyone already
        /// knows. It is SHARED, and that calls for three precautions:
        ///
        ///   - the OK button fires the SplitAccepted event, which only has a listener
        ///     when it was the game that opened it. So we subscribe ours.
        ///   - InventoryGui.UpdateSplitDialog runs every frame and on Enter calls
        ///     OnSplitOk() directly, without going through the event. Hence the prefix below.
        ///   - Escape makes the game call HideSplitDialog, which doesn't know our
        ///     listeners. Hence we release them in its postfix.
        ///
        /// Without all three, either OK does nothing, or Enter starts a drag with a
        /// null m_splitItem.
        /// </summary>
        private static void AbrirSplit(Agregado ag, Container soDeste)
        {
            var gui = InventoryGui.instance;
            if (gui == null || gui.m_splitDialog == null || ag.Total <= 1)
            {
                // Without a dialog (or a single item) the footer already handles it.
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

        /// <summary>Enter in the dialog calls this directly; we divert it when it's ours.</summary>
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

        /// <summary>Escape goes through here; we release our listeners along with it.</summary>
        [HarmonyPatch(typeof(InventoryGui), "HideSplitDialog")]
        internal static class SplitHideHook
        {
            private static void Postfix()
            {
                if (_meuSplit) LimparSplit();
            }
        }

        // ==================================================================
        // Store: the drop area
        // ==================================================================
        private static GameObject _deposito;
        private static TMP_Text _depTitulo, _depPlano;
        private static string _depAssinatura;

        /// <summary>
        /// Covers the whole panel while you're holding an item. Covering everything
        /// is on purpose: if only one strip accepted the item, you'd miss your aim and
        /// the click would land on the slot below, taking something else.
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

        /// <summary>Shows/hides the area and keeps the plan up to date. Called every frame.</summary>
        private static void AtualizarDeposito()
        {
            if (_deposito == null) return;

            var item = ItemNaMao(out _, out int qtd);
            bool mostrar = item != null && _aberto;

            if (_deposito.activeSelf != mostrar) _deposito.SetActive(mostrar);
            if (!mostrar) { _depAssinatura = null; return; }

            // Recomputing the plan every frame would be wasteful: it only changes if the
            // item, the quantity or the chest contents change.
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

            // The item may have left the inventory between grabbing and dropping (another
            // mod, another player). Checking avoids duplicating an item from a stale reference.
            if (de == null || !de.ContainsItem(item)) { SoltarArrasto(); return; }

            Guardar(item, qtd);
            SoltarArrasto();
            _assinatura = null;       // force rebuilding the grid with the new contents
            _proximaVarredura = 0f;
        }

        // ==================================================================
        // Store: execution
        // ==================================================================
        private static void Guardar(ItemDrop.ItemData item, int quantidade)
        {
            var player = Player.m_localPlayer;
            if (item == null || player == null) return;

            // A quest item doesn't leave the inventory: it's the same refusal the game
            // makes in OnSelectedItem, and without it you could lose a quest item in a chest.
            if (item.m_shared.m_questItem)
            {
                player.Message(MessageHud.MessageType.Center, "$msg_cantmove");
                return;
            }

            // Storing an equipped piece requires unequipping first, otherwise the character
            // keeps the bonus of an armor that's already inside the chest.
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
                Plugin.Log.LogInfo($"[CHESTS] {_nomeRodada}: {sobra} didn't fit in nearby chests");

            Concluir();
        }

        // ---- reading the game's drag --------------------------------------
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

        /// <summary>Ends the drag the same way the game itself ends it.</summary>
        private static void SoltarArrasto()
        {
            var gui = InventoryGui.instance;
            if (gui != null) MetodoSetupDrag?.Invoke(gui, new object[] { null, null, 1 });
        }

        // ==================================================================
        // Button
        // ==================================================================
        private static void GarantirBotao()
        {
            if (_botao != null) return;

            var gui = InventoryGui.instance;
            if (gui == null || gui.m_player == null)
            {
                Reclamar("InventoryGui or the inventory panel doesn't exist yet");
                return;
            }

            GameObject go;

            if (gui.m_takeAllButton != null)
            {
                // Cloning one of the game's own buttons brings art, font and click sound.
                go = Object.Instantiate(gui.m_takeAllButton.gameObject, gui.m_player);
                _botao = go.GetComponent<Button>();
                _botaoTxt = go.GetComponentInChildren<TMP_Text>(true);
            }
            else
            {
                // Without the template, a plain button -- ugly is better than missing.
                // Before, this was a silent `return`, and "the button doesn't appear"
                // became a mystery with no clue in the log.
                Reclamar("m_takeAllButton not found; using a plain button");

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

            Plugin.Log.LogInfo($"[CHESTS] button created at {rt.anchoredPosition} "
                             + $"(inventory panel {gui.m_player.rect.width:0}x{gui.m_player.rect.height:0})");
            AtualizarBotao();
        }

        private static float _proximaReclamacao;

        /// <summary>Warns in the log, at most once every 5s, why the button didn't come up.</summary>
        private static void Reclamar(string motivo)
        {
            if (Time.realtimeSinceStartup < _proximaReclamacao) return;
            _proximaReclamacao = Time.realtimeSinceStartup + 5f;
            Plugin.Log.LogWarning($"[CHESTS] button not created: {motivo}");
        }

        private static void AtualizarBotao()
        {
            if (_botaoTxt != null)
                _botaoTxt.text = _aberto
                    ? Lang.T("Close chests", "Fechar baús")
                    : Lang.T("Nearby chests", "Baús próximos");
        }

        // ==================================================================
        // Panel -- manual positioning
        // ==================================================================
        /// <summary>Anchors to the panel's top-left corner and positions by (x, y from the top).</summary>
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
        /// Fits the panel into the crafting panel's rectangle. Width and height at
        /// 0 means copy the crafting one's, which is what aligns correctly at any
        /// resolution; a non-zero value wins.
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

            // ---- shell, copying the crafting panel's rectangle exactly
            _painel = new GameObject("VT_PainelBaus", typeof(RectTransform));
            _painel.transform.SetParent(pai, false);
            _painel.transform.SetSiblingIndex(gui.m_crafting.GetSiblingIndex());

            var c = gui.m_crafting;
            var prt = _painel.GetComponent<RectTransform>();
            prt.anchorMin = c.anchorMin;
            prt.anchorMax = c.anchorMax;
            prt.pivot = c.pivot;
            PosicionarPainel();

            // The crafting panel's wood, with the same 9-slice border. No drawing a
            // border by hand: the game's sprite already brings its own.
            EstiloJogo.Descobrir();

            var img = _painel.AddComponent<Image>();
            EstiloJogo.AplicarFundo(img);

            LayoutRebuilder.ForceRebuildLayoutImmediate(prt);

            // If the rectangle still hasn't resolved (stretch anchor in a parent that
            // didn't go through layout), measuring would give zero and the panel would come out empty.
            float larg = prt.rect.width  > 60f ? prt.rect.width  : 340f;
            float alt  = prt.rect.height > 80f ? prt.rect.height : 520f;
            if (prt.rect.width <= 60f || prt.rect.height <= 80f)
            {
                prt.sizeDelta = new Vector2(larg, alt);
                Plugin.Log.LogInfo($"[CHESTS] crafting rectangle didn't resolve; using {larg}x{alt}");
            }
            float dentroLarg = larg - Pad * 2f;

            // ---- header
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

            // ---- occupancy bar: how much of the surrounding chests is still free
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
            frt.anchorMax = new Vector2(0f, 1f);   // width comes from sizeDelta.x
            frt.pivot = new Vector2(0f, 0.5f);
            frt.offsetMin = new Vector2(1f, 1f);
            frt.offsetMax = new Vector2(1f, -1f);

            _ocupTxt = NovoTexto(_painel.transform, 11f, Texto);
            _ocupTxt.alignment = TextAlignmentOptions.MidlineRight;
            Por(_ocupTxt.gameObject, Pad + dentroLarg - largTxt, y, largTxt, AltBarra + 4f);
            y += AltBarra + 8f;

            Divisor(Pad, y, dentroLarg);
            y += 7f;

            // ---- search
            CriarBusca(Pad, y, dentroLarg);
            y += AltBusca + 7f;

            // ---- sorting
            float x = Pad;
            x += Chip(0, Lang.T("Amount", "Quantidade"), x, y, () => { _ordemPorNome = false; Montar(); });
            x += Chip(1, Lang.T("Name", "Nome"), x, y, () => { _ordemPorNome = true; Montar(); });
            Chip(2, Lang.T("By chest", "Por baú"), Pad + dentroLarg - 62f, y, () => { _porBau = !_porBau; Montar(); }, 62f);
            y += AltChip + 7f;

            Divisor(Pad, y, dentroLarg);
            y += 8f;

            // ---- scrollable area
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

            // ---- footer
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
        /// A field of our own instead of cloning the build menu's.
        ///
        /// The clone looked like the obvious choice -- the game's art for free -- but its
        /// art is a light pill, drawn for the build menu's light background, and on a
        /// dark panel it screams. Along with it came a key badge, an uppercase
        /// placeholder and the focus behavior of GuiInputField, which is a subclass and
        /// does things of its own. A simple, dark TMP_InputField matches the panel and
        /// hides nothing.
        /// </summary>
        private static void CriarBusca(float x, float y, float larg)
        {
            var caixa = new GameObject("VT_BuscaSimples", typeof(RectTransform));
            caixa.transform.SetParent(_painel.transform, false);
            Por(caixa, x, y, larg, AltBusca);
            // Dark box over the wood, so the typed text has contrast.
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
        // Grid
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

            // Columns from the real width: the panel changes size with the resolution.
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
            // Destroy only happens at the end of the frame; without detaching from the
            // parent now, the old children would still show over the new ones for a frame.
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

            // The game's slot: native art, border and tooltip.
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

            // The game's UpdateGui would write "347/50" here -- that's why we use only the
            // slot, and not the whole InventoryGrid.
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
                // Hovering the mouse no longer touches the footer: with the quantity line
                // there, moving the cursor to the "10" button would swap the item under it.
                // The slot's native tooltip already covers quick curiosity.
                // Same modifiers the game uses in its grids
                // (InventoryGrid.OnLeftDown): Shift = split, Ctrl = move.
                handler.m_onLeftClick = _ =>
                {
                    bool shift = ZInput.GetKey(KeyCode.LeftShift) || ZInput.GetKey(KeyCode.RightShift);
                    bool ctrl = ZInput.GetKey(KeyCode.LeftControl) || ZInput.GetKey(KeyCode.RightControl);

                    _focado = ag;
                    _qtdEscolhida = 0;

                    if (shift) { AbrirSplit(ag, soDeste); return; }
                    if (ctrl) { Pegar(ag, soDeste, ag.Total); return; }

                    // A plain click takes one stack, like taking from an open chest.
                    Pegar(ag, soDeste, Mathf.Max(1, ag.Amostra.m_shared.m_maxStackSize));
                };
            }
        }

        // ==================================================================
        // Quantity line
        // ==================================================================
        private static GameObject _linhaQtd;
        private static TMP_InputField _campoQtd;
        private static readonly List<TMP_Text> _rotulosAtalho = new List<TMP_Text>();
        private static int _qtdEscolhida;

        /// <summary>
        /// Builds the line "TAKE [1] [10] [stack] [All]  [-] [n] [+]  [Take]".
        ///
        /// It is built ONCE and afterwards only its label and visibility are updated.
        /// Recreating the button on each selection would destroy the object under the
        /// mouse mid-click, which in uGUI swallows the event.
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

            // Four shortcuts: 1, 10, one stack, all. The text of each changes with the
            // selected item (ore's stack is 30, wood's is 50).
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

            var pegar = MiniBotao(_linhaQtd.transform, Lang.T("Take", "Pegar"), cx, 0f, larg - cx, 22f,
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
                // A shortcut that makes no sense for this item is dimmed instead of
                // disappearing: a button that dances around is worse than an inert button.
                bool util = valores[i] <= _focado.Total;
                t.color = util ? Texto : new Color(Apagado.r, Apagado.g, Apagado.b, 0.4f);
            }

            if (_campoQtd != null && !_campoQtd.isFocused)
                _campoQtd.SetTextWithoutNotify(_qtdEscolhida.ToString());
        }

        /// <summary>
        /// Occupancy bar for the surrounding chests. The number that matters is "how many
        /// slots can still be filled", so that's the one highlighted, not the percentage.
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

            // Green while there's room left; turns red when it's tight, so the
            // bar says something at a glance instead of being mere decoration.
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
        // Pieces
        // ==================================================================
        private static float Chip(int i, string texto, float x, float y,
                                  UnityEngine.Events.UnityAction ao, float larg = 0f)
        {
            if (larg <= 0f) larg = texto.Length * 7f + 22f;

            var gui = InventoryGui.instance;
            GameObject go;
            Image img;
            TMP_Text txt;

            // The same game button as the others, just smaller: art, font and click sound
            // come together, and it stays consistent with the button that opens the panel.
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

            // Tint on top of the original color: that way it works both with the game's
            // button art and with the plan B rectangle.
            var b = _chipCorBase[i];
            _chip[i].color = ligado ? b : new Color(b.r, b.g, b.b, b.a * 0.5f);
            if (_chipTxt[i] != null) _chipTxt[i].color = ligado ? Ouro : Apagado;
        }

        /// <summary>
        /// Small button with the game's art. Returns the label's TMP, which is what the
        /// callers need to update afterwards.
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

        /// <summary>Number-only field, for typing the exact quantity.</summary>
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
            // Enter takes directly, like in the game's stack-split dialog.
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
        // Ctrl+click in the inventory stores into chests
        // ==================================================================
        /// <summary>
        /// Ctrl+click on an item arrives here as Modifier.Move. With a chest open, the
        /// game moves it into the chest; with no chest open, it does this:
        ///
        ///     else if (Player.m_localPlayer.DropItem(grid.GetInventory(), item, item.m_stack))
        ///
        /// that is, DROPS IT ON THE GROUND. With the panel open that's almost always an
        /// accident -- you wanted to store it. So we take over the command: distribute
        /// into nearby chests, exactly like dropping the item onto the panel.
        ///
        /// It only interferes when the panel is open and no chest is open. Outside that,
        /// the game's behavior stays intact.
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

                // Only the player's grid: the open chest's grid is the game's business.
                if (grid == null || grid != __instance.m_playerGrid) return true;

                // If something is already in hand, the click is a "drop" -- not ours.
                if (ItemNaMao(out _, out _) != null) return true;

                Guardar(item, item.m_stack);
                _assinatura = null;
                _proximaVarredura = 0f;
                return false;
            }
        }

        // ==================================================================
        // Typing must not become a command
        // ==================================================================
        /// <summary>
        /// InventoryGui.Update closes the screen with this:
        ///
        ///     bool flag = ZInput.GetButtonDown("Inventory") || ... || ZInput.GetButtonDown("Use");
        ///     if (m_shownFrames > 1 &amp;&amp; flag) { ...; Hide(); }
        ///
        /// With no text-field check whatsoever -- the game never had a search field
        /// inside the inventory. So typing "resina" in our field sends a
        /// "Use" (E) and the screen closes; then the character starts walking with the
        /// next letters, which is exactly the reported symptom.
        ///
        /// We silence only the buttons that close the screen, and only while the field
        /// has focus. Escape is left out on purpose: it goes through GetKeyDown, another
        /// method, so it keeps working and you never get stuck.
        /// </summary>
        [HarmonyPatch(typeof(ZInput), nameof(ZInput.GetButtonDown))]
        internal static class BotaoHook
        {
            private static bool Prefix(string name, ref bool __result)
            {
                if (!_aberto) return true;

                // "Use" (E) dies while the panel is open, with or without focus in the
                // field: with the panel on screen the E has no other meaning, and that way
                // the fix doesn't depend on getting focus detection right.
                if (name == "Use") { __result = false; return false; }

                if (EhMovimento(name)) { __result = false; return false; }

                // The others only with the field focused, otherwise TAB would stop
                // closing the inventory normally.
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
        /// Whoever moves the character is PlayerController.TakeInput -- a method
        /// different from Player.TakeInput, with its own list. And in it the inventory
        /// only counts when a controller is connected:
        ///
        ///     (!ZInput.IsGamepadActive() || !InventoryGui.IsVisible())
        ///
        /// On keyboard and mouse this is always true, that is: walking with the
        /// inventory open is normal Valheim behavior. That's why the block
        /// I had put in Player.TakeInput had no effect whatsoever -- the
        /// movement never went through there.
        ///
        /// In the same condition is the game's own solution for the build menu's
        /// search field (!Hud.instance.m_buildUi.SearchFieldFocused). We do the
        /// equivalent for ours.
        /// </summary>
        [HarmonyPatch(typeof(PlayerController), "TakeInput")]
        internal static class ControleHook
        {
            private static void Postfix(ref bool __result)
            {
                if (_buscaFocada) __result = false;
            }
        }

        /// <summary>Belt and suspenders: covers actions that go through Player.</summary>
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
        /// Blocking at the source, not at a gate.
        ///
        /// PlayerController.FixedUpdate reads walking like this:
        ///
        ///     if (ZInput.GetButton("Forward"))  zero.z += 1f;
        ///
        /// Note GetButton, not GetButtonDown: walking is a HELD key, and they're
        /// different methods. Patching only GetButtonDown wouldn't stop the movement.
        ///
        /// This is reinforcement, not the main defense: ZInput.GetButton is a
        /// one-line wrapper (`m_instance?.TryGetButtonState(...) ?? false`), the size
        /// Mono's JIT likes to inline -- and an inlined method doesn't go through Harmony's
        /// detour. That's why the real lock is in FixedUpdate below.
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
        /// The lock that can't fail.
        ///
        /// I've already tried Player.TakeInput, PlayerController.TakeInput and ZInput.GetButton;
        /// all three show up in the list of applied patches and the character kept
        /// walking. What the three have in common is being small methods, inlining
        /// candidates -- when Mono pastes their body into FixedUpdate, Harmony's
        /// detour is left orphaned and the patch becomes decoration.
        ///
        /// FixedUpdate doesn't run that risk: it's a MonoBehaviour message, called by
        /// the engine through a pointer, never inlined. We skip it entirely and zero the
        /// controls the same way the game itself does when it refuses input --
        /// without that, a movement already in progress would continue forever.
        /// </summary>
        [HarmonyPatch(typeof(PlayerController), "FixedUpdate")]
        internal static class MovimentoHook
        {
            private static bool Prefix(PlayerController __instance)
            {
                // The criterion is the PANEL BEING OPEN, not focus in the field.
                //
                // Four versions in a row locked movement "while the field has
                // focus", and the log showed the block running -- but for only a few
                // frames. Detecting focus of an InputField created at runtime is
                // slippery: clicking a slot, moving the mouse or rebuilding the grid
                // remove focus without warning, and in those gaps WASD walks again.
                //
                // With the panel open you're handling a chest, not walking. That removes
                // the whole dependency on a signal that didn't prove reliable.
                if (!_aberto || !ModConfig.ChestSearchBlockMove.Value) return true;

                var p = CampoPersonagem?.GetValue(__instance) as Player;
                if (p != null)
                    p.SetControls(Vector3.zero, false, false, false, false,
                                  false, false, false, false, false, false);

                if (ModConfig.ChestSearchDebug.Value && ++_logMov % 200 == 1)
                    Plugin.Log.LogInfo("[CHESTS] movement locked (panel open)");

                return false;
            }
        }

        // ==================================================================
        // The real cause: the typed "E" was closing the inventory
        // ==================================================================
        /// <summary>
        /// This was it all along, and it was in the first report: "the character walks AND I
        /// LEAVE THE INVENTORY". The order of events:
        ///
        ///   1. you type "resina"
        ///   2. the "e" fires ZInput.GetButtonDown("Use") inside InventoryGui.Update
        ///   3. the inventory closes  ->  the panel closes  ->  _aberto becomes false
        ///   4. with no panel open, the movement block leaves the scene
        ///   5. the next letters (a, s, d, w) walk the character
        ///
        /// I had been treating step 5 and the problem was in 2. Worse: the silencer
        /// I put in GetButtonDown never had a chance, because it's a one-line
        /// wrapper and Mono inlines it -- the same reason as GetButton.
        ///
        /// Hide() doesn't run that risk: it's almost thirty lines and is called from
        /// several places, so Harmony's detour is worth it. While you type, it
        /// simply doesn't close.
        ///
        /// Escape still closes on purpose: it doesn't go through "Use" nor is it
        /// zeroed by ResetButtonStatus before the Hide, so you can tell the deliberate
        /// exit from the typed letter. Nobody gets stuck.
        /// </summary>
        [HarmonyPatch(typeof(InventoryGui), nameof(InventoryGui.Hide))]
        internal static class HideHook
        {
            private static bool Prefix()
            {
                if (!_aberto || !_buscaFocada) return true;
                if (ZInput.GetKeyDown(KeyCode.Escape)) return true;

                if (ModConfig.ChestSearchDebug.Value && ++_logFechar % 50 == 1)
                    Plugin.Log.LogInfo("[CHESTS] ignored an inventory close (you were typing)");
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
