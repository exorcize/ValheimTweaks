using System.Reflection;
using HarmonyLib;
using UnityEngine;

namespace ValheimTweaks.Patches
{
    /// <summary>
    /// Minerar sozinho enquanto a mira estiver no minerio.
    ///
    /// Liga e desliga por tecla. Com a picareta na mao e o minerio sob a mira,
    /// enfileira golpes ate o alvo quebrar; quando quebra, para -- nao fica
    /// batendo no chao.
    ///
    /// ---- Por que enfileira em vez de chamar o ataque direto ----
    /// O Player ja tem uma fila:
    ///
    ///     m_queuedAttackTimer = 0.5f;              // ao apertar o botao
    ///     ...
    ///     if ((m_queuedAttackTimer > 0f || m_attackHold)
    ///         &amp;&amp; StartAttack(null, secondaryAttack: false)) ...
    ///
    /// Escrevendo nessa variavel, o proprio jogo decide QUANDO bater -- respeitando
    /// stamina, animacao em curso, cooldown e recuo. Chamar StartAttack na mao
    /// atropelaria tudo isso e sairia golpe fora de hora.
    ///
    /// Tres condicoes obrigatorias, checadas todo frame: picareta equipada, alvo
    /// minerável sob a mira e dentro do alcance. Qualquer uma falhando, a fila
    /// nao e alimentada e o personagem para sozinho no golpe seguinte.
    /// </summary>
    internal static class AutoMinePatch
    {
        private static readonly FieldInfo HoveringField =
            AccessTools.Field(typeof(Player), "m_hovering");

        private static readonly FieldInfo QueuedAttackField =
            AccessTools.Field(typeof(Player), "m_queuedAttackTimer");

        private static bool _ligado;

        /// <summary>Alvo valido sob a mira, ou null.</summary>
        private static GameObject AlvoMineravel(Player player)
        {
            if (HoveringField == null) return null;

            var go = HoveringField.GetValue(player) as GameObject;
            if (go == null) return null;

            // MineRock5 e o deposito de minerio; MineRock e pedra grande;
            // Destructible cobre o resto do que a picareta quebra.
            if (go.GetComponentInParent<MineRock5>() != null) return go;
            if (go.GetComponentInParent<MineRock>() != null) return go;

            var d = go.GetComponentInParent<Destructible>();
            if (d != null) return go;

            return null;
        }

        private static bool ComPicareta(Player player)
        {
            var arma = player.GetCurrentWeapon();
            return arma != null
                && arma.m_shared.m_skillType == Skills.SkillType.Pickaxes;
        }

        internal static void Update()
        {
            var player = Player.m_localPlayer;
            if (player == null) return;

            // Alternar: so fora de menu e de chat, senao a tecla dispararia digitando.
            if (!InventoryGui.IsVisible()
                && (Chat.instance == null || !Chat.instance.HasFocus())
                && !Console.IsVisible()
                && ModConfig.AutoMineKey.Value.IsDown())
            {
                _ligado = !_ligado;
                player.Message(MessageHud.MessageType.Center,
                    _ligado ? "Minerar automatico: LIGADO" : "Minerar automatico: desligado");
            }

            if (!_ligado || QueuedAttackField == null) return;

            // Qualquer condicao que falhe apenas para de alimentar a fila: o golpe
            // em curso termina normalmente e o personagem para.
            if (InventoryGui.IsVisible() || Minimap.IsOpen()) return;
            if (!ComPicareta(player)) return;

            var alvo = AlvoMineravel(player);
            if (alvo == null) return;

            float d = Vector3.Distance(player.transform.position, alvo.transform.position);
            if (d > ModConfig.AutoMineDistance.Value) return;

            // Mesmo valor que o jogo usa ao apertar o botao de ataque.
            QueuedAttackField.SetValue(player, 0.5f);
        }

        /// <summary>Desliga ao sair do mundo, para nao voltar minerando.</summary>
        internal static void Desligar() => _ligado = false;

        internal static bool Ligado => _ligado;
    }
}
