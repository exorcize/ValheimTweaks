# Achados — decompilação e medição

Tudo aqui saiu de ler `assembly_valheim.dll` decompilada ou de medir em execução.
Nada é de memória ou de suposição. Onde algo **não** foi verificado, está dito.

---

## Bugs e limitações do próprio jogo

### O filtro anisotrópico nunca é aplicado
A opção existe no menu, é lida das prefs, guardada no state e salva de volta — mas
não existe um único `QualitySettings.anisotropicFiltering` em toda a
`assembly_valheim.dll`. O checkbox não faz nada. → `TextureFilterPatch`

### O SSAO roda em meia resolução mesmo no máximo
```csharp
// CameraEffects.SetSSAO
case 1:  m_amplifyOcclusion.Downsample = true;  SampleCount = Low;
default: m_amplifyOcclusion.Downsample = true;  SampleCount = Medium;
```
`Downsample = true` está hardcoded nos **dois** casos, e o teto é `Medium` num
efeito que suporta `VeryHigh`. → `SsaoPatch`

### O anti-aliasing nunca é configurado
O jogo só faz `profile.antialiasing.enabled = enabled` e nunca toca em
`.settings` — fica no FXAA preset `Default` para sempre. MSAA não é alternativa:
medido em execução, a câmera é `DeferredShading` com `allowMSAA = False`, e o
Unity ignora MSAA em deferred. Resta FXAA de qualidade alta ou TAA.
→ `AntiAliasingPatch`

---

## Mapeamento real dos presets

`GraphicsSettingsManager` — os números do registro são índices, não valores:

| Setting | 0 | 1 | 2 | 3 |
|---|---|---|---|---|
| `GetLodBias` | 1.0 | 1.5 | 2.0 | **5.0** |
| `GetPointLightLimit` | 4 | 15 | 40 | −1 (ilimitado) |
| `GetPointLightShadowLimit` | 0 | 1 | **3** | −1 |
| `GetLightLimit` (pixel lights) | 2 | 4 | 8 | — |
| ShadowQuality | 2 casc/80 m | 3/120 m | 4/150 m | — |

⚠️ **Valor no código ≠ valor em execução.** `ClutterSystem` tem `m_distance = 40`
e `m_grassPatchSize = 8` no fonte, mas medido em execução dá **45 e 10** — o prefab
sobrescreve. Sempre confirmar pelo diagnóstico.

---

## As três distâncias (são dials diferentes)

| O que você vê | Dial | Mecanismo |
|---|---|---|
| inimigos, animais | `SimulationDistance.Near` | zonas simuladas |
| item no chão, recurso, pedra | `QualitySettings.lodBias` | tamanho em tela (LODGroup) |
| cenário ao longe | `SimulationDistance.Far` | ghost zones |

`ItemDrop.cs` **não tem nenhum código de culling** — nem distância, nem visibilidade.
Não existe `layerCullDistances` no assembly. `LodFadeInOut` é só um truque de spawn.
O sumiço de item é LODGroup puro, e `lodBias` o multiplica **linearmente**.

Distância de criatura = `near * 64 + 32` m (`ZoneSystem.m_zoneSize = 64`):
near 2 → 160 m · 3 → 224 m · 5 → 352 m · 6 → 416 m

O número que o menu mostra ("circular 480 m") é `near+far`, que inclui ghost zones
— e **ghost zone não tem criatura**, só cenário estático (`ZNetView.m_distant`).

---

## Custo de host

### Posse de ZDO — periódico, num frame só
```csharp
ReleaseZDOS: a cada 2s ->
    ReleaseNearbyZDOS(voce) + ReleaseNearbyZDOS(cada peer)
ReleaseNearbyZDOS -> FindSectorObjects(as 121 zonas) + itera cada ZDO
```
Com 4 amigos: 5 varreduras completas a cada 2 s, concentradas num frame.
O(zonas × (peers+1)), e zonas = (2·near+1)². → `ZdoReleasePatch` (**não medido ainda**)

### Geração de zona — instancia tudo e joga fora
`SpawnZone` em modo Ghost roda `PlaceLocations` + `PlaceVegetation` + `PlaceZoneCtrl`
e **destrói tudo em seguida**, só para registrar os ZDOs. Custo alto, mas **uma vez
por zona** (`SetZoneGenerated`). E `ZoneSystem.Update` chama `CreateGhostZones` para
você e para **cada peer** — como host, você paga a exploração dos seus amigos.

→ Consequência: mundo novo engasga muito mais que mundo já explorado, e isso melhora
sozinho conforme se explora.

### O que NÃO é gargalo
`ZDOMan.SendZDOToPeers2` já é amortizado — um peer por frame. Bom design.

---

## Medições (12/09)

### Andando — inconclusivo, e por quê
| | config | média | P99 | engasgos |
|---|---|---|---|---|
| A | vanilla | 4,47 | 7,71 | 26 |
| B | limites do mod | 5,16 | 11,11 | 66 |
| C | near=3 | 5,15 | 11,84 | 90 |
| **A2** | **vanilla de novo** | 5,38 | 12,19 | **81** |
| D | GcSliceMs=1 | 5,37 | 11,44 | 56 |

A2 tem config **idêntica** a A e deu 81 contra 26. A rodada A era outlier (medida
logo após o load). B/C/A2/D são a mesma coisa dentro do ruído.

**Nenhum lever testado teve efeito mensurável.** A afirmação anterior de que os
patches pioravam 2,5× estava errada — era ruído tratado como sinal.

Confounds identificados: trajeto diferente a cada rodada, mundo crescendo entre
rodadas, e o profiler disparando junto com a mudança de config (media-se o churn).

### Parado — instrumento de precisão
```
BASE_parado (4 runs, 15s):  media 4,49 ms (min 4,41 / max 4,54)  ->  ±1,5%
                            engasgos 6,8 (min 6 / max 9)
```
Parado elimina trajeto e crescimento de mundo. **É a condição de medição correta**
para A/B de custo. Andando serve só para reproduzir o sintoma, não para medir.

~88% do engasgo é ligado a movimento (6,8 parado × 15s contra ~80 andando × 20s).

### OBS — descartado por medição
705 MB e 615 s de CPU em 59 min ≈ **0,17 núcleo**. Não é o culpado.
`valheim.exe` sozinho usa ~3,7 núcleos. Dentro do processo só há
`gameoverlayrenderer64.dll` (Steam) — nenhum RTSS.

---

## Portal lento: era a simulação, não o cronômetro

`Player.UpdateTeleport` tem três limiares fixos (2 s inicial, 8 s de piso para
viagem longa, 15 s de desistência) — mas nenhum deles era o gargalo. Instrumentado:
**100% do tempo** estava preso em `ZNetScene.IsAreaReady`.

O motivo está em `ZoneSystem`:

```csharp
public bool IsZoneLoaded(Vector2s zoneID) {
    if (m_zones.ContainsKey(zoneID))
        return !m_loadingObjectsInZones.ContainsKey(zoneID);
    return false;
}
```

A zona do destino só fica pronta quando **todo objeto dela** termina de carregar o
asset (`SoftReferenceableAssets`, assíncrono). `CreateLocalZones` pede a zona
central primeiro e já retorna, mas nos frames seguintes entram as outras — e a
central passa a disputar a mesma fila.

Medido no mesmo portal, mesma máquina:

| near | zonas | tempo |
|---|---|---|
| 5 | 121 | 14,70 s · 16,82 s |
| 2 | 25 | 3,70 s · 1,48 s |
| 1 | 9 | **2,03 s · 0,69 s** |

Solução: reduzir só enquanto `IsTeleporting()` e restaurar ao chegar.

⚠️ **Não** usar `ZNet.ApplySimulationDistance` nem `SimulationDistanceServerHandshake`
para isso: eles mexem no teto que o host envia aos peers, e derrubariam a distância
de simulação de todos os jogadores a cada portal. O caminho correto é alterar só o
valor *desejado local* e chamar `ZoneSystem.ApplySettings()`, que apenas relê.

Hipótese descartada no caminho: `IsAreaReady` usa `new SimulationDistance(1, 0)`,
só o anel imediato — o raio dela nunca foi o problema, e sim a fila compartilhada.

## Método (aprendido do jeito difícil)

1. **Repetir antes de concluir.** Uma medição não distingue efeito de ruído.
2. **Assentar antes de medir.** Mudança de config causa churn; capturar junto mede o churn.
3. **Medir parado.** Andando a variação é ±50%; parado é ±1,5%.
4. **Controle com a config original.** Foi o que derrubou a conclusão errada.
5. **Instrumentar antes de otimizar.** Explicação mecânica convincente ≠ efeito real.

## Construir a partir dos baús: onde o jogo cobra

O martelo passa por `Player.UpdatePlacement`, que faz, nessa ordem:

```
HaveRequirements(piece, RequirementMode.CanBuild)   -> Inventory.CountItems / HaveItem
ConsumeResources(piece.m_resources, 0, -1, 1)       -> Inventory.RemoveItem(nome, qtd, quality, true)
```

`RequirementMode` é aninhado em `Player` (`Player.RequirementMode`): `CanBuild=0`,
`IsKnown=1`, `CanAlmostBuild=2`. A checagem de quantidade é
`CountItems(nome, -1, true) < req.m_amount`; o consumo é
`m_inventory.RemoveItem(nome, amount, itemQuality, true)` por `Requirement`.

Implementação: não reescrevemos a regra. Durante essas chamadas os baús próximos
"viram" parte da mochila — `CountItems`/`HaveItem` somam, `RemoveItem` tira a
diferença dos baús. Assim estação, DLC e discovery continuam por conta do jogo.

⚠️ `ConsumeResources` é pequeno e o Mono pode inlinar (mesmo problema do
`ZInput.GetButton`). A janela de consumo por isso fica no `UpdatePlacement`, que é
grande e nunca é inlinado — senão o patch não pegaria e o custo não sairia dos baús.

Custo: um `OverlapSphere` a cada `BuildFromChestsRefresh` (ou ao andar ~2 m), com
tabela nome → total reconstruída na hora; a checagem vira consulta O(1). Só baús
que você possui e não estão em uso entram na conta, para não dessincronizar ZDO de
outro jogador.
