# Armadilhas — DDIA Cap. 7 (Transactions) · Camada B · RECONCILIADO (Run A × Run B)

Alvo fixo: .NET 8 · C# 12 · EF Core 8 · SQL Server (T-SQL).

Entradas: `work/traps/DDIA-07-A.md` (19 armadilhas, framing "mecanismo") × `work/traps/DDIA-07-B.md` (15 armadilhas, framing "erro sênior").

Regras aplicadas nesta camada:

- **Não comprime.** Todo item de entrada aparece em algum grupo; nenhum exemplo concreto foi descartado.
- **`**IDs:**`** aponta para os itens de origem (`A:` = Run A, `B:` = Run B).
- **`**Acordo:** 2`** = as duas passadas encontraram a mesma armadilha. **`**Acordo:** 1`** = singleton, preservado sem desconto de confiança.
- **`## CONFLITO-{n}`** = as duas passadas descrevem a mesma armadilha e **discordam num detalhe factual**. As duas versões ficam íntegras, sem arbitragem, com a linha `QUESTAO EM DISPUTA:`.
- **`**A verificar:**`** = afirmação factual forte e testável sobre EF Core 8 / SQL Server que esta camada **não decidiu**. Alimenta a fase de verificação empírica.
- Onde as duas passadas iluminam facetas diferentes da mesma armadilha, os dois blocos `Aparece como` / `Correto` foram mantidos e rotulados por faceta; onde mostram a mesma coisa, ficou o exemplo mais preciso e mais curto.
- Numeração de `TRAP-` é nova e sequencial (1–22); os CONFLITOs têm numeração própria. Quando as passadas divergiram no EIXO, o item registra a divergência e adota o eixo onde mora a correção.

---

## TRAPS RECONCILIADAS

### TRAP-dados-1: RCSI-muda-o-significado-de-ReadCommitted-sem-mudar-uma-linha-de-codigo

**IDs:** A:TRAP-dados-1
**Acordo:** 1

**Aparece como:**
```csharp
using var tx = await db.Database.BeginTransactionAsync(IsolationLevel.ReadCommitted);
var conta = await db.Contas.SingleAsync(c => c.Id == id);   // lê Saldo
conta.Saldo -= valor;                                        // decide em C#
await db.SaveChangesAsync();
await tx.CommitAsync();
```
**O que acontece:** depois que alguém roda `ALTER DATABASE ... SET READ_COMMITTED_SNAPSHOT ON` para "acabar com o bloqueio", esse mesmo código passa a perder updates com frequência muito maior — sem alteração de código, de isolation level ou de deploy.

**Por quê:** com RCSI=OFF o SQL Server é uma das raríssimas engines que implementa read committed com lock: o SELECT pede lock S e fica *bloqueado atrás* do lock X de um escritor em voo; quando destrava, lê o valor já novo. Com RCSI=ON o SELECT não pede lock nenhum — pega a versão da linha no version store do tempdb, anterior à escrita em voo, e retorna imediatamente. A janela de leitura obsoleta deixa de ser "enquanto o outro segura o X" e passa a ser "toda a duração da transação concorrente". O acoplamento acidental que fazia o read-modify-write parecer correto era o bloqueio, não a transação.

**Correto:**
```csharp
// (a) mutação resolvida no servidor: leitura e escrita no mesmo statement, sob lock X
await db.Contas.Where(c => c.Id == id && c.Saldo >= valor)
    .ExecuteUpdateAsync(s => s.SetProperty(c => c.Saldo, c => c.Saldo - valor));

// (b) se a decisão exige C#, pedir o lock na leitura
var conta = await db.Contas
    .FromSql($"SELECT * FROM Contas WITH (UPDLOCK, ROWLOCK) WHERE Id = {id}")
    .SingleAsync();
```
**Detecta com:** `SELECT name, is_read_committed_snapshot_on, snapshot_isolation_state FROM sys.databases;` — e revisar todo read-modify-write em C# quando a flag estiver ON.

**Verificável:** sim — duas sessões: A abre transação e faz `UPDATE Contas SET Saldo=50 WHERE Id=1` sem commitar; B faz `SELECT Saldo FROM Contas WHERE Id=1`: com RCSI=OFF B bloqueia, com RCSI=ON B retorna o valor antigo na hora.

**A verificar:** Com RCSI=OFF, um `SELECT` em READ COMMITTED sobre uma linha com `UPDATE` não commitado de outra sessão bloqueia até o commit dela e devolve o valor novo; com RCSI=ON o mesmo `SELECT` retorna imediatamente o valor anterior?

**A verificar:** Com RCSI=ON, a janela em que a leitura devolve o valor obsoleto dura toda a transação concorrente, e não apenas enquanto o lock X é retido?

**A verificar:** Ligar RCSI aumenta mensuravelmente a taxa de lost update de um read-modify-write em C# que antes rodava com RCSI=OFF?

**Fonte:** DDIA-Chapter 7. Transactions

---

### TRAP-query-2: read-modify-write-em-memoria-vira-SET-de-valor-absoluto-e-perde-update

**IDs:** A:TRAP-query-6 + B:TRAP-dados-2
**Acordo:** 2
**Eixo divergente:** A=query, B=dados — adotado `query` (a correção é a reescrita do statement).

**Aparece como (faceta A — acumulador puro):**
```csharp
var contador = await db.Contadores.SingleAsync(c => c.Chave == "pedidos");
contador.Valor += 1;
await db.SaveChangesAsync();
```

**Aparece como (faceta B — débito com guarda):**
```csharp
var wallet = await db.Wallets.SingleAsync(w => w.Id == id);
if (wallet.Balance < amount) throw new InsufficientFundsException();
wallet.Balance -= amount;
await db.SaveChangesAsync();
```
**O que acontece:** (A) sob concorrência, N incrementos produzem menos de N — some dinheiro do jeito mais banal possível. (B) sob duas requisições concorrentes, um dos débitos some e o saldo final fica maior do que deveria.

**Por quê:** o `+=`/`-=` acontece na memória do processo. O EF materializa `UPDATE Contadores SET Valor = @p0 WHERE Id = @p1` / `UPDATE [Wallets] SET [Balance] = @p0 WHERE [Id] = @p1`, com `@p0` sendo um valor absoluto calculado a partir de uma leitura já potencialmente obsoleta — read-modify-write com o "modify" a um round-trip de distância do lock. READ COMMITTED — default do SQL Server — não impede isso: a segunda escrita ocorre *depois* do commit da primeira, então não é dirty write. É lost update, e nenhum isolamento abaixo de serializável o detecta sozinho.
— faceta A: o ORM torna esse erro mais fácil de escrever do que o correto.
— faceta B: o ORM esconde o ciclo read-modify-write atrás de um `-=`; a guarda `if (Balance < amount)` dá a impressão adicional de que a decisão foi validada, quando ela foi validada contra uma leitura obsoleta.

**Correto (faceta A):**
```csharp
// UPDATE Contadores SET Valor = Valor + 1 WHERE Chave = @p0
await db.Contadores.Where(c => c.Chave == "pedidos")
    .ExecuteUpdateAsync(s => s.SetProperty(c => c.Valor, c => c.Valor + 1));
```

**Correto (faceta B — guarda migrada para o predicado):**
```csharp
var rows = await db.Wallets
    .Where(w => w.Id == id && w.Balance >= amount)
    .ExecuteUpdateAsync(s => s.SetProperty(w => w.Balance, w => w.Balance - amount));
if (rows == 0) throw new InsufficientFundsException();
```
Gera `SET [Balance] = [Balance] - @amount WHERE [Id] = @id AND [Balance] >= @amount`: leitura, cálculo e escrita viram um único statement atômico no servidor.

**Detecta com:** grep no log do EF (`optionsBuilder.LogTo(...)`) por `SET [Coluna] = @p0` (constante) em vez de expressão sobre a própria coluna, especialmente em colunas acumuladoras; analisador de código sinalizando `+=`/`-=` em propriedade rastreada seguido de `SaveChanges`; teste de carga com `Parallel.ForAsync` de N incrementos comparando o total.

**Verificável:** sim — (A) 500 tarefas concorrentes incrementando a mesma linha; contar o valor final. (B) saldo inicial 200, 200 tasks paralelas debitando 1: com o primeiro bloco sobra saldo residual, com o segundo chega exatamente a 0.

**A verificar:** EF Core 8 emite `UPDATE ... SET [Valor] = @p0` (valor absoluto) para `contador.Valor += 1` sobre entidade rastreada, e `SET [Valor] = [Valor] + @p` para `ExecuteUpdateAsync(s => s.SetProperty(c => c.Valor, c => c.Valor + 1))`?

**A verificar:** Sob READ COMMITTED (RCSI=OFF, default on-prem), N incrementos concorrentes pelo caminho `+=`/`SaveChanges` produzem total < N, e pelo caminho `ExecuteUpdate` produzem exatamente N?

**Fonte:** DDIA-Chapter 7. Transactions

---

### TRAP-dados-3: Snapshot-nao-previne-write-skew-e-troca-deadlock-por-corrupcao

**IDs:** A:TRAP-dados-2 + B:TRAP-dados-8
**Acordo:** 2

**Aparece como:**
```csharp
await using var tx = await db.Database.BeginTransactionAsync(IsolationLevel.Snapshot);
var dePlantao = await db.Shifts.CountAsync(s => s.ShiftId == id && s.OnCall);
if (dePlantao >= 2)
{
    var meu = await db.Shifts.SingleAsync(s => s.ShiftId == id && s.DoctorId == me);
    meu.OnCall = false;
    await db.SaveChangesAsync();
}
await tx.CommitAsync();
```
**O que acontece:** dois médicos saem do plantão ao mesmo tempo e o turno fica vazio. Nenhuma exceção, nenhum 3960, nenhum deadlock — o invariante morre em silêncio.

**Por quê:** snapshot no SQL Server detecta conflito *write-write na mesma linha* e aborta com erro 3960. Aqui as duas transações escrevem linhas diferentes e só têm a *leitura* em comum, e leitura sob snapshot não deixa lock nem rastro que o motor possa cruzar no commit. Não há nada a detectar. Write skew é a quebra de uma invariante que atravessa várias linhas; só isolamento realmente serializável a previne.
— faceta A (o detalhe perverso): se o código antes rodava em `RepeatableRead`, o SQL Server segurava lock S nas duas linhas até o fim da transação, os dois UPDATEs precisavam converter S→X cruzado e o par morria em deadlock 1205 — falha barulhenta, retriável, correta. Migrar de RepeatableRead para Snapshot "para eliminar deadlocks" troca erro visível por corrupção silenciosa.
— faceta B (o nome): "snapshot" soa mais forte que "read committed" e o dev extrapola para "serializável".

**Correto (faceta A — range lock explícito, cobre linhas que ainda não existem):**
```csharp
using var tx = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable);
var emPlantao = await db.Plantoes.FromSql(
    $"SELECT * FROM Plantoes WITH (UPDLOCK, HOLDLOCK) WHERE TurnoId = {turnoId} AND EmPlantao = 1")
    .ToListAsync();                       // range lock cobre inclusive linhas que ainda não existem
if (emPlantao.Count >= 2) { /* UPDATE ... */ await db.SaveChangesAsync(); }
await tx.CommitAsync();
```

**Correto (faceta B — só o nível de isolamento):**
```csharp
await using var tx = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable);
var dePlantao = await db.Shifts.CountAsync(s => s.ShiftId == id && s.OnCall);
// ... mesmo corpo; agora o COUNT adquire RangeS-S sobre as chaves lidas
```
Exige índice em `ShiftId` e tratamento de deadlock (1205), que passa a ser resultado esperado sob contenção.

**Detecta com:** grep por `IsolationLevel.Snapshot` em torno de padrões `Count/Any/Sum` seguidos de `SaveChanges`; revisar toda transação SNAPSHOT/RCSI que decide por agregado e escreve em linha **diferente** da lida — esse é o formato canônico de write skew; `SELECT transaction_isolation_level FROM sys.dm_exec_sessions WHERE session_id=@@SPID` (5 = snapshot); Extended Event `lock_acquired` confirma a ausência de `RangeS-S`.

**Verificável:** sim — duas sessões em snapshot com `WAITFOR DELAY '00:00:02'` após o `COUNT`: ambas contam 2 plantonistas, cada uma atualiza a sua linha, ambas commitam e `SELECT COUNT(*) WHERE EmPlantao=1` devolve 0; sob SERIALIZABLE uma bloqueia ou é escolhida vítima de deadlock.

**A verificar:** Duas transações `IsolationLevel.Snapshot` que leem o mesmo agregado e atualizam linhas **diferentes** ambas commitam sem 3960?

**A verificar:** O mesmo par de transações sob `IsolationLevel.RepeatableRead` termina em deadlock 1205 (conversão S→X cruzada) em vez de corromper o invariante — isto é, a migração RepeatableRead→Snapshot troca 1205 por corrupção silenciosa?

**A verificar:** Sob `IsolationLevel.Serializable`, o `CountAsync` traduzido pelo EF Core 8 adquire locks `RangeS-S` sobre as chaves lidas (com índice em `ShiftId`) e impede o write skew?

**Fonte:** DDIA-Chapter 7. Transactions

---

### TRAP-query-4: check-then-insert-nao-e-protegido-nem-por-transacao-nem-por-snapshot

**IDs:** A:TRAP-query-8 + B:TRAP-query-3
**Acordo:** 2

**Aparece como (faceta A — sob Snapshot):**
```csharp
using var tx = await db.Database.BeginTransactionAsync(IsolationLevel.Snapshot);
bool conflita = await db.Reservas.AnyAsync(r => r.SalaId == 123
    && r.Fim > inicio && r.Inicio < fim);
if (!conflita)
    db.Reservas.Add(new Reserva { SalaId = 123, Inicio = inicio, Fim = fim });
await db.SaveChangesAsync();
await tx.CommitAsync();
```

**Aparece como (faceta B — sob a transação default, READ COMMITTED):**
```csharp
await using var tx = await db.Database.BeginTransactionAsync();
var conflita = await db.Bookings.AnyAsync(b =>
    b.RoomId == roomId && b.End > start && b.Start < end);
if (!conflita)
{
    db.Bookings.Add(new Booking { RoomId = roomId, Start = start, End = end });
    await db.SaveChangesAsync();
}
await tx.CommitAsync();
```
**O que acontece:** duas requisições simultâneas criam reservas sobrepostas na mesma sala. Nenhuma exceção em lado nenhum; a transação explícita não impediu nada.

**Por quê:** a consulta verifica *ausência* de linhas — não retornou linha alguma, então não existe objeto onde pendurar lock. É a definição de fantasma: a inserção da outra transação muda o resultado do predicado depois que o `if` já foi avaliado.
— faceta A: Snapshot garante que a leitura é *consistente*, não que ela continue *verdadeira*; e a detecção de conflito write-write não dispara, porque as duas transações inserem linhas distintas. O padrão "checar ausência e então inserir" é imune a todas as proteções de MVCC por construção.
— faceta B: `BeginTransaction` dá atomicidade (A), não isolamento serializável (I) — o default continua READ COMMITTED. O dev lê "estou numa transação" e conclui "estou protegido".

**Correto (faceta A — o invariante vira constraint):**
```csharp
// CREATE UNIQUE INDEX UX_ReservaSlot ON ReservaSlot(SalaId, InicioSlot);
db.ReservaSlots.AddRange(SlotsDe(inicio, fim)
    .Select(s => new ReservaSlot { SalaId = 123, InicioSlot = s }));
try { await db.SaveChangesAsync(); }
catch (DbUpdateException e) when (e.InnerException is SqlException { Number: 2601 or 2627 })
{ return Results.Conflict("horário já reservado"); }
```

**Correto (faceta B — o predicado vira key-range lock):**
```sql
BEGIN TRAN;
SELECT TOP (1) 1 FROM dbo.Bookings WITH (UPDLOCK, HOLDLOCK)
WHERE RoomId = @roomId AND [End] > @start AND [Start] < @end;
IF @@ROWCOUNT = 0
    INSERT dbo.Bookings (RoomId, [Start], [End]) VALUES (@roomId, @start, @end);
COMMIT;
```
`HOLDLOCK` transforma o predicado num key-range lock, que trava a faixa mesmo vazia. Exige índice em `(RoomId, [Start])` para o range ficar estreito (ver TRAP-query-6).

**Detecta com:** grep por `AnyAsync`/`CountAsync` seguido de `Add`/`AddRange` no mesmo bloco; auditoria de constraints — se o invariante não existe como índice único ou nível serializable, ele não existe; Extended Event `lock_acquired` na sessão: sem `HOLDLOCK` nenhum lock de modo `RangeS-U`/`RangeI-N` aparece na tabela.

**Verificável:** sim — duas requisições disparadas com `Task.WhenAll` no mesmo intervalo (ou duas sessões com `WAITFOR DELAY '00:00:02'` entre o `SELECT` e o `INSERT`): sem `HOLDLOCK` gravam as duas, com `HOLDLOCK` uma bloqueia ou vira vítima de deadlock.

**A verificar:** `AnyAsync` + `Add` + `SaveChanges` dentro de uma transação `Snapshot` (A) e dentro de uma transação default READ COMMITTED (B) permitem, nos dois casos, duas reservas sobrepostas sem exceção alguma?

**A verificar:** `SELECT ... WITH (UPDLOCK, HOLDLOCK)` sobre um predicado de intervalo **vazio** produz locks `RangeS-U`/`RangeI-N` e faz a segunda sessão bloquear ou virar vítima 1205?

**Fonte:** DDIA-Chapter 7. Transactions

---

### TRAP-query-5: UPDLOCK-em-resultado-vazio-nao-trava-nada

**IDs:** B:TRAP-query-7
**Acordo:** 1

**Aparece como:**
```sql
BEGIN TRAN;
DECLARE @id INT;
SELECT @id = UserId FROM dbo.Usernames WITH (UPDLOCK)
WHERE Name = @name;
IF @id IS NULL
    INSERT dbo.Usernames (Name, UserId) VALUES (@name, @userId);
COMMIT;
```
**O que acontece:** duas sessões inserem o mesmo `@name`. O `UPDLOCK` — adicionado justamente para "fechar a corrida" — não impediu nada.

**Por quê:** `UPDLOCK` trava as linhas **retornadas**. Zero linhas retornadas, zero recursos travados. O hint funciona quando a decisão depende de linhas que existem (ex.: "há pelo menos dois médicos de plantão"), e falha quando depende da *ausência* de linhas. Travar ausência exige lock de range (`HOLDLOCK`/SERIALIZABLE) sobre a chave de índice, que é a aproximação prática de um predicate lock.

**Correto:**
```sql
CREATE UNIQUE INDEX UX_Usernames_Name ON dbo.Usernames(Name);
```
```csharp
db.Usernames.Add(new Username { Name = name, UserId = userId });
try { await db.SaveChangesAsync(); }
catch (DbUpdateException e) when (e.InnerException is SqlException { Number: 2601 or 2627 })
{ throw new UsernameTakenException(name); }
```
Para unicidade pontual, a constraint é mais barata e mais confiável que qualquer hint; `HOLDLOCK` só é necessário quando o predicado é um intervalo.

**Detecta com:** Extended Event `lock_acquired` filtrado por `mode`: só aparecem `U`/`X` de linha, nenhum `RangeI-N`. Também: revisar todo `WITH (UPDLOCK)` cujo `SELECT` possa retornar vazio.

**Verificável:** sim — duas sessões com `WAITFOR DELAY '00:00:02'` entre `SELECT` e `INSERT`: sem índice único, duas linhas; com índice, uma recebe 2601.

**A verificar:** `SELECT ... WITH (UPDLOCK) WHERE Name = @name` que retorna zero linhas adquire algum lock de range (`RangeI-N`) ou nenhum recurso — isto é, duas sessões conseguem inserir o mesmo `@name`?

**A verificar:** `UPDLOCK` **sem** `HOLDLOCK` sobre um resultado **não vazio** basta para serializar as duas sessões (contraste com o caso vazio)?

**Fonte:** DDIA-Chapter 7. Transactions

---

### TRAP-query-6: Serializable-sem-indice-adequado-tranca-a-tabela-inteira

**IDs:** A:TRAP-query-9 + B:TRAP-query-13
**Acordo:** 2

**Aparece como:**
```csharp
// sem índice em (SalaId, Inicio)
using var tx = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable);
bool livre = !await db.Reservas.AnyAsync(r => r.SalaId == 123
    && r.Fim > inicio && r.Inicio < fim);
if (livre) { db.Reservas.Add(nova); await db.SaveChangesAsync(); }
await tx.CommitAsync();
```
**O que acontece:** a correção funciona e o throughput desaba: reservas de salas *diferentes* passam a se bloquear e a produzir deadlocks entre si.

**Por quê:** serializable no SQL Server é 2PL com key-range lock, e um key-range lock precisa de uma chave de índice onde se ancorar. Sem índice sobre o predicado o plano vira scan, e o motor tranca todo o range varrido do índice clusterizado — na prática a tabela inteira, até o commit. É o fallback descrito no capítulo: lock compartilhado na tabela é seguro e catastrófico. O isolamento continua correto e a concorrência vira zero.
— faceta A: consequência que inverte a intuição — sob serializable, criar o índice não é otimização opcional, é o que define a *granularidade da correção*. O mesmo vale para `HOLDLOCK` manual.
— faceta B: é a armadilha do dev que acertou o diagnóstico (write skew / check-then-insert, TRAP-query-4) e implementou a cura sem olhar o plano.

**Correto (faceta A — índice de cobertura para o predicado de intervalo):**
```sql
CREATE INDEX IX_Reservas_Sala_Periodo ON dbo.Reservas (SalaId, Inicio) INCLUDE (Fim);
-- agora o RangeS-S cobre apenas SalaId=123 na faixa varrida, e salas distintas não colidem
```

**Correto (faceta B — índice único, que ainda serve de rede independente do isolamento):**
```sql
CREATE UNIQUE INDEX UX_Bookings_Room_Day ON dbo.Bookings(RoomId, [Day]);
```
O range lock passa a cobrir só as chaves de `@roomId`, e a unicidade ainda serve de rede de segurança independente do nível de isolamento.

**Detecta com:** `SELECT resource_type, request_mode FROM sys.dm_tran_locks WHERE request_session_id=@@SPID;` procurando `OBJECT`/`RangeS-S` em vez de `KEY`; `SET STATISTICS XML ON` na consulta serializável — `Clustered Index Scan` em vez de `Index Seek` é o sinal; waits `LCK_M_RS_S` em `sys.dm_os_wait_stats`.

**Verificável:** sim — rodar a transação serializable e inspecionar `sys.dm_tran_locks` antes do commit, com e sem o índice; rodar o bloco em duas sessões com `roomId` diferentes: sem o índice a segunda bloqueia, com o índice as duas passam.

**A verificar:** Sob `IsolationLevel.Serializable` sem índice no predicado, `sys.dm_tran_locks` mostra range/`OBJECT` lock sobre o índice clusterizado inteiro, e duas sessões com `SalaId`/`RoomId` **diferentes** se bloqueiam mutuamente?

**A verificar:** Criado o índice em `(SalaId, Inicio)` / `(RoomId, Day)`, os locks passam a ser `KEY`/`RangeS-S` restritos à faixa e as duas sessões de salas diferentes passam sem bloquear?

**Fonte:** DDIA-Chapter 7. Transactions

---

### TRAP-dados-7: deadlock-de-conversao-entre-duas-transacoes-que-tocam-uma-linha-so

**IDs:** A:TRAP-dados-10
**Acordo:** 1

**Aparece como:**
```csharp
using var tx = await db.Database.BeginTransactionAsync(IsolationLevel.RepeatableRead);
var estoque = await db.Estoques.SingleAsync(e => e.Sku == sku);   // lock S, mantido até o fim
if (estoque.Qtd >= n)
{
    estoque.Qtd -= n;
    await db.SaveChangesAsync();                                  // precisa de X na mesma linha
}
await tx.CommitAsync();
```
**O que acontece:** sob concorrência no mesmo SKU, uma fração grande das requisições morre com deadlock 1205 — e o deadlock envolve duas transações que tocam **uma única linha**, sem ordem de acesso invertida em lugar nenhum.

**Por quê:** sob repeatable read/serializable o lock S da leitura é retido até o commit (é o "two-phase" do 2PL). Dois processos leem a mesma linha: locks S são compatíveis entre si, ambos passam. Na hora do UPDATE cada um precisa converter S→X, e a conversão só acontece quando ninguém mais segura S — cada um está esperando o outro soltar. Deadlock de conversão. Toda a heurística de "sempre acesse as tabelas na mesma ordem" é inútil aqui, porque só existe uma linha.

**Correto:**
```csharp
using var tx = await db.Database.BeginTransactionAsync(IsolationLevel.RepeatableRead);
var estoque = await db.Estoques                    // U é compatível com S, incompatível com U:
    .FromSql($"SELECT * FROM Estoques WITH (UPDLOCK, ROWLOCK) WHERE Sku = {sku}")
    .SingleAsync();                                // serializa os leitores-que-vão-escrever
if (estoque.Qtd >= n) { estoque.Qtd -= n; await db.SaveChangesAsync(); }
await tx.CommitAsync();
```
**Detecta com:** XEvent `xml_deadlock_report` na sessão `system_health` — procurar deadlock cujo `waitresource` é o mesmo dos dois lados, com `requestMode="X"` e `owner-list` em `S`.

**Verificável:** sim — duas sessões em repeatable read fazendo SELECT na mesma linha e depois UPDATE: uma vira vítima 1205 de forma determinística.

**A verificar:** Duas sessões em `RepeatableRead` que fazem `SELECT` e depois `UPDATE` na **mesma linha** produzem deadlock 1205 de conversão de forma determinística (`waitresource` idêntico dos dois lados, `requestMode="X"`, `owner-list` em `S`)?

**A verificar:** Trocar a leitura por `WITH (UPDLOCK, ROWLOCK)` elimina o deadlock e serializa os leitores-que-vão-escrever?

**Fonte:** DDIA-Chapter 7. Transactions

---

### TRAP-query-8: ExecuteUpdate-ignora-o-token-de-concorrencia-e-o-change-tracker

**IDs:** A:TRAP-query-5 + B:TRAP-dados-4
**Acordo:** 2
**Eixo divergente:** A=query, B=dados — adotado `query` (a correção é a reescrita do predicado da query).
**Nota:** A:TRAP-query-5 descreve duas armadilhas num item só; a segunda está em TRAP-dados-9.

**Aparece como:**
```csharp
// Product.RowVersion mapeado com [Timestamp] / IsRowVersion()
var rows = await db.Products
    .Where(p => p.Id == id)
    .ExecuteUpdateAsync(s => s.SetProperty(p => p.Price, newPrice));
```
**O que acontece:** a verificação de versão simplesmente não é emitida, o `UPDATE` sobrescreve alterações concorrentes em silêncio, e `DbUpdateConcurrencyException` nunca é lançada — apesar de o token de concorrência existir e estar mapeado.

**Por quê:** quem injeta `AND [RowVersion] = @original` no `WHERE` é o change tracker, dentro de `SaveChanges`. `ExecuteUpdate` compila para um `UPDATE ... FROM ... WHERE` executado direto no servidor (`UPDATE [p] SET [Price] = @p0 FROM [Products] AS [p] WHERE [p].[Id] = @id`), sem passar pelo tracker: não existe *original value* de versão para injetar no WHERE nem verificação de linhas afetadas. A proteção contra lost update desaparece sem aviso, no exato ponto em que o código parece mais "eficiente".
— faceta A: toda entidade já rastreada continua com o valor antigo em memória — um `SaveChanges` posterior sobre ela regrava o estado velho por cima.
— faceta B: é o pior caso de armadilha — o time adota `rowversion` para todo o domínio e depois "otimiza" as rotas quentes trocando `SaveChanges` por `ExecuteUpdate`, apagando a proteção sem alterar uma linha do modelo.

**Correto (faceta B — token no predicado, `rowversion`/`byte[]`):**
```csharp
var rows = await db.Products
    .Where(p => p.Id == id && p.RowVersion == loadedRowVersion)
    .ExecuteUpdateAsync(s => s.SetProperty(p => p.Price, newPrice));
if (rows == 0)
    throw new DbUpdateConcurrencyException("Produto alterado por outra transação.");
```

**Correto (faceta A — token `int` incrementado no mesmo statement, dentro de transação explícita):**
```csharp
using var tx = await db.Database.BeginTransactionAsync();
var n = await db.Pedidos.Where(p => p.Id == id && p.Versao == versaoLida)   // Versao: int
    .ExecuteUpdateAsync(s => s.SetProperty(p => p.Status, Status.Pago)
                              .SetProperty(p => p.Versao, p => p.Versao + 1));
if (n == 0) throw new DbUpdateConcurrencyException("versão obsoleta");
await db.SaveChangesAsync();
await tx.CommitAsync();
```
**Detecta com:** capturar o SQL gerado (`LogTo` + `EnableSensitiveDataLogging`) e conferir a ausência da coluna de versão no WHERE; buscar `ExecuteUpdateAsync`/`ExecuteDeleteAsync` sobre entidades que declaram `IsRowVersion()`.

**Verificável:** sim — ler a entidade em duas sessões, aplicar `ExecuteUpdateAsync` em ambas e verificar que nenhuma falha, contra o mesmo teste com `SaveChanges` (que falha na segunda); ou carregar a entidade, alterar a linha por outra conexão e rodar o `ExecuteUpdate`: o primeiro bloco devolve 1 (deveria ser conflito), o segundo devolve 0.

**A verificar:** `ExecuteUpdateAsync` sobre entidade com `[Timestamp]`/`IsRowVersion()` emite SQL **sem** `AND [RowVersion] = @original`, permitindo que duas sessões sobrescrevam uma à outra sem `DbUpdateConcurrencyException`?

**A verificar:** EF Core 8 traduz `Where(p => p.RowVersion == loadedRowVersion)` com `RowVersion` do tipo `byte[]` para `WHERE [RowVersion] = @p` dentro de `ExecuteUpdateAsync` (versão B), ou só o token `int` (versão A) é traduzível?

**A verificar:** Entidades já rastreadas mantêm o valor antigo em memória depois de um `ExecuteUpdateAsync`, e um `SaveChangesAsync` posterior sobre elas regrava o estado velho por cima?

**Fonte:** DDIA-Chapter 7. Transactions

---

### TRAP-dados-9: ExecuteUpdate-nao-entra-na-transacao-do-SaveChanges

**IDs:** A:TRAP-query-5 + B:TRAP-dados-5
**Acordo:** 2
**Eixo divergente:** A=query, B=dados — adotado `dados` (a correção é o limite transacional, não o statement).
**Nota:** A:TRAP-query-5 descreve duas armadilhas num item só; a primeira está em TRAP-query-8.

**Aparece como:**
```csharp
db.Emails.Add(new Email { RecipientId = uid, Unread = true });
await db.SaveChangesAsync();
await db.Users.Where(u => u.Id == uid)
    .ExecuteUpdateAsync(s => s.SetProperty(u => u.UnreadCount, u => u.UnreadCount + 1));
```
**O que acontece:** se a segunda chamada falhar, o e-mail fica gravado e o contador não — o denormalizado diverge de forma permanente, sem erro visível depois. Na variante A (`ExecuteUpdate` do status seguido de `SaveChanges` do resto do agregado), se o `SaveChanges` seguinte falhar o status já está pago e commitado.

**Por quê:** `SaveChanges` abre a própria transação e a fecha. `ExecuteUpdate` executa imediatamente, em autocommit separado, e não inicia transação implícita. São duas unidades atômicas independentes, não uma. O dev vê duas linhas de código no mesmo método e assume um único commit; a única pista está no log de `BEGIN TRAN`.

**Correto:**
```csharp
await using var tx = await db.Database.BeginTransactionAsync();
db.Emails.Add(new Email { RecipientId = uid, Unread = true });
await db.SaveChangesAsync();
await db.Users.Where(u => u.Id == uid)
    .ExecuteUpdateAsync(s => s.SetProperty(u => u.UnreadCount, u => u.UnreadCount + 1));
await tx.CommitAsync();
```
**Detecta com:** Extended Event `database_transaction_begin` — o bloco errado produz duas transações distintas; XEvent `sql_batch_completed` mostrando dois `BEGIN TRAN` distintos; ou um `DbCommandInterceptor` logando `@@TRANCOUNT` antes de cada comando.

**Verificável:** sim — forçar exceção na segunda chamada (ex.: `CHECK (UnreadCount <= 0)` temporário) e conferir que a linha em `Emails` sobreviveu.

**A verificar:** `ExecuteUpdateAsync` chamado sem transação explícita executa em autocommit próprio — duas `BEGIN TRAN` distintas no XEvent `database_transaction_begin` — sem iniciar transação implícita que englobe o `SaveChanges` anterior?

**A verificar:** `ExecuteUpdateAsync` chamado depois de `BeginTransactionAsync()` no mesmo `DbContext` participa daquela transação (um rollback desfaz o update)?

**Fonte:** DDIA-Chapter 7. Transactions

---

### TRAP-query-10: ExecuteUpdate-em-lote-unico-escala-para-lock-de-tabela-por-volta-de-5000-linhas

**IDs:** A:TRAP-query-19
**Acordo:** 1

**Aparece como:**
```csharp
// arquivar tudo de uma vez parece a versão eficiente (um round-trip, sem tracking)
await db.Pedidos.Where(p => p.Data < corte)          // casa 300 mil linhas
    .ExecuteUpdateAsync(s => s.SetProperty(p => p.Arquivado, true));
```
**O que acontece:** um único statement bloqueia a tabela inteira para escrita durante minutos; as requisições concorrentes acumulam em lock wait e estouram command timeout em cascata, incluindo caminhos de negócio que não têm relação com o arquivamento.

**Por quê:** o motor adquire locks X por linha e, ao passar de cerca de 5.000 locks em um mesmo objeto (ou sob pressão de memória de lock), escala para um lock X no objeto inteiro. Como é uma única transação implícita, esse lock só cai no commit final — a duração do bloqueio é a duração do statement, não a de uma linha. Sob RCSI os leitores escapam (leem versões), o que torna o problema mais difícil de enxergar em monitoramento de leitura, enquanto todo escritor da tabela para. É exatamente o cenário do capítulo: uma transação que toca muitos dados e segura muitos locks derruba o percentil alto do sistema todo.

**Correto:**
```csharp
int n;
do
{
    var lote = await db.Pedidos.Where(p => p.Data < corte && !p.Arquivado)
        .OrderBy(p => p.Id).Select(p => p.Id).Take(2000).ToListAsync();
    n = await db.Pedidos.Where(p => lote.Contains(p.Id))
        .ExecuteUpdateAsync(s => s.SetProperty(p => p.Arquivado, true));
} while (n > 0);
```
**Detecta com:** XEvent `lock_escalation`; `SELECT resource_type, request_mode FROM sys.dm_tran_locks WHERE request_session_id = <spid>` mostrando `OBJECT`/`X`; waits `LCK_M_X` em `sys.dm_os_wait_stats`.

**Verificável:** sim — rodar o `ExecuteUpdateAsync` grande e, em outra sessão, consultar `sys.dm_tran_locks` procurando um lock X de `resource_type = OBJECT`.

**A verificar:** Um `ExecuteUpdateAsync` único que casa ~300 mil linhas dispara `lock_escalation` e produz um lock `X` de `resource_type = OBJECT` mantido até o fim do statement?

**A verificar:** O limiar de escalonamento é ~5.000 locks no mesmo objeto?

**A verificar:** Sob RCSI, leitores concorrentes continuam passando durante esse statement enquanto todo escritor da tabela bloqueia?

**A verificar:** Lotes de 2.000 linhas ficam abaixo do limiar e não escalam?

**Fonte:** DDIA-Chapter 7. Transactions

---

### TRAP-dados-11: rowversion-sob-Snapshot-nunca-lanca-DbUpdateConcurrencyException

**IDs:** A:TRAP-dados-4
**Acordo:** 1

**Aparece como:**
```csharp
public class Pedido { public int Id { get; set; } public int Qtd { get; set; }
                      [Timestamp] public byte[] Versao { get; set; } = default!; }
// ...
using var tx = await db.Database.BeginTransactionAsync(IsolationLevel.Snapshot);
pedido.Qtd = novaQtd;
try { await db.SaveChangesAsync(); }
catch (DbUpdateConcurrencyException) { await ReconciliarAsync(); }   // nunca executa
await tx.CommitAsync();
```
**O que acontece:** o handler de concorrência nunca roda; o que sobe é um `DbUpdateException` embrulhando `SqlException 3960`, tratado lá em cima como erro 500.

**Por quê:** o compare-and-set do EF é o predicado `WHERE Id=@id AND Versao=@v`. Sob snapshot, esse predicado é avaliado contra a versão da linha no snapshot, onde `Versao` ainda é a antiga — então a linha *casa*. O caminho "0 linhas afetadas", único sinal que o EF traduz em `DbUpdateConcurrencyException`, não acontece. Quando o motor vai efetivar a escrita, percebe que a linha foi alterada e commitada depois do início do snapshot e aborta a transação inteira com 3960. É literalmente o aviso do capítulo: compare-and-set cujo WHERE lê de um snapshot antigo não é compare-and-set.

**Correto:**
```csharp
try { await db.SaveChangesAsync(); }
catch (DbUpdateConcurrencyException) { await ReconciliarAsync(); }
catch (DbUpdateException e) when (e.InnerException is SqlException { Number: 3960 })
{ await ReconciliarAsync(); }        // mesmo evento de negócio, outra roupa do motor
```
**Detecta com:** log estruturado de `SqlException.Number`; contador `SQLServer:Transactions → Update conflict ratio` subindo enquanto a métrica de conflito otimista da aplicação fica em zero.

**Verificável:** sim — abrir transação snapshot, ler a entidade, atualizar a linha por outra sessão, e observar qual exceção o `SaveChangesAsync` produz.

**A verificar:** Dentro de uma transação `IsolationLevel.Snapshot`, `SaveChangesAsync` sobre entidade com `[Timestamp]` cuja linha foi alterada e commitada por outra sessão depois do início do snapshot lança `DbUpdateException`/`SqlException 3960` — e **nunca** `DbUpdateConcurrencyException` (0 linhas afetadas)?

**A verificar:** O predicado `WHERE Id=@id AND Versao=@v` é avaliado contra a versão da linha **no snapshot** (e portanto casa), e não contra a linha corrente?

**Fonte:** DDIA-Chapter 7. Transactions

---

### TRAP-query-12: leituras-multi-statement-sem-transacao-veem-pontos-no-tempo-diferentes

**IDs:** A:TRAP-query-7 + B:TRAP-query-6
**Acordo:** 2

**Aparece como (faceta A — uma query LINQ que virou N SELECTs por configuração global):**
```csharp
// virou default global do projeto para matar a explosão cartesiana
o.UseSqlServer(cs, s => s.UseQuerySplittingBehavior(QuerySplittingBehavior.SplitQuery));
// ...
var pedido = await db.Pedidos.Include(p => p.Itens).Include(p => p.Pagamentos)
                             .SingleAsync(p => p.Id == id);
var diferenca = pedido.Itens.Sum(i => i.Valor) - pedido.Pagamentos.Sum(x => x.Valor);
```

**Aparece como (faceta B — duas leituras explícitas somadas):**
```csharp
var corrente  = await db.Accounts.SingleAsync(a => a.Id == 1);
var poupanca  = await db.Accounts.SingleAsync(a => a.Id == 2);
return corrente.Balance + poupanca.Balance;
```
**O que acontece:** (A) o agregado pode conter itens de um instante e pagamentos de outro; a conciliação acusa diferenças que não existem em nenhum estado real do banco. (B) o total "perde" dinheiro em trânsito — lê a conta de origem antes da transferência e a de destino depois.

**Por quê:** sem transação explícita cada statement é sua própria transação autocommit e enxerga um ponto no tempo próprio — read skew clássico. READ COMMITTED não protege: cada leitura viu dados commitados, só que de *commits diferentes*; ele garante snapshot **por statement**, não por unidade de trabalho.
— faceta A: split query emite três SELECTs independentes. É o mecanismo do backup inconsistente do capítulo, comprimido em milissegundos, e a mudança que o introduziu foi uma otimização de performance que ninguém revisou como mudança de semântica transacional.
— faceta B: ligar RCSI não resolve, porque RCSI também é por statement — a diferença entre `READ_COMMITTED_SNAPSHOT` e `ALLOW_SNAPSHOT_ISOLATION` é exatamente essa. O `DbContext` é um cache de identidade; ele não congela ponto no tempo nenhum.

**Correto (faceta A):**
```csharp
using var tx = await db.Database.BeginTransactionAsync(IsolationLevel.Snapshot);
var pedido = await db.Pedidos.Include(p => p.Itens).Include(p => p.Pagamentos)
                             .SingleAsync(p => p.Id == id);
await tx.CommitAsync();   // só leitura, mas os 3 SELECTs vêm do mesmo snapshot
```

**Correto (faceta B):**
```csharp
await using var tx = await db.Database.BeginTransactionAsync(IsolationLevel.Snapshot);
var corrente = await db.Accounts.SingleAsync(a => a.Id == 1);
var poupanca = await db.Accounts.SingleAsync(a => a.Id == 2);
await tx.CommitAsync();
return corrente.Balance + poupanca.Balance;
```
Requer `ALTER DATABASE [App] SET ALLOW_SNAPSHOT_ISOLATION ON;` — sem isso o `BeginTransaction` falha com erro 3952 no primeiro comando.

**Detecta com:** log do EF mostrando N comandos para uma única query LINQ sem `BEGIN TRANSACTION` entre eles; buscar `UseQuerySplittingBehavior` e `AsSplitQuery` em código que soma/concilia coleções irmãs; contar transações por requisição no Extended Event `database_transaction_begin` — toda leitura multi-entidade que alimenta um agregado deveria contar 1.

**Verificável:** sim — (A) carregar o agregado em loop enquanto outra sessão insere item e pagamento na mesma transação; observar a diferença aparecer. (B) duas contas com 500, um loop em outra conexão transferindo 100 de ida e volta, e o método acima em loop: sem transação aparecem totais 900 e 1100; com `Snapshot`, sempre 1000.

**A verificar:** Com `QuerySplittingBehavior.SplitQuery` e sem transação, os N SELECTs de uma **única** query LINQ rodam em autocommits distintos e podem devolver coleções irmãs de pontos no tempo diferentes?

**A verificar:** RCSI dá consistência por statement e não por unidade de trabalho — duas leituras sequenciais fora de transação divergem mesmo com `READ_COMMITTED_SNAPSHOT ON`?

**A verificar:** Sem `ALLOW_SNAPSHOT_ISOLATION ON`, `BeginTransactionAsync(IsolationLevel.Snapshot)` falha com erro **3952** no primeiro comando?

**Fonte:** DDIA-Chapter 7. Transactions

---

### TRAP-dados-13: o-mapa-de-identidade-do-EF-simula-repeatable-read-que-o-banco-nunca-prometeu

**IDs:** A:TRAP-dados-15
**Acordo:** 1

**Aparece como:**
```csharp
var p = await db.Pedidos.SingleAsync(x => x.Id == id);       // Status = Pendente
await ProcessarPagamentoExternoAsync(p);                     // 800 ms; outro worker cancela
var atual = await db.Pedidos.SingleAsync(x => x.Id == id);   // "reler para conferir"
if (atual.Status == Status.Cancelado) return;                // nunca entra
await ConfirmarAsync(p);
```
**O que acontece:** a segunda consulta vai ao banco de verdade, o servidor devolve `Cancelado` — e o EF **descarta** esse resultado: `atual` é a mesmíssima instância de `p`, ainda `Pendente`.

**Por quê:** o EF Core resolve identidade pelo change tracker. Se a chave já está rastreada, a query devolve a instância existente e não sobrescreve os valores current/original com os do banco. Isso dá ao código a *ilusão* de repeatable read sem nenhum snapshot no servidor: os bytes novos vieram pelo fio, foram materializados e jogados fora no cliente. A releitura defensiva — o instrumento com que o dev tenta se proteger de dado obsoleto — é justamente o que nunca funciona em contexto de vida longa (worker, contexto scoped reutilizado, handler que faz I/O no meio).

**Correto:**
```csharp
await db.Entry(p).ReloadAsync();      // aplica os valores do banco na instância rastreada
if (p.Status == Status.Cancelado) return;

// ou, para consulta pura, sair do mapa de identidade:
var atual = await db.Pedidos.AsNoTracking().SingleAsync(x => x.Id == id);
```
**Detecta com:** log do EF mostra o SELECT sendo emitido enquanto o objeto em memória não muda; `db.ChangeTracker.Entries<Pedido>().Count()` > 0 antes da releitura; `GetDatabaseValues()` divergindo de `CurrentValues`.

**Verificável:** sim — carregar a entidade, alterar a linha por fora (SSMS), reconsultar pelo mesmo `DbContext` e comparar com `AsNoTracking`.

**A verificar:** Um segundo `SingleAsync` pela mesma chave, no mesmo `DbContext` com a entidade já rastreada, devolve a instância rastreada com os valores **antigos** (descartando os bytes que vieram do servidor), enquanto `AsNoTracking()` devolve os valores novos?

**A verificar:** `Entry(p).ReloadAsync()` sobrescreve `CurrentValues` **e** `OriginalValues` da instância rastreada com os do banco?

**Fonte:** DDIA-Chapter 7. Transactions

---

### TRAP-dados-14: TransactionScope-sem-TransactionOptions-e-Serializable

**IDs:** A:TRAP-dados-11 + B:TRAP-dados-1
**Acordo:** 2

**Aparece como (faceta A — construtor vazio, método async):**
```csharp
using var scope = new TransactionScope();       // parece só "agrupar operações"
db.Pedidos.Add(pedido);
await db.SaveChangesAsync();
await outroRepositorio.GravarAsync(pedido.Id);
scope.Complete();
```

**Aparece como (faceta B — async flow ligado, isolamento ainda default):**
```csharp
using var scope = new TransactionScope(TransactionScopeAsyncFlowOption.Enabled);
var order = await db.Orders.SingleAsync(o => o.Id == id);
order.Status = OrderStatus.Paid;
await db.SaveChangesAsync();
scope.Complete();
```
**O que acontece:** todo statement dentro do escopo roda em SERIALIZABLE, com range locks, em código que ninguém revisou como transacional; e o bloco morre com `TransactionAbortedException` aos 60 segundos. Na faceta A, em método async, a transação ambiente ainda pode não fluir através do `await`.

**Por quê:** o construtor sem `TransactionOptions` herda `TransactionManager.DefaultIsolationLevel`, que devolve `IsolationLevel.Serializable`, e `TransactionManager.DefaultTimeout`, que é 1 minuto — herança do modelo do MSDTC, não do SQL Server, cujo default é read committed. O provider aplica esse nível na conexão ao enlistar, então um bloco escrito "só para agrupar" transforma o caminho quente em 2PL completo: em SQL Server, SERIALIZABLE é 2PL com key-range locks — o `SELECT` passa a segurar lock compartilhado de range até o commit, leitores bloqueiam escritores, range locks aparecem em predicados sem índice, e as latências de percentil alto colapsam sob contenção. Exatamente o contrário do que o dev assume ao ver `TransactionScope` como "só um agrupador".
— faceta A: somando, `TransactionScope` guarda a transação ambiente em `Transaction.Current`, ligada ao contexto de execução; sem `TransactionScopeAsyncFlowOption.Enabled`, o que roda depois do primeiro `await` pode não ver a transação ambiente, e o `Dispose` em outra thread lança.

**Correto:**
```csharp
var opts = new TransactionOptions
{
    IsolationLevel = System.Transactions.IsolationLevel.ReadCommitted,  // não é System.Data
    Timeout = TimeSpan.FromSeconds(10)
};
using var scope = new TransactionScope(TransactionScopeOption.Required, opts,
                                       TransactionScopeAsyncFlowOption.Enabled);
```
**Detecta com:** `SELECT transaction_isolation_level FROM sys.dm_exec_sessions WHERE session_id = @@SPID;` (4 = serializable, 2 = read committed); grep por `new TransactionScope(` sem `TransactionOptions`; ou Extended Event `lock_acquired` mostrando modos `RangeS-S`.

**Verificável:** sim — dentro do escopo, executar a query de `sys.dm_exec_sessions` pelo mesmo `DbContext` (`db.Database.SqlQuery<int>`) e ler o nível efetivo: 4 com o construtor sem `TransactionOptions`, 2 com `TransactionOptions` explícito.

**A verificar:** `new TransactionScope()` **e** `new TransactionScope(TransactionScopeAsyncFlowOption.Enabled)` fazem `SELECT transaction_isolation_level FROM sys.dm_exec_sessions WHERE session_id=@@SPID` devolver 4 (Serializable), e com `TransactionOptions{IsolationLevel=ReadCommitted}` devolver 2?

**A verificar:** `TransactionManager.DefaultIsolationLevel` é `Serializable` e `TransactionManager.DefaultTimeout` é 1 minuto, com o escopo abortando em `TransactionAbortedException` aos ~60 s?

**A verificar:** Sem `TransactionScopeAsyncFlowOption.Enabled`, o código após o primeiro `await` deixa de ver `Transaction.Current` e o `Dispose` lança?

**Fonte:** DDIA-Chapter 7. Transactions

---

### TRAP-dados-15: o-segundo-DbContext-do-escopo-de-DI-nao-participa-da-sua-transacao

**IDs:** A:TRAP-dados-17
**Acordo:** 1

**Aparece como:**
```csharp
using var tx = await db.Database.BeginTransactionAsync();
db.Pedidos.Add(pedido);
await db.SaveChangesAsync();
await _auditoria.RegistrarAsync(pedido.Id);   // usa outro DbContext injetado
await tx.CommitAsync();
```
**O que acontece:** a auditoria é commitada na hora, fora da transação. Se o `CommitAsync` falhar ou o processo morrer, fica registro de auditoria de um pedido que não existe — e nenhum aviso é emitido em momento algum.

**Por quê:** transação em banco relacional é propriedade da **conexão**, não do processo, da requisição nem do escopo de DI. O agrupamento é feito pelo servidor entre o `BEGIN` e o `COMMIT` *daquela sessão*. Cada `DbContext` registrado no container abre a sua própria `SqlConnection`, portanto sua própria sessão: a transação iniciada em um é literalmente invisível para o outro. Os dois "funcionam", os dois gravam, e o escopo de DI dá a impressão sintática de uma unidade de trabalho que não existe no servidor.

**Correto:**
```csharp
var conn = db.Database.GetDbConnection();
await using var auditDb = new AuditDb(new DbContextOptionsBuilder<AuditDb>()
    .UseSqlServer(conn).Options);                    // mesma conexão física
using var tx = await db.Database.BeginTransactionAsync();
await auditDb.Database.UseTransactionAsync(tx.GetDbTransaction());
// alternativa mais simples: um único DbContext para toda a unidade de trabalho
```
**Detecta com:** `SELECT session_id, open_transaction_count FROM sys.dm_exec_sessions WHERE login_name = ...` mostrando dois SPIDs da mesma requisição; `sys.dm_tran_session_transactions` com dois `session_id` distintos.

**Verificável:** sim — lançar exceção depois do `RegistrarAsync` e antes do `CommitAsync`, e conferir que a linha de auditoria sobrevive.

**A verificar:** Dois `DbContext` distintos do mesmo escopo de DI abrem `SqlConnection`/SPIDs distintos, e a transação aberta em um **não** cobre as escritas do outro (a auditoria sobrevive a uma exceção antes do `CommitAsync`)?

**A verificar:** `UseTransactionAsync(tx.GetDbTransaction())` sobre um segundo `DbContext` construído com a **mesma** `DbConnection` faz as duas escritas caírem na mesma transação?

**Fonte:** DDIA-Chapter 7. Transactions

---

### TRAP-dados-16: retry-dentro-do-mesmo-SqlTransaction-depois-do-1205

**IDs:** A:TRAP-dados-12
**Acordo:** 1

**Aparece como:**
```csharp
using var tx = await db.Database.BeginTransactionAsync();
for (var i = 0; i < 3; i++)
{
    try { await db.SaveChangesAsync(); break; }
    catch (SqlException e) when (e.Number == 1205) { await Task.Delay(50); }
}
await tx.CommitAsync();
```
**O que acontece:** a segunda tentativa não roda dentro de transação nenhuma, e o commit final estoura `InvalidOperationException` ("This SqlTransaction has completed") ou `SqlException 3903`.

**Por quê:** 1205 é erro abortante de transação: o servidor já desfez e *encerrou* a transação da sessão escolhida como vítima antes de devolver o erro ao cliente. O objeto `IDbContextTransaction` do lado do .NET é apenas uma casca sobre um `BEGIN` que não existe mais — comandos emitidos depois rodam em autocommit (gravando de verdade, sem proteção) e o `COMMIT` não encontra par. Pior: o change tracker permanece intacto, então o retry reenvia exatamente os mesmos INSERTs; se parte deles tiver escapado em autocommit, você duplica linhas. Retry é uma operação sobre a *unidade de trabalho inteira*, nunca sobre um statement dentro de uma transação já morta.

**Correto:**
```csharp
var strategy = db.Database.CreateExecutionStrategy();   // 1205 já é transiente no provider
await strategy.ExecuteAsync(async () =>
{
    await using var ctx = await factory.CreateDbContextAsync();
    await using var tx = await ctx.Database.BeginTransactionAsync();
    await AplicarMudancasAsync(ctx);
    await ctx.SaveChangesAsync();
    await tx.CommitAsync();
});
```
**Detecta com:** procurar `catch` de `SqlException`/`DbUpdateException` dentro de um bloco `using` de transação; nos logs, `InvalidOperationException: This SqlTransaction has completed` e erro 3903.

**Verificável:** sim — provocar um deadlock com duas sessões e observar que o `CommitAsync` posterior falha em vez de commitar o retry.

**A verificar:** Depois de 1205 na sessão vítima, comandos emitidos pelo mesmo `IDbContextTransaction` rodam em autocommit (gravam de verdade) e o `CommitAsync` seguinte lança `InvalidOperationException` / `SqlException 3903`?

**A verificar:** O change tracker permanece intacto após o 1205, de modo que a segunda tentativa reenvia os mesmos INSERTs e pode duplicar linhas?

**Fonte:** DDIA-Chapter 7. Transactions

---

### TRAP-dados-17: retry-de-DbUpdateConcurrencyException-sem-reload

**IDs:** B:TRAP-dados-15
**Acordo:** 1

**Aparece como:**
```csharp
for (var i = 0; i < 3; i++)
{
    try { await db.SaveChangesAsync(); break; }
    catch (DbUpdateConcurrencyException) { /* conflito: tenta de novo */ }
}
```
**O que acontece:** as três tentativas falham exatamente igual; e como o `catch` engole a exceção, o método retorna sucesso sem ter gravado nada.

**Por quê:** o `WHERE [RowVersion] = @original` é montado a partir do `OriginalValues` do change tracker, que continua com o valor lido no início. Reexecutar `SaveChanges` reenvia byte a byte o mesmo `UPDATE`, que continua afetando 0 linhas. Retry só faz sentido quando a premissa é relida — abortar-e-tentar-de-novo pressupõe reconstruir a decisão, não repetir a escrita.

**Correto:**
```csharp
catch (DbUpdateConcurrencyException ex)
{
    var entry = ex.Entries.Single();
    await entry.ReloadAsync();                        // relê linha e token
    if (entry.State == EntityState.Detached) throw;   // apagada por outra transação
    entry.CurrentValues[nameof(Product.Price)] = Recalcular(entry);
}
```
**Detecta com:** log do SQL de cada tentativa — texto e parâmetros idênticos denunciam o problema; e revisão de todo `catch (DbUpdateConcurrencyException)` que não toque em `ex.Entries` nem em `Reload`.

**Verificável:** sim — carregar a entidade, alterar a linha por outra conexão, chamar o bloco: o errado lança/engole três vezes, o correto comita na segunda tentativa.

**A verificar:** Reexecutar `SaveChangesAsync` após `DbUpdateConcurrencyException`, sem `Reload`, reenvia SQL e parâmetros idênticos (mesmo `@original` vindo de `OriginalValues`) e afeta 0 linhas nas três tentativas?

**A verificar:** `entry.ReloadAsync()` sobre uma linha apagada por outra transação deixa `entry.State == EntityState.Detached`?

**Fonte:** DDIA-Chapter 7. Transactions

---

### TRAP-dados-18: retry-cego-em-commit-ambiguo-duplica

**IDs:** B:TRAP-dados-11
**Acordo:** 1

**Aparece como:**
```csharp
await Policy.Handle<SqlException>().RetryAsync(3).ExecuteAsync(async () =>
{
    db.ChangeTracker.Clear();
    db.Payments.Add(new Payment { OrderId = orderId, Amount = amount });
    await db.SaveChangesAsync();
});
```
**O que acontece:** sob perda de rede no ACK do commit, o pagamento é gravado duas vezes.

**Por quê:** a falha cai na janela entre "servidor comitou" e "cliente recebeu confirmação". O cliente não distingue "não comitou" de "comitou e a resposta se perdeu" — são o mesmo `SqlException` do ponto de vista dele. Retry resolve o primeiro caso e duplica no segundo. Nenhum isolamento ajuda: o problema é de exatamente-uma-vez ponta a ponta, não de concorrência.

**Correto:**
```sql
CREATE UNIQUE INDEX UX_Payments_RequestId ON dbo.Payments(RequestId);
```
```csharp
db.Payments.Add(new Payment { OrderId = orderId, Amount = amount, RequestId = requestId });
try { await db.SaveChangesAsync(); }
catch (DbUpdateException e) when (e.InnerException is SqlException { Number: 2601 or 2627 })
{ /* uma tentativa anterior já aplicou; sucesso idempotente */ }
```
`requestId` precisa ser gerado **antes** da primeira tentativa e reutilizado por todas.

**Detecta com:** query de auditoria — `SELECT OrderId, Amount, COUNT(*) FROM dbo.Payments GROUP BY OrderId, Amount HAVING COUNT(*) > 1;`

**Verificável:** sim — um `DbTransactionInterceptor` que lança `SqlException` em `TransactionCommitted` simula o ACK perdido: o bloco errado insere duas linhas, o correto insere uma.

**A verificar:** Um `DbTransactionInterceptor` que lança em `TransactionCommitted` reproduz o ACK perdido — o retry insere a segunda linha, e o índice único em `RequestId` a converte em 2601/2627 (transformando o retry em sucesso idempotente)?

**Fonte:** DDIA-Chapter 7. Transactions

---

### TRAP-dados-19: timeout-de-comando-nao-e-transitorio-para-o-EF

**IDs:** B:TRAP-dados-10
**Acordo:** 1

**Aparece como:**
```csharp
services.AddDbContext<AppDb>(o =>
    o.UseSqlServer(cs, s => s.EnableRetryOnFailure(maxRetryCount: 5)));
```
**O que acontece:** deadlock é reexecutado cinco vezes, mas timeout de comando (`SqlException.Number == -2`) sobe direto ao chamador sem nenhuma tentativa — e é ele que domina os incidentes.

**Por quê:** no `SqlServerTransientExceptionDetector` do EF Core 8, o `case -2:` está **comentado** no código-fonte; o método só devolve `true` extra para `ex is TimeoutException`, e o SqlClient lança `SqlException` com `Number = -2`, não `TimeoutException`. Em SQL Server on-prem, `READ_COMMITTED_SNAPSHOT` vem **desligado** por default (ao contrário do Azure SQL Database), então leitor bloqueado por escritor é o modo normal de espera — e a espera termina no `CommandTimeout` de 30 s, gerando exatamente o erro que o retry ignora.

**Correto:**
```csharp
o.UseSqlServer(cs, s => s.EnableRetryOnFailure(
    maxRetryCount: 5,
    maxRetryDelay: TimeSpan.FromSeconds(5),
    errorNumbersToAdd: new[] { -2 }));
```
E reduzir a espera na origem: `SET LOCK_TIMEOUT 2000` faz o bloqueio falhar como 1222 (já transitório) em 2 s em vez de queimar 30 s de espera assíncrona.

**Detecta com:** contar `SqlException.Number == -2` na telemetria e cruzar com o contador de retries do EF; ou simplesmente ler `SqlServerTransientExceptionDetector.ShouldRetryOn` da versão do provider em uso.

**Verificável:** sim — abrir uma transação que atualiza uma linha e não comitar; do app, ler a mesma linha com `CommandTimeout = 2`: a exceção sobe em ~2 s, sem as 5 tentativas.

**A verificar:** No EF Core 8, `SqlServerTransientExceptionDetector.ShouldRetryOn` retorna `false` para `SqlException.Number == -2` (o `case -2:` está comentado no fonte) e `true` apenas para `ex is TimeoutException`?

**A verificar:** SqlClient lança `SqlException` com `Number = -2` (e **não** `TimeoutException`) em command timeout?

**A verificar:** `READ_COMMITTED_SNAPSHOT` vem **desligado** por default em SQL Server on-prem e **ligado** por default no Azure SQL Database?

**A verificar:** `errorNumbersToAdd: new[] { -2 }` faz o `EnableRetryOnFailure` passar a retentar command timeout?

**A verificar:** `SET LOCK_TIMEOUT 2000` converte a espera de lock em `SqlException 1222` em ~2 s, e 1222 é retentado pela estratégia?

**Fonte:** DDIA-Chapter 7. Transactions

---

### TRAP-dados-20: sem-XACT_ABORT-um-erro-de-statement-nao-desfaz-a-transacao-e-o-COMMIT-executa

**IDs:** A:TRAP-dados-13
**Acordo:** 1

**Aparece como:**
```sql
CREATE PROCEDURE dbo.RegistrarBaixa @PedidoId int AS
BEGIN
  BEGIN TRANSACTION;
    INSERT INTO dbo.Baixas (PedidoId, Valor) VALUES (@PedidoId, 100);
    UPDATE dbo.Pedidos SET Status = 3 WHERE Id = @PedidoId;   -- viola CHECK e falha
  COMMIT TRANSACTION;
END
```
**O que acontece:** chamada por `await db.Database.ExecuteSqlRawAsync("EXEC dbo.RegistrarBaixa @p0", id)` sem transação do EF, o UPDATE falha, o COMMIT executa mesmo assim e a baixa fica gravada sozinha — enquanto o .NET recebe exceção e o chamador conclui que nada foi persistido.

**Por quê:** no SQL Server, por padrão, a maioria dos erros de runtime (violação de constraint, conversão, overflow aritmético) aborta apenas o **statement**, não o batch nem a transação. A execução continua na próxima instrução do batch — e a próxima instrução é o `COMMIT`. Atomicidade em T-SQL não vem de graça com `BEGIN TRANSACTION`: ela depende de `SET XACT_ABORT ON` ou de TRY/CATCH com ROLLBACK explícito. O "A" do ACID aqui é uma escolha de configuração de sessão.

**Correto:**
```sql
CREATE PROCEDURE dbo.RegistrarBaixa @PedidoId int AS
BEGIN
  SET XACT_ABORT ON;            -- qualquer erro de runtime aborta a transação inteira
  BEGIN TRANSACTION;
    INSERT INTO dbo.Baixas (PedidoId, Valor) VALUES (@PedidoId, 100);
    UPDATE dbo.Pedidos SET Status = 3 WHERE Id = @PedidoId;
  COMMIT TRANSACTION;
END
```
**Detecta com:** buscar procedures com `BEGIN TRAN` em `sys.sql_modules` sem `XACT_ABORT` nem `BEGIN TRY` (`SELECT object_name(object_id), definition FROM sys.sql_modules WHERE definition LIKE '%BEGIN TRAN%' AND definition NOT LIKE '%XACT_ABORT%'`).

**Verificável:** sim — criar o CHECK que faz o UPDATE falhar, chamar a proc e contar linhas em `Baixas`.

**A verificar:** Sem `SET XACT_ABORT ON` e sem TRY/CATCH, uma violação de CHECK no `UPDATE` aborta só o statement, o `COMMIT TRANSACTION` seguinte executa e a linha inserida antes fica gravada — enquanto o cliente .NET recebe exceção?

**A verificar:** `SET XACT_ABORT ON` faz o mesmo erro abortar a transação inteira, deixando `Baixas` vazia?

**Fonte:** DDIA-Chapter 7. Transactions

---

### TRAP-dados-21: log-de-erro-dentro-do-CATCH-em-transacao-dopada-apaga-o-erro-original

**IDs:** A:TRAP-dados-14
**Acordo:** 1

**Aparece como:**
```sql
BEGIN TRY
  BEGIN TRANSACTION;
    INSERT INTO dbo.Pedidos (Id, ClienteId) VALUES (1, 999);   -- FK inválida
  COMMIT TRANSACTION;
END TRY
BEGIN CATCH
  INSERT INTO dbo.ErroLog (Msg) VALUES (ERROR_MESSAGE());      -- estoura 3930 aqui
  ROLLBACK TRANSACTION;
END CATCH
```
**O que acontece:** o CATCH lança um segundo erro, o registro de auditoria nunca é gravado, e o que chega no .NET é `SqlException 3930` — a causa real (violação de FK) desaparece do incidente.

**Por quê:** certos erros deixam a transação **uncommittable** (dopada): `XACT_STATE()` retorna -1 e o motor recusa qualquer operação que escreva no log de transações até que haja ROLLBACK. Um INSERT de auditoria dentro do CATCH é exatamente uma dessas operações. O código de tratamento de erro é, ele próprio, o que destrói a informação do erro — e o padrão "logar antes de dar rollback" parece a ordem óbvia.

**Correto:**
```sql
BEGIN CATCH
  DECLARE @msg nvarchar(4000) = ERROR_MESSAGE(), @num int = ERROR_NUMBER();
  IF XACT_STATE() <> 0 ROLLBACK TRANSACTION;      -- -1 dopada, 1 ativa: os dois exigem rollback
  INSERT INTO dbo.ErroLog (Msg, Numero) VALUES (@msg, @num);   -- fora da transação morta
  THROW;                                           -- devolve o erro original ao .NET
END CATCH
```
**Detecta com:** ocorrências de `SqlException.Number == 3930` no log da aplicação; grep por `BEGIN CATCH` com INSERT/UPDATE antes do `ROLLBACK`.

**Verificável:** sim — inserir com FK inválida dentro do TRY e observar 3930 chegando no lugar de 547.

**A verificar:** Violação de FK dentro de `BEGIN TRY` / `BEGIN TRANSACTION` deixa `XACT_STATE()` em -1 (uncommittable)?

**A verificar:** Um `INSERT` de auditoria dentro do `BEGIN CATCH` com `XACT_STATE() = -1` lança `SqlException 3930` e substitui o erro original (547) na exceção que chega ao .NET?

**Fonte:** DDIA-Chapter 7. Transactions

---

### TRAP-dados-22: transacao-aberta-durante-I-O-externo

**IDs:** A:TRAP-dados-18 + B:TRAP-dados-12
**Acordo:** 2

**Aparece como (faceta A — transação snapshot só de leitura, longa):**
```csharp
using var tx = await db.Database.BeginTransactionAsync(IsolationLevel.Snapshot);
await foreach (var p in db.Pedidos.Where(p => p.Pendente).AsAsyncEnumerable())
    await _gateway.EnviarAsync(p);         // 200 ms de HTTP por item, 50 mil itens
await tx.CommitAsync();
```

**Aparece como (faceta B — transação de escrita com efeito externo no meio):**
```csharp
await using var tx = await db.Database.BeginTransactionAsync();
db.Orders.Add(order);
await db.SaveChangesAsync();
var receipt = await _gateway.ChargeAsync(order.Total);   // HTTP, fora do banco
order.ReceiptId = receipt.Id;
await db.SaveChangesAsync();
await tx.CommitAsync();
```
**O que acontece:** (A) por três horas o tempdb cresce sem parar e **todas** as escritas do banco — inclusive de tabelas que esse job nem toca — ficam mais lentas; no limite, o tempdb enche e transações alheias falham. (B) quando o commit falha, o cartão continua debitado; e enquanto o HTTP corre, os locks exclusivos das linhas já gravadas seguem retidos.

**Por quê:** a transação cobre só o banco, e o custo de mantê-la aberta é pago por terceiros.
— faceta A (version store): sob snapshot/RCSI o SQL Server mantém no version store do tempdb toda versão de linha necessária ao snapshot ativo mais antigo, e a limpeza avança apenas até a transação mais antiga viva. Uma transação snapshot aberta por horas congela o ponteiro de limpeza: cada UPDATE de qualquer outra transação, em qualquer tabela, empilha versões que não podem ser recolhidas. Inverte a intuição confortável de que "leitura sob snapshot não atrapalha ninguém": ela não pega lock nenhum, mas o custo dela é pago pelo resto do servidor, em escrita, o tempo todo.
— faceta B (locks + efeito irreversível): rollback não desfaz efeito externo. Pior: sob 2PL (e sob o lock de escrita de qualquer nível de isolamento), o lock X adquirido no primeiro `SaveChanges` é mantido até o commit — ou seja, pela duração inteira de uma chamada de rede que não tem limite superior conhecido. A transação vira tão longa quanto o pior p99 de um serviço de terceiros, e todas as escritas concorrentes naquelas linhas enfileiram atrás dela.

**Correto (faceta A — materializar e sair da transação antes do I/O):**
```csharp
List<int> ids;
using (var tx = await db.Database.BeginTransactionAsync(IsolationLevel.Snapshot))
{
    ids = await db.Pedidos.Where(p => p.Pendente).Select(p => p.Id).ToListAsync();
    await tx.CommitAsync();
}
foreach (var id in ids) await _gateway.EnviarAsync(id);   // I/O externo fora da transação
```

**Correto (faceta B — outbox transacional):**
```csharp
await using var tx = await db.Database.BeginTransactionAsync();
db.Orders.Add(order);
db.Outbox.Add(new OutboxMessage { Type = "Charge", Payload = json, OrderId = order.Id });
await db.SaveChangesAsync();
await tx.CommitAsync();
// worker separado consome o Outbox e chama o gateway, com chave de idempotência
```
**Detecta com:** `sys.dm_tran_version_store_space_usage`; `sys.dm_tran_active_snapshot_database_transactions` ordenado por `elapsed_time_seconds`; contadores `SQLServer:Transactions → Version Store Size (KB)` e `Longest Transaction Running Time`; `sys.dm_tran_active_transactions` cruzado com `sys.dm_exec_sessions` procurando transações abertas em sessões `sleeping`; ou um `DbTransactionInterceptor` medindo a duração entre `TransactionStarted` e `TransactionCommitted` e alertando acima de ~100 ms.

**Verificável:** sim — (A) abrir a transação snapshot, deixá-la parada, gerar carga de UPDATE em outra sessão e acompanhar o crescimento em `sys.dm_tran_version_store_space_usage`. (B) injetar 5 s de latência no gateway e observar em `sys.dm_tran_locks` os locks `X` mantidos pela sessão durante todo o delay.

**A verificar:** Uma transação `Snapshot` aberta e ociosa impede a limpeza do version store, de modo que `sys.dm_tran_version_store_space_usage` cresce com UPDATEs de **outras** sessões e **outras** tabelas?

**A verificar:** Os locks `X` adquiridos pelo primeiro `SaveChangesAsync` permanecem em `sys.dm_tran_locks` durante toda a chamada HTTP (5 s injetados), até o `CommitAsync`?

**Fonte:** DDIA-Chapter 7. Transactions

---

## CONFLITOS

Nenhuma contradição abaixo foi arbitrada. As duas versões estão íntegras; a `QUESTAO EM DISPUTA` é o que a fase empírica precisa resolver.

---

## CONFLITO-1: EnableRetryOnFailure — o que exatamente e reexecutado, e qual e o dano

**IDs:** A:TRAP-dados-3 + B:TRAP-dados-9
**Acordo:** 2 quanto à existência da armadilha (o retry apaga o conflito que o motor detectou de propósito) — **contestado** quanto ao mecanismo e ao resultado.
**Eixo:** dados (as duas passadas concordam no eixo).

**Convergem em:** `SqlServerTransientExceptionDetector` do EF Core 8 classifica 1205, 1222 e 3960 (A acrescenta 601) como transitórios; o abort que o motor emitiu *para forçar releitura* é tratado como falha de infraestrutura e replayado; a premissa nunca é relida; o saldo final fica errado sem exceção visível.

**Divergem em:** o que a estratégia reexecuta (o delegate inteiro × só a operação que falhou), qual erro dispara o caso (3960 × 1205), e o que acontece com o valor gravado (o decremento se **acumula** × o **mesmo** `@p0` é reenviado).

---

### Versão A — `A:TRAP-dados-3: EnableRetryOnFailure-reexecuta-o-delegate-mas-nao-a-leitura`

**Aparece como:**
```csharp
var strategy = db.Database.CreateExecutionStrategy();     // EnableRetryOnFailure ligado
await strategy.ExecuteAsync(async () =>
{
    using var tx = await db.Database.BeginTransactionAsync(IsolationLevel.Snapshot);
    var c = await db.Contas.SingleAsync(x => x.Id == id); // Saldo = 100
    c.Saldo -= 30;                                        // decide 70
    await db.SaveChangesAsync();                          // 3960 se outro commitou
    await tx.CommitAsync();
});
```
**O que acontece:** o SQL Server aborta com 3960 (a proteção contra lost update funcionando), o EF Core retenta o delegate — e o retry aplica o `-= 30` de novo sobre a mesma instância já mutada, gravando 40 a partir de uma leitura que nunca foi refeita.

**Por quê:** dois mecanismos se somam. (1) O erro 3960 está na lista de transientes do `SqlServerTransientExceptionDetector` do EF Core 8, junto com 1205, 1222 e 601 — ou seja, o abort que o motor emitiu *justamente para forçar você a reler* é classificado como falha de infraestrutura e replayado. (2) O `DbContext` sobrevive entre tentativas: a entidade continua rastreada com `Saldo = 70`, e o `SingleAsync` da segunda tentativa devolve a instância do change tracker sem sobrescrever com os valores do banco. A releitura é um no-op e a decisão se acumula.

**Correto:**
```csharp
await strategy.ExecuteAsync(async () =>
{
    await using var ctx = await factory.CreateDbContextAsync();   // estado zerado por tentativa
    using var tx = await ctx.Database.BeginTransactionAsync(IsolationLevel.Snapshot);
    var c = await ctx.Contas.SingleAsync(x => x.Id == id);        // leitura de verdade
    c.Saldo -= 30;
    await ctx.SaveChangesAsync();
    await tx.CommitAsync();
});
```
**Detecta com:** `LogTo` no nível `Information` (evento `ExecutionStrategyRetrying`) mostrando retentativa sem novo SELECT com valores diferentes; contador perfmon `SQLServer:Transactions → Update conflict ratio`.

**Verificável:** sim — forçar 3960 com uma segunda sessão que atualiza a linha entre o SELECT e o SaveChanges, e conferir se o saldo final caiu 30 ou 60.

**Fonte:** DDIA-Chapter 7. Transactions

---

### Versão B — `B:TRAP-dados-9: enableretryonfailure-converte-deadlock-em-lost-update`

**Aparece como:**
```csharp
services.AddDbContext<AppDb>(o => o.UseSqlServer(cs, s => s.EnableRetryOnFailure()));
// ...
var wallet = await db.Wallets.SingleAsync(w => w.Id == id);
wallet.Balance -= amount;
await db.SaveChangesAsync();   // deadlock (1205) é reexecutado silenciosamente
```
**O que acontece:** os deadlocks somem dos logs da aplicação e o saldo passa a ficar errado. O time comemora a "correção" da instabilidade.

**Por quê:** `SqlServerTransientExceptionDetector` (EF Core 8) classifica 1205 (deadlock), 1222 (lock timeout) e 3960 (conflito de snapshot) como transitórios. A `SqlServerRetryingExecutionStrategy` reexecuta **a operação que falhou** — o `SaveChangesAsync` — e não o `SingleAsync` que produziu a premissa. O `UPDATE` reenviado carrega o mesmo `@p0`, calculado a partir de uma leitura agora obsoleta justamente porque a transação vencedora do deadlock alterou a linha. O banco tinha detectado o conflito; o retry o apagou.

**Correto:**
```csharp
var strategy = db.Database.CreateExecutionStrategy();
await strategy.ExecuteAsync(async () =>
{
    db.ChangeTracker.Clear();                       // a premissa precisa ser relida
    var w = await db.Wallets.SingleAsync(x => x.Id == id);
    w.Balance -= amount;
    await db.SaveChangesAsync();
});
```
Melhor ainda: tornar a unidade atômica no servidor (`ExecuteUpdate` com expressão sobre a coluna), aí o retry é seguro por construção.

**Detecta com:** comparar a contagem de `xml_deadlock_report` no servidor com a contagem de exceções 1205 na telemetria da app — divergência grande significa retry cego; e o evento `CoreEventId.ExecutionStrategyRetrying` no log do EF.

**Verificável:** sim — duas transações tocando as mesmas duas linhas em ordem invertida, em loop; comparar o saldo final com a soma esperada dos débitos.

**Fonte:** DDIA-Chapter 7. Transactions

---

**QUESTAO EM DISPUTA:** quando um `SaveChangesAsync` **não** envolvido em `strategy.ExecuteAsync` falha com erro transitório sob `EnableRetryOnFailure`, a `SqlServerRetryingExecutionStrategy` reexecuta **apenas esse `SaveChangesAsync`** (versão B) ou o retry só existe quando há um delegate e então **o delegate inteiro**, incluindo o `SingleAsync`, é reexecutado (versão A)?

**QUESTAO EM DISPUTA (secundária):** com o mesmo `DbContext` reutilizado entre tentativas e a entidade já rastreada com o valor mutado, a segunda tentativa grava um valor **acumulado** (100 → 70 → 40, versão A) ou reenvia o **mesmo** `@p0` = 70 (versão B)?

**QUESTAO EM DISPUTA (secundária):** `db.ChangeTracker.Clear()` no início do delegate (versão B) é suficiente para forçar releitura real, ou um `DbContext` novo por tentativa (versão A) é necessário?

---

## CONFLITO-2: NOLOCK — o mecanismo do salto/duplicacao e o que basta para corrigir

**IDs:** A:TRAP-query-16 + B:TRAP-query-14
**Acordo:** 2 quanto à existência da armadilha (READ UNCOMMITTED não é "dirty read com dados quase certos": ele pula e duplica linhas **commitadas** e pode estourar 601) — **contestado** quanto à direção do movimento da linha e quanto ao remédio suficiente.
**Eixo:** query (as duas passadas concordam no eixo).

**Convergem em:** sem lock S a varredura pode percorrer o índice em ordem de alocação; page split / mudança da chave do índice clusterizado durante a varredura produz linha contada duas vezes ou linha omitida; o erro 601 é a manifestação explícita e o EF Core 8 o classifica como transitório.

**Divergem em:** qual movimento causa a **omissão** (linha que se move **para trás**, versão B, × linha que **escapa à frente do ponteiro de varredura**, versão A) e o que basta como correção (transação `Snapshot`, versão A, × ligar RCSI no banco e remover todo `NOLOCK`, versão B).

---

### Versão A — `A:TRAP-query-16: NOLOCK-nao-e-so-dirty-read-ele-pula-e-duplica-linhas-commitadas`

**Aparece como:**
```csharp
// "é só relatório, não precisa de consistência"
using var scope = new TransactionScope(TransactionScopeOption.Required,
    new TransactionOptions { IsolationLevel = System.Transactions.IsolationLevel.ReadUncommitted },
    TransactionScopeAsyncFlowOption.Enabled);
var total = await db.Lancamentos.Where(l => l.Data >= inicio).SumAsync(l => l.Valor);
scope.Complete();
```
**O que acontece:** o total pode contar a mesma linha duas vezes, ou omitir linhas que estavam commitadas antes *e* depois da consulta. Às vezes a query nem termina: `SqlException 601`.

**Por quê:** sem lock S o motor pode percorrer o índice em ordem de alocação. Se um UPDATE concorrente causa page split ou move a linha (mudança de chave do índice clusterizado), a mesma linha aparece em duas páginas visitadas, ou escapa à frente do ponteiro de varredura. Não é "dirty read com dados quase certos": é perda e duplicação de linhas **commitadas**, que nenhum raciocínio sobre "dados em voo" prevê. O 601 é o caso feliz, em que o motor percebe e aborta — e ele está na lista de transientes do EF Core 8, então `EnableRetryOnFailure` reexecuta a consulta e apaga o único sintoma visível.

**Correto:**
```csharp
using var tx = await db.Database.BeginTransactionAsync(IsolationLevel.Snapshot);
var total = await db.Lancamentos.Where(l => l.Data >= inicio).SumAsync(l => l.Valor);
await tx.CommitAsync();   // sem locks, sem bloquear escritores, e sem pular linha
```
**Detecta com:** grep por `NOLOCK`, `READUNCOMMITTED` e `IsolationLevel.ReadUncommitted`; ocorrências de erro 601; comparar o mesmo SUM sob snapshot e sob NOLOCK durante carga de escrita.

**Verificável:** sim — laço de UPDATEs que alteram a chave clusterizada enquanto um `SELECT SUM(...) WITH (NOLOCK)` roda em outra sessão; o total oscila fora do intervalo possível.

**Fonte:** DDIA-Chapter 7. Transactions

---

### Versão B — `B:TRAP-query-14: nolock-nao-e-so-leitura-suja`

**Aparece como:**
```csharp
var total = await db.Database.SqlQuery<decimal>(
    $"""
     SELECT SUM(Total) AS Value FROM dbo.Orders WITH (NOLOCK)
     WHERE CreatedAt >= {desde}
     """).SingleAsync();
```
**O que acontece:** além de somar linhas de transações que depois sofrem rollback, a consulta pode contar a **mesma linha duas vezes**, **pular** linhas, ou estourar com erro 601.

**Por quê:** `NOLOCK` é READ UNCOMMITTED. Sem lock compartilhado, a varredura em ordem de alocação pode reencontrar uma linha que se moveu para frente durante um page split, e perder uma que se moveu para trás. O erro 601 — "Could not continue scan with NOLOCK due to data movement" — é a manifestação explícita disso; o EF Core 8 inclusive o classifica como transitório, o que diz o quanto ele ocorre. O dev acha que trocou exatidão por velocidade; na verdade trocou exatidão por *nada determinístico*.

**Correto:**
```sql
ALTER DATABASE [App] SET READ_COMMITTED_SNAPSHOT ON WITH ROLLBACK IMMEDIATE;
```
Com RCSI ligado, leituras usam versionamento de linha: não bloqueiam escritores, não são bloqueadas por eles e não leem sujo. Aí todo `WITH (NOLOCK)` do código pode e deve ser removido.

**Detecta com:**
```sql
SELECT t.text FROM sys.dm_exec_cached_plans p
CROSS APPLY sys.dm_exec_sql_text(p.plan_handle) t
WHERE t.text LIKE '%NOLOCK%';
SELECT is_read_committed_snapshot_on FROM sys.databases WHERE name = DB_NAME();
```
**Verificável:** sim — `SELECT COUNT(*) ... WITH (NOLOCK)` em loop numa tabela sofrendo `UPDATE` na coluna da chave clusterizada devolve contagens acima e abaixo do valor real.

**Fonte:** DDIA-Chapter 7. Transactions

---

**QUESTAO EM DISPUTA:** numa varredura em ordem de alocação sob READ UNCOMMITTED, a linha **omitida** é a que se moveu para uma página **já visitada** ("para trás", versão B) ou a que "escapou à frente do ponteiro de varredura" (versão A)?

**QUESTAO EM DISPUTA (secundária):** ligar `READ_COMMITTED_SNAPSHOT` (versão B) elimina os saltos e duplicações do relatório, ou é preciso uma transação `IsolationLevel.Snapshot` explícita (versão A) porque RCSI é por statement e o `SUM` de um único statement/multi-statement se comporta diferente?

**QUESTAO EM DISPUTA (secundária):** com 601 na lista de transientes do EF Core 8, `EnableRetryOnFailure` de fato reexecuta a consulta e apaga o sintoma (afirmação exclusiva da versão A)?

---

## ESTATISTICA

- Entradas: 34 traps (Run A: 19; Run B: 15)
- Saída após fusão: 24 grupos (22 traps reconciliadas + 2 conflitos preservados)
- ACORDO 2: 11 grupos (9 sem conflito + 2 conflitos)
- ACORDO 1: 13 grupos
- CONFLITOS: 2
- EIXO dados: 16 grupos
- EIXO query: 8 grupos
- Cobertura: 34/34 traps de origem representadas.
- Nota de cobertura: `A:TRAP-query-5` continha duas afirmações independentes e, por isso, alimenta dois grupos (`ExecuteUpdate` versus token de concorrência; `ExecuteUpdate` versus transação de `SaveChanges`).
- Fences de código: 134 delimitadores, quantidade par.
