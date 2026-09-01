# Metodologia

Como 6.297 extrações brutas viraram 450 itens, e por que o pipeline é assim.

## Princípio operante

> Um item verificado vale dez plausíveis. Na dúvida entre extrair mais ou
> conferir melhor, confira melhor.

## Pipeline

```
5 livros (PDF)
  └─ conversão + fatiamento por capítulo ............ 68 unidades
      └─ FASE 1: extração triplicada .................. 204 execuções → 6.297 itens
          └─ FASE 2: reconciliação por consenso
              └─ FASE 3: camada B (armadilhas) ........ só eixo `dados`
                  └─ FASE 4: verificação empírica ..... 8 repros → 24 itens com EVIDENCIA
                      └─ FASE 5: síntese cruzada ...... 15 pontes
                          └─ FASE 6: red team + consolidação → 450 itens
                              └─ FASE 7/8: agente + suíte de regressão
```

## Fase 1 — por que três extrações

Cada capítulo passa por **três agentes independentes com framings diferentes**:

| Run | Framing | Prioriza |
|---|---|---|
| A | mecanismo | a cadeia causal; o `PORQUE` desce um nível abaixo do princípio |
| B | erro sênior | o que um dev com 5 anos erraria com confiança |
| C | limite | onde a regra deixa de valer |

Prompt idêntico três vezes reproduziria a mesma falha três vezes. Framing
diferente decorrelaciona erro.

Regras duras do extrator: nunca transcrever; toda afirmação termina numa
consequência observável em .NET 8; teto de versão em C# 12; fonte fechada
(só o capítulo, sem web nem documentação).

## Fase 6 — como 6.297 viraram 450

**Filtro mecânico** (regras 2 e 3 da consolidação):

- descarta item cujo `PORQUE` apenas reescreve o `PRINCIPIO`
  (sobreposição de tokens > 72%)
- descarta `EM_DOTNET` vago — sem token de API concreto ou com menos de
  60 caracteres

Corte: 6.297 → 3.270.

**Fusão semântica em dois níveis:** nível 1 agrupa por decisão equivalente em
lotes; nível 2 funde títulos que descrevem a mesma decisão. Cobertura de ID
validada a cada lote — item que não entra em grupo nenhum é preservado isolado,
nunca perdido.

**Teto de 90 por eixo**, ordenado por impacto: correção, depois performance,
depois manutenção.

## Fase 4 — o que "verificado" significa aqui

Cada repro tem `RESULTADO.md` com:

```
AFIRMACAO      a afirmação testada, uma frase
COMO RODAR     comando exato
SAIDA OBSERVADA  saída real, verbatim, sem edição
VEREDITO       CONFIRMA | REFUTA | INCONCLUSIVO
EVIDENCIA      uma frase que só quem viu a saída consegue escrever
AMBIENTE       runtime e versão de servidor observados
```

`CONFIRMA` exige **valor que distingue a hipótese da alternativa** — não basta o
programa terminar. `REFUTA` é resultado de qualidade. `INCONCLUSIVO` é resposta
honesta e aparece no repositório.

Ambiente: .NET 8.0.28, EF Core 8.0.30, SQL Server 2022 (16.0.4265.3) em container.

## O que o pipeline pegou que consenso sozinho não pegaria

Três casos em que a Fase 4 corrigiu a Fase 2:

1. **UDF escalar e paralelismo** — os três runs concordaram com o folclore. A
   medição mostrou que vale só abaixo do compatibility level 150. A suíte de
   regressão foi corrigida: agora reprova quem afirma sem qualificar o nível.

2. **Limiar do LOH** — a base dizia "array acima de 85.000 elementos". A medição
   mostrou que o limiar incide sobre o tamanho total do objeto: ~84.976 elementos
   para `byte[]` em x64, porque o cabeçalho conta.

3. **Struct mutável em coleção** — a base generalizava para "coleção". A medição
   mostrou que `List<T>` perde a mutação (devolve cópia) mas array não (devolve
   referência). Sem a distinção, o dev troca struct por classe sem necessidade.

Também apareceu um **fato ausente das 6.297 extrações**: o nível de isolamento
sobrevive ao retorno da conexão ao pool. Nenhum livro disse; o experimento
mostrou, com controle `Pooling=false`.

## Incidente de integridade registrado

O kernel Python compartilhado entre subagentes causou colisão de variáveis
globais: 46 blocos de extração foram gravados nos arquivos de outros agentes —
**deslocados, não duplicados**. Detectados por varredura de prefixo de ID,
resgatados para quarentena antes de qualquer reescrita, e restaurados. Nenhum
item perdido.

Correção permanente: escrita por `eval` proibida no contrato do extrator; só
`write`/`edit`.

## Reproduzir o pipeline

Não está neste repositório. Exige os cinco PDFs (com copyright) e ~204 execuções
de subagente. O que está aqui é o **resultado auditável**: os itens, os IDs de
origem, os repros e as saídas observadas.
