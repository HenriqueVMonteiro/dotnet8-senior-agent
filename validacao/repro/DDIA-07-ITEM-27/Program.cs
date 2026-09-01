using Microsoft.Data.SqlClient;
using System.Data;
using System.Runtime.InteropServices;

// DDIA-07-ITEM-27: SNAPSHOT nao impede write skew.
// Invariante: pelo menos 1 medico de plantao. Duas transacoes leem "2 de plantao",
// cada uma se retira, e ambas commitam -> invariante violada sem erro.
// Comparacao: SERIALIZABLE com indice no predicado deve impedir.

const string Master = "Server=localhost,14333;User Id=sa;Password=Repro#2024pw;TrustServerCertificate=True;Encrypt=False;Database=master";
const string Db = "ReproDb_I27";
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
await Exec(Master, $"ALTER DATABASE [{Db}] SET ALLOW_SNAPSHOT_ISOLATION ON;");

await RunScenario(IsolationLevel.Snapshot, "SNAPSHOT");
await RunScenario(IsolationLevel.Serializable, "SERIALIZABLE");

Console.WriteLine("\nFIM");

async Task RunScenario(IsolationLevel level, string label)
{
    await Exec(cs, """
        DROP TABLE IF EXISTS dbo.Plantao;
        CREATE TABLE dbo.Plantao(Id int PRIMARY KEY, Turno int NOT NULL, DePlantao bit NOT NULL);
        CREATE INDEX IX_Plantao_Turno_DePlantao ON dbo.Plantao(Turno, DePlantao);
        INSERT INTO dbo.Plantao(Id, Turno, DePlantao) VALUES (1, 10, 1), (2, 10, 1);
        """);

    Console.WriteLine($"\n--- {label} (indice IX_Plantao_Turno_DePlantao presente) ---");

    await using var a = new SqlConnection(cs);
    await using var b = new SqlConnection(cs);
    await a.OpenAsync();
    await b.OpenAsync();

    var ta = (SqlTransaction)await a.BeginTransactionAsync(level);
    var tb = (SqlTransaction)await b.BeginTransactionAsync(level);

    const string countSql = "SELECT COUNT(*) FROM dbo.Plantao WHERE Turno = 10 AND DePlantao = 1";
    string? errA = null, errB = null;

    // Ambas leem o mesmo agregado antes de qualquer escrita.
    var ca = await ScalarTx<int>(a, ta, countSql, 5);
    Console.WriteLine($"A leu contagem={ca}");
    var cb = await ScalarTx<int>(b, tb, countSql, 5);
    Console.WriteLine($"B leu contagem={cb}");

    // Cada uma se retira de uma LINHA DIFERENTE: nenhuma colisao de linha.
    var wa = Task.Run(async () =>
    {
        try { await ExecTx(a, ta, "UPDATE dbo.Plantao SET DePlantao = 0 WHERE Id = 1", 8); }
        catch (SqlException ex) { errA = $"SqlException {ex.Number}"; }
    });
    var wb = Task.Run(async () =>
    {
        try { await ExecTx(b, tb, "UPDATE dbo.Plantao SET DePlantao = 0 WHERE Id = 2", 8); }
        catch (SqlException ex) { errB = $"SqlException {ex.Number}"; }
    });
    await Task.WhenAll(wa, wb);
    Console.WriteLine($"A update -> {errA ?? "ok"} | B update -> {errB ?? "ok"}");

    string commitA = "ok", commitB = "ok";
    try { if (errA is null) await ta.CommitAsync(); else { await ta.RollbackAsync(); commitA = "rollback"; } }
    catch (SqlException ex) { commitA = $"SqlException {ex.Number}"; }
    try { if (errB is null) await tb.CommitAsync(); else { await tb.RollbackAsync(); commitB = "rollback"; } }
    catch (SqlException ex) { commitB = $"SqlException {ex.Number}"; }
    Console.WriteLine($"commit A -> {commitA} | commit B -> {commitB}");

    var final = (int)(await Query1(cs, countSql))!;
    Console.WriteLine($"RESULTADO final de plantao={final} -> invariante {(final >= 1 ? "PRESERVADA" : "VIOLADA")}");
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
