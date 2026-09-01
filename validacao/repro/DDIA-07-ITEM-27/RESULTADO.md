# DDIA-07-ITEM-27

AFIRMACAO: `IsolationLevel.Snapshot` não impede write skew — duas transações que leem o mesmo agregado e escrevem em linhas diferentes commitam ambas e violam a invariante; SERIALIZABLE com índice sobre o predicado impede.

COMO RODAR: `cd validacao/repro/DDIA-07-ITEM-27 && dotnet run -c Release`

SAIDA OBSERVADA:
```
AMBIENTE runtime=.NET 8.0.28
AMBIENTE sqlserver=16.0.4265.3

--- SNAPSHOT (indice IX_Plantao_Turno_DePlantao presente) ---
A leu contagem=2
B leu contagem=2
A update -> ok | B update -> ok
commit A -> ok | commit B -> ok
RESULTADO final de plantao=0 -> invariante VIOLADA

--- SERIALIZABLE (indice IX_Plantao_Turno_DePlantao presente) ---
A leu contagem=2
B leu contagem=2
A update -> ok | B update -> SqlException 1205
commit A -> ok | commit B -> rollback
RESULTADO final de plantao=1 -> invariante PRESERVADA

FIM
```

VEREDITO: CONFIRMA

EVIDENCIA: Sob SNAPSHOT as duas transações leram `contagem=2`, atualizaram linhas distintas (`Id = 1` e `Id = 2`) e commitaram sem erro algum, deixando `de plantao=0`; o mesmo roteiro sob SERIALIZABLE fez a sessão B receber `SqlException 1205` e preservou `de plantao=1`.

AMBIENTE: .NET 8.0.28; SQL Server 16.0.4265.3 Developer; banco dedicado `ReproDb_I27` com `ALLOW_SNAPSHOT_ISOLATION ON`; índice `IX_Plantao_Turno_DePlantao` presente nos dois cenários.

NOTA ADICIONAL AO ITEM: a proteção sob SERIALIZABLE chegou como **deadlock 1205**, não como bloqueio limpo seguido de sucesso. Consequência prática: subir para SERIALIZABLE sem política de retry troca corrupção silenciosa por falha de requisição. Detalhe não presente no item reconciliado; deve ser incorporado na consolidação.
