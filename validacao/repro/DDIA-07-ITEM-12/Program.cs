using Microsoft.Data.SqlClient;
using System.Runtime.InteropServices;

// DDIA-07-ITEM-12: READ COMMITTED por lock (RCSI OFF) vs por versao (RCSI ON).
// Afirmacao: com RCSI OFF o leitor em READ COMMITTED espera o lock X do escritor;
// com RCSI ON o mesmo SELECT retorna imediatamente o valor anterior commitado.

const string Master = "Server=localhost,14333;User Id=sa;Password=Repro#2024pw;TrustServerCertificate=True;Encrypt=False;Database=master";
const string Db = "ReproDb_I12";
string dbConn = $"Server=localhost,14333;User Id=sa;Password=Repro#2024pw;TrustServerCertificate=True;Encrypt=False;Database={Db}";

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

await Exec(dbConn, """
    CREATE TABLE dbo.Conta(Id int PRIMARY KEY, Saldo int NOT NULL);
    INSERT INTO dbo.Conta(Id, Saldo) VALUES (1, 100);
    """);

foreach (var rcsi in new[] { false, true })
{
    await Exec(Master, $"ALTER DATABASE [{Db}] SET READ_COMMITTED_SNAPSHOT {(rcsi ? "ON" : "OFF")} WITH ROLLBACK IMMEDIATE;");
    var flag = await Query1(dbConn, "SELECT is_read_committed_snapshot_on FROM sys.databases WHERE name = DB_NAME()");
    Console.WriteLine($"\n--- RCSI={(rcsi ? "ON" : "OFF")} (sys.databases.is_read_committed_snapshot_on={flag}) ---");

    // Escritor: atualiza sem commitar, mantendo o lock X na linha.
    await using var writer = new SqlConnection(dbConn);
    await writer.OpenAsync();
    var tx = (SqlTransaction)await writer.BeginTransactionAsync(System.Data.IsolationLevel.ReadCommitted);
    await using (var up = new SqlCommand("UPDATE dbo.Conta SET Saldo = 999 WHERE Id = 1", writer, tx))
        await up.ExecuteNonQueryAsync();
    Console.WriteLine("ESCRITOR aplicou UPDATE Saldo=999 e NAO commitou");

    // Leitor: READ COMMITTED, timeout curto para transformar bloqueio em evidencia observavel.
    await using var reader = new SqlConnection(dbConn);
    await reader.OpenAsync();
    await using var sel = new SqlCommand("SELECT Saldo FROM dbo.Conta WHERE Id = 1", reader) { CommandTimeout = 4 };

    var sw = System.Diagnostics.Stopwatch.StartNew();
    try
    {
        var saldo = (int)(await sel.ExecuteScalarAsync())!;
        Console.WriteLine($"LEITOR retornou Saldo={saldo} em {sw.ElapsedMilliseconds}ms SEM BLOQUEIO");
    }
    catch (SqlException ex)
    {
        Console.WriteLine($"LEITOR BLOQUEADO: SqlException Number={ex.Number} apos {sw.ElapsedMilliseconds}ms");
    }

    await tx.RollbackAsync();
}

Console.WriteLine("\nFIM");

static async Task Exec(string cs, string sql)
{
    await using var c = new SqlConnection(cs);
    await c.OpenAsync();
    await using var cmd = new SqlCommand(sql, c) { CommandTimeout = 60 };
    await cmd.ExecuteNonQueryAsync();
}

static async Task<object?> Query1(string cs, string sql)
{
    await using var c = new SqlConnection(cs);
    await c.OpenAsync();
    await using var cmd = new SqlCommand(sql, c);
    return await cmd.ExecuteScalarAsync();
}
