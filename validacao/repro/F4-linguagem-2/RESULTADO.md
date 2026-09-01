# F4-linguagem-2 — lote 2 (eixo `linguagem`) + refazendo o INCONCLUSIVO do lote 1

COMO RODAR: `cd validacao/repro/F4-linguagem-2 && dotnet run -c Release`

AMBIENTE: .NET 8.0.28

SAIDA OBSERVADA:
```
AMBIENTE runtime=.NET 8.0.28

== B1: async void vs async Task ==
B1 async void  -> try/catch do chamador: nao capturou
B1 async Task  -> try/catch do chamador: capturou
B1 excecoes que escaparam para o handler de ultimo recurso: 1

== B2: .Result sob SynchronizationContext de uma thread ==
B2 com .Result  -> DEADLOCK (timeout de 3s)
B2 com await    -> concluiu com 42

== B3: backtracking catastrofico ==
B3 backtracking classico: TIMEOUT apos 2111ms
B3 NonBacktracking:       sem match=True em 137ms

== B4: finalizador atrasa a coleta ==
B4 com finalizador -> sobreviveu a coleta, geracao=-2
B4 sem finalizador -> alcancavel? NAO (coletado)
B4 finalizadores executados no total: 1

== B5: boxing de int, com barreira (refaz o INCONCLUSIVO do lote 1) ==
B5 boxing em array vivo: 2400000 bytes em 100k -> 24,0 bytes/caixa
B5 sem boxing:           0 bytes em 100k

FIM
```

## Vereditos

| # | Afirmação | Veredito |
|---|---|---|
| B1 | Exceção de `async void` não é capturável pelo `try/catch` do chamador | **CONFIRMA** |
| B2 | `.Result` em contexto com `SynchronizationContext` de uma thread trava | **CONFIRMA** |
| B3 | `^(a+)+$` sofre backtracking catastrófico; `NonBacktracking` não | **CONFIRMA** |
| B4 | Finalizador atrasa a coleta e promove a geração | **INCONCLUSIVO — teste defeituoso** |
| B5 | Boxing de `int` custa 24 bytes por caixa | **CONFIRMA** (fecha o INCONCLUSIVO do lote 1) |

## Evidência por afirmação

**B1 — CONFIRMA.** O `try/catch` ao redor da chamada `async void` **não capturou**
a `InvalidOperationException`, enquanto o mesmo `try/catch` ao redor de
`await DispararTask()` **capturou**. O contador de escape marcou 1: a exceção só
não derrubou o processo porque o próprio método a engoliu internamente. Em
produção, sem esse `catch` interno, `async void` termina o processo — não há
`Task` para carregar a falha.

**B2 — CONFIRMA.** Sob um `SynchronizationContext` de fila única, `.Result`
**não concluiu em 3 segundos** (deadlock: a continuação precisa da thread que está
bloqueada esperando o resultado), enquanto `await` no mesmo contexto concluiu com
`42`. Única variável alterada: `.Result` contra `await`.

**B3 — CONFIRMA.** Sobre 31 caracteres, o motor clássico **estourou o timeout de
2 s** (2111 ms) e `RegexOptions.NonBacktracking` respondeu corretamente
(`sem match=True`) em **137 ms**. O padrão e a entrada são idênticos nos dois casos.

**B4 — INCONCLUSIVO, e o defeito é meu.** A linha de saída se contradiz:
diz "sobreviveu a coleta" e ao mesmo tempo `geracao=-2`, que no meu código
significa "referência fraca já limpa". O rótulo estava escrito à mão como se o
resultado fosse conhecido de antemão — erro de construção do experimento.

O que a execução realmente mostra: `WeakReference<T>` **curta** é limpa no momento
da finalização, independentemente de o finalizador ressuscitar o objeto. Logo este
teste mede semântica de referência fraca, não promoção de geração. O único fato
sustentado é `Finalizados = 1`: o finalizador **executou**.

Para medir promoção de geração corretamente seria preciso manter uma referência
**forte** e ler `GC.GetGeneration` depois do ciclo de finalização. Nada é afirmado
sobre promoção a partir desta execução.

**B5 — CONFIRMA, e fecha o INCONCLUSIVO do lote 1.** Com o resultado do boxing
armazenado em `object[]` vivo — barreira contra a eliminação pelo otimizador —
mediu-se **2.400.000 bytes para 100.000 caixas = exatamente 24,0 bytes por caixa**,
contra **0 bytes** no laço aritmético equivalente. No lote 1, o mesmo laço com
apenas `GC.KeepAlive` rendeu 23.976 bytes (1% do real) porque o JIT eliminou a
alocação. Lição registrada: microbenchmark sem barreira mede o otimizador.

## Consequência para a base

- B1, B2, B3 e B5 entram em `base/principios/linguagem.md` com `EVIDENCIA`.
- B4 **não entra**. Um experimento com rótulo escrito antes do resultado é
  exatamente o vício que a Fase 4 existe para impedir; fica registrado como
  defeito, não como conhecimento.
