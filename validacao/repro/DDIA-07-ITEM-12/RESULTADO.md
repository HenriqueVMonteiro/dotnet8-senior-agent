# DDIA-07-ITEM-12

AFIRMACAO: READ COMMITTED no SQL Server é implementado por lock quando `READ_COMMITTED_SNAPSHOT` está OFF — o leitor espera o lock X do escritor — e por versão de linha quando está ON, devolvendo o valor anterior sem bloquear.

COMO RODAR: `cd validacao/repro/DDIA-07-ITEM-12 && dotnet run -c Release`

SAIDA OBSERVADA:
```
AMBIENTE runtime=.NET 8.0.28
AMBIENTE sqlserver=16.0.4265.3

--- RCSI=OFF (sys.databases.is_read_committed_snapshot_on=False) ---
ESCRITOR aplicou UPDATE Saldo=999 e NAO commitou
LEITOR BLOQUEADO: SqlException Number=-2 apos 4039ms

--- RCSI=ON (sys.databases.is_read_committed_snapshot_on=True) ---
ESCRITOR aplicou UPDATE Saldo=999 e NAO commitou
LEITOR retornou Saldo=100 em 14ms SEM BLOQUEIO

FIM
```

VEREDITO: CONFIRMA

EVIDENCIA: O mesmo `SELECT Saldo FROM dbo.Conta WHERE Id = 1`, contra o mesmo escritor não commitado, estourou o `CommandTimeout` de 4 s com `SqlException.Number = -2` em 4039 ms com RCSI OFF, e devolveu o valor anterior commitado `Saldo=100` em 14 ms com RCSI ON — bloqueio contra desvio de versão, no mesmo banco e mesma sessão de teste.

AMBIENTE: .NET 8.0.28; SQL Server 16.0.4265.3 Developer; banco dedicado `ReproDb_I12` criado e reconfigurado pelo próprio repro, sem alterar `ReproDb`.

NOTA: o valor `-2` é timeout de comando do SqlClient, não erro de lock; ele é a evidência do bloqueio porque o único fator alterado entre as duas execuções foi a opção de banco. `SET LOCK_TIMEOUT` produziria 1222 e serve como variante.
