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
    internal static class Baus
    {
        private static readonly List<Container> Buffer = new List<Container>();

        internal static List<Container> Proximos(float raio)
        {
            Buffer.Clear();

            var player = Player.m_localPlayer;
            if (player == null) return Buffer;

            int mascara = LayerMask.GetMask("piece", "piece_nonsolid");
            var colisores = Physics.OverlapSphere(player.transform.position, raio, mascara);

            foreach (var c in colisores)
            {
                var cont = c.GetComponentInParent<Container>();
                if (cont == null || Buffer.Contains(cont)) continue;

                // Without a valid ZDO it's still loading: skipping avoids a lost RPC.
                var nview = cont.GetComponent<ZNetView>();
                if (nview == null || !nview.IsValid()) continue;

                Buffer.Add(cont);
            }
            return Buffer;
        }

        /// <summary>The chest's proper name, or the type's name. Never empty.</summary>
        internal static string NomeVisivel(Container bau)
        {
            if (bau == null) return "";
            string proprio = StoreHudPatch.NomeDoBau(bau);
            if (!string.IsNullOrEmpty(proprio)) return proprio;
            return Localization.instance.Localize(bau.m_name);
        }
    }
}
