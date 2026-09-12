using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using UnityEngine;

namespace ValheimTweaks.Patches
{
    /// <summary>
    /// Orcamento de instanciacao por frame -- candidato numero 1 para ENGASGO
    /// (frametime irregular com fps medio alto).
    ///
    /// ZNetScene.CreateObjects define o teto:
    ///     int maxCreatedPerFrame = 10;
    ///     if (InLoadingScreen()) maxCreatedPerFrame = 100;
    ///
    /// mas CreateObjectsSorted logo em seguida faz:
    ///     int num = Mathf.Max(m_tempCurrentObjects2.Count / 100, maxCreatedPerFrame);
    ///
    /// Ou seja: o teto de 10 vale so enquanto houver poucos objetos pendentes.
    /// Passando de 1000 pendentes ele CRESCE SEM LIMITE -- 5000 pendentes viram
    /// 50 instanciacoes num frame so, 20000 viram 200.
    ///
    /// E quando ha muitos pendentes? Ao atravessar fronteira de zona, que carrega
    /// um anel inteiro de zonas novas. Com near = 5 esse anel tem 11x11 = 121
    /// zonas em vez das 25 do vanilla -- ou seja, a propria simulation distance
    /// alta alimenta a rajada. Andar pelo mundo = rajada atras de rajada.
    ///
    /// Este patch poe um teto de verdade nesse numero. O custo e pop-in: objeto
    /// aparece um pouco mais devagar depois de atravessar zona. A troca e
    /// carregar mais lento em vez de travar.
    ///
    /// Implementacao: Mathf.Max aparece UMA unica vez no metodo (verificado no
    /// decompilado), entao o transpiler so insere uma chamada ao nosso clamp logo
    /// depois dela. E a edicao de IL minima possivel -- nada de reescrever o metodo.
    /// </summary>
    internal static class ObjectBudgetPatch
    {
        /// <summary>Chamado pelo IL injetado, recebe o resultado do Mathf.Max.</summary>
        internal static int ClampBudget(int budget)
        {
            int cap = ModConfig.MaxObjectsPerFrame.Value;
            if (cap <= 0) return budget;           // 0 = nao limitar
            return budget > cap ? cap : budget;
        }

        [HarmonyPatch(typeof(ZNetScene), "CreateObjectsSorted")]
        internal static class CreateObjectsSortedHook
        {
            private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
            {
                MethodInfo mathfMax = AccessTools.Method(
                    typeof(Mathf), nameof(Mathf.Max), new[] { typeof(int), typeof(int) });
                MethodInfo clamp = AccessTools.Method(
                    typeof(ObjectBudgetPatch), nameof(ClampBudget));

                bool injetado = false;

                foreach (var ins in instructions)
                {
                    yield return ins;

                    if (!injetado
                        && (ins.opcode == OpCodes.Call || ins.opcode == OpCodes.Callvirt)
                        && ins.operand as MethodInfo == mathfMax)
                    {
                        yield return new CodeInstruction(OpCodes.Call, clamp);
                        injetado = true;
                    }
                }

                if (!injetado)
                {
                    // Sem isso o patch falharia em silencio numa versao futura do jogo.
                    Plugin.Log.LogError(
                        "ObjectBudgetPatch: Mathf.Max nao encontrado em CreateObjectsSorted. " +
                        "O jogo mudou -- o limite de instanciacao por frame NAO esta ativo.");
                }
            }
        }
    }
}
