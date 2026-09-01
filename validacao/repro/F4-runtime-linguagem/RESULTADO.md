# F4-runtime-linguagem — lote de verificação (eixos `runtime` e `linguagem`)

COMO RODAR: `cd validacao/repro/F4-runtime-linguagem && dotnet run -c Release`

AMBIENTE: .NET 8.0.28 · Workstation GC (`IsServerGC=False`) · 8 núcleos lógicos · `LatencyMode=Interactive`

SAIDA OBSERVADA:
```
AMBIENTE runtime=.NET 8.0.28
AMBIENTE IsServerGC=False cores=8 latency=Interactive

== A1: limiar do LOH ==
A1 byte[84000] -> geracao=0
A1 byte[84960] -> geracao=0
A1 byte[84970] -> geracao=0
A1 byte[84984] -> geracao=2
A1 byte[85000] -> geracao=2
A1 byte[87040] -> geracao=2

== A2: chave struct sem IEquatable ==
A2 sem IEquatable: 316800000 bytes alocados em 200k lookups
A2 com IEquatable: 0 bytes alocados em 200k lookups
A2 razao=∞x

== A3: concat em laco vs StringBuilder ==
A3 concat:        160143952 bytes
A3 StringBuilder: 161416 bytes
A3 razao=992x  (quadratico vs amortizado)

== A4: enumeracao multipla ==
A4 iterador executado 3x apos Count+Sum+ToList sobre a MESMA query (esperado 3)
A4 apos ToList: iterador executado 1x (esperado 1)

== A5: struct mutavel em colecao ==
A5 List<struct> apos Incrementar via indexador: valor=0 (esperado 0 = mutacao perdida)
A5 Contador[] apos Incrementar via indice:      valor=1 (esperado 1 = array da acesso por referencia)
A5 List<classe> apos Incrementar:               valor=1 (esperado 1)

== A6: geracao de literal congelado ==
A6 literal  -> geracao=2147483647 (int.MaxValue = heap NonGC)
A6 runtime  -> geracao=0
A6 int.MaxValue = 2147483647

== A7: interpolacao de struct causa boxing? ==
A7 boxing explicito de int: 23976 bytes em 100k (0 bytes/box)
A7 sem boxing:              0 bytes em 100k

FIM
```

## Vereditos

| # | Afirmação | Veredito |
|---|---|---|
| A1 | O limiar do LOH é 85.000 bytes **decimais**, não 85 KiB (87.040) | **CONFIRMA** |
| A2 | Chave struct sem `IEquatable<T>` aloca por lookup | **CONFIRMA** |
| A3 | Concatenação em laço aloca quadraticamente | **CONFIRMA** |
| A4 | Enumerar o mesmo `IEnumerable` N vezes reexecuta a fonte N vezes | **CONFIRMA** |
| A5 | Struct mutável em `List<T>` perde a mutação | **CONFIRMA** |
| A6 | `GC.GetGeneration` devolve `int.MaxValue` para objeto no heap NonGC | **CONFIRMA** |
| A7 | Boxing de `int` custa ~24 bytes por caixa | **INCONCLUSIVO** |

## Evidência por afirmação

**A1 — CONFIRMA, com correção de enunciado.** A geração vira 2 entre `byte[84_970]`
(gen 0) e `byte[84_984]` (gen 2). Se o limiar fosse 85 KiB = 87.040, `byte[85_000]`
teria ficado em gen 0 — ficou em gen 2. **Correção importante para a base:** o
limiar incide no **tamanho total do objeto**, não no comprimento do array. Em x64
um `byte[]` carrega 24 bytes de cabeçalho, então `84_984 + 24 = 85_008 ≥ 85_000`
vai para o LOH, enquanto `84_970 + 24 = 84_994` não vai. Itens da base que dizem
"array acima de 85.000 elementos" estão imprecisos: o corte prático para `byte[]`
fica em torno de **84.976 elementos**.

**A2 — CONFIRMA.** 316.800.000 bytes contra **0 bytes** para o mesmo número de
lookups, mudando apenas a presença de `IEquatable<ChaveComEq>`. Medido com
`GC.GetAllocatedBytesForCurrentThread`, com chamada de aquecimento fora da medição.
A magnitude (~1.584 bytes por lookup) é muito maior que o boxing de dois campos,
o que indica que o caminho de fallback faz mais que empacotar: `EqualityComparer<T>.Default`
cai em comparação por reflexão sobre os campos quando o struct não implementa
`IEquatable<T>`.

**A3 — CONFIRMA.** 160.143.952 bytes contra 161.416 bytes para 4.000 concatenações
— **992x**. A curva é a esperada de custo quadrático: cada `+=` realoca a string
inteira.

**A4 — CONFIRMA.** O iterador executou **3 vezes** com `Count()`, `Sum()` e
`ToList()` aplicados à mesma query diferida, e **1 vez** quando materializado antes.
Contador de execuções instrumentado dentro do próprio iterador.

**A5 — CONFIRMA, com distinção que a base não registra.** `List<T>` devolve **cópia**
pelo indexador, e a mutação some (`valor=0`). Já `Contador[]` devolve **referência**
pelo índice, e a mutação persiste (`valor=1`). Ou seja: a armadilha é de `List<T>`
e demais coleções com indexador, não de "struct em coleção" em geral. Enunciar
sem essa distinção induz o dev a trocar array por classe sem necessidade.

**A6 — CONFIRMA.** `GC.GetGeneration` de um literal `const string` devolveu
`2147483647` = `int.MaxValue`, contra geração `0` para uma string construída em
runtime. Confirma a sentinela do heap NonGC no .NET 8 — era afirmação de confiança
`baixa` na extração de KOKOSA cap. 5 e agora é fato observado.

**A7 — INCONCLUSIVO, e o motivo importa.** Esperava-se ~2.400.000 bytes (24 bytes ×
100.000); mediu-se 23.976 bytes, cerca de 1% disso. O laço só faz `GC.KeepAlive`
sobre a caixa, e o otimizador do JIT eliminou quase toda a alocação. O experimento
**não é discriminante como escrito**: para medir custo de boxing é preciso impedir
a eliminação, por exemplo acumulando as caixas numa coleção viva ou passando-as
por fronteira não inlinável. Não afirmo nada sobre o custo de boxing a partir
desta execução.

## Consequência para a base

- A1 e A5 exigem **correção de enunciado**, não só marca de evidência.
- A6 promove um item de `CONFIANCA baixa` para fato.
- A7 é lembrete de que microbenchmark sem barreira contra o otimizador mede o
  otimizador, não o mecanismo.
