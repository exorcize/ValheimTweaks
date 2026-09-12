using UnityEngine;

namespace ValheimTweaks.Patches
{
    /// <summary>
    /// Campo de visao.
    ///
    /// GameCamera.m_fov e 65 fixo no prefab, e o menu nao expoe. UpdateCamera faz
    /// m_camera.fieldOfView = m_fov todo frame, entao basta escrever no campo da
    /// instancia que o jogo propaga sozinho (inclusive para a m_skyCamera).
    ///
    /// Cuidado: FOV alto puxa mais geometria para dentro do frustum, entao custa
    /// draw calls. E acima de ~100 a distorcao nas bordas fica agressiva.
    /// </summary>
    internal static class FovPatch
    {
        internal static void Apply()
        {
            float fov = ModConfig.FieldOfView.Value;
            if (fov <= 0f) return; // 0 = nao sobrescrever

            var cam = GameCamera.instance;
            if (cam == null) return; // fora de mundo; reaplica no proximo load

            if (!Mathf.Approximately(cam.m_fov, fov))
            {
                Plugin.Log.LogInfo($"FOV: {cam.m_fov} -> {fov}");
                cam.m_fov = fov;
            }
        }
    }
}
