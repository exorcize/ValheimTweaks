# ValheimTweaks

Mod BepInEx pessoal para Valheim. Performance, visual e rede.

Alvo: **Valheim / Unity 6000.0.75f1 (Mono)**, BepInEx 5.4.2350 via r2modman.

---

## Estado atual — v0.1.0

| Bloco | Status |
|---|---|
| Esqueleto + hot reload de config | ✅ |
| Diagnóstico do pipeline de render | ✅ |
| Timeout de rede configurável | ✅ |
| Filtro anisotrópico (bug do jogo) | ✅ |
| SimulationDistance forçada pelo host | ⏳ próximo |
| Fog / ambient (`EnvMan.SetEnv`) | ⏳ |
| MSAA / SSAO full-res | ⏳ depende do diagnóstico |
| Sync host→cliente (ServerSync) | ⏳ |

---

## O que já faz

### 1. Timeout de rede — `02 - Rede`

O jogo trava o timeout de RPC em **30 s**:

```csharp
// ZRpc.cs
private static float m_timeout;
static ZRpc() { m_timeout = 600f; }
public static void SetLongTimeout(bool enable) {
    if (enable) m_timeout = 90f; else m_timeout = 30f;
}
// ZNet.Start() -> ZRpc.SetLongTimeout(enable: false)   // 30 s
```

Postfix em `SetLongTimeout` sobrescreve o campo. Pega todos os call sites e não
encosta na DLL do jogo.

> ⚠️ Precisa estar ativo nas **duas pontas**. Quem não tiver o mod derruba a
> conexão pelo lado dele no tempo padrão.

### 2. Filtro anisotrópico — `03 - Visual`

**Bug do próprio Valheim.** A opção "texturas anisotrópicas" existe no menu, é lida
das prefs, guardada no state e salva de volta — mas **nunca é aplicada**. Não existe
um único `QualitySettings.anisotropicFiltering` em toda a `assembly_valheim.dll`.

O mod aplica de verdade e reaplica sempre que o jogo mexe nas próprias settings
(via o evento público `GraphicsSettingsManager.GraphicsSettingsChanged`, sem Harmony).

Efeito: chão, estrada, terreno e piso param de borrar em ângulo raso.

### 3. Diagnóstico — `01 - Diagnostico`

Ao entrar no mundo, despeja no log o estado real: rendering path de cada câmera,
MSAA, anisotrópico, LOD bias, sombras, `LightLod`, clutter, fog, simulation
distance (com contagem de zonas) e modo de tela.

Serve para **parar de especular**. Em especial responde se MSAA é viável —
só funciona em `Forward`.

Marque `DumpNow = true` no `.cfg` para forçar um dump na hora; ele se desmarca sozinho.

---

## Build

```bash
dotnet build -c Release
```

Compila contra os assemblies reais do jogo (se a API não existir nesta versão,
é erro de compilação, não crash em runtime) e **copia o DLL direto para o perfil
do r2modman**. Sem NuGet, sem restore de rede.

Caminhos são sobrescrevíveis:

```bash
dotnet build -c Release -p:ValheimDir="D:\Steam\steamapps\common\Valheim"
```

---

## Ciclo de teste

O hot reload é o que torna isso viável: **editar o `.cfg` aplica em jogo, sem reiniciar.**

```
BepInEx/config/com.kyoka.valheimtweaks.cfg
```

Um `FileSystemWatcher` detecta a escrita, faz `Config.Reload()` e reaplica.
(O watcher dispara em thread de pool — a reaplicação é marshalada para a main
thread no `Update()`, porque API do Unity é main-thread only.)

### Dois mundos, propósitos diferentes

| | Mundo **LAB** (novo) | Mundo **REAL** |
|---|---|---|
| Para quê | visual — A/B de AA, fog, SSAO, anisotrópico | performance de verdade |
| Como | `devcommands` + cena travada | jogo normal, com os amigos online |
| Por quê | comparação só vale se a cena for idêntica | custo de host é **por peer** |

Mundo vazio não reproduz lag: o problema é base construída + zonas simuladas + peers.

Comandos para travar a cena (confirmados em `Terminal.cs`):
`devcommands`, `tod 0.5`, `env clear`, `goto`, `pos`, `freefly`, `debugmode`.

> ⚠️ `devcommands` marca o mundo como *cheated* e derruba achievements.
> Por isso fica no LAB, não no save real.

### Tela

- **Testar** → borderless (captura de tela funciona)
- **Jogar** → fullscreen exclusivo (sem DWM no caminho, menos latência)

---

## Menu

`BepInEx.ConfigurationManager` (tecla **F1**) gera a UI sozinho a partir das
definições em `ModConfig.cs`: `bool` vira switch, `AcceptableValueRange` vira
slider, enum vira dropdown.

Aba nativa no menu do jogo é viável depois — existe `Valheim.SettingsGui.ISettingsTab`
e o `Settings.cs` descobre as abas por `GetComponent<ISettingsTab>()`.

---

## Estrutura

```
src/
├─ Plugin.cs          entry, hot reload, orquestração
├─ ModConfig.cs       todas as opções
├─ Diagnostics.cs     dump do pipeline
└─ Patches/
   ├─ TimeoutPatch.cs        Postfix ZRpc.SetLongTimeout
   └─ TextureFilterPatch.cs  anisotrópico (evento, sem Harmony)
```
