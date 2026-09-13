# ValheimTweaks

Ajustes de vídeo e rede que o menu do Valheim não oferece.

Tudo é opcional e vem desligado ou no padrão do jogo, menos o filtro anisotrópico
e o anti-serrilhado, que são praticamente de graça.

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

**Campo de visão** — fixo em 65 no jogo, ajustável aqui.

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
