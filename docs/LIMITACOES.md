# Limitações — leia antes de confiar

Este documento existe porque a alternativa é você descobrir isso sozinho, em
produção, no pior momento possível.

## 1. Só 24 de 450 itens foram verificados por execução

**426 itens não têm evidência empírica.** São extração de livro por modelo de
linguagem, filtrada por consenso de três execuções independentes com framings
diferentes.

Consenso decorrelaciona **desleixo**, não **crença sistemática errada**. Prova
disso está no próprio repositório: os três runs concordaram que "UDF escalar mata
o paralelismo". Os três estavam errados a partir do compatibility level 150, e só
o experimento pegou (`validacao/repro/F4-query`).

**Como usar na prática:**

| Marca | Como tratar |
|---|---|
| `EVIDENCIA` | fato — a saída observada está no `RESULTADO.md` citado |
| `ACORDO 3` + `alta` | regra provável; confira antes de decisão cara |
| `ACORDO 1` ou `baixa` | hipótese; exija medição antes de agir |

## 2. A confiança foi autoavaliada pelo extrator

O campo `CONFIANCA` (`alta`/`media`/`baixa`) foi atribuído pelo mesmo modelo que
escreveu o item. Isso mede fluência, não sustentação.

No piloto, **zero de 128 itens** receberam `baixa` — implausível para um capítulo
cuja ponte para .NET é inteiramente inferencial. O prompt foi corrigido (a
distribuição melhorou nas rodadas seguintes), mas a calibração continua sendo
autodeclarada, não auditada.

## 3. O eixo `hardware` tem taxa de descarte suspeita

O filtro automático descartou **26%** do eixo `hardware`, contra **39%** de
`linguagem`. O esperado era o oposto: CSAPP fala C e x86, e a regra de ponte
("todo item termina numa consequência observável em .NET 8") deveria eliminar
muito mais.

Explicação provável: o filtro mecânico só detecta `PORQUE` que repete o
`PRINCIPIO` e `EM_DOTNET` vago. Ele **não detecta ponte semanticamente forçada** —
um item com manifestação concreta em C# mas cujo mecanismo alegado não é
realmente o mesmo do livro.

A amostragem manual (extensão de sinal, mascaramento de shift, IEEE 754,
alinhamento) mostrou pontes genuínas. Mas o eixo `hardware` **não passou por red
team manual**. Trate seus itens sem `EVIDENCIA` com ceticismo extra.

## 4. Alvo congelado em .NET 8 / C# 12

Por desenho, não por descuido. O agente **recusa** emitir `field` keyword,
`System.Threading.Lock`, `CountBy`/`AggregateBy` e qualquer coisa de C# 13+ ou
.NET 9+.

Isso é uma feature enquanto você está em .NET 8 e um estorvo quando migrar.
Não há caminho automático de atualização da base — exigiria reextrair de edições
novas dos livros.

## 5. Camada B só existe para o eixo `dados`

`base/referencia/dados.md` tem 24 armadilhas com código antes/depois. Os outros
quatro eixos **não têm camada B** — a Fase 3 do pipeline nunca rodou em escala
para eles.

Consequência: em `linguagem`, `query`, `runtime` e `hardware` você tem o
princípio e o mecanismo, mas não o exemplo mínimo de código que dispara a
armadilha.

## 6. A base é em português

Corta a audiência internacional. Os identificadores técnicos, nomes de API e
blocos de código estão em inglês, mas princípio, mecanismo e limite estão em
português.

## 7. Cobertura de capítulos ≠ cobertura de assunto

"11/11 capítulos" significa que todos os capítulos do escopo foram processados,
não que todo assunto relevante virou item. O teto de 90 itens por eixo descarta
decisões reais: `dados` colapsou para 291 decisões distintas e publicou 90;
`linguagem` colapsou para 325 e publicou 90.

**O corte foi por impacto** (correção > performance > manutenção). Nos cinco
eixos o teto foi preenchido inteiramente por itens de correção — ou seja,
**nenhum item de performance ou manutenção sobreviveu ao corte**. Se você procura
otimização e não correção, a base tem menos para oferecer do que o número 450
sugere.

## 8. Capítulos excluídos por decisão de escopo

Fora da base, deliberadamente:

- **CS12**: LINQ to XML, Dynamic Programming, Cryptography, Native/COM Interop
- **TSQL**: SQL Graph
- **DDIA**: The Future of Data Systems
- **CSAPP**: Processor Architecture (Y86), Linking, Network Programming, e os
  capítulos 1 e 8 (Tour, Exceptional Control Flow)

O caso mais discutível é o **CSAPP cap. 8**: trata sinais e controle de fluxo
excepcional, que tenta parecer uma ponte para exceções em .NET. Foi excluído
justamente porque essa ponte seria inventada — exceção de hardware e exceção
gerenciada são mecanismos diferentes.

## 9. Um experimento ficou INCONCLUSIVO

`validacao/repro/F4-runtime-linguagem`, item A7: a medição de custo de boxing
rendeu 1% do previsto porque o JIT eliminou a alocação — o laço só fazia
`GC.KeepAlive`. Foi refeito com barreira em `F4-linguagem-2` (B5) e deu
exatamente 24,0 bytes por caixa.

Fica documentado como lembrete: **microbenchmark sem barreira mede o otimizador,
não o mecanismo.**
