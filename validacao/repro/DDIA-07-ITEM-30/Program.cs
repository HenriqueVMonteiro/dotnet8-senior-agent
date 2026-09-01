using Microsoft.Data.SqlClient;
using System.Data;
using System.Runtime.InteropServices;

// DDIA-07-ITEM-30: para travar a AUSENCIA de uma linha, UPDLOCK sozinho nao basta;
// e preciso HOLDLOCK (lock de range). Padrao check-then-insert em READ COMMITTED.

const string Master = "Server=localhost,14333;User Id=sa;Password=Repro#2024pw;TrustServerCertificate=True;Encrypt=False;Database=master";
const string Db = "ReproDb_I30";
string cs = $"Server=localhost,14333;User Id=sa;Password=Repro#2024pw;TrustServerCertificate=True;Encrypt=False;Database={Db}";

Console.WriteLine($"AMBIENTE runtime={RuntimeInformation.FrameworkDescription}");
Console.WriteLine($"AMBIENTE sqlserver={await Query1(Master, "SELECT CONVERT(varchar(50), SERVERPROPERTY('ProductVersion'))")}");

await Exec(Master, $"""
    IF DB_ID('{Db}') IS NOT NULL
    BEGIN
        ALTER DATABASE [{Db}] SET SINGLE_USER WITH ROLLBACK IMMEDIATE;
        DROP DATABASE [{Db}];
    END;
    CREATE DATABASE [{Db}];
    """);

await Scenario("sem hint", "");
await Scenario("UPDLOCK", "WITH (UPDLOCK)");
await Scenario("UPDLOCK, HOLDLOCK", "WITH (UPDLOCK, HOLDLOCK)");

Console.WriteLine("\nFIM");

async Task Scenario(string label, string hint)
{
    await Exec(cs, """
        DROP TABLE IF EXISTS dbo.Reserva;
        CREATE TABLE dbo.Reserva(Id int IDENTITY PRIMARY KEY, Codigo varchar(20) NOT NULL);
        CREATE INDEX IX_Reserva_Codigo ON dbo.Reserva(Codigo);
        """);

    Console.WriteLine($"\n--- check-then-insert, hint: {label} ---");

    string check = $"SELECT COUNT(*) FROM dbo.Reserva {hint} WHERE Codigo = 'X1'";

    // As duas sessoes executam check-then-insert CONCORRENTEMENTE: o hint pode
    // bloquear ja no SELECT, e isso tambem e resultado observavel.
    // Gate assincrono: nada de bloqueio de thread, senao a segunda sessao nunca comeca.
    var arrived = 0;
    var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
    var outcomes = new string[2];

    async Task Session(int slot, string tag)
    {
        await using var conn = new SqlConnection(cs);
        await conn.OpenAsync();
        var tx = (SqlTransaction)await conn.BeginTransactionAsync(IsolationLevel.ReadCommitted);
        try
        {
            if (Interlocked.Increment(ref arrived) == 2) gate.SetResult();
            await gate.Task;
            int seen;
            try { seen = await ScalarTx<int>(conn, tx, check, 6); }
            catch (SqlException ex) { outcomes[slot] = $"{tag}: SELECT bloqueou/falhou ({Describe(ex)})"; await SafeRollback(tx); return; }

            if (seen != 0) { outcomes[slot] = $"{tag}: viu {seen} linha(s), nao inseriu"; await SafeRollback(tx); return; }

            try { await ExecTx(conn, tx, "INSERT INTO dbo.Reserva(Codigo) VALUES ('X1')", 6); }
            catch (SqlException ex) { outcomes[slot] = $"{tag}: viu 0, INSERT falhou ({Describe(ex)})"; await SafeRollback(tx); return; }

            try { await tx.CommitAsync(); outcomes[slot] = $"{tag}: viu 0, INSERT ok, COMMIT ok"; }
            catch (SqlException ex) { outcomes[slot] = $"{tag}: viu 0, INSERT ok, COMMIT falhou ({Describe(ex)})"; }
        }
        catch (Exception ex) { outcomes[slot] = $"{tag}: {ex.GetType().Name}"; await SafeRollback(tx); }
    }

    await Task.WhenAll(Task.Run(() => Session(0, "A")), Task.Run(() => Session(1, "B")));
    Console.WriteLine(outcomes[0]);
    Console.WriteLine(outcomes[1]);

    var total = (int)(await Query1(cs, "SELECT COUNT(*) FROM dbo.Reserva WHERE Codigo = 'X1'"))!;
    Console.WriteLine($"RESULTADO linhas 'X1'={total} -> {(total == 1 ? "unicidade OK" : total == 0 ? "nenhuma gravada" : "DUPLICADO")}");
}

static string Describe(SqlException ex) => ex.Number switch
{
    -2 => "timeout de comando -2 = bloqueado",
    1205 => "deadlock 1205",
    2601 or 2627 => $"violacao de unicidade {ex.Number}",
    _ => $"SqlException {ex.Number}"
};

static async Task SafeRollback(SqlTransaction tx)
{
    try { await tx.RollbackAsync(); } catch { /* transacao ja desfeita pelo servidor */ }
}

static async Task Exec(string connStr, string sql)
{
    await using var c = new SqlConnection(connStr);
    await c.OpenAsync();
    await using var cmd = new SqlCommand(sql, c) { CommandTimeout = 60 };
    await cmd.ExecuteNonQueryAsync();
}

static async Task<object?> Query1(string connStr, string sql)
{
    await using var c = new SqlConnection(connStr);
    await c.OpenAsync();
    await using var cmd = new SqlCommand(sql, c);
    return await cmd.ExecuteScalarAsync();
}

static async Task<T> ScalarTx<T>(SqlConnection c, SqlTransaction tx, string sql, int timeout)
{
    await using var cmd = new SqlCommand(sql, c, tx) { CommandTimeout = timeout };
    return (T)(await cmd.ExecuteScalarAsync())!;
}

static async Task ExecTx(SqlConnection c, SqlTransaction tx, string sql, int timeout)
{
    await using var cmd = new SqlCommand(sql, c, tx) { CommandTimeout = timeout };
    await cmd.ExecuteNonQueryAsync();
}
