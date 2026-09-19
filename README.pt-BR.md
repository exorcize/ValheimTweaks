# ValheimTweaks

Ajustes de vídeo e rede que o menu do Valheim não oferece.

Tudo é opcional e vem desligado ou no padrão do jogo, menos o filtro anisotrópico
e o anti-serrilhado, que são praticamente de graça.

> O texto dentro do jogo segue o idioma escolhido no menu do jogo (inglês ou
> português). Este documento é a versão em português; o principal é o
> [README.md](README.md).

---

## Requisitos

- BepInEx (o r2modman instala junto)
- Configuration Manager — é o que abre o menu de opções com **F1**

## Principais opções

**Filtro anisotrópico** — chão, estradas e pisos deixam de ficar borrados quando
vistos de ângulo. É o ajuste com melhor retorno e custo quase zero.

**Névoa ajustável** — o Valheim desenha bastante névoa, e ela lava as cores ao
longe. Baixar para 0.4 abre o horizonte sem tirar o clima. Não custa desempenho.

**Anti-serrilhado com qualidade** — o jogo só liga e desliga. Aqui dá para
escolher entre FXAA em cinco níveis ou TAA, que limpa muito melhor folhagem e
cercas.

**Sombra de contato (SSAO)** — em resolução cheia e com mais qualidade do que o
menu permite. É o que dá profundidade e tira o aspecto de objeto colado no chão.

**Tela cheia exclusiva** — o menu do jogo só alterna entre janela e sem bordas.
A exclusiva costuma deixar o movimento mais suave, principalmente em monitor de
alta taxa de atualização.

**Timeout de rede** — o jogo desiste de uma conexão parada em 30 segundos, o que
derruba gente com internet instável. Aqui dá para aumentar.

**Distância de simulação** — permite ir além do limite do menu, para ver
inimigos e animais de mais longe. Pesa em quem hospeda.

**Portal rápido** — a travessia demora porque o jogo espera a área de destino
terminar de carregar, e ela disputa fila com mais de cem zonas ao redor. O mod
reduz esse alcance só durante a passagem e restaura ao chegar. Medido: de 15
segundos para menos de 2. Vale só para você, não muda nada para os outros.

**Campo de visão** — fixo em 65 no jogo, ajustável aqui.

**Minerar automático** — uma tecla liga e desliga; com a picareta na mão e o
minério sob a mira, o personagem bate sozinho até quebrar e para. Só minério, não
pedra: o mod olha o que o alvo solta.

**Guardar nos baús (teclas)** — marque itens no inventário e uma tecla despeja
tudo nos baús próximos, encaixando nas pilhas que já existem. Uma lista no canto
mostra o que foi guardado e onde. Vem **sem teclas atribuídas**: o Ctrl+clique do
painel cobre o mesmo caso. Quem preferir assim escolhe as teclas no F1
(`StoreMarkKey`, `StoreMarkedKey`, `StoreHoveredKey`).

**Painel dos baús** — abra o inventário e clique em **Baús próximos** (ou
configure uma tecla em `ChestSearchKey`). No lugar do painel de produção aparece
tudo o que está nos baús ao redor, com busca por nome que ignora acento
(`carvao` acha "Carvão"). Uma barra no topo mostra quantos slots ainda estão
livres somando todos os baús.

Clicar num item pega uma pilha, igual a pegar de um baú aberto. **Shift+clique**
abre a telinha de dividir do próprio jogo para escolher a quantidade, e
**Ctrl+clique** pega tudo. O rodapé também traz atalhos de 1, 10, uma pilha ou
tudo, com `-/+` e um campo para digitar o número exato.

Para guardar, **Ctrl+clique** num item do inventário manda ele direto para os
baús. (Sem o painel aberto, esse mesmo atalho larga o item no chão — é o
comportamento do jogo.) Ou pegue o item como faria normalmente e clique no
painel. Antes de confirmar ele mostra para onde cada parte vai e por quê:
primeiro completando pilhas que já existem, depois nos baús que já têm aquele
item, e só então em espaço livre. Se algo não couber, ele diz quanto.

Enquanto o painel está aberto o personagem não anda, para digitar na busca não
sair mexendo com ele. Fecha pelo botão ou com Esc. Quem preferir andar com ele
aberto desliga em `ChestSearchBlockMove`.

**Espiar baú** — mirando um baú de perto, o conteúdo aparece no canto direito
sem precisar abrir.

**Reparo automático** — conserta todo o equipamento gasto ao abrir o inventário
perto de uma bancada ou forja, em vez de um item por clique. Reparo no Valheim
não consome material, então isso só poupa cliques.

**Construir dos baús próximos** — ao construir com o martelo, a madeira e a pedra
que estão nos baús, carroças e navios ao redor contam como se estivessem na
mochila. O menu de construção marca a peça como construível quando o
armazenamento por perto cobre o custo, e o material sai de lá na hora de colocar.
Vale só para construção: fabricar em estação continua usando a mochila. Serve
para deixar o carrinho de madeira perto da obra em vez de uma pilha em cada slot.

Além desses, há controle de sombras, densidade e alcance da grama, limite de
luzes de tocha, tesselação do terreno e alguns ajustes de desempenho.

## Em multijogador

A maior parte das opções é visual e vale só para quem as configurou — o seu
ajuste não muda nada na tela dos outros.

Duas exceções:

- **Timeout de rede**: cada jogador encerra a própria conexão, então todos
  precisam ter o mod e o mesmo valor. Basta um sem o mod para derrubar.
- **Distância de simulação**: quem hospeda define o teto. Os outros podem usar
  menos, nunca mais.

## Ferramentas

Há uma seção de diagnóstico que escreve no log um resumo das configurações de
vídeo em uso e mede o desempenho por alguns segundos, com média, percentis e
contagem de engasgos. Útil para comparar ajustes com número em vez de impressão.

Junto vem uma opção para travar a hora do dia e o clima na sua tela, sem afetar
o mundo nem os outros jogadores, para conseguir comparar dois ajustes na mesma
cena.

Há também uma **auditoria de baús** (`ChestAuditLog`): escreve no log toda
interação de rede com baús — quem abriu, empilhou ou pegou tudo de qual baú, se o
dono autorizou, o id e o dono do baú — e, com `ChestAuditContents`, a lista de
itens antes e depois de cada mudança. Serve para responder "para onde foi meu
item" com fatos. Desligue numa base movimentada para o log não crescer.

## Sobre desempenho

Este mod não tenta otimizar o motor do jogo. Para isso vale instalar o
**ValheimPerformanceOptimizations**, que faz esse trabalho a fundo — streaming
de objetos, colisão de terreno, culling de luzes e de som, integridade
estrutural — sem alterar o comportamento do jogo. Os dois convivem: um cuida do
visual e da rede, o outro do motor.

Os poucos ajustes de desempenho daqui vêm todos no padrão do jogo e existem para
casos específicos (fumaça em base com muitas fogueiras, intervalo de rede em
servidor cheio).

## Alterando as opções

Pelo **F1** em jogo, ou editando direto:

```
BepInEx/config/com.kyoka.valheimtweaks.cfg
```

As mudanças valem na hora, sem reiniciar.
