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
        private class Location
        {
            internal Container Chest;
            internal string Name;
            internal int Amount;
        }

        private class Aggregate
        {
            internal string Key;
            internal ItemDrop.ItemData Sample;
            internal string LocalName;
            internal int Total;
            internal readonly List<Location> Locations = new List<Location>();
        }

        /// <summary>Capacity of a chest, in slots (each stack takes one).</summary>
        private class ChestInfo
        {
            internal Container Chest;
            internal string Name;
            internal int Used;
            internal int Total;
            internal int Free => Total - Used;
        }

        // ==================================================================
        // State
        // ==================================================================
        private static bool _open;
        private static readonly List<Aggregate> _all = new List<Aggregate>();
        private static readonly List<ChestInfo> _chests = new List<ChestInfo>();
        private static int _slotsUsed, _slotsTotal;
        private static float _nextScan;
        private static bool _byChest;
        private static bool _sortByName;
        private static Aggregate _focused;
        private static int _chestsSeen;
        private static string _signature;
        private static bool _searchFocused;

        // UI
        private static GameObject _panel;
        private static RectTransform _scroll, _content;
        private static TMP_Text _summary, _footerName, _footerTotal, _footerLocations, _footerAction;
        private static TMP_Text _occupancyText;
        private static Image _occupancyFill;
        private static TMP_InputField _search;
        private static Button _button;
        private static TMP_Text _buttonText;
        private static readonly Image[] _chip = new Image[3];
        private static readonly TMP_Text[] _chipText = new TMP_Text[3];
        private static readonly List<GameObject> _trash = new List<GameObject>();

        // ---- palette
        private static readonly Color Gold = Hex("#E6D3A2");
        private static readonly Color TextColor = Hex("#A2947C");
        private static readonly Color Dim = Hex("#6F6353");
        private static readonly Color LineColor = new Color(0.745f, 0.627f, 0.431f, 0.18f);
        private static readonly Color Border = Hex("#4A3D2C");
        private static readonly Color ChipBackground = Hex("#171208");

        private static Color Hex(string hex)
            => ColorUtility.TryParseHtmlString(hex, out var c) ? c : Color.white;


        // ---- panel measurements (all in canvas pixels, top down)
        private const float Pad = 12f;
        private const float TitleHeight = 17f;
        private const float SummaryHeight = 14f;
        private const float SearchHeight = 26f;
        private const float ChipHeight = 26f;
        private const float BarHeight = 12f;
        private const float FooterHeight = 92f;
        private const float CellGap = 5f;
        private const float LabelHeight = 15f;

        // ==================================================================
        // Cycle
        // ==================================================================
        internal static void Update()
        {
            ProcessQueue();

            if (!ModConfig.ChestSearchEnabled.Value)
            {
                // Turning it off in F1 has to make the button disappear too. Before,
                // it stayed on screen and clickable, giving the impression that the
                // option did nothing.
                Close();
                if (_button != null) { Object.Destroy(_button.gameObject); _button = null; _buttonText = null; }
                return;
            }

            // Optional key: if the inventory is closed, opens both at once. It stays
            // outside the IsVisible block for that reason.
            if (OpenKeyPressed())
            {
                if (!InventoryGui.IsVisible())
                {
                    InventoryGui.instance?.Show(null);
                    _openOnShow = true;   // the panel only exists after the screen is built
                }
                else Toggle();
            }

            if (!InventoryGui.IsVisible())
            {
                Close();
                return;
            }

            EnsureButton();

            if (_openOnShow)
            {
                _openOnShow = false;
                if (!_open) Toggle();
            }

            // Read by a Prefix that runs dozens of times per frame: we compute it
            // once here so that over there it's just a bool read.
            bool before = _searchFocused;
            _searchFocused = _open && SearchHasFocus();
            if (before != _searchFocused && ModConfig.ChestSearchDebug.Value)
                Plugin.Log.LogInfo($"[CHESTS] search focus: {_searchFocused}");

            if (!_open) return;

            // InventoryGui.Show reactivates crafting. If the screen was reopened
            // underneath us, it would come back on top of the panel -- we reassert
            // every frame.
            ShowCrafting(false);

            // Position and size come from config every frame, so you can fine-tune
            // the fit through F1 without restarting the game.
            PositionPanel();

            // If the font wasn't ready when the panel was built, the texts came out
            // fontless and TMP spams the log every frame. Reapply once it resolves.
            if (_fontVersion != StoreHudPatch.FontVersion)
            {
                _fontVersion = StoreHudPatch.FontVersion;
                StoreHudPatch.ApplyFontToAll(_panel.transform);
            }

            UpdateDropArea();

            // Resizing only changes the frame: the innards were positioned by hand for
            // the old size and would be shrunk into a corner. Rebuild when it changes.
            var size = _panel.GetComponent<RectTransform>().rect.size;
            if ((size - _builtSize).sqrMagnitude > 4f) Rebuild();

            if (Time.realtimeSinceStartup >= _nextScan)
            {
                _nextScan = Time.realtimeSinceStartup + ModConfig.ChestSearchRefresh.Value;
                Scan();

                // Rebuilding the grid twice a second would destroy and recreate
                // dozens of slots for nothing, flickering the tooltip and losing the
                // item under the mouse. Only rebuild when chest contents truly changed.
                string now = Signature();
                if (now != _signature) { _signature = now; Build(); }
            }
        }

        private static Vector2 _builtSize;
        private static int _fontVersion;

        /// <summary>
        /// Rebuilds the whole panel while preserving what you already typed. It only
        /// happens when the size changes (F1 tweak, resolution change), so the cost of
        /// recreating doesn't matter.
        /// </summary>
        private static void Rebuild()
        {
            string text = _search != null ? _search.text : "";

            _panel.transform.SetParent(null, false);
            Object.Destroy(_panel);
            // Everything below was a child of the panel and died with it. Clearing the
            // references avoids reusing a destroyed object after the rebuild.
            _panel = null;
            _search = null;
            _scroll = null;
            _content = null;
            _occupancyFill = null;
            _occupancyText = null;
            _amountRow = null;
            _amountField = null;
            _dropArea = null;
            _dropTitle = null;
            _dropPlan = null;
            _dropSignature = null;
            _shortcutLabels.Clear();
            _trash.Clear();

            EnsurePanel();
            if (_panel == null) return;

            _panel.SetActive(true);
            if (_search != null) _search.text = text;
            _signature = null;
        }

        /// <summary>
        /// TMP_InputField.isFocused alone wasn't enough in practice -- the field is a
        /// GuiInputField (a game subclass) and its state doesn't always match. The
        /// EventSystem is the source of truth about who's receiving the keyboard.
        /// </summary>
        private static bool SearchHasFocus()
        {
            // Applies to BOTH fields: typing the quantity also can't turn into a
            // movement command.
            if (_amountField != null && _amountField.isFocused) return true;
            if (_search == null) return false;
            if (_search.isFocused) return true;

            var es = UnityEngine.EventSystems.EventSystem.current;
            if (es == null) return false;

            var selected = es.currentSelectedGameObject;
            return selected != null && (selected == _search.gameObject || selected.transform.IsChildOf(_search.transform));
        }

        private static bool _openOnShow;

        /// <summary>
        /// The key can't fire while you're typing in the search or in chat --
        /// otherwise its letter would close the panel mid-search.
        /// </summary>
        private static bool OpenKeyPressed()
        {
            var shortcut = ModConfig.ChestSearchKey.Value;
            if (shortcut.MainKey == KeyCode.None) return false;
            if (_searchFocused) return false;
            if (Chat.instance != null && Chat.instance.HasFocus()) return false;
            if (Console.IsVisible() || TextInput.IsVisible() || Minimap.IsOpen()) return false;
            return shortcut.IsDown();
        }

        private static void Close()
        {
            if (!_open && _panel == null) return;

            // Closing with the split dialog open would leave our listeners hanging on
            // the game's shared object.
            if (_mySplit) { InventoryGui.instance?.m_splitDialog?.SetActive(false); ClearSplit(); }

            if (_panel != null && _panel.activeSelf) _panel.SetActive(false);
            ShowCrafting(true);
            _open = false;
            _focused = null;
            _searchFocused = false;
            UpdateButton();
        }

        private static void Toggle()
        {
            if (_open) { Close(); return; }

            EnsurePanel();
            if (_panel == null) return;

            _open = true;
            _signature = null;
            _nextScan = 0f;
            ShowCrafting(false);
            _panel.SetActive(true);
            if (_search != null) _search.ActivateInputField();
            UpdateButton();
        }

        /// <summary>
        /// The panel takes the crafting panel's place instead of drawing on top of it.
        /// </summary>
        private static void ShowCrafting(bool visible)
        {
            var gui = InventoryGui.instance;
            if (gui == null || gui.m_crafting == null) return;
            if (gui.m_crafting.gameObject.activeSelf != visible)
                gui.m_crafting.gameObject.SetActive(visible);
        }

        // ==================================================================
        // Scan
        // ==================================================================
        private static string Key(ItemDrop.ItemData item)
        {
            // Quality only enters the key when the item has levels -- otherwise two
            // swords of different quality would become a single stack on screen.
            return item.m_shared.m_maxQuality > 1
                ? item.m_shared.m_name + "#" + item.m_quality
                : item.m_shared.m_name;
        }

        private static void Scan()
        {
            _all.Clear();
            _chests.Clear();
            _slotsUsed = 0;
            _slotsTotal = 0;
            var map = new Dictionary<string, Aggregate>();

            var chests = Chests.Nearby(ModConfig.ChestSearchRadius.Value);
            _chestsSeen = chests.Count;

            foreach (var chest in chests)
            {
                var inv = chest.GetInventory();
                if (inv == null) continue;

                string chestName = Chests.VisibleName(chest);

                // A slot is per STACK, not per unit: NrOfItems returns m_inventory.Count,
                // which is the number of stacks. 120 wood with a stack of 50 takes 3.
                var info = new ChestInfo
                {
                    Chest = chest,
                    Name = chestName,
                    Used = inv.NrOfItems(),
                    Total = inv.GetWidth() * inv.GetHeight(),
                };
                _chests.Add(info);
                _slotsUsed += info.Used;
                _slotsTotal += info.Total;

                foreach (var item in inv.GetAllItems())
                {
                    string key = Key(item);
                    if (!map.TryGetValue(key, out var agg))
                    {
                        agg = new Aggregate
                        {
                            Key = key,
                            Sample = item,
                            LocalName = Localization.instance.Localize(item.m_shared.m_name),
                        };
                        map[key] = agg;
                        _all.Add(agg);
                    }

                    agg.Total += item.m_stack;

                    var loc = agg.Locations.Find(o => o.Chest == chest);
                    if (loc == null) agg.Locations.Add(new Location { Chest = chest, Name = chestName, Amount = item.m_stack });
                    else loc.Amount += item.m_stack;
                }
            }

            if (_focused != null && !_all.Contains(_focused))
            {
                string key = _focused.Key;
                _focused = _all.Find(a => a.Key == key);
            }
        }

        private static string Signature()
        {
            var sb = new StringBuilder();
            sb.Append(_chestsSeen).Append('/').Append(_slotsUsed)
              .Append('/').Append(_slotsTotal).Append('|');
            foreach (var a in _all) sb.Append(a.Key).Append(':').Append(a.Total).Append(';');
            return sb.ToString();
        }

        // Searching "carvao" has to find "Carvão", and "carvão" too. An explicit
        // table instead of String.Normalize(FormD): Unicode decomposition
        // depends on ICU, which in Unity's Mono isn't always complete -- and a
        // search that fails silently is worse than having no search.
        private const string Accented = "áàâãäéèêëíìîïóòôõöúùûüçñýÿ";
        private const string Unaccented = "aaaaaeeeeiiiiooooouuuucnyy";

        private static string Normalize(string s)
        {
            if (string.IsNullOrEmpty(s)) return "";

            // The two tables walk in pairs by index: if someone edits one and
            // forgets the other, the error would be silent and swap letter by letter.
            if (Accented.Length != Unaccented.Length) return s.ToLowerInvariant();

            var sb = new StringBuilder(s.Length);
            foreach (char raw in s)
            {
                char c = char.ToLowerInvariant(raw);
                int i = Accented.IndexOf(c);
                sb.Append(i >= 0 ? Unaccented[i] : c);
            }
            return sb.ToString();
        }

        private static List<Aggregate> Filtered()
        {
            string q = Normalize(_search != null ? _search.text.Trim() : "");

            var list = new List<Aggregate>();
            foreach (var a in _all)
                if (q.Length == 0 || Normalize(a.LocalName).Contains(q))
                    list.Add(a);

            if (_sortByName)
                list.Sort((x, y) => string.Compare(x.LocalName, y.LocalName, System.StringComparison.CurrentCulture));
            else
                list.Sort((x, y) =>
                {
                    int d = y.Total.CompareTo(x.Total);
                    return d != 0 ? d : string.Compare(x.LocalName, y.LocalName, System.StringComparison.CurrentCulture);
                });

            return list;
        }

        // ==================================================================
        // Take
        // ==================================================================
        private class Request
        {
            internal Container Chest;
            internal string Key;
            internal int Amount;
            internal string Name;
            /// <summary>true = backpack -> chest (store); false = chest -> backpack (take).</summary>
            internal bool Storing;
            /// <summary>
            /// Exact stack to draw from when storing. Without it the move would take
            /// from whichever stack of that key comes first, so clicking one of two
            /// identical stacks could empty the other one.
            /// </summary>
            internal ItemDrop.ItemData Only;
        }

        private static readonly List<Request> _queue = new List<Request>();
        private static readonly Dictionary<Container, float> _lastRequest =
            new Dictionary<Container, float>();

        private static Request _inFlight;
        private static float _inFlightUntil;
        private static int _taken;
        private static string _roundName;

        private static bool FilterActive => _inFlight != null && Time.realtimeSinceStartup < _inFlightUntil;

        /// <summary>
        /// Am I already the ZDO owner? Then I can touch the inventory directly -- it's
        /// the same condition the game itself requires before granting the request.
        /// </summary>
        private static bool IsOwner(Container chest) => Chests.Usable(chest);

        /// <summary>
        /// Takes <paramref name="amount"/> units of the item, gathering from as
        /// many chests as needed. Pass int.MaxValue for "all".
        /// </summary>
        private static void Take(Aggregate agg, Container onlyFrom, int amount)
        {
            if (agg == null || Player.m_localPlayer == null) return;

            int wanted = Mathf.Clamp(amount, 1, agg.Total);

            // From the fullest chest first: fewer requests for the same amount.
            var locations = new List<Location>(agg.Locations);
            locations.Sort((a, b) => b.Amount.CompareTo(a.Amount));

            _taken = 0;
            _roundName = agg.LocalName;

            var destination = Player.m_localPlayer.GetInventory();

            foreach (var o in locations)
            {
                if (wanted <= 0) break;
                if (o.Amount <= 0 || o.Chest == null) continue;
                if (onlyFrom != null && o.Chest != onlyFrom) continue;

                int n = Mathf.Min(o.Amount, wanted);
                wanted -= n;

                if (IsOwner(o.Chest))
                {
                    // Fast path: no RPC, no wait, no 2s limit.
                    _taken += Move(destination, o.Chest.GetInventory(), agg.Key, n);
                }
                else
                {
                    _queue.Add(new Request { Chest = o.Chest, Key = agg.Key, Amount = n, Name = agg.LocalName });
                }
            }

            Complete();
        }

        /// <summary>Moves up to <paramref name="amount"/> of the item, respecting space.</summary>
        private static readonly MethodInfo ChangedMethod =
            AccessTools.Method(typeof(Inventory), "Changed", new[] { typeof(bool), typeof(bool) });

        private static void Notify(Inventory inv)
            => ChangedMethod?.Invoke(inv, new object[] { true, false });

        /// <summary>How many units of this item exist in the inventory.</summary>
        private static int Count(Inventory inv, string key)
        {
            if (inv == null) return 0;
            int n = 0;
            foreach (var it in inv.GetAllItems())
                if (Key(it) == key) n += it.m_stack;
            return n;
        }

        /// <summary>
        /// Moves up to <paramref name="amount"/> units between two inventories.
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
        private static int Move(Inventory destination, Inventory source, string key, int amount,
                                ItemDrop.ItemData only = null)
        {
            if (destination == null || source == null || amount <= 0) return 0;

            int beforeSource = Count(source, key);
            int beforeDestination = Count(destination, key);

            int remaining = amount;

            foreach (var item in new List<ItemDrop.ItemData>(source.GetAllItems()))
            {
                if (remaining <= 0) break;
                if (Key(item) != key) continue;

                // When the player acted on one specific stack, touch only that stack.
                if (only != null && !ReferenceEquals(item, only)) continue;

                int n = Mathf.Min(item.m_stack, remaining);

                // Debit the source first, then measure how much actually left it. We do
                // not trust the mutated ItemData that AddItem gets: depending on whether
                // it merges into an existing stack or makes a new one, its effect on the
                // passed object is not something to depend on (trusting it made the item
                // come out twice).
                var part = item.Clone();
                int sourceBefore = Count(source, key);
                source.RemoveItem(item, n);
                int removed = sourceBefore - Count(source, key);
                if (removed <= 0) { remaining -= n; continue; }

                // Measure what the destination actually took, instead of trusting the
                // object, and give back exactly what did not fit.
                part.m_stack = removed;
                int destBefore = Count(destination, key);
                destination.AddItem(part);
                int back = removed - (Count(destination, key) - destBefore);
                if (back > 0)
                {
                    var returned = item.Clone();
                    returned.m_stack = back;
                    if (!source.AddItem(returned))
                        Plugin.Log.LogError(
                            $"[CHESTS] LOST {back}x '{key}': neither the chest nor "
                          + "the backpack accepted it. Tell the mod author.");
                }

                remaining -= n;
            }

            Notify(source);
            Notify(destination);

            int movedOut = beforeSource - Count(source, key);
            int movedIn = Count(destination, key) - beforeDestination;

            // The store path carries a specific stack, so log it: this is the path
            // where "it disappeared" reports come from.
            if (only != null)
                Plugin.Log.LogInfo($"[CHESTS] store-move '{key}': asked {amount}, "
                                 + $"out {movedOut}, in {movedIn}");

            // Safety net: if the two ends don't match, someone gained or
            // lost an item. There's no safe way to undo it here, but yelling in the log
            // turns "it vanished, I don't know how" into something investigable.
            if (movedOut != movedIn)
                Plugin.Log.LogError(
                    $"[CHESTS] IMBALANCE in '{key}': {movedOut} left the source but "
                  + $"{movedIn} entered the destination (request {amount}). Tell the mod author.");
            else if (movedIn > 0 && movedIn < amount)
                Plugin.Log.LogInfo($"[CHESTS] moved {movedIn} of {amount} (destination full?)");

            return movedIn;
        }

        /// <summary>
        /// The owner refused the stack (chest in use, not yours). Without this the request
        /// would sit in flight until the 3s timeout, stalling every request behind it --
        /// which is what made storing several stacks feel slow.
        /// </summary>
        [HarmonyPatch(typeof(Container), "RPC_StackResponse")]
        internal static class StackResponseHook
        {
            private static void Postfix(bool granted)
            {
                if (granted || _inFlight == null || !_inFlight.Storing) return;
                Plugin.Log.LogInfo($"[CHESTS] refused by '{Chests.VisibleName(_inFlight.Chest)}'");
                _inFlight = null;
                Complete();
            }
        }

        /// <summary>
        /// Moves one whole stack, safely. The quick-store shares the same footgun with
        /// AddItem's partial/failed cases, so it goes through here instead of rolling
        /// its own add-then-remove.
        /// </summary>
        internal static int SafeTransfer(Inventory destination, Inventory source,
                                         ItemDrop.ItemData item)
        {
            if (item == null) return 0;
            return Move(destination, source, Key(item), item.m_stack, item);
        }

        /// <summary>
        /// One request at a time: the RPC is asynchronous and the filter is global, so
        /// two chests in flight at once would mix up the responses.
        /// </summary>
        private static void ProcessQueue()
        {
            if (_inFlight != null)
            {
                if (Time.realtimeSinceStartup < _inFlightUntil) return;
                Plugin.Log.LogInfo($"[CHESTS] no response from '{Chests.VisibleName(_inFlight.Chest)}'");
                _inFlight = null;
                Complete();
            }

            if (_queue.Count == 0) return;

            var p = _queue[0];
            if (p.Chest == null) { _queue.RemoveAt(0); return; }

            // The game refuses two TakeAlls on the same chest within 2s
            // (Container.RPC_RequestTakeAll, m_lastTakeAllTime). Waiting is better than
            // losing the request silently. The store RPC (RPC_RequestStack) has no
            // such limit, so it doesn't wait.
            if (!p.Storing
                && _lastRequest.TryGetValue(p.Chest, out float t)
                && Time.realtimeSinceStartup - t < 2.05f) return;

            _queue.RemoveAt(0);
            if (!p.Storing) _lastRequest[p.Chest] = Time.realtimeSinceStartup;

            _inFlight = p;
            _inFlightUntil = Time.realtimeSinceStartup + 3f;

            if (p.Storing)
            {
                Plugin.Log.LogInfo($"[CHESTS] request {p.Amount} '{p.Key}' -> {Chests.VisibleName(p.Chest)}");
                // Same handshake as "take", in reverse: the owner grants ownership
                // (ForceSendZDO + SetOwner) before the inventory is touched.
                p.Chest.StackAll();
            }
            else if (!p.Chest.TakeAll(Player.m_localPlayer))
            {
                _inFlight = null;   // refused on the spot; the game already warned
                Complete();
            }
        }

        private static void Complete()
        {
            if (_inFlight != null || _queue.Count > 0) return;
            if (_taken <= 0) return;

            Player.m_localPlayer?.Message(MessageHud.MessageType.TopLeft,
                $"{_taken} {_roundName}");

            _taken = 0;
            _signature = null;     // force a rebuild
            _nextScan = 0f;
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
                if (!FilterActive) return true;

                var p = _inFlight;

                // Check that it's the response for OUR chest: if the player pressed
                // the game's own "take all" within the window, we don't hijack it.
                if (p.Chest == null || fromInventory != p.Chest.GetInventory()) return true;

                _inFlight = null;
                _taken += Move(__instance, fromInventory, p.Key, p.Amount);
                Complete();
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
                if (!FilterActive) return true;

                var p = _inFlight;
                if (p == null || !p.Storing) return true;
                if (p.Chest == null || __instance != p.Chest.GetInventory()) return true;

                _inFlight = null;
                int n = Move(__instance, fromInventory, p.Key, p.Amount, p.Only);
                _taken += n;
                __result = n;
                Complete();
                return false;
            }
        }

        // ==================================================================
        // Store: planning
        // ==================================================================
        private class Destination
        {
            internal Container Chest;
            internal string Name;
            internal int Amount;
            internal string Reason;
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
        /// It only plans. Store() executes, and the panel shows the plan before you
        /// confirm -- storing without knowing where it ended up is worse than not storing.
        /// </summary>
        private static List<Destination> PlanDeposit(ItemDrop.ItemData item, int amount, out int leftover)
        {
            var plan = new List<Destination>();
            leftover = amount;
            if (item == null || amount <= 0) return plan;

            // C# doesn't let a local function touch an out parameter, so the balance
            // runs in a normal variable and returns to 'leftover' at the end.
            int left = amount;

            string name = item.m_shared.m_name;
            int stack = Mathf.Max(1, item.m_shared.m_maxStackSize);
            float level = item.m_worldLevel;

            // Free slots get consumed over the course of the plan, otherwise two passes
            // would reserve the same slot and the "didn't fit" count would come out optimistic.
            var free = new Dictionary<Container, int>();
            foreach (var b in _chests) free[b.Chest] = b.Free;

            void Put(Container chest, string chestName, int n, string reason)
            {
                if (n <= 0) return;
                // One entry per chest. Each entry becomes one Move or one RPC round-trip,
                // and several entries for the same chest meant several handshakes per stack
                // (the slow part when storing many stacks), and several chances to go wrong.
                var j = plan.Find(p => p.Chest == chest);
                if (j != null) j.Amount += n;
                else plan.Add(new Destination { Chest = chest, Name = chestName, Amount = n, Reason = reason });
                left -= n;
            }

            bool HasItem(Container chest)
            {
                var inv = chest.GetInventory();
                return inv != null && inv.ContainsItemByName(name);
            }

            // With StoreOnlyOwnedChests on, a chest the other player's client owns is left
            // out of the plan entirely, so the store never goes through the RPC handshake.
            bool DontUse(Container chest)
                => ModConfig.StoreOnlyOwnedChests.Value && !Chests.Usable(chest);

            // 1) top off existing stacks
            foreach (var b in _chests)
            {
                if (left <= 0) break;
                if (DontUse(b.Chest)) continue;
                var inv = b.Chest.GetInventory();
                if (inv == null) continue;
                int fits = inv.FindFreeStackSpace(name, level);
                Put(b.Chest, b.Name, Mathf.Min(fits, left), Lang.T("tops off stack", "completa pilha"));
            }

            // 2) chests that already have the item
            foreach (var b in _chests)
            {
                if (left <= 0) break;
                if (DontUse(b.Chest)) continue;
                if (free[b.Chest] <= 0 || !HasItem(b.Chest)) continue;
                int fits = Mathf.Min(free[b.Chest] * stack, left);
                free[b.Chest] -= Mathf.CeilToInt(fits / (float)stack);
                Put(b.Chest, b.Name, fits, Lang.T("with the rest", "junto do resto"));
            }

            // 3) anyone with space
            foreach (var b in _chests)
            {
                if (left <= 0) break;
                if (DontUse(b.Chest)) continue;
                if (free[b.Chest] <= 0) continue;
                int fits = Mathf.Min(free[b.Chest] * stack, left);
                free[b.Chest] -= Mathf.CeilToInt(fits / (float)stack);
                Put(b.Chest, b.Name, fits, Lang.T("free space", "espaço livre"));
            }

            leftover = left;
            return plan;
        }

        // ==================================================================
        // Stack-split dialog (the game's own)
        // ==================================================================
        private static bool _mySplit;
        private static Aggregate _splitAgg;
        private static Container _splitChest;

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
        private static void OpenSplit(Aggregate agg, Container onlyFrom)
        {
            var gui = InventoryGui.instance;
            if (gui == null || gui.m_splitDialog == null || agg.Total <= 1)
            {
                // Without a dialog (or a single item) the footer already handles it.
                DrawFooter();
                return;
            }

            _mySplit = true;
            _splitAgg = agg;
            _splitChest = onlyFrom;

            gui.m_splitDialog.UpdateLimits(agg.Total, altMode: false);
            gui.m_splitDialog.UpdateIcon(agg.Sample.GetIcon(), agg.LocalName);
            gui.m_splitDialog.SplitAccepted += SplitConfirmed;
            gui.m_splitDialog.SplitCanceled += SplitCanceled;
            gui.m_splitDialog.SetActive(active: true);
        }

        private static void ClearSplit()
        {
            var gui = InventoryGui.instance;
            if (gui != null && gui.m_splitDialog != null)
            {
                gui.m_splitDialog.SplitAccepted -= SplitConfirmed;
                gui.m_splitDialog.SplitCanceled -= SplitCanceled;
            }
            _mySplit = false;
            _splitAgg = null;
            _splitChest = null;
        }

        private static void SplitConfirmed()
        {
            var gui = InventoryGui.instance;
            if (gui == null || _splitAgg == null) { ClearSplit(); return; }

            int n = Mathf.RoundToInt(gui.m_splitDialog.SliderValue);
            var agg = _splitAgg;
            var chest = _splitChest;

            gui.m_splitDialog.SetActive(active: false);
            ClearSplit();

            Take(agg, chest, n);
        }

        private static void SplitCanceled()
        {
            InventoryGui.instance?.m_splitDialog?.SetActive(active: false);
            ClearSplit();
        }

        /// <summary>Enter in the dialog calls this directly; we divert it when it's ours.</summary>
        [HarmonyPatch(typeof(InventoryGui), "OnSplitOk")]
        internal static class SplitOkHook
        {
            private static bool Prefix()
            {
                if (!_mySplit) return true;
                SplitConfirmed();
                return false;
            }
        }

        [HarmonyPatch(typeof(InventoryGui), "OnSplitCancel")]
        internal static class SplitCancelHook
        {
            private static bool Prefix()
            {
                if (!_mySplit) return true;
                SplitCanceled();
                return false;
            }
        }

        /// <summary>Escape goes through here; we release our listeners along with it.</summary>
        [HarmonyPatch(typeof(InventoryGui), "HideSplitDialog")]
        internal static class SplitHideHook
        {
            private static void Postfix()
            {
                if (_mySplit) ClearSplit();
            }
        }

        // ==================================================================
        // Store: the drop area
        // ==================================================================
        private static GameObject _dropArea;
        private static TMP_Text _dropTitle, _dropPlan;
        private static string _dropSignature;

        /// <summary>
        /// Covers the whole panel while you're holding an item. Covering everything
        /// is on purpose: if only one strip accepted the item, you'd miss your aim and
        /// the click would land on the slot below, taking something else.
        /// </summary>
        private static void BuildDropArea(float width, float height)
        {
            _dropArea = new GameObject("VT_Deposito", typeof(RectTransform));
            _dropArea.transform.SetParent(_panel.transform, false);
            Place(_dropArea, 0f, 0f, width, height);

            var background = _dropArea.AddComponent<Image>();
            background.color = new Color(0.047f, 0.035f, 0.02f, 0.93f);

            var bt = _dropArea.AddComponent<Button>();
            bt.targetGraphic = background;
            bt.onClick.AddListener(DropOnPanel);

            var t = NewText(_dropArea.transform, 17f, Hex("#8FD0E8"));
            GameStyle.ApplyTitle(t);
            t.color = Hex("#8FD0E8");
            t.alignment = TextAlignmentOptions.Center;
            t.enableWordWrapping = true;
            Place(t.gameObject, Pad, height * 0.22f, width - Pad * 2f, 50f);
            _dropTitle = t;

            var p = NewText(_dropArea.transform, 12f, TextColor);
            p.alignment = TextAlignmentOptions.Top;
            p.enableWordWrapping = true;
            Place(p.gameObject, Pad + 14f, height * 0.22f + 56f, width - Pad * 2f - 28f, height * 0.5f);
            _dropPlan = p;

            _dropArea.SetActive(false);
        }

        /// <summary>Shows/hides the area and keeps the plan up to date. Called every frame.</summary>
        private static void UpdateDropArea()
        {
            if (_dropArea == null) return;

            var item = ItemInHand(out _, out int amount);
            bool show = item != null && _open;

            if (_dropArea.activeSelf != show) _dropArea.SetActive(show);
            if (!show) { _dropSignature = null; return; }

            // Recomputing the plan every frame would be wasteful: it only changes if the
            // item, the quantity or the chest contents change.
            string sig = item.m_shared.m_name + "#" + amount + "#" + _signature;
            if (sig == _dropSignature) return;
            _dropSignature = sig;

            string name = Localization.instance.Localize(item.m_shared.m_name);
            var plan = PlanDeposit(item, amount, out int leftover);

            _dropTitle.text = plan.Count > 0
                ? string.Format(Lang.T("Store {0} {1}", "Guardar {0} {1}"), amount, name)
                : string.Format(Lang.T("No room for {0}", "Sem espaço para {0}"), name);

            var sb = new StringBuilder();
            if (plan.Count == 0)
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
                foreach (var d in plan)
                    sb.Append($"<color=#DCCFB2>{d.Amount}</color>  {d.Name}")
                      .Append($"   <color=#6F6353>{d.Reason}</color>\n");

                if (leftover > 0)
                    sb.Append("\n<color=#C08080>"
                            + string.Format(Lang.T("{0} won't fit", "{0} não cabe"), leftover)
                            + "</color>");
                else
                    sb.Append("\n<color=#6F6353><i>"
                            + Lang.T("click to confirm", "clique para confirmar")
                            + "</i></color>");
            }
            _dropPlan.text = sb.ToString();
        }

        private static void DropOnPanel()
        {
            var item = ItemInHand(out var from, out int amount);
            if (item == null) return;

            // The item may have left the inventory between grabbing and dropping (another
            // mod, another player). Checking avoids duplicating an item from a stale reference.
            if (from == null || !from.ContainsItem(item)) { ReleaseDrag(); return; }

            Store(item, amount);
            ReleaseDrag();
            _signature = null;       // force rebuilding the grid with the new contents
            _nextScan = 0f;
        }

        // ==================================================================
        // Store: execution
        // ==================================================================
        private static void Store(ItemDrop.ItemData item, int amount)
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

            var plan = PlanDeposit(item, amount, out int leftover);
            if (plan.Count == 0)
            {
                // If the only chests with room are owned by the other player, they were
                // skipped on purpose (StoreOnlyOwnedChests). Say so instead of the vague
                // "no room", which read like a bug.
                int skipped = 0;
                foreach (var b in _chests)
                    if (ModConfig.StoreOnlyOwnedChests.Value && !Chests.Usable(b.Chest)) skipped++;

                player.Message(MessageHud.MessageType.Center, skipped > 0
                    ? Lang.T("No room in your chests (the other player's are skipped)",
                             "Sem espaço nos SEUS baús (os do outro jogador são ignorados)")
                    : Lang.T("No room in nearby chests", "Sem espaço nos baús por perto"));

                Plugin.Log.LogInfo($"[CHESTS] store '{Key(item)}': nothing stored, "
                                 + $"leftover {leftover}, non-owned chests skipped {skipped}");
                return;
            }

            _taken = 0;
            _roundName = Localization.instance.Localize(item.m_shared.m_name);

            var backpack = player.GetInventory();
            string key = Key(item);

            // Store logging stays on: this is where "the item disappeared" reports come
            // from, and it is a handful of lines per action, not per frame.
            Plugin.Log.LogInfo($"[CHESTS] store '{key}': asked {amount} (stack {item.m_stack}), "
                             + $"plan {plan.Count} chest(s), leftover {leftover}");
            foreach (var d in plan)
                Plugin.Log.LogInfo($"[CHESTS]   plan {d.Amount} -> {Chests.VisibleName(d.Chest)}"
                                 + $" (owner={IsOwner(d.Chest)})");

            foreach (var d in plan)
            {
                if (IsOwner(d.Chest))
                    _taken += Move(d.Chest.GetInventory(), backpack, key, d.Amount, item);
                else
                    _queue.Add(new Request
                    {
                        Chest = d.Chest, Key = key, Amount = d.Amount,
                        Name = _roundName, Storing = true, Only = item,
                    });
            }

            if (leftover > 0)
                Plugin.Log.LogInfo($"[CHESTS] {_roundName}: {leftover} didn't fit in nearby chests");

            Complete();
        }

        // ---- reading the game's drag --------------------------------------
        private static readonly FieldInfo DragItemField =
            AccessTools.Field(typeof(InventoryGui), "m_dragItem");
        private static readonly FieldInfo DragInventoryField =
            AccessTools.Field(typeof(InventoryGui), "m_dragInventory");
        private static readonly FieldInfo DragAmountField =
            AccessTools.Field(typeof(InventoryGui), "m_dragAmount");
        private static readonly MethodInfo SetupDragMethod =
            AccessTools.Method(typeof(InventoryGui), "SetupDragItem");

        private static ItemDrop.ItemData ItemInHand(out Inventory from, out int amount)
        {
            from = null; amount = 0;
            var gui = InventoryGui.instance;
            if (gui == null || DragItemField == null) return null;

            var item = DragItemField.GetValue(gui) as ItemDrop.ItemData;
            if (item == null) return null;

            from = DragInventoryField?.GetValue(gui) as Inventory;
            amount = DragAmountField != null ? (int)DragAmountField.GetValue(gui) : item.m_stack;
            return item;
        }

        /// <summary>Ends the drag the same way the game itself ends it.</summary>
        private static void ReleaseDrag()
        {
            var gui = InventoryGui.instance;
            if (gui != null) SetupDragMethod?.Invoke(gui, new object[] { null, null, 1 });
        }

        // ==================================================================
        // Button
        // ==================================================================
        private static void EnsureButton()
        {
            if (_button != null) return;

            var gui = InventoryGui.instance;
            if (gui == null || gui.m_player == null)
            {
                Complain("InventoryGui or the inventory panel doesn't exist yet");
                return;
            }

            GameObject go;

            if (gui.m_takeAllButton != null)
            {
                // Cloning one of the game's own buttons brings art, font and click sound.
                go = Object.Instantiate(gui.m_takeAllButton.gameObject, gui.m_player);
                _button = go.GetComponent<Button>();
                _buttonText = go.GetComponentInChildren<TMP_Text>(true);
            }
            else
            {
                // Without the template, a plain button -- ugly is better than missing.
                // Before, this was a silent `return`, and "the button doesn't appear"
                // became a mystery with no clue in the log.
                Complain("m_takeAllButton not found; using a plain button");

                go = new GameObject("VT_BotaoBaus", typeof(RectTransform));
                go.transform.SetParent(gui.m_player, false);

                var img = go.AddComponent<Image>();
                img.color = ChipBackground;
                _button = go.AddComponent<Button>();
                _button.targetGraphic = img;

                _buttonText = NewText(go.transform, 13f, Gold);
                _buttonText.alignment = TextAlignmentOptions.Center;
                Stretch(_buttonText.rectTransform);
            }

            go.name = "VT_BotaoBaus";
            go.SetActive(true);

            _button.onClick.RemoveAllListeners();
            _button.onClick.AddListener(Toggle);
            _button.interactable = true;

            var rt = go.GetComponent<RectTransform>();
            rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(1f, 0f);
            rt.sizeDelta = new Vector2(150f, 30f);
            rt.anchoredPosition = new Vector2(ModConfig.ChestSearchButtonX.Value,
                                              ModConfig.ChestSearchButtonY.Value);

            Plugin.Log.LogInfo($"[CHESTS] button created at {rt.anchoredPosition} "
                             + $"(inventory panel {gui.m_player.rect.width:0}x{gui.m_player.rect.height:0})");
            UpdateButton();
        }

        private static float _nextComplaint;

        /// <summary>Warns in the log, at most once every 5s, why the button didn't come up.</summary>
        private static void Complain(string reason)
        {
            if (Time.realtimeSinceStartup < _nextComplaint) return;
            _nextComplaint = Time.realtimeSinceStartup + 5f;
            Plugin.Log.LogWarning($"[CHESTS] button not created: {reason}");
        }

        private static void UpdateButton()
        {
            if (_buttonText != null)
                _buttonText.text = _open
                    ? Lang.T("Close chests", "Fechar baús")
                    : Lang.T("Nearby chests", "Baús próximos");
        }

        // ==================================================================
        // Panel -- manual positioning
        // ==================================================================
        /// <summary>Anchors to the panel's top-left corner and positions by (x, y from the top).</summary>
        private static RectTransform Place(GameObject go, float x, float yFromTop, float width, float height)
        {
            var rt = go.GetComponent<RectTransform>() ?? go.AddComponent<RectTransform>();
            rt.anchorMin = rt.anchorMax = new Vector2(0f, 1f);
            rt.pivot = new Vector2(0f, 1f);
            rt.anchoredPosition = new Vector2(x, -yFromTop);
            rt.sizeDelta = new Vector2(width, height);
            return rt;
        }

        /// <summary>
        /// Fits the panel into the crafting panel's rectangle. Width and height at
        /// 0 means copy the crafting one's, which is what aligns correctly at any
        /// resolution; a non-zero value wins.
        /// </summary>
        private static void PositionPanel()
        {
            var gui = InventoryGui.instance;
            if (_panel == null || gui == null || gui.m_crafting == null) return;

            var crafting = gui.m_crafting;
            var panelRt = _panel.GetComponent<RectTransform>();

            panelRt.anchoredPosition = crafting.anchoredPosition
                + new Vector2(ModConfig.ChestSearchX.Value, ModConfig.ChestSearchY.Value);

            float w = ModConfig.ChestSearchWidth.Value;
            float h = ModConfig.ChestSearchHeight.Value;
            panelRt.sizeDelta = new Vector2(w > 0f ? w : crafting.sizeDelta.x,
                                            h > 0f ? h : crafting.sizeDelta.y);
        }

        private static void EnsurePanel()
        {
            if (_panel != null) return;

            var gui = InventoryGui.instance;
            if (gui == null || gui.m_crafting == null) return;

            var parent = gui.m_crafting.parent as RectTransform;
            if (parent == null) return;

            // ---- shell, copying the crafting panel's rectangle exactly
            _panel = new GameObject("VT_PainelBaus", typeof(RectTransform));
            _panel.transform.SetParent(parent, false);
            _panel.transform.SetSiblingIndex(gui.m_crafting.GetSiblingIndex());

            var crafting = gui.m_crafting;
            var panelRt = _panel.GetComponent<RectTransform>();
            panelRt.anchorMin = crafting.anchorMin;
            panelRt.anchorMax = crafting.anchorMax;
            panelRt.pivot = crafting.pivot;
            PositionPanel();

            // The crafting panel's wood, with the same 9-slice border. No drawing a
            // border by hand: the game's sprite already brings its own.
            GameStyle.Discover();

            var img = _panel.AddComponent<Image>();
            GameStyle.ApplyBackground(img);

            LayoutRebuilder.ForceRebuildLayoutImmediate(panelRt);

            // If the rectangle still hasn't resolved (stretch anchor in a parent that
            // didn't go through layout), measuring would give zero and the panel would come out empty.
            float width = panelRt.rect.width  > 60f ? panelRt.rect.width  : 340f;
            float height = panelRt.rect.height > 80f ? panelRt.rect.height : 520f;
            if (panelRt.rect.width <= 60f || panelRt.rect.height <= 80f)
            {
                panelRt.sizeDelta = new Vector2(width, height);
                Plugin.Log.LogInfo($"[CHESTS] crafting rectangle didn't resolve; using {width}x{height}");
            }
            float innerWidth = width - Pad * 2f;

            // ---- header
            float y = Pad;

            var title = NewText(_panel.transform, 18f, Gold);
            title.text = Lang.T("Nearby chests", "Baús próximos");
            GameStyle.ApplyTitle(title);
            title.alignment = TextAlignmentOptions.Center;
            Place(title.gameObject, Pad, y, innerWidth, TitleHeight);
            y += TitleHeight + 4f;

            _summary = NewText(_panel.transform, 11.5f, TextColor);
            _summary.alignment = TextAlignmentOptions.Center;
            Place(_summary.gameObject, Pad, y, innerWidth, SummaryHeight);
            y += SummaryHeight + 5f;

            // ---- occupancy bar: how much of the surrounding chests is still free
            float textWidth = 112f;
            var track = new GameObject("trilho", typeof(RectTransform));
            track.transform.SetParent(_panel.transform, false);
            Place(track, Pad, y + 2f, innerWidth - textWidth - 8f, BarHeight);
            var bg = track.AddComponent<Image>();
            bg.color = new Color(0.07f, 0.055f, 0.03f, 1f);
            bg.raycastTarget = false;

            var fillGo = new GameObject("fill", typeof(RectTransform));
            fillGo.transform.SetParent(track.transform, false);
            _occupancyFill = fillGo.AddComponent<Image>();
            _occupancyFill.raycastTarget = false;
            var frt = _occupancyFill.rectTransform;
            frt.anchorMin = new Vector2(0f, 0f);
            frt.anchorMax = new Vector2(0f, 1f);   // width comes from sizeDelta.x
            frt.pivot = new Vector2(0f, 0.5f);
            frt.offsetMin = new Vector2(1f, 1f);
            frt.offsetMax = new Vector2(1f, -1f);

            _occupancyText = NewText(_panel.transform, 11f, TextColor);
            _occupancyText.alignment = TextAlignmentOptions.MidlineRight;
            Place(_occupancyText.gameObject, Pad + innerWidth - textWidth, y, textWidth, BarHeight + 4f);
            y += BarHeight + 8f;

            Divider(Pad, y, innerWidth);
            y += 7f;

            // ---- search
            CreateSearch(Pad, y, innerWidth);
            y += SearchHeight + 7f;

            // ---- sorting
            float x = Pad;
            x += Chip(0, Lang.T("Amount", "Quantidade"), x, y, () => { _sortByName = false; Build(); });
            x += Chip(1, Lang.T("Name", "Nome"), x, y, () => { _sortByName = true; Build(); });
            Chip(2, Lang.T("By chest", "Por baú"), Pad + innerWidth - 62f, y, () => { _byChest = !_byChest; Build(); }, 62f);
            y += ChipHeight + 7f;

            Divider(Pad, y, innerWidth);
            y += 8f;

            // ---- scrollable area
            float scrollHeight = height - y - FooterHeight - Pad - 8f;

            var roloGo = new GameObject("rolo", typeof(RectTransform));
            roloGo.transform.SetParent(_panel.transform, false);
            _scroll = Place(roloGo, Pad, y, innerWidth, scrollHeight);
            roloGo.AddComponent<RectMask2D>();

            // A raycast target on the viewport itself. Without it the pointer lands on
            // the panel background (an ancestor of the ScrollRect) whenever it is not
            // exactly over a slot's icon, and the wheel event travels up to a parent
            // that has no scroll handler -- so scrolling only worked over the items.
            // Transparent on purpose: it is here to catch the ray, not to be seen.
            var viewportHit = roloGo.AddComponent<Image>();
            viewportHit.color = new Color(0f, 0f, 0f, 0f);

            var sr = roloGo.AddComponent<ScrollRect>();
            sr.horizontal = false;
            sr.scrollSensitivity = ModConfig.ChestSearchScrollSpeed.Value;
            sr.movementType = ScrollRect.MovementType.Clamped;

            var conteudoGo = new GameObject("conteudo", typeof(RectTransform));
            conteudoGo.transform.SetParent(roloGo.transform, false);
            _content = conteudoGo.GetComponent<RectTransform>();
            _content.anchorMin = new Vector2(0f, 1f);
            _content.anchorMax = new Vector2(1f, 1f);
            _content.pivot = new Vector2(0.5f, 1f);
            _content.anchoredPosition = Vector2.zero;
            _content.sizeDelta = new Vector2(0f, 0f);
            sr.content = _content;
            sr.viewport = _scroll;

            // ---- footer
            float footerY = height - FooterHeight - Pad + 4f;
            Divider(Pad, footerY - 7f, innerWidth);

            _footerName = NewText(_panel.transform, 12.5f, Gold);
            Place(_footerName.gameObject, Pad, footerY, innerWidth - 110f, 15f);

            _footerTotal = NewText(_panel.transform, 11.5f, TextColor);
            _footerTotal.alignment = TextAlignmentOptions.MidlineRight;
            Place(_footerTotal.gameObject, Pad + innerWidth - 110f, footerY, 110f, 15f);

            _footerLocations = NewText(_panel.transform, 11f, TextColor);
            _footerLocations.enableWordWrapping = true;
            _footerLocations.alignment = TextAlignmentOptions.TopLeft;
            Place(_footerLocations.gameObject, Pad, footerY + 17f, innerWidth, 26f);

            _footerAction = NewText(_panel.transform, 11f, Dim);
            _footerAction.alignment = TextAlignmentOptions.TopLeft;
            Place(_footerAction.gameObject, Pad, footerY + 44f, innerWidth, 15f);

            BuildAmountRow(Pad, footerY + 42f, innerWidth);
            BuildDropArea(width, height);

            UpdateChips();
            _builtSize = new Vector2(width, height);
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
        private static void CreateSearch(float x, float y, float width)
        {
            var box = new GameObject("VT_BuscaSimples", typeof(RectTransform));
            box.transform.SetParent(_panel.transform, false);
            Place(box, x, y, width, SearchHeight);
            // Dark box over the wood, so the typed text has contrast.
            var border = box.AddComponent<Image>();
            border.color = Border;

            var inside = new GameObject("fundo", typeof(RectTransform));
            inside.transform.SetParent(box.transform, false);
            var drt = inside.GetComponent<RectTransform>();
            drt.anchorMin = Vector2.zero; drt.anchorMax = Vector2.one;
            drt.offsetMin = new Vector2(1f, 1f); drt.offsetMax = new Vector2(-1f, -1f);
            var dim = inside.AddComponent<Image>();
            dim.color = new Color(0.05f, 0.04f, 0.03f, 0.85f);
            dim.raycastTarget = false;

            var area = new GameObject("area", typeof(RectTransform));
            area.transform.SetParent(box.transform, false);
            var areaRt = area.GetComponent<RectTransform>();
            areaRt.anchorMin = Vector2.zero; areaRt.anchorMax = Vector2.one;
            areaRt.offsetMin = new Vector2(8f, 2f); areaRt.offsetMax = new Vector2(-8f, -2f);
            area.AddComponent<RectMask2D>();

            var txt = NewText(area.transform, 12.5f, Hex("#E8DCC2"));
            Stretch(txt.rectTransform);
            var placeholder = NewText(area.transform, 12.5f, Dim);
            Stretch(placeholder.rectTransform);
            placeholder.text = Lang.T("search item...", "buscar item...");
            placeholder.fontStyle = FontStyles.Italic;

            var field = box.AddComponent<TMP_InputField>();
            field.textViewport = areaRt;
            field.textComponent = txt;
            field.placeholder = placeholder;
            field.lineType = TMP_InputField.LineType.SingleLine;
            field.caretColor = Gold;
            field.customCaretColor = true;
            field.caretWidth = 2;
            field.selectionColor = new Color(0.72f, 0.63f, 0.45f, 0.35f);
            field.onValueChanged.AddListener(_ => { _focused = null; Build(); });

            _search = field;
        }

        // ==================================================================
        // Grid
        // ==================================================================
        private static void Build()
        {
            if (_panel == null || _content == null) return;

            int sum = 0;
            foreach (var a in _all) sum += a.Total;
            _summary.text = string.Format(
                Lang.T("{0} chests · {1} kinds · {2} items", "{0} baús · {1} tipos · {2} itens"),
                _chestsSeen, _all.Count, sum)
                         + $"   <color=#6F6353>{ModConfig.ChestSearchRadius.Value:0} m</color>";

            UpdateOccupancy();

            UpdateChips();
            Clear();

            var list = Filtered();

            float side = ModConfig.ChestSearchSlotSize.Value;
            float cell = side + LabelHeight;
            float width = _scroll.rect.width;

            // Columns from the real width: the panel changes size with the resolution.
            int cols = ModConfig.ChestSearchColumns.Value > 0
                ? ModConfig.ChestSearchColumns.Value
                : Mathf.Max(1, Mathf.FloorToInt((width + CellGap) / (side + CellGap)));

            float y = 0f;

            if (list.Count == 0)
            {
                var empty = NewText(_content, 12f, Dim);
                empty.fontStyle = FontStyles.Italic;
                empty.alignment = TextAlignmentOptions.Top;
                empty.enableWordWrapping = true;
                empty.text = _all.Count == 0
                    ? string.Format(Lang.T("No chest within {0} m.", "Nenhum baú a {0} m."),
                                    ModConfig.ChestSearchRadius.Value.ToString("0"))
                    : Lang.T("Nothing with that name in nearby chests.",
                             "Nada com esse nome nos baús por perto.");
                Place(empty.gameObject, 0f, 22f, width, 40f);
                _trash.Add(empty.gameObject);
                y = 70f;
            }
            else if (!_byChest)
            {
                y = Grid(list, null, 0f, cols, side, cell, width);
            }
            else
            {
                var order = new List<Container>();
                foreach (var a in list)
                    foreach (var o in a.Locations)
                        if (o.Amount > 0 && !order.Contains(o.Chest)) order.Add(o.Chest);

                foreach (var chest in order)
                {
                    var fromChest = new List<Aggregate>();
                    int chestSum = 0;
                    foreach (var a in list)
                    {
                        var o = a.Locations.Find(z => z.Chest == chest);
                        if (o != null && o.Amount > 0) { fromChest.Add(a); chestSum += o.Amount; }
                    }
                    if (fromChest.Count == 0) continue;

                    var header = NewText(_content, 11.5f, Hex("#D0BB8A"));
                    var info = _chests.Find(x => x.Chest == chest);
                    string capacity = info != null
                        ? $" · {info.Used}/{info.Total} slots"
                          + (info.Free > 0
                                ? " · <color=#7E9C50>"
                                  + string.Format(Lang.T("{0} free", "{0} livres"), info.Free)
                                  + "</color>"
                                : " · <color=#B0603F>" + Lang.T("full", "cheio") + "</color>")
                        : "";
                    header.text = $"{Chests.VisibleName(chest)}   <color=#6F6353>"
                             + string.Format(Lang.T("{0} kinds", "{0} tipos"), fromChest.Count)
                             + $"{capacity}</color>";
                    Place(header.gameObject, 0f, y, width, 15f);
                    _trash.Add(header.gameObject);
                    y += 18f;

                    y = Grid(fromChest, chest, y, cols, side, cell, width) + 8f;
                }
            }

            _content.sizeDelta = new Vector2(0f, Mathf.Max(y, _scroll.rect.height));
            _content.anchoredPosition = Vector2.zero;
            DrawFooter();
        }

        private static float Grid(List<Aggregate> list, Container onlyFrom, float y0,
                                   int cols, float side, float cell, float width)
        {
            float step = (width - cols * side) / Mathf.Max(1, cols - 1);
            if (cols == 1) step = 0f;
            step = Mathf.Min(step, CellGap * 3f);

            for (int i = 0; i < list.Count; i++)
            {
                int col = i % cols;
                int row = i / cols;
                float x = col * (side + step);
                float y = y0 + row * (cell + CellGap);
                NewCell(list[i], onlyFrom, x, y, side);
            }

            int rows = Mathf.CeilToInt(list.Count / (float)cols);
            return y0 + rows * (cell + CellGap);
        }

        private static void Clear()
        {
            // Destroy only happens at the end of the frame; without detaching from the
            // parent now, the old children would still show over the new ones for a frame.
            foreach (var g in _trash)
                if (g != null) { g.transform.SetParent(null, false); Object.Destroy(g); }
            _trash.Clear();

            for (int i = _content.childCount - 1; i >= 0; i--)
            {
                var child = _content.GetChild(i).gameObject;
                child.transform.SetParent(null, false);
                Object.Destroy(child);
            }
        }

        private static void NewCell(Aggregate agg, Container onlyFrom, float x, float y, float side)
        {
            var gui = InventoryGui.instance;
            if (gui?.m_playerGrid?.m_elementPrefab == null) return;

            var root = new GameObject("cel", typeof(RectTransform));
            root.transform.SetParent(_content, false);
            Place(root, x, y, side, side + LabelHeight);
            _trash.Add(root);

            // The game's slot: native art, border and tooltip.
            var slotGo = Object.Instantiate(gui.m_playerGrid.m_elementPrefab, root.transform);
            slotGo.SetActive(true);
            Place(slotGo, 0f, 0f, side, side);

            var element = slotGo.GetComponent<InventoryElement>();
            var item = agg.Sample;

            int amount = agg.Total;
            if (onlyFrom != null)
            {
                var o = agg.Locations.Find(z => z.Chest == onlyFrom);
                amount = o != null ? o.Amount : 0;
            }

            element.m_icon.enabled = true;
            element.m_icon.sprite = item.GetIcon();
            element.m_icon.color = Color.white;

            // The game's UpdateGui would write "347/50" here -- that's why we use only the
            // slot, and not the whole InventoryGrid.
            element.m_amount.enabled = true;
            element.m_amount.text = amount.ToString();

            element.m_durability.gameObject.SetActive(false);
            element.m_equiped.enabled = false;
            element.m_queued.enabled = false;
            element.m_noteleport.enabled = false;
            element.m_food.enabled = false;
            element.m_quality.enabled = item.m_shared.m_maxQuality > 1;
            if (element.m_quality.enabled) element.m_quality.text = item.m_quality.ToString();
            if (element.m_selected != null) element.m_selected.SetActive(false);

            var bind = slotGo.transform.Find("binding");
            if (bind != null)
            {
                var bt = bind.GetComponent<TMP_Text>();
                if (bt != null) bt.enabled = false;
            }

            if (element.m_tooltip != null)
                element.m_tooltip.Set(item.m_shared.m_name, item.GetTooltip(), gui.m_playerGrid.m_tooltipAnchor);

            var label = NewText(root.transform, ModConfig.ChestSearchLabelSize.Value, TextColor);
            label.text = agg.LocalName;
            label.alignment = TextAlignmentOptions.Top;
            label.enableWordWrapping = false;
            label.overflowMode = TextOverflowModes.Ellipsis;
            Place(label.gameObject, -2f, side + 1f, side + 4f, LabelHeight);

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

                    _focused = agg;
                    _chosenAmount = 0;

                    if (shift) { OpenSplit(agg, onlyFrom); return; }
                    if (ctrl) { Take(agg, onlyFrom, agg.Total); return; }

                    // A plain click takes one stack, like taking from an open chest.
                    Take(agg, onlyFrom, Mathf.Max(1, agg.Sample.m_shared.m_maxStackSize));
                };
            }
        }

        // ==================================================================
        // Quantity line
        // ==================================================================
        private static GameObject _amountRow;
        private static TMP_InputField _amountField;
        private static readonly List<TMP_Text> _shortcutLabels = new List<TMP_Text>();
        private static int _chosenAmount;

        /// <summary>
        /// Builds the line "TAKE [1] [10] [stack] [All]  [-] [n] [+]  [Take]".
        ///
        /// It is built ONCE and afterwards only its label and visibility are updated.
        /// Recreating the button on each selection would destroy the object under the
        /// mouse mid-click, which in uGUI swallows the event.
        /// </summary>
        private static void BuildAmountRow(float x, float y, float width)
        {
            _amountRow = new GameObject("VT_LinhaQtd", typeof(RectTransform));
            _amountRow.transform.SetParent(_panel.transform, false);
            Place(_amountRow, x, y, width, 24f);
            _shortcutLabels.Clear();

            var rot = NewText(_amountRow.transform, 10.5f, Dim);
            rot.text = Lang.T("TAKE", "PEGAR");
            rot.characterSpacing = 6f;
            Place(rot.gameObject, 0f, 6f, 42f, 14f);

            float cx = 46f;

            // Four shortcuts: 1, 10, one stack, all. The text of each changes with the
            // selected item (ore's stack is 30, wood's is 50).
            for (int i = 0; i < 4; i++)
            {
                int idx = i;
                var b = MiniButton(_amountRow.transform, "", cx, 0f, 38f, 22f,
                                  () => AmountShortcut(idx));
                _shortcutLabels.Add(b);
                cx += 41f;
            }

            cx += 6f;
            MiniButton(_amountRow.transform, "−", cx, 0f, 24f, 22f, () => AdjustAmount(-1));
            cx += 26f;

            _amountField = NumberField(_amountRow.transform, cx, 0f, 54f, 22f);
            cx += 58f;

            MiniButton(_amountRow.transform, "+", cx, 0f, 24f, 22f, () => AdjustAmount(+1));
            cx += 30f;

            var takeLabel = MiniButton(_amountRow.transform, Lang.T("Take", "Pegar"), cx, 0f, width - cx, 22f,
                                  () => TakeChosen());
            takeLabel.color = Gold;

            _amountRow.SetActive(false);
        }

        private static void AmountShortcut(int i)
        {
            if (_focused == null) return;
            int stack = Mathf.Max(1, _focused.Sample.m_shared.m_maxStackSize);
            int[] v = { 1, 10, stack, _focused.Total };
            _chosenAmount = Mathf.Clamp(v[i], 1, _focused.Total);
            UpdateAmountRow();
        }

        private static void AdjustAmount(int d)
        {
            if (_focused == null) return;
            _chosenAmount = Mathf.Clamp(_chosenAmount + d, 1, _focused.Total);
            UpdateAmountRow();
        }

        private static void TakeChosen()
        {
            if (_focused == null) return;
            Take(_focused, null, Mathf.Clamp(_chosenAmount, 1, _focused.Total));
        }

        private static void UpdateAmountRow()
        {
            if (_amountRow == null) return;

            bool has = _focused != null && _focused.Total > 0;
            if (_amountRow.activeSelf != has) _amountRow.SetActive(has);
            if (!has) return;

            int stack = Mathf.Max(1, _focused.Sample.m_shared.m_maxStackSize);
            if (_chosenAmount < 1 || _chosenAmount > _focused.Total)
                _chosenAmount = Mathf.Min(stack, _focused.Total);

            string[] texts = { "1", "10", stack.ToString(), Lang.T("All", "Tudo") };
            int[] values = { 1, 10, stack, _focused.Total };

            for (int i = 0; i < _shortcutLabels.Count && i < 4; i++)
            {
                var t = _shortcutLabels[i];
                if (t == null) continue;
                t.text = texts[i];
                // A shortcut that makes no sense for this item is dimmed instead of
                // disappearing: a button that dances around is worse than an inert button.
                bool useful = values[i] <= _focused.Total;
                t.color = useful ? TextColor : new Color(Dim.r, Dim.g, Dim.b, 0.4f);
            }

            if (_amountField != null && !_amountField.isFocused)
                _amountField.SetTextWithoutNotify(_chosenAmount.ToString());
        }

        /// <summary>
        /// Occupancy bar for the surrounding chests. The number that matters is "how many
        /// slots can still be filled", so that's the one highlighted, not the percentage.
        /// </summary>
        private static void UpdateOccupancy()
        {
            if (_occupancyFill == null || _occupancyText == null) return;

            int free = _slotsTotal - _slotsUsed;
            float frac = _slotsTotal > 0 ? (float)_slotsUsed / _slotsTotal : 0f;

            var track = _occupancyFill.rectTransform.parent as RectTransform;
            float width = track != null ? track.rect.width - 2f : 0f;
            _occupancyFill.rectTransform.sizeDelta =
                new Vector2(Mathf.Max(0f, width * Mathf.Clamp01(frac)), 0f);

            // Green while there's room left; turns red when it's tight, so the
            // bar says something at a glance instead of being mere decoration.
            _occupancyFill.color = frac >= 0.85f ? Hex("#B0603F")
                            : frac >= 0.65f ? Hex("#B09A45")
                                            : Hex("#7E9C50");

            _occupancyText.text = _slotsTotal > 0
                ? $"<color=#E6D3A2>{free}</color> " + Lang.T("free slots", "slots livres")
                : "";
        }

        private static void DrawFooter()
        {
            if (_footerName == null) return;

            if (_focused == null || _focused.Total <= 0)
            {
                _footerName.text = "";
                _footerTotal.text = "";
                _footerLocations.text = "";
                _footerAction.text = "<i>"
                    + Lang.T("Click an item to choose how many to take.",
                             "Clique num item para escolher quanto pegar.")
                    + "</i>";
                UpdateAmountRow();
                return;
            }

            var locations = new List<Location>(_focused.Locations);
            locations.Sort((a, b) => b.Amount.CompareTo(a.Amount));

            _footerName.text = _focused.LocalName;
            _footerTotal.text = string.Format(
                Lang.T("{0} in {1} chest(s)", "{0} em {1} baú(s)"),
                _focused.Total, locations.Count);

            var sb = new StringBuilder();
            for (int i = 0; i < locations.Count && i < 5; i++)
            {
                if (i > 0) sb.Append("   ");
                sb.Append($"{locations[i].Name} <color=#DCCFB2>×{locations[i].Amount}</color>");
            }
            if (locations.Count > 5) sb.Append($"   +{locations.Count - 5}");
            _footerLocations.text = sb.ToString();

            _footerAction.text = "";
            UpdateAmountRow();
        }

        // ==================================================================
        // Pieces
        // ==================================================================
        private static float Chip(int i, string text, float x, float y,
                                  UnityEngine.Events.UnityAction action, float width = 0f)
        {
            if (width <= 0f) width = text.Length * 7f + 22f;

            var gui = InventoryGui.instance;
            GameObject go;
            Image img;
            TMP_Text txt;

            // The same game button as the others, just smaller: art, font and click sound
            // come together, and it stays consistent with the button that opens the panel.
            if (gui != null && gui.m_takeAllButton != null)
            {
                go = Object.Instantiate(gui.m_takeAllButton.gameObject, _panel.transform);
                go.name = "VT_chip";
                go.SetActive(true);

                var b = go.GetComponent<Button>();
                b.onClick.RemoveAllListeners();
                b.onClick.AddListener(action);
                b.interactable = true;

                img = go.GetComponent<Image>();
                txt = go.GetComponentInChildren<TMP_Text>(true);
                if (txt != null) { txt.text = text; txt.fontSize = 12f; txt.enabled = true; }
            }
            else
            {
                go = new GameObject("chip", typeof(RectTransform));
                go.transform.SetParent(_panel.transform, false);
                img = go.AddComponent<Image>();
                img.color = ChipBackground;
                txt = NewText(go.transform, 12f, TextColor);
                txt.alignment = TextAlignmentOptions.Center;
                txt.text = text;
                Stretch(txt.rectTransform);
                var b = go.AddComponent<Button>();
                b.targetGraphic = img;
                b.onClick.AddListener(action);
            }

            Place(go, x, y, width, ChipHeight);

            _chip[i] = img;
            _chipText[i] = txt;
            _chipBaseColor[i] = img != null ? img.color : Color.white;
            return width + 5f;
        }

        private static void UpdateChips()
        {
            Paint(0, !_sortByName);
            Paint(1, _sortByName);
            Paint(2, _byChest);
        }

        private static readonly Color[] _chipBaseColor = new Color[3];

        private static void Paint(int i, bool active)
        {
            if (_chip[i] == null) return;

            // Tint on top of the original color: that way it works both with the game's
            // button art and with the plan B rectangle.
            var b = _chipBaseColor[i];
            _chip[i].color = active ? b : new Color(b.r, b.g, b.b, b.a * 0.5f);
            if (_chipText[i] != null) _chipText[i].color = active ? Gold : Dim;
        }

        /// <summary>
        /// Small button with the game's art. Returns the label's TMP, which is what the
        /// callers need to update afterwards.
        /// </summary>
        private static TMP_Text MiniButton(Transform parent, string text, float x, float y,
                                          float width, float height, UnityEngine.Events.UnityAction action)
        {
            var gui = InventoryGui.instance;
            GameObject go;
            TMP_Text txt;

            if (gui != null && gui.m_takeAllButton != null)
            {
                go = Object.Instantiate(gui.m_takeAllButton.gameObject, parent);
                go.SetActive(true);
                var b = go.GetComponent<Button>();
                b.onClick.RemoveAllListeners();
                b.onClick.AddListener(action);
                b.interactable = true;
                txt = go.GetComponentInChildren<TMP_Text>(true);
                if (txt != null) { txt.enabled = true; txt.fontSize = 11.5f; }
            }
            else
            {
                go = new GameObject("mini", typeof(RectTransform));
                go.transform.SetParent(parent, false);
                var img = go.AddComponent<Image>();
                img.color = ChipBackground;
                txt = NewText(go.transform, 11.5f, TextColor);
                txt.alignment = TextAlignmentOptions.Center;
                Stretch(txt.rectTransform);
                var b = go.AddComponent<Button>();
                b.targetGraphic = img;
                b.onClick.AddListener(action);
            }

            go.name = "mini";
            if (txt != null) { txt.text = text; txt.alignment = TextAlignmentOptions.Center; }
            Place(go, x, y, width, height);
            return txt;
        }

        /// <summary>Number-only field, for typing the exact quantity.</summary>
        private static TMP_InputField NumberField(Transform parent, float x, float y, float width, float height)
        {
            var box = new GameObject("VT_Qtd", typeof(RectTransform));
            box.transform.SetParent(parent, false);
            Place(box, x, y, width, height);

            var border = box.AddComponent<Image>();
            border.color = Border;

            var inside = new GameObject("fundo", typeof(RectTransform));
            inside.transform.SetParent(box.transform, false);
            var drt = inside.GetComponent<RectTransform>();
            drt.anchorMin = Vector2.zero; drt.anchorMax = Vector2.one;
            drt.offsetMin = new Vector2(1f, 1f); drt.offsetMax = new Vector2(-1f, -1f);
            var dim = inside.AddComponent<Image>();
            dim.color = new Color(0.05f, 0.04f, 0.03f, 0.9f);
            dim.raycastTarget = false;

            var area = new GameObject("area", typeof(RectTransform));
            area.transform.SetParent(box.transform, false);
            var areaRt = area.GetComponent<RectTransform>();
            areaRt.anchorMin = Vector2.zero; areaRt.anchorMax = Vector2.one;
            areaRt.offsetMin = new Vector2(4f, 1f); areaRt.offsetMax = new Vector2(-4f, -1f);
            area.AddComponent<RectMask2D>();

            var txt = NewText(area.transform, 12f, Hex("#F0E4C6"));
            txt.alignment = TextAlignmentOptions.Center;
            Stretch(txt.rectTransform);

            var field = box.AddComponent<TMP_InputField>();
            field.textViewport = areaRt;
            field.textComponent = txt;
            field.lineType = TMP_InputField.LineType.SingleLine;
            field.characterValidation = TMP_InputField.CharacterValidation.Integer;
            field.characterLimit = 6;
            field.caretColor = Gold;
            field.customCaretColor = true;
            field.caretWidth = 2;
            field.selectionColor = new Color(0.72f, 0.63f, 0.45f, 0.35f);

            field.onValueChanged.AddListener(s =>
            {
                if (_focused == null) return;
                if (int.TryParse(s, out int v))
                    _chosenAmount = Mathf.Clamp(v, 1, _focused.Total);
            });
            // Enter takes directly, like in the game's stack-split dialog.
            field.onSubmit.AddListener(_ => TakeChosen());

            return field;
        }

        private static void Divider(float x, float y, float width)
        {
            var go = new GameObject("div", typeof(RectTransform));
            go.transform.SetParent(_panel.transform, false);
            Place(go, x, y, width, 1f);
            var img = go.AddComponent<Image>();
            img.color = LineColor;
            img.raycastTarget = false;
        }

        private static TMP_Text NewText(Transform parent, float size, Color color)
        {
            var go = new GameObject("txt", typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var t = go.AddComponent<TextMeshProUGUI>();
            StoreHudPatch.ApplyFont(t);
            t.fontSize = size;
            t.color = color;
            t.alignment = TextAlignmentOptions.MidlineLeft;
            t.enableWordWrapping = false;
            t.raycastTarget = false;
            return t;
        }

        private static void Stretch(RectTransform rt)
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
        internal static class CtrlStoreHook
        {
            private static bool Prefix(InventoryGui __instance, InventoryGrid grid,
                                       ItemDrop.ItemData item, InventoryGrid.Modifier mod)
            {
                if (!_open || item == null) return true;
                if (mod != InventoryGrid.Modifier.Move) return true;
                if (__instance.IsContainerOpen()) return true;

                // Only the player's grid: the open chest's grid is the game's business.
                if (grid == null || grid != __instance.m_playerGrid) return true;

                // If something is already in hand, the click is a "drop" -- not ours.
                if (ItemInHand(out _, out _) != null) return true;

                Store(item, item.m_stack);
                _signature = null;
                _nextScan = 0f;
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
        internal static class ButtonHook
        {
            private static bool Prefix(string name, ref bool __result)
            {
                if (!_open) return true;

                // "Use" (E) dies while the panel is open, with or without focus in the
                // field: with the panel on screen the E has no other meaning, and that way
                // the fix doesn't depend on getting focus detection right.
                if (name == "Use") { __result = false; return false; }

                if (IsMovement(name)) { __result = false; return false; }

                // The others only with the field focused, otherwise TAB would stop
                // closing the inventory normally.
                if (!_searchFocused) return true;

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
        internal static class ControlHook
        {
            private static void Postfix(ref bool __result)
            {
                if (_searchFocused) __result = false;
            }
        }

        /// <summary>Belt and suspenders: covers actions that go through Player.</summary>
        [HarmonyPatch(typeof(Player), "TakeInput")]
        internal static class TakeInputHook
        {
            private static void Postfix(ref bool __result)
            {
                if (_searchFocused) __result = false;
            }
        }

        private static bool IsMovement(string name)
        {
            switch (name)
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
        internal static class HeldButtonHook
        {
            private static bool Prefix(string name, ref bool __result)
            {
                if (!_open || !IsMovement(name)) return true;
                __result = false;
                return false;
            }
        }

        private static readonly FieldInfo CharacterField =
            AccessTools.Field(typeof(PlayerController), "m_character");

        private static int _logMove;

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
        internal static class MovementHook
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
                if (!_open || !ModConfig.ChestSearchBlockMove.Value) return true;

                var p = CharacterField?.GetValue(__instance) as Player;
                if (p != null)
                    p.SetControls(Vector3.zero, false, false, false, false,
                                  false, false, false, false, false, false);

                if (ModConfig.ChestSearchDebug.Value && ++_logMove % 200 == 1)
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
        ///   3. the inventory closes  ->  the panel closes  ->  _open becomes false
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
                if (!_open || !_searchFocused) return true;
                if (ZInput.GetKeyDown(KeyCode.Escape)) return true;

                if (ModConfig.ChestSearchDebug.Value && ++_logClose % 50 == 1)
                    Plugin.Log.LogInfo("[CHESTS] ignored an inventory close (you were typing)");
                return false;
            }

            private static void Postfix(bool __runOriginal)
            {
                if (__runOriginal) Close();
            }
        }

        private static int _logClose;
    }
}
