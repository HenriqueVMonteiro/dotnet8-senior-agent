using Microsoft.Data.SqlClient;
using System.Runtime.InteropServices;
using System.Transactions;
using IsolationLevel = System.Transactions.IsolationLevel;

// DDIA-07-ITEM-05 / ITEM-06: o construtor sem argumentos de TransactionScope usa
// Serializable (nao o default do banco), e TransactionScope sem
// TransactionScopeAsyncFlowOption.Enabled falha quando ha await dentro do escopo.
// Mapa de transaction_isolation_level em sys.dm_exec_sessions:
// 0=Unspecified 1=ReadUncommitted 2=ReadCommitted 3=RepeatableRead 4=Serializable 5=Snapshot

const string Cs = "Server=localhost,14333;User Id=sa;Password=Repro#2024pw;TrustServerCertificate=True;Encrypt=False;Database=ReproDb";
const string Probe = """
    SELECT CASE transaction_isolation_level
             WHEN 0 THEN '0/Unspecified' WHEN 1 THEN '1/ReadUncommitted'
             WHEN 2 THEN '2/ReadCommitted' WHEN 3 THEN '3/RepeatableRead'
             WHEN 4 THEN '4/Serializable' WHEN 5 THEN '5/Snapshot' END
    FROM sys.dm_exec_sessions WHERE session_id = @@SPID
    """;

Console.WriteLine($"AMBIENTE runtime={RuntimeInformation.FrameworkDescription}");
Console.WriteLine($"AMBIENTE TransactionManager.DefaultTimeout={TransactionManager.DefaultTimeout}");
Console.WriteLine($"AMBIENTE sqlserver={await ReadScalar("SELECT CONVERT(varchar(50), SERVERPROPERTY('ProductVersion'))")}");

Console.WriteLine("\n=== A) nivel de isolamento efetivo dentro do escopo (leitura SINCRONA, sem await) ===");
Console.WriteLine($"sem escopo algum                       -> {ReadSync()}");
Report("new TransactionScope()", () => new TransactionScope());
Report("TransactionOptions IsolationLevel=ReadCommitted",
    () => new TransactionScope(TransactionScopeOption.Required,
        new TransactionOptions { IsolationLevel = IsolationLevel.ReadCommitted }));
Report("TransactionScopeAsyncFlowOption.Enabled", () => new TransactionScope(TransactionScopeAsyncFlowOption.Enabled));

Console.WriteLine("\n=== B) await dentro do escopo, com hop real de thread ===");
await AsyncProbe("SEM AsyncFlowOption", () => new TransactionScope());
await AsyncProbe("COM AsyncFlowOption", () => new TransactionScope(TransactionScopeAsyncFlowOption.Enabled));

Console.WriteLine("\n=== C) o nivel de isolamento vaza pelo pool de conexoes? ===");
Console.WriteLine($"fora de qualquer escopo, apos os escopos Serializable -> {ReadSync()}");
using (new TransactionScope(TransactionScopeOption.Required,
       new TransactionOptions { IsolationLevel = IsolationLevel.ReadCommitted }))
{
    Console.WriteLine($"dentro de escopo ReadCommitted                        -> {ReadSync()}");
}
Console.WriteLine($"fora de escopo, logo depois do escopo ReadCommitted    -> {ReadSync()}");
Console.WriteLine($"conexao NAO pooled (Pooling=false), fora de escopo     -> {ReadSyncNoPool()}");

Console.WriteLine("\nFIM");

static void Report(string label, Func<TransactionScope> make)
{
    var scope = make();
    string isolamento, fim;
    try
    {
        isolamento = ReadSync();
        scope.Complete();
        scope.Dispose();
        fim = "Complete+Dispose ok";
    }
    catch (Exception ex) { isolamento = "n/d"; fim = ex.GetType().Name; }
    Console.WriteLine($"{label,-38} -> {isolamento} ({fim})");
}

static async Task AsyncProbe(string label, Func<TransactionScope> make)
{
    var scope = make();
    var t0 = Environment.CurrentManagedThreadId;
    var antes = Transaction.Current is null ? "null" : "presente";

    await Task.Run(() => Thread.Sleep(30)).ConfigureAwait(false);

    var t1 = Environment.CurrentManagedThreadId;
    var depois = Transaction.Current is null ? "null" : "presente";
    var isolamento = await ReadAsync();

    string fim;
    try { scope.Complete(); scope.Dispose(); fim = "Complete+Dispose ok"; }
    catch (Exception ex) { fim = $"{ex.GetType().Name}: {ex.Message.Split('.')[0]}"; }

    Console.WriteLine($"{label}: Transaction.Current antes={antes} depois={depois} | thread {t0}->{t1} | isolamento apos hop={isolamento} | {fim}");
}

static string ReadSync()
{
    using var c = new SqlConnection(Cs);
    c.Open();
    using var cmd = new SqlCommand(Probe, c);
    return (string)cmd.ExecuteScalar()!;
}


static string ReadSyncNoPool()
{
    using var c = new SqlConnection(Cs + ";Pooling=false");
    c.Open();
    using var cmd = new SqlCommand(Probe, c);
    return (string)cmd.ExecuteScalar()!;
}
static async Task<string> ReadAsync()
{
    await using var c = new SqlConnection(Cs);
    await c.OpenAsync();
    await using var cmd = new SqlCommand(Probe, c);
    return (string)(await cmd.ExecuteScalarAsync())!;
}

static async Task<string> ReadScalar(string sql)
{
    await using var c = new SqlConnection(Cs);
    await c.OpenAsync();
    await using var cmd = new SqlCommand(sql, c);
    return (string)(await cmd.ExecuteScalarAsync())!;
}
