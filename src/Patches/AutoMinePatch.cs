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

        private static bool _enabled;
        private static float _noticeUntil;
        private static GameObject _notice;
        private static TextMeshProUGUI _noticeText;

        internal static bool Enabled => _enabled;

        // ------------------------------------------------------------------
        // Does the target drop ore?
        // ------------------------------------------------------------------
        private static bool DropsOre(DropTable table)
        {
            if (table?.m_drops == null) return false;

            string list = ModConfig.AutoMineOres.Value ?? "";
            if (list.Length == 0) return false;

            foreach (var d in table.m_drops)
            {
                if (d.m_item == null) continue;
                string name = d.m_item.name.ToLowerInvariant();

                foreach (var fragment in list.Split(','))
                {
                    var f = fragment.Trim().ToLowerInvariant();
                    if (f.Length > 0 && name.Contains(f)) return true;
                }
            }
            return false;
        }

        /// <summary>Collider of the ore under the crosshair, or null.</summary>
        private static Collider AimedOre(Player player)
        {
            if (HoveringField == null) return null;

            var go = HoveringField.GetValue(player) as GameObject;
            if (go == null) return null;

            // Three different ways a minable target can carry its drop table.
            // Tin doesn't use MineRock: it's a Destructible with the separate
            // DropOnDestroyed component, which is why the previous version
            // ignored it.
            bool isOre = false;

            var rock5 = go.GetComponentInParent<MineRock5>();
            if (rock5 != null) isOre = DropsOre(rock5.m_dropItems);

            if (!isOre)
            {
                var rock = go.GetComponentInParent<MineRock>();
                if (rock != null) isOre = DropsOre(rock.m_dropItems);
            }

            if (!isOre)
            {
                var drop = go.GetComponentInParent<DropOnDestroyed>();
                if (drop != null) isOre = DropsOre(drop.m_dropWhenDestroyed);
            }

            if (!isOre)
            {
                Diagnose(go);
                return null;
            }

            // The targeted collider is the exact piece: MineRock5 is split into
            // several areas, and using the root object's would measure the wrong
            // distance.
            return go.GetComponent<Collider>() ?? go.GetComponentInParent<Collider>();
        }

        private static string _lastDiagnosis;

        /// <summary>
        /// Logs what was rejected and why. Without this, "it doesn't work on tin"
        /// becomes trial and error; with it, the log itself says which component
        /// the target uses and what it drops.
        /// </summary>
        private static void Diagnose(GameObject go)
        {
            if (!ModConfig.AutoMineDebug.Value) return;

            string name = go.name;
            if (name == _lastDiagnosis) return; // doesn't repeat every frame
            _lastDiagnosis = name;

            var parts = new System.Text.StringBuilder();
            parts.Append($"[AUTO-MINE] target rejected: {name}");

            var r5 = go.GetComponentInParent<MineRock5>();
            var r = go.GetComponentInParent<MineRock>();
            var dd = go.GetComponentInParent<DropOnDestroyed>();
            var de = go.GetComponentInParent<Destructible>();

            parts.Append($" | MineRock5={r5 != null} MineRock={r != null} " +
                          $"DropOnDestroyed={dd != null} Destructible={de != null}");

            var table = r5?.m_dropItems ?? r?.m_dropItems ?? dd?.m_dropWhenDestroyed;
            if (table?.m_drops != null)
            {
                parts.Append(" | drops:");
                foreach (var d in table.m_drops)
                    if (d.m_item != null) parts.Append(' ').Append(d.m_item.name);
            }
            else parts.Append(" | no drop table");

            Plugin.Log.LogInfo(parts.ToString());
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
        private static float DistanceToSurface(Collider col, Vector3 origin)
        {
            var mesh = col as MeshCollider;
            bool supported = col is BoxCollider || col is SphereCollider
                             || col is CapsuleCollider || (mesh != null && mesh.convex);

            Vector3 point = supported ? col.ClosestPoint(origin)
                                      : col.ClosestPointOnBounds(origin);
            return Vector3.Distance(origin, point);
        }

        private static float WeaponReach(Player player)
        {
            var weapon = player.GetCurrentWeapon();
            var attack = weapon?.m_shared?.m_attack;
            float baseRange = (attack != null && attack.m_attackRange > 0f) ? attack.m_attackRange : 2f;
            return baseRange * ModConfig.AutoMineRangeScale.Value;
        }

        private static bool HasPickaxe(Player player)
        {
            var weapon = player.GetCurrentWeapon();
            return weapon != null && weapon.m_shared.m_skillType == Skills.SkillType.Pickaxes;
        }

        // ------------------------------------------------------------------
        internal static void Update()
        {
            var player = Player.m_localPlayer;
            if (player == null) { ShowNotice(false); return; }

            bool typing = (Chat.instance != null && Chat.instance.HasFocus())
                          || Console.IsVisible() || TextInput.IsVisible();

            if (!typing && ModConfig.AutoMineKey.Value.IsDown())
            {
                _enabled = !_enabled;
                // On purpose does NOT use MessageHud: its duration belongs to the
                // game and applies to all messages. The indicator itself gives the
                // feedback, with timing we control.
                _noticeUntil = Time.realtimeSinceStartup + ModConfig.AutoMineToggleSeconds.Value;
            }

            // Stays visible while on; when turned off, it still shows for the
            // warning's duration so you can see it turned off.
            ShowNotice(_enabled || Time.realtimeSinceStartup < _noticeUntil);

            if (!_enabled || QueuedAttackField == null) return;
            if (InventoryGui.IsVisible() || Minimap.IsOpen()) return;
            if (!HasPickaxe(player)) return;

            var col = AimedOre(player);
            if (col == null) return;

            // Distance to the target's surface, not to its center.
            Vector3 origin = player.transform.position + Vector3.up * 1f;
            float d = DistanceToSurface(col, origin);
            if (d > WeaponReach(player)) return;

            // Same value the game writes when the attack button is pressed.
            QueuedAttackField.SetValue(player, 0.5f);
        }

        // ------------------------------------------------------------------
        // Permanent indicator while on
        // ------------------------------------------------------------------
        private static void ShowNotice(bool visible)
        {
            if (!visible)
            {
                if (_notice != null && _notice.activeSelf) _notice.SetActive(false);
                return;
            }

            if (_notice == null)
            {
                if (Hud.instance == null || Hud.instance.m_rootObject == null) return;

                _notice = new GameObject("VT_AutoMinerar", typeof(RectTransform));
                _notice.transform.SetParent(Hud.instance.m_rootObject.transform, worldPositionStays: false);

                var rt = _notice.GetComponent<RectTransform>();
                rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0.5f, 0f);
                rt.anchoredPosition = new Vector2(0f, ModConfig.AutoMineHudY.Value);
                rt.sizeDelta = new Vector2(400f, 26f);

                _noticeText = _notice.AddComponent<TextMeshProUGUI>();
                _noticeText.fontSize = 15f;
                _noticeText.alignment = TextAlignmentOptions.Center;
                _noticeText.raycastTarget = false;
            }

            // Reapply on every display: if the HUD wasn't ready yet when the
            // object was created, the font would have stayed on the fallback
            // forever. And without a font TMP looks for LiberationSans, which
            // Valheim doesn't include -- the label comes out with no font at all.
            // Better to hide it and try again next frame than to show it broken.
            if (!StoreHudPatch.ApplyFont(_noticeText))
            {
                if (_notice.activeSelf) _notice.SetActive(false);
                return;
            }

            // Right after toggling, it highlights the state; then it returns to the
            // discreet label. No emoji: Valheim's font lacks those glyphs and they
            // come out as little squares.
            bool justToggled = Time.realtimeSinceStartup < _noticeUntil;
            if (justToggled)
                _noticeText.text = _enabled
                    ? "<color=#B8DD97>" + Lang.T("Auto-mining ON", "Minerar automático LIGADO") + "</color>"
                    : "<color=#C08080>" + Lang.T("Auto-mining off", "Minerar automático desligado") + "</color>";
            else
                _noticeText.text = "<color=#D8C48A>" + Lang.T("Auto-mining", "Minerar automático") + "</color>";

            if (!_notice.activeSelf) _notice.SetActive(true);
        }
    }
}
