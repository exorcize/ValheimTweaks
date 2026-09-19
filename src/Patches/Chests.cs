using System.Collections.Generic;
using UnityEngine;

namespace ValheimTweaks.Patches
{
    /// <summary>
    /// Chests around the player.
    ///
    /// Applies to carts and ships too: all three are Container in the code.
    ///
    /// A chest only exists as an object in the scene while its zone is loaded.
    /// Outside that it isn't in memory and there's nothing to count -- hence
    /// "chests around" and not "all the chests in the base".
    /// </summary>
    internal static class Chests
    {
        private static readonly List<Container> Buffer = new List<Container>();

        internal static List<Container> Nearby(float radius)
        {
            Buffer.Clear();

            var player = Player.m_localPlayer;
            if (player == null) return Buffer;

            int mask = LayerMask.GetMask("piece", "piece_nonsolid");
            var colliders = Physics.OverlapSphere(player.transform.position, radius, mask);

            foreach (var c in colliders)
            {
                var container = c.GetComponentInParent<Container>();
                if (container == null || Buffer.Contains(container)) continue;

                // Without a valid ZDO it's still loading: skipping avoids a lost RPC.
                var nview = container.GetComponent<ZNetView>();
                if (nview == null || !nview.IsValid()) continue;

                Buffer.Add(container);
            }
            return Buffer;
        }

        /// <summary>
        /// True when the local player can safely change this chest's contents: it is ours
        /// (not owned by the other player's client), nobody is using it, and the guard
        /// stone allows access.
        ///
        /// Storing into a chest we do not own goes through an RPC handshake. If the two
        /// sides disagree about ownership, one version overwrites the other and items
        /// vanish -- which is exactly the "my item disappeared" report. Nothing is lost
        /// by skipping those chests: there is always another one around.
        /// </summary>
        internal static bool Usable(Container chest)
        {
            if (chest == null) return false;

            var nview = chest.GetComponent<ZNetView>();
            if (nview == null || !nview.IsValid() || !nview.IsOwner()) return false;
            if (chest.IsInUse()) return false;
            if (chest.m_checkGuardStone
                && !PrivateArea.CheckAccess(chest.transform.position, 0f, false)) return false;

            return true;
        }

        /// <summary>The chest's proper name, or the type's name. Never empty.</summary>
        internal static string VisibleName(Container chest)
        {
            if (chest == null) return "";
            string custom = StoreHudPatch.ChestName(chest);
            if (!string.IsNullOrEmpty(custom)) return custom;
            return Localization.instance.Localize(chest.m_name);
        }
    }
}
