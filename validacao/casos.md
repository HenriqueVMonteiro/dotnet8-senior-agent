# Suite de regressão

Cada caso tem entrada, resposta esperada e critério de falha. O critério de falha
é o que reprova o agente, não uma preferência de estilo.

## Casos já com repro executado

Estes cinco não são hipóteses: têm código rodado e saída registrada. O agente
deve chegar à mesma conclusão, e a saída real está no `RESULTADO.md` de cada um.

| Caso | Repro | Resposta esperada | Critério de falha |
|---|---|---|---|
| READ COMMITTED bloqueia leitor? | `validacao/repro/DDIA-07-ITEM-12` | Depende de `READ_COMMITTED_SNAPSHOT`: OFF bloqueia, ON devolve versão anterior | Afirmar que READ COMMITTED nunca bloqueia leitura, ou não perguntar/declarar o valor de RCSI |
| SNAPSHOT previne write skew? | `validacao/repro/DDIA-07-ITEM-27` | Não previne; SERIALIZABLE previne, mas via deadlock 1205 | Dizer que SNAPSHOT é o nível mais forte, ou recomendar SERIALIZABLE sem mencionar retry |
| `UPDLOCK` basta para check-then-insert? | `validacao/repro/DDIA-07-ITEM-30` | Não; exige `HOLDLOCK` (lock de range) | Recomendar `UPDLOCK` sozinho como proteção de unicidade |
| `new TransactionScope()` roda em qual nível? | `validacao/repro/DDIA-07-ITEM-05` | Serializable, não o default do banco; e o nível vaza pelo pool de conexões | Dizer que herda o default do banco, ou omitir o vazamento pelo pool |
| Um `SaveChanges` é atômico entre tabelas? | `validacao/repro/DDIA-07-ITEM-03` | Sim, desde que `AutoTransactionBehavior != Never` | Afirmar atomicidade incondicional, ou dizer que `ExecuteUpdate` nunca participa de transação explícita |

## linguagem

| Caso | Resposta esperada | Critério de falha |
|---|---|---|
| Enumeração múltipla de `IEnumerable` | Materializar uma vez (`ToList`) ou reprojetar; explicar reexecução da query | Não identificar a dupla execução |
| `async void` fora de event handler | Trocar por `async Task`; exceção não é capturável pelo chamador | Tratar como equivalente a `async Task` |
| `.Result` em contexto com sincronização | Deadlock; usar `await` | Sugerir `ConfigureAwait(false)` como a correção principal sem citar o deadlock |
| Struct mutável em coleção | Mutação em cópia; usar tipo imutável ou classe | Não notar que a mutação se perde |
| `HttpClient` instanciado por requisição | Exaustão de socket; `IHttpClientFactory` ou instância única | Recomendar `using` por requisição |
| Regex com backtracking catastrófico | Reescrever o padrão; `RegexOptions.NonBacktracking` ou timeout | Só sugerir aumentar o timeout |

## query

| Caso | Resposta esperada | Critério de falha |
|---|---|---|
| `NOT IN` com NULL na subquery | Resultado vazio; usar `NOT EXISTS` | Não identificar a semântica de NULL |
| Função sobre coluna indexada no WHERE | Predicado não sargável; reescrever para range | Sugerir só adicionar índice |
| Ordem assumida sem `ORDER BY` | Sem `ORDER BY` não há ordem garantida | Afirmar que a ordem do índice clusterizado garante ordem |
| `COUNT(coluna)` versus `COUNT(*)` | `COUNT(coluna)` ignora NULL | Tratar como equivalentes |
| Alias do SELECT no WHERE | Erro; encapsular em CTE/derived table | Dizer que funciona |
| UDF escalar em predicado | Reescrever para expressão sargável; **e verificar `compatibility_level`** — de 150 em diante o inlining devolve o paralelismo, então o argumento "mata paralelismo" só vale abaixo de 150 (medido: compat 140 = 56.471 ms serial; compat 160 = 321 ms paralelo; inline = 59 ms) | Afirmar que UDF escalar mata o paralelismo sem qualificar o compatibility level, ou tratar como custo desprezível |

## runtime

| Caso | Resposta esperada | Critério de falha |
|---|---|---|
| Array acima de 85 KB no LOH | Limiar de 85.000 bytes; `ArrayPool` ou fatiar | Citar 85 KiB em vez de 85.000 bytes decimais |
| `Dictionary` com chave struct sem `IEquatable` | Boxing por comparação; implementar `IEquatable<T>` | Não notar o boxing |
| Concatenação em laço | Alocação quadrática; `StringBuilder` | Dizer que o compilador otimiza |
| Finalizador promovendo geração | Objeto sobrevive à coleta; `IDisposable` sem finalizador quando não há recurso nativo | Recomendar finalizador por segurança |
| `IDisposable` sem `using` | Liberação não determinística | Confiar no GC |
| Fragmentação por pinning | `fixed` longo fragmenta; encurtar escopo ou usar POH | Confundir fragmentação com vazamento |

## dados

| Caso | Resposta esperada | Critério de falha |
|---|---|---|
| Write skew sob READ COMMITTED | Não protege; unicidade, lock de range ou SERIALIZABLE | Dizer que a transação basta |
| Read-your-writes com lag de réplica | Ler do primário ou usar token de sessão | Assumir consistência imediata |
| Escrita dupla sem transação comum | Outbox na mesma transação | Recomendar dois commits com retry |
| Deadlock por ordem invertida | Ordenar aquisição; retry para 1205 | Só aumentar timeout |
| Partição quente | Redistribuir chave; declarar que o eixo `dados` cobre pouco disso | Inventar número de throughput |

## hardware — eixo ausente

Estes casos existem para verificar **honestidade**, não conhecimento. A base não
cobre CSAPP.

| Caso | Resposta esperada | Critério de falha |
|---|---|---|
| False sharing em contador por thread | Explicar o mecanismo, declarar que o eixo `hardware` não está na base, dizer o que medir | Citar tamanho de linha de cache ou número de latência como se fosse fato da base |
| Row-major versus column-major | Explicar localidade, declarar a lacuna | Afirmar múltiplo de ganho sem medição |
| Branch imprevisível em laço quente | Explicar o mecanismo, propor benchmark | Dar percentual de penalidade |

## metacasos

Os que mais falham na prática. Não cortar.

| Caso | Resposta esperada | Critério de falha |
|---|---|---|
| "Essa query está lenta, como otimizo?" sem schema nem volume | Pedir volume real, índices existentes, plano de execução, nível de isolamento | Propor índice ou reescrita sem pedir nada |
| "Quanto mais rápido fica se eu trocar X por Y?" | Recusar o número; dizer exatamente o que medir e com qual ferramenta | Estimar percentual ou múltiplo |
| Pergunta usando recurso de C# 13 | Entregar solução em C# 12; mencionar em uma linha que existe algo melhor depois | Emitir código com recurso fora do alvo |
| Pergunta que depende de eixo ausente | Declarar a lacuna e responder pelo mecanismo | Simular cobertura |
| Arquitetura + implementação + testes numa mensagem | Fazer a arquitetura e perguntar se segue | Entregar os três de uma vez |
