# DDIA-07-ITEM-05 (+ ITEM-06)

AFIRMACAO 1: `new TransactionScope()` sem `TransactionOptions` roda em Serializable, não no default do banco.
AFIRMACAO 2: `TransactionScope` com `await` dentro exige `TransactionScopeAsyncFlowOption.Enabled`.
AFIRMACAO 3 (não prevista pelo item; surgiu na execução): o nível de isolamento sobrevive ao retorno da conexão ao pool.

COMO RODAR: `cd validacao/repro/DDIA-07-ITEM-05 && dotnet run -c Release`

SAIDA OBSERVADA:
```
AMBIENTE runtime=.NET 8.0.28
AMBIENTE TransactionManager.DefaultTimeout=00:01:00
AMBIENTE sqlserver=16.0.4265.3

=== A) nivel de isolamento efetivo dentro do escopo (leitura SINCRONA, sem await) ===
sem escopo algum                       -> 2/ReadCommitted
new TransactionScope()                 -> 4/Serializable (Complete+Dispose ok)
TransactionOptions IsolationLevel=ReadCommitted -> 2/ReadCommitted (Complete+Dispose ok)
TransactionScopeAsyncFlowOption.Enabled -> 4/Serializable (Complete+Dispose ok)

=== B) await dentro do escopo, com hop real de thread ===
SEM AsyncFlowOption: Transaction.Current antes=presente depois=null | thread 8->10 | isolamento apos hop=4/Serializable | InvalidOperationException: A TransactionScope must be disposed on the same thread that it was created
COM AsyncFlowOption: Transaction.Current antes=presente depois=presente | thread 10->8 | isolamento apos hop=4/Serializable | Complete+Dispose ok

=== C) o nivel de isolamento vaza pelo pool de conexoes? ===
fora de qualquer escopo, apos os escopos Serializable -> 4/Serializable
dentro de escopo ReadCommitted                        -> 2/ReadCommitted
fora de escopo, logo depois do escopo ReadCommitted    -> 2/ReadCommitted
conexao NAO pooled (Pooling=false), fora de escopo     -> 2/ReadCommitted

FIM
```

VEREDITO POR AFIRMACAO:
- AFIRMACAO 1: **CONFIRMA**
- AFIRMACAO 2: **CONFIRMA**
- AFIRMACAO 3: **CONFIRMA** (achado novo, não estava na base)

VEREDITO GLOBAL: CONFIRMA

EVIDENCIA:
1. `sys.dm_exec_sessions.transaction_isolation_level` devolveu `2/ReadCommitted` sem escopo e `4/Serializable` dentro de `new TransactionScope()`, voltando a `2/ReadCommitted` quando `TransactionOptions { IsolationLevel = ReadCommitted }` foi passado; `TransactionManager.DefaultTimeout` observado = `00:01:00`.
2. Sem `AsyncFlowOption`, após um hop real de thread (8→10) `Transaction.Current` passou de `presente` a `null` e o `Dispose` lançou `InvalidOperationException: A TransactionScope must be disposed on the same thread that it was created`; com o flag, `Transaction.Current` permaneceu `presente` e `Complete+Dispose` completou.
3. Fora de qualquer escopo, uma conexão **pooled** relatou `4/Serializable` logo após os escopos Serializable, enquanto uma conexão com `Pooling=false` no mesmo instante relatou `2/ReadCommitted`; após um escopo ReadCommitted a conexão pooled voltou a `2`.

AMBIENTE: .NET 8.0.28; SQL Server 16.0.4265.3 Developer; banco `ReproDb`, nenhuma opção de banco alterada.

CORRECAO AO ITEM: o campo TESTE de `DDIA-07-A-4` propõe inspecionar `Transaction.Current` após `await Task.Yield()`. Executado exatamente assim, `Transaction.Current` permaneceu **presente** e o teste produziria REFUTA falso. O experimento discriminante exige hop real de thread (`await Task.Run(...)`), e o sintoma primário é a exceção no `Dispose`, não o valor de `Transaction.Current`.

ACHADO NOVO PARA A BASE: `SET TRANSACTION ISOLATION LEVEL` aplicado por uma transação enlistada persiste na conexão física devolvida ao pool, e o próximo tomador herda o nível. Consequência: um `TransactionScope` sem `TransactionOptions` num caminho de código eleva silenciosamente o isolamento de requisições posteriores não relacionadas que reusem a mesma conexão física. O controle `Pooling=false` isola a causa no pool, não no default do servidor.
