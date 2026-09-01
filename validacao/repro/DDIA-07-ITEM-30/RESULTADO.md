# DDIA-07-ITEM-30

AFIRMACAO: Para travar a AUSÊNCIA de uma linha no padrão check-then-insert, `UPDLOCK` sozinho não basta; é necessário `HOLDLOCK` (lock de range).

COMO RODAR: `cd validacao/repro/DDIA-07-ITEM-30 && dotnet run -c Release`

SAIDA OBSERVADA:
```
AMBIENTE runtime=.NET 8.0.28
AMBIENTE sqlserver=16.0.4265.3

--- check-then-insert, hint: sem hint ---
A: viu 0, INSERT ok, COMMIT ok
B: viu 0, INSERT ok, COMMIT ok
RESULTADO linhas 'X1'=2 -> DUPLICADO

--- check-then-insert, hint: UPDLOCK ---
A: viu 0, INSERT ok, COMMIT ok
B: viu 0, INSERT ok, COMMIT ok
RESULTADO linhas 'X1'=2 -> DUPLICADO

--- check-then-insert, hint: UPDLOCK, HOLDLOCK ---
A: viu 0, INSERT ok, COMMIT ok
B: viu 1 linha(s), nao inseriu
RESULTADO linhas 'X1'=1 -> unicidade OK

FIM
```

VEREDITO: CONFIRMA

EVIDENCIA: Com `WITH (UPDLOCK)` as duas sessões concorrentes viram 0 linhas e gravaram, resultando em 2 linhas `'X1'` — exatamente o mesmo resultado de não usar hint nenhum; só com `WITH (UPDLOCK, HOLDLOCK)` a sessão B passou a ver 1 linha e desistir, fechando em 1 linha.

AMBIENTE: .NET 8.0.28; SQL Server 16.0.4265.3 Developer; banco dedicado `ReproDb_I30`; READ COMMITTED com RCSI OFF (default); índice não único `IX_Reserva_Codigo` sobre o predicado.

NOTA: as duas sessões são liberadas por um gate assíncrono para garantir corrida real. No cenário `UPDLOCK, HOLDLOCK` a sessão B foi serializada atrás do range lock e, ao ser liberada, já enxergou a linha de A — nesta execução não houve deadlock 1205, resultado compatível com o cenário em que uma das sessões adquire o range antes da outra.
