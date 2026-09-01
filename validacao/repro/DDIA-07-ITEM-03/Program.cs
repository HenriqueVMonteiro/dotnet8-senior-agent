using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using System.Runtime.InteropServices;

// ARBITRAGEM ITEM-03 x ITEM-04.
// Tensao: B-10/C-28 afirmam sem qualificacao que UM SaveChanges e atomico entre
// tabelas; A-2 afirma que a garantia depende de AutoTransactionBehavior.
// Testa tambem se ExecuteUpdate participa de BeginTransaction e se, sem
// transacao explicita, ele commita sozinho.

const string Cs = "Server=localhost,14333;User Id=sa;Password=Repro#2024pw;TrustServerCertificate=True;Encrypt=False;Database=ReproDb";

Console.WriteLine($"AMBIENTE runtime={RuntimeInformation.FrameworkDescription}");
Console.WriteLine($"AMBIENTE efcore={typeof(DbContext).Assembly.GetName().Version}");

await Reset();

// --- A) UM SaveChanges, dois INSERTs, segundo viola CHECK, AutoTransactionBehavior default
await Cenario("A) 1 SaveChanges, WhenNeeded (default)", AutoTransactionBehavior.WhenNeeded);

// --- B) mesmo cenario com Never
await Cenario("B) 1 SaveChanges, Never", AutoTransactionBehavior.Never);

// --- C) ExecuteUpdate sem transacao explicita: commita sozinho?
await Reset();
await using (var ctx = New(AutoTransactionBehavior.WhenNeeded))
{
    await ctx.Pais.Where(p => p.Id == 1).ExecuteUpdateAsync(s => s.SetProperty(p => p.Nome, "MUDADO"));
    try
    {
        ctx.Filhos.Add(new Filho { Id = 90, PaiId = 1, Valor = -5 }); // viola CHECK
        await ctx.SaveChangesAsync();
        Console.WriteLine("C) SaveChanges nao lancou (inesperado)");
    }
    catch (DbUpdateException)
    {
        Console.WriteLine("C) SaveChanges lancou DbUpdateException, como esperado");
    }
}
Console.WriteLine($"C) ExecuteUpdate sem transacao explicita -> Pai.Nome={await PaiNome()} (persistiu = escopo de commit proprio)");

// --- D) ExecuteUpdate dentro de BeginTransaction, com rollback
await Reset();
await using (var ctx = New(AutoTransactionBehavior.WhenNeeded))
{
    await using var tx = await ctx.Database.BeginTransactionAsync();
    await ctx.Pais.Where(p => p.Id == 1).ExecuteUpdateAsync(s => s.SetProperty(p => p.Nome, "MUDADO"));
    await tx.RollbackAsync();
}
Console.WriteLine($"D) ExecuteUpdate dentro de BeginTransaction + rollback -> Pai.Nome={await PaiNome()} (original = participou da transacao)");

Console.WriteLine("\nFIM");

async Task Cenario(string label, AutoTransactionBehavior behavior)
{
    await Reset();
    await using var ctx = New(behavior);
    ctx.Pais.Add(new Pai { Id = 2, Nome = "novo-pai" });
    ctx.Filhos.Add(new Filho { Id = 91, PaiId = 2, Valor = -5 }); // CHECK Valor >= 0 viola
    string erro;
    try { await ctx.SaveChangesAsync(); erro = "nenhum"; }
    catch (DbUpdateException ex) { erro = ex.InnerException?.GetType().Name ?? "DbUpdateException"; }

    var pais = await Count("SELECT COUNT(*) FROM dbo.Pai WHERE Id = 2");
    var filhos = await Count("SELECT COUNT(*) FROM dbo.Filho WHERE Id = 91");
    var atomico = pais == 0 && filhos == 0;
    Console.WriteLine($"{label}: erro={erro} | Pai gravado={pais} | Filho gravado={filhos} -> {(atomico ? "ATOMICO" : "PARCIAL: orfao gravado")}");
}

AppDb New(AutoTransactionBehavior behavior)
{
    var ctx = new AppDb();
    ctx.Database.AutoTransactionBehavior = behavior;
    return ctx;
}

static async Task Reset()
{
    await using var ctx = new AppDb();
    await ctx.Database.ExecuteSqlRawAsync("""
        DROP TABLE IF EXISTS dbo.Filho;
        DROP TABLE IF EXISTS dbo.Pai;
        CREATE TABLE dbo.Pai(Id int PRIMARY KEY, Nome nvarchar(50) NOT NULL);
        CREATE TABLE dbo.Filho(
            Id int PRIMARY KEY,
            PaiId int NOT NULL REFERENCES dbo.Pai(Id),
            Valor int NOT NULL CONSTRAINT CK_Filho_Valor CHECK (Valor >= 0));
        INSERT INTO dbo.Pai(Id, Nome) VALUES (1, 'original');
        """);
}

static async Task<int> Count(string sql)
{
    await using var ctx = new AppDb();
    await using var conn = ctx.Database.GetDbConnection();
    await conn.OpenAsync();
    await using var cmd = conn.CreateCommand();
    cmd.CommandText = sql;
    return (int)(await cmd.ExecuteScalarAsync())!;
}

static async Task<string> PaiNome()
{
    await using var ctx = new AppDb();
    return (await ctx.Pais.AsNoTracking().SingleAsync(p => p.Id == 1)).Nome;
}

class Pai
{
    public int Id { get; set; }
    public string Nome { get; set; } = "";
}

class Filho
{
    public int Id { get; set; }
    public int PaiId { get; set; }
    public int Valor { get; set; }
}

class AppDb : DbContext
{
    public DbSet<Pai> Pais => Set<Pai>();
    public DbSet<Filho> Filhos => Set<Filho>();

    protected override void OnConfiguring(DbContextOptionsBuilder b) =>
        b.UseSqlServer(Cs);

    protected override void OnModelCreating(ModelBuilder b)
    {
        b.Entity<Pai>().ToTable("Pai");
        b.Entity<Filho>().ToTable("Filho");
        b.Entity<Pai>().Property(p => p.Id).ValueGeneratedNever();
        b.Entity<Filho>().Property(f => f.Id).ValueGeneratedNever();
    }

    public const string Cs = "Server=localhost,14333;User Id=sa;Password=Repro#2024pw;TrustServerCertificate=True;Encrypt=False;Database=ReproDb";
}
