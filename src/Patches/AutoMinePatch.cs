using System.Reflection;
using HarmonyLib;
using TMPro;
using UnityEngine;

namespace ValheimTweaks.Patches
{
    /// <summary>
    /// Mine by yourself while the crosshair is on the ore.
    ///
    /// ---- Only ore, not stone ----
    /// MineRock5 and MineRock are used both for copper deposits and for common
    /// stone, so the type doesn't distinguish them. What distinguishes them is
    /// what the target DROPS: both expose m_dropItems (DropTable) with
    /// m_drops[].m_item, and it's enough to check whether any drop matches the
    /// ore list. Stone drops Stone, which isn't in the list -- and so the rule
    /// works for modded ore too, as long as the name is added to the config.
    ///
    /// ---- Real reach ----
    /// Aiming isn't enough: the distance is measured to the CLOSEST POINT of the
    /// collider (Collider.ClosestPoint), not to the transform. A deposit is large
    /// and its center can be meters beyond the surface -- measuring from the
    /// center left the character hitting whatever was in front. And the limit is
    /// the real reach of the equipped weapon (m_attack.m_attackRange), not a
    /// fixed number.
    ///
    /// ---- How it stops ----
    /// The conditions are checked every frame and the attack queue is only fed
    /// while all of them pass. Broke the ore, moved the aim away, sheathed the
    /// pickaxe or walked off: the current swing finishes and the character stops.
    /// There is no separate "broke it" detection.
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
        // Does the target drop ore?
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

        /// <summary>Collider of the ore under the crosshair, or null.</summary>
        private static Collider MinerioMirado(Player player)
        {
            if (HoveringField == null) return null;

            var go = HoveringField.GetValue(player) as GameObject;
            if (go == null) return null;

            // Three different ways a minable target can carry its drop table.
            // Tin doesn't use MineRock: it's a Destructible with the separate
            // DropOnDestroyed component, which is why the previous version
            // ignored it.
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

            // The targeted collider is the exact piece: MineRock5 is split into
            // several areas, and using the root object's would measure the wrong
            // distance.
            return go.GetComponent<Collider>() ?? go.GetComponentInParent<Collider>();
        }

        private static string _ultimoDiagnostico;

        /// <summary>
        /// Logs what was rejected and why. Without this, "it doesn't work on tin"
        /// becomes trial and error; with it, the log itself says which component
        /// the target uses and what it drops.
        /// </summary>
        private static void Diagnosticar(GameObject go)
        {
            if (!ModConfig.AutoMineDebug.Value) return;

            string nome = go.name;
            if (nome == _ultimoDiagnostico) return; // doesn't repeat every frame
            _ultimoDiagnostico = nome;

            var partes = new System.Text.StringBuilder();
            partes.Append($"[AUTO-MINE] target rejected: {nome}");

            var r5 = go.GetComponentInParent<MineRock5>();
            var r = go.GetComponentInParent<MineRock>();
            var dd = go.GetComponentInParent<DropOnDestroyed>();
            var de = go.GetComponentInParent<Destructible>();

            partes.Append($" | MineRock5={r5 != null} MineRock={r != null} " +
                          $"DropOnDestroyed={dd != null} Destructible={de != null}");

            var tabela = r5?.m_dropItems ?? r?.m_dropItems ?? dd?.m_dropWhenDestroyed;
            if (tabela?.m_drops != null)
            {
                partes.Append(" | drops:");
                foreach (var d in tabela.m_drops)
                    if (d.m_item != null) partes.Append(' ').Append(d.m_item.name);
            }
            else partes.Append(" | no drop table");

            Plugin.Log.LogInfo(partes.ToString());
        }

        /// <summary>
        /// Distance to the SURFACE of the target.
        ///
        /// Collider.ClosestPoint only accepts box, sphere, capsule and CONVEX
        /// mesh -- and ore deposits are non-convex meshes. In those cases Unity
        /// not only spits out a warning per frame into the log (thousands in a
        /// few minutes of play) but also returns the queried point itself: the
        /// distance came out zero and the reach limit never rejected anything.
        ///
        /// For unsupported colliders we use the bounding box. Less precise than
        /// the mesh, but it works on any type, doesn't pollute the log and --
        /// what matters -- doesn't lie about the distance.
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
                // On purpose does NOT use MessageHud: its duration belongs to the
                // game and applies to all messages. The indicator itself gives the
                // feedback, with timing we control.
                _avisoAte = Time.realtimeSinceStartup + ModConfig.AutoMineToggleSeconds.Value;
            }

            // Stays visible while on; when turned off, it still shows for the
            // warning's duration so you can see it turned off.
            MostrarAviso(_ligado || Time.realtimeSinceStartup < _avisoAte);

            if (!_ligado || QueuedAttackField == null) return;
            if (InventoryGui.IsVisible() || Minimap.IsOpen()) return;
            if (!ComPicareta(player)) return;

            var col = MinerioMirado(player);
            if (col == null) return;

            // Distance to the target's surface, not to its center.
            Vector3 origem = player.transform.position + Vector3.up * 1f;
            float d = DistanciaAteSuperficie(col, origem);
            if (d > AlcanceDaArma(player)) return;

            // Same value the game writes when the attack button is pressed.
            QueuedAttackField.SetValue(player, 0.5f);
        }

        // ------------------------------------------------------------------
        // Permanent indicator while on
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

            // Reapply on every display: if the HUD wasn't ready yet when the
            // object was created, the font would have stayed on the fallback
            // forever. And without a font TMP looks for LiberationSans, which
            // Valheim doesn't include -- the label comes out with no font at all.
            // Better to hide it and try again next frame than to show it broken.
            if (!StoreHudPatch.AplicarFonte(_avisoTxt))
            {
                if (_aviso.activeSelf) _aviso.SetActive(false);
                return;
            }

            // Right after toggling, it highlights the state; then it returns to the
            // discreet label. No emoji: Valheim's font lacks those glyphs and they
            // come out as little squares.
            bool recemAlternado = Time.realtimeSinceStartup < _avisoAte;
            if (recemAlternado)
                _avisoTxt.text = _ligado
                    ? "<color=#B8DD97>" + Lang.T("Auto-mining ON", "Minerar automático LIGADO") + "</color>"
                    : "<color=#C08080>" + Lang.T("Auto-mining off", "Minerar automático desligado") + "</color>";
            else
                _avisoTxt.text = "<color=#D8C48A>" + Lang.T("Auto-mining", "Minerar automático") + "</color>";

            if (!_aviso.activeSelf) _aviso.SetActive(true);
        }
    }
}
