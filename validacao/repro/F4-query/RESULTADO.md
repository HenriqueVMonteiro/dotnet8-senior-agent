# F4-query — lote de verificação (eixo `query`)

COMO RODAR:
```bash
docker start sqlbase
docker cp validacao/repro/F4-query/verificar.sql sqlbase:/tmp/v.sql
docker exec sqlbase /opt/mssql-tools18/bin/sqlcmd -S localhost -U sa -P 'Repro#2024pw' -C -i /tmp/v.sql
docker cp validacao/repro/F4-query/verificar-udf.sql sqlbase:/tmp/u.sql
docker exec sqlbase /opt/mssql-tools18/bin/sqlcmd -S localhost -U sa -P 'Repro#2024pw' -C -i /tmp/u.sql
docker cp validacao/repro/F4-query/verificar-plano.sql sqlbase:/tmp/p.sql
docker exec sqlbase /opt/mssql-tools18/bin/sqlcmd -S localhost -U sa -P 'Repro#2024pw' -C -i /tmp/p.sql
```

AMBIENTE: SQL Server 16.0.4265.3 Developer · `ReproDb_F4Q` · `compatibility_level` 160 · tabela `dbo.Evento` com **6.901.129** linhas e índice `IX_Evento_Quando`

## SAIDA OBSERVADA

```
== Q1: NOT IN com NULL na subquery ==
Q1 NOT IN devolveu: 0 linha(s)
Q1 NOT EXISTS devolveu: 1 linha(s)  (cliente 3 nao tem pedido)

== Q2: COUNT(coluna) vs COUNT(*) ==
Warning: Null value is eliminated by an aggregate or other SET operation.
Q2 COUNT(*)     = 4
Q2 COUNT(Total) = 3  (Total tem 1 NULL)

== Q3: alias do SELECT referenciado no WHERE ==
Q3 alias no WHERE: FALHOU erro 207 - Invalid column name 'Ano'.
Q3 alias no ORDER BY: EXECUTOU (esperado - ORDER BY vem depois do SELECT)

== Q4: sargabilidade ==
Q4 linhas na tabela: 6901129
Q4a NAO sargavel: WHERE YEAR(Quando) = 2023
Table 'Evento'. Scan count 9, logical reads 15594
Q4b sargavel: WHERE Quando >= '2023-01-01' AND Quando < '2024-01-01'
Table 'Evento'. Scan count 1, logical reads 1177

== Q6: UDF escalar vs inline (compat 160) ==
Q6a com UDF escalar:  CPU time = 2275 ms,  elapsed time = 322 ms
Q6b inline sargavel:  CPU time = 58 ms,    elapsed time = 59 ms

== Q7: plano ==
compatibility_level = 160
Paralelismo=SIM  UdfNoPlano=NAO   (consulta do lote com UDF)

== Q8: mesma UDF, variando compatibility_level ==
Q8 UDF sob compat 140:  CPU time = 43508 ms,  elapsed time = 56471 ms
Q8 UDF sob compat 160:  CPU time = 2192 ms,   elapsed time = 321 ms
```

## Vereditos

| # | Afirmação | Veredito |
|---|---|---|
| Q1 | `NOT IN` com NULL na subquery devolve conjunto vazio; `NOT EXISTS` não | **CONFIRMA** |
| Q2 | `COUNT(coluna)` ignora NULL, `COUNT(*)` não | **CONFIRMA** |
| Q3 | Alias do SELECT não pode ser referenciado no WHERE, mas pode no ORDER BY | **CONFIRMA** |
| Q4 | Função sobre coluna indexada no WHERE destrói a sargabilidade | **CONFIRMA** |
| Q6/Q8 | "UDF escalar em predicado mata o paralelismo" | **REFUTA em compat ≥ 150** |

## Evidência por afirmação

**Q1 — CONFIRMA.** `NOT IN` devolveu **0** e `NOT EXISTS` devolveu **1** sobre os
mesmos dados. O cliente 3 realmente não tem pedido; o `NOT IN` o esconde porque a
subquery contém um `ClienteId` NULL e a comparação vira `UNKNOWN`.

**Q2 — CONFIRMA.** `COUNT(*) = 4` contra `COUNT(Total) = 3`, com o próprio servidor
emitindo o aviso "Null value is eliminated by an aggregate".

**Q3 — CONFIRMA, com o limite explícito.** O alias no WHERE falhou com **erro 207
`Invalid column name 'Ano'`**; o mesmo alias no ORDER BY **executou**. A assimetria
é a ordem lógica de processamento: ORDER BY vem depois do SELECT, WHERE vem antes.

**Q4 — CONFIRMA, com número medido.** Sobre 6.901.129 linhas, mesmo resultado
(525.599) por dois caminhos:
- `WHERE YEAR(Quando) = 2023` → **15.594 leituras lógicas**, scan count 9
- `WHERE Quando >= '2023-01-01' AND Quando < '2024-01-01'` → **1.177 leituras lógicas**, scan count 1

**13,2x mais páginas lidas** para o mesmo resultado. Note que o não sargável ainda
usa o índice (scan count 9 = varredura paralela), então "não sargável" aqui
significa varrer o índice inteiro em vez de buscar a faixa.

**Q6/Q8 — REFUTA a forma incondicional.** Este é o achado do lote:

| compatibility_level | CPU | elapsed | paralelismo |
|---|---|---|---|
| 140 (sem inlining) | 43.508 ms | **56.471 ms** | não (CPU ≈ elapsed) |
| 160 (com inlining) | 2.192 ms | **321 ms** | sim (CPU ≫ elapsed) |

Sob compat 140 a UDF escalar de fato serializa o plano e o elapsed explode.
Sob compat 160 — o default do SQL Server 2022 — o **scalar UDF inlining (Froid)**
elimina a UDF do plano (`UdfNoPlano=NAO`, `Paralelismo=SIM`) e o elapsed cai
**176x**. A UDF continua mais caro que a expressão inline sargável (321 ms contra
59 ms, ~5,4x), mas a afirmação "mata o paralelismo" é **falsa no alvo declarado**.

## Consequências

1. `validacao/casos.md`, caso "UDF escalar em predicado": o critério de reprovação
   precisa do qualificador de compatibility level. Corrigido.
2. Item novo para `base/principios/query.md`, com `EVIDENCIA`: o custo de UDF
   escalar depende do compatibility level, e isso é uma fronteira de
   aplicabilidade — exatamente o que o framing C deveria ter capturado e não
   capturou em nenhum dos três runs.
3. A UDF inlinável não é gratuita: 5,4x mais lenta que o predicado sargável.
   O conselho de reescrever continua válido; o mecanismo alegado estava errado.
