# DDIA-07-ITEM-03 × ITEM-04 — arbitragem

AFIRMACAO 1 (B-10, C-28): um `SaveChanges` é atômico entre tabelas — afirmado **sem qualificação**.
AFIRMACAO 2 (A-2): essa garantia depende de `AutoTransactionBehavior` não estar em `Never`.
AFIRMACAO 3 (trap): `ExecuteUpdate` não entra na transação do `SaveChanges`.

COMO RODAR: `cd validacao/repro/DDIA-07-ITEM-03 && dotnet run -c Release`

SAIDA OBSERVADA:
```
AMBIENTE runtime=.NET 8.0.28
AMBIENTE efcore=8.0.30.0
A) 1 SaveChanges, WhenNeeded (default): erro=SqlException | Pai gravado=0 | Filho gravado=0 -> ATOMICO
B) 1 SaveChanges, Never: erro=SqlException | Pai gravado=1 | Filho gravado=0 -> PARCIAL: orfao gravado
C) SaveChanges lancou DbUpdateException, como esperado
C) ExecuteUpdate sem transacao explicita -> Pai.Nome=MUDADO (persistiu = escopo de commit proprio)
D) ExecuteUpdate dentro de BeginTransaction + rollback -> Pai.Nome=original (participou da transacao)

FIM
```

VEREDITO POR AFIRMACAO:
- AFIRMACAO 1: **CONFIRMA, mas só sob o default.** Com `WhenNeeded`, o INSERT do pai foi desfeito junto com o filho que violou o CHECK: `Pai gravado=0`.
- AFIRMACAO 2: **CONFIRMA.** Trocando apenas `AutoTransactionBehavior` para `Never`, o mesmo `SaveChanges` deixou `Pai gravado=1` e `Filho gravado=0` — órfão persistido entre tabelas.
- AFIRMACAO 3: **CONFIRMA PARCIALMENTE / REFUTA na forma absoluta.** Sem transação explícita, `ExecuteUpdate` tem escopo de commit próprio e persistiu (`Nome=MUDADO`) apesar da falha do `SaveChanges` seguinte; **dentro** de `BeginTransaction`, o rollback o desfez (`Nome=original`), logo ele participa da transação explícita.

VEREDITO GLOBAL: CONFIRMA

EVIDENCIA: A única variável alterada entre os cenários A e B foi `ctx.Database.AutoTransactionBehavior`; a atomicidade entre `dbo.Pai` e `dbo.Filho` desapareceu com `Never` (`Pai gravado=1`). Nos cenários C e D a única variável foi a existência de `BeginTransactionAsync`, e o valor final de `Pai.Nome` mudou de `MUDADO` para `original`.

AMBIENTE: .NET 8.0.28; EF Core 8.0.30; SQL Server 16.0.4265.3; banco `ReproDb`, tabelas `dbo.Pai`/`dbo.Filho` recriadas pelo próprio repro.

## Decisão de arbitragem

Não era contradição: era **regra e sua precondição**, escritas por runs diferentes.

- ITEM-04 mantém a garantia de atomicidade de um `SaveChanges`, agora com a
  precondição explícita `AutoTransactionBehavior != Never`. Afirmação
  incondicional é proibida — ela falha exatamente no caso em que alguém "otimizou"
  depois de ver `BEGIN TRAN` no profiler.
- ITEM-03 deixa de ser singleton frágil e passa a ser a precondição de ITEM-04,
  com `EVIDENCIA`. Os dois passam a citar um ao outro.
- A trap sobre `ExecuteUpdate` perde a forma absoluta: o texto correto é
  "`ExecuteUpdate` não abre transação implícita que englobe o `SaveChanges`; sem
  transação explícita ele commita sozinho, e com transação explícita ele
  participa dela".
