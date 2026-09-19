using System.Collections.Generic;
using UnityEngine;

namespace ValheimTweaks.Patches
{
    /// <summary>
    /// Baús ao redor do jogador.
    ///
    /// Vale para carroça e navio também: os três são Container no código.
    ///
    /// Um baú só existe como objeto na cena enquanto a zona dele está carregada.
    /// Fora disso não está na memória e não há o que contar -- por isso "baús ao
    /// redor" e não "todos os baús da base".
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

                // Sem ZDO valido ainda esta carregando: pular evita RPC perdido.
                var nview = cont.GetComponent<ZNetView>();
                if (nview == null || !nview.IsValid()) continue;

                Buffer.Add(cont);
            }
            return Buffer;
        }

        /// <summary>Nome proprio do bau, ou o nome do tipo. Nunca vazio.</summary>
        internal static string NomeVisivel(Container bau)
        {
            if (bau == null) return "";
            string proprio = StoreHudPatch.NomeDoBau(bau);
            if (!string.IsNullOrEmpty(proprio)) return proprio;
            return Localization.instance.Localize(bau.m_name);
        }
    }
}
