using UnityEngine;

namespace ValheimTweaks.Patches
{
    /// <summary>
    /// Field of view.
    ///
    /// GameCamera.m_fov is fixed at 65 in the prefab, and the menu does not expose it.
    /// UpdateCamera sets m_camera.fieldOfView = m_fov every frame, so it is enough to
    /// write to the instance field and the game propagates it on its own (including to m_skyCamera).
    ///
    /// Careful: a high FOV pulls more geometry into the frustum, so it costs
    /// draw calls. And above ~100 the distortion at the edges becomes aggressive.
    /// </summary>
    internal static class FovPatch
    {
        internal static void Apply()
        {
            float fov = ModConfig.FieldOfView.Value;
            if (fov <= 0f) return; // 0 = do not override

            var cam = GameCamera.instance;
            if (cam == null) return; // outside the world; reapplies on the next load

            if (!Mathf.Approximately(cam.m_fov, fov))
            {
                Plugin.Log.LogInfo($"FOV: {cam.m_fov} -> {fov}");
                cam.m_fov = fov;
            }
        }
    }
}
