using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
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

        // Container.Load is private. It is the only thing that copies the ZDO into the
        // chest's inventory, and the game calls it from CheckForChanges, once a second.
        private static readonly MethodInfo LoadMethod = AccessTools.Method(typeof(Container), "Load");

        /// <summary>
        /// Makes the chest read its ZDO now.
        ///
        /// Without this, an inventory we are about to write into can be up to a second
        /// behind the copy that just arrived from its previous owner, and the write would
        /// be saved on top of contents that are already outdated.
        /// </summary>
        internal static void Reload(Container chest)
        {
            if (chest == null || LoadMethod == null) return;

            try { LoadMethod.Invoke(chest, null); }
            catch (System.Exception e)
            {
                Plugin.Log.LogWarning($"[CHESTS] could not read '{VisibleName(chest)}' from the "
                                    + $"network before writing: {e.Message}");
            }
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

        /// <summary>
        /// If we are the host, take ownership of a chest owned by someone else before writing
        /// to it. The host is authoritative, so the change is safe and instant -- no waiting
        /// on the other client's handshake, which is what left the chest "not yours" until it
        /// was opened by hand. Clients cannot do this; they still use the handshake.
        /// </summary>
        internal static bool TryClaim(Container chest)
        {
            if (chest == null) return false;

            var net = ZNet.instance;
            if (net == null || !net.IsServer()) return false;

            var nview = chest.GetComponent<ZNetView>();
            if (nview == null || !nview.IsValid()) return false;
            if (nview.IsOwner()) return true;
            if (chest.IsInUse()) return false;
            if (chest.m_checkGuardStone
                && !PrivateArea.CheckAccess(chest.transform.position, 0f, false)) return false;

            nview.ClaimOwnership();
            return nview.IsOwner();
        }

        /// <summary>
        /// True when a chest we do not own can be handed over: its owner is the local player,
        /// has no owner yet, or is a peer that is currently connected. A chest whose owner is
        /// offline can never answer the handshake, and the request would sit in flight for
        /// the full timeout -- which is what made storing into the other player's chest feel
        /// like it froze.
        /// </summary>
        internal static bool OwnerOnline(Container chest)
        {
            var nview = chest.GetComponent<ZNetView>();
            var zdo = nview != null ? nview.GetZDO() : null;
            if (zdo == null) return false;

            long owner = zdo.GetOwner();
            if (owner == 0 || owner == ZNet.GetUID()) return true;

            var net = ZNet.instance;
            var peers = net != null ? net.GetConnectedPeers() : null;
            if (peers == null) return false;

            foreach (var peer in peers)
                if (peer != null && peer.m_uid == owner) return true;

            return false;
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
