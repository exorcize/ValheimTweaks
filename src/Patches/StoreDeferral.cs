using System;
using System.Collections.Generic;
using UnityEngine;

namespace ValheimTweaks.Patches
{
    /// <summary>
    /// Holds back a store into a chest owned by another player's client until that
    /// client's copy of the chest has actually arrived.
    ///
    /// ---- The hole this closes ----
    /// Container.RPC_RequestStack hands the ownership over and answers in the same breath:
    ///
    ///     ZDOMan.instance.ForceSendZDO(uid, m_nview.GetZDO().m_uid);   // the contents
    ///     m_nview.GetZDO().SetOwner(uid);                              // the ownership
    ///     m_nview.InvokeRPC(uid, "RPC_StackResponse", true);           // the answer
    ///
    /// The answer is a routed RPC and lands at once. The contents travel on ZDOMan's send
    /// loop, throttled by bandwidth and, on a dedicated server, relayed twice. Writing when
    /// the answer lands means writing into an inventory that is still the old one -- and
    /// Container only reads the new one from CheckForChanges, on a one second timer --
    /// then saving it with the old DataRevision + 1. ZDOMan.RPC_ZDOData throws that whole
    /// write away on every other machine:
    ///
    ///     if (num4 &lt;= zDO.DataRevision) { ...ownership only...; continue; }
    ///
    /// The items appear in the chest locally and are erased on the next ownership handover,
    /// which restores the previous owner's copy byte for byte. Measured in a real session:
    /// 42 needles, 49 core wood, 34 carrots and more, each one landing in the chest and then
    /// rolling back to the exact state it had before the store. The game's own quick-stack
    /// has the same hole; it just fires it once instead of once per stack.
    ///
    /// ---- What we do instead ----
    /// Wait for the ZDO's DataRevision to change -- that is the owner's copy landing -- read
    /// the container from it right away, and only then write. The save now sits on top of
    /// the newest revision and is accepted everywhere. If nothing arrives within
    /// StoreHandshakeWaitSec the move happens anyway: no new revision means there was
    /// nothing to catch up on, which is the case that was always safe.
    /// </summary>
    internal static class StoreDeferral
    {
        private class Pending
        {
            internal Container Chest;
            internal uint Revision;
            internal float Deadline;
            internal Action<string> Run;
        }

        private static readonly List<Pending> _pending = new List<Pending>();

        /// <summary>The chest's ZDO data revision, or 0 when it has no valid ZDO.</summary>
        internal static uint Revision(Container chest)
        {
            var nview = chest != null ? chest.GetComponent<ZNetView>() : null;
            var zdo = nview != null && nview.IsValid() ? nview.GetZDO() : null;
            return zdo != null ? zdo.DataRevision : 0u;
        }

        /// <summary>
        /// Runs <paramref name="run"/> once the owner's copy of the chest has arrived, or at
        /// the latest after StoreHandshakeWaitSec. The reason is passed on so the log says
        /// which of the two it was. <paramref name="revisionAtRequest"/> must be the revision
        /// read BEFORE the request went out: anything newer than that is the copy we want.
        /// </summary>
        internal static void Defer(Container chest, uint revisionAtRequest, Action<string> run)
        {
            if (run == null) return;
            if (chest == null) { run("chest gone"); return; }

            _pending.Add(new Pending
            {
                Chest = chest,
                Revision = revisionAtRequest,
                Deadline = Time.realtimeSinceStartup
                         + Mathf.Max(0f, ModConfig.StoreHandshakeWaitSec.Value),
                Run = run,
            });
        }

        /// <summary>Called every frame from the plugin's Update.</summary>
        internal static void Tick()
        {
            if (_pending.Count == 0) return;

            float now = Time.realtimeSinceStartup;

            // Backwards, because a run may queue a new wait and it must not be visited
            // in this same pass.
            for (int i = _pending.Count - 1; i >= 0; i--)
            {
                var p = _pending[i];

                if (p.Chest == null)
                {
                    _pending.RemoveAt(i);
                    p.Run("chest gone");
                    continue;
                }

                bool arrived = Revision(p.Chest) != p.Revision;
                if (!arrived && now < p.Deadline) continue;

                _pending.RemoveAt(i);

                // Read it now instead of waiting for the container's own one second timer.
                // On the deadline path this costs nothing -- Load returns immediately when
                // the revision it already read is the current one -- and on the other it is
                // the whole point: between the arrival and that tick we would still be
                // working off the old inventory.
                Chests.Reload(p.Chest);
                p.Run(arrived ? "owner's copy arrived" : "nothing newer arrived");
            }
        }
    }
}
