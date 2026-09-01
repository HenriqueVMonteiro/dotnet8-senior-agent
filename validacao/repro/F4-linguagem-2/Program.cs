using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;

// FASE 4 lote 2 — eixo `linguagem` + refazendo o experimento que saiu INCONCLUSIVO.

Console.WriteLine($"AMBIENTE runtime={RuntimeInformation.FrameworkDescription}");
Console.WriteLine();
int escapadas = 0;


// ---------- B1: async void nao e capturavel pelo chamador ----------
Console.WriteLine("== B1: async void vs async Task ==");
string b1Void = "nao capturou";
try { DispararVoid(); await Task.Delay(150); }
catch (InvalidOperationException) { b1Void = "capturou"; }
Console.WriteLine($"B1 async void  -> try/catch do chamador: {b1Void}");

string b1Task = "nao capturou";
try { await DispararTask(); }
catch (InvalidOperationException) { b1Task = "capturou"; }
Console.WriteLine($"B1 async Task  -> try/catch do chamador: {b1Task}");
Console.WriteLine($"B1 excecoes que escaparam para o handler de ultimo recurso: {escapadas}");
Console.WriteLine();

// ---------- B2: .Result em contexto com sincronizacao trava ----------
Console.WriteLine("== B2: .Result sob SynchronizationContext de uma thread ==");
Console.WriteLine($"B2 com .Result  -> {RodarNoContexto(usarResult: true)}");
Console.WriteLine($"B2 com await    -> {RodarNoContexto(usarResult: false)}");
Console.WriteLine();

// ---------- B3: backtracking catastrofico ----------
Console.WriteLine("== B3: backtracking catastrofico ==");
var entrada = new string('a', 30) + "!";
var padrao = @"^(a+)+$";
var swBt = Stopwatch.StartNew();
string b3Classico;
try
{
    b3Classico = Regex.IsMatch(entrada, padrao, RegexOptions.None, TimeSpan.FromSeconds(2))
        ? $"match em {swBt.ElapsedMilliseconds}ms" : $"sem match em {swBt.ElapsedMilliseconds}ms";
}
catch (RegexMatchTimeoutException) { b3Classico = $"TIMEOUT apos {swBt.ElapsedMilliseconds}ms"; }
Console.WriteLine($"B3 backtracking classico: {b3Classico}");

var swNb = Stopwatch.StartNew();
var b3Nb = Regex.IsMatch(entrada, padrao, RegexOptions.NonBacktracking);
Console.WriteLine($"B3 NonBacktracking:       sem match={!b3Nb} em {swNb.ElapsedMilliseconds}ms");
Console.WriteLine();

// ---------- B4: finalizador promove o objeto de geracao ----------
Console.WriteLine("== B4: finalizador atrasa a coleta ==");
int genFin = -1, genSem = -1;
CriarEObservar(out genFin, out genSem);
Console.WriteLine($"B4 com finalizador -> sobreviveu a coleta, geracao={genFin}");
Console.WriteLine($"B4 sem finalizador -> alcancavel? {(genSem == -2 ? "NAO (coletado)" : $"sim, geracao={genSem}")}");
Console.WriteLine($"B4 finalizadores executados no total: {ComFinalizador.Finalizados}");
Console.WriteLine();

// ---------- B5: custo do boxing, agora COM barreira contra o otimizador ----------
Console.WriteLine("== B5: boxing de int, com barreira (refaz o INCONCLUSIVO do lote 1) ==");
var sink = new object[100_000];
long b5Box = Medir(() => { for (int i = 0; i < 100_000; i++) sink[i] = i; });
long b5Sem = Medir(() => { long acc = 0; for (int i = 0; i < 100_000; i++) acc += i; Consumir(acc); });
Console.WriteLine($"B5 boxing em array vivo: {b5Box} bytes em 100k -> {(double)b5Box / 100_000:F1} bytes/caixa");
Console.WriteLine($"B5 sem boxing:           {b5Sem} bytes em 100k");
Console.WriteLine();

Console.WriteLine("FIM");

// ---------- infraestrutura ----------

static long Medir(Action acao)
{
    acao();
    var antes = GC.GetAllocatedBytesForCurrentThread();
    acao();
    return GC.GetAllocatedBytesForCurrentThread() - antes;
}

[MethodImpl(MethodImplOptions.NoInlining)]
static void Consumir(long v) { if (v == long.MinValue) Console.Write(""); }

async void DispararVoid()
{
    try
    {
        await Task.Delay(10);
        throw new InvalidOperationException("de async void");
    }
    catch (InvalidOperationException) { escapadas++; } // sem isso o processo morre
}

static async Task DispararTask()
{
    await Task.Delay(10);
    throw new InvalidOperationException("de async Task");
}

static string RodarNoContexto(bool usarResult)
{
    string resultado = "nao concluiu";
    var t = new Thread(() =>
    {
        var ctx = new ContextoDeUmaThread();
        SynchronizationContext.SetSynchronizationContext(ctx);
        ctx.Post(async _ =>
        {
            try
            {
                if (usarResult)
                {
                    var v = TrabalhoAsync().Result;   // reentra no mesmo contexto
                    resultado = $"concluiu com {v}";
                }
                else
                {
                    var v = await TrabalhoAsync();
                    resultado = $"concluiu com {v}";
                }
            }
            catch (Exception ex) { resultado = ex.GetType().Name; }
            finally { ctx.Encerrar(); }
        }, null);
        ctx.Loop();
    });
    t.IsBackground = true;
    t.Start();
    return t.Join(TimeSpan.FromSeconds(3)) ? resultado : "DEADLOCK (timeout de 3s)";
}

static async Task<int> TrabalhoAsync()
{
    await Task.Delay(20);
    return 42;
}

[MethodImpl(MethodImplOptions.NoInlining)]
static void CriarEObservar(out int comFin, out int semFin)
{
    var wrFin = CriarComFinalizador();
    var wrSem = CriarSemFinalizador();
    GC.Collect();
    GC.WaitForPendingFinalizers();
    GC.Collect();
    comFin = wrFin.TryGetTarget(out var alvo) ? GC.GetGeneration(alvo) : -2;
    semFin = wrSem.TryGetTarget(out var alvo2) ? GC.GetGeneration(alvo2) : -2;
}

[MethodImpl(MethodImplOptions.NoInlining)]
static WeakReference<ComFinalizador> CriarComFinalizador() => new(new ComFinalizador());

[MethodImpl(MethodImplOptions.NoInlining)]
static WeakReference<SemFinalizador> CriarSemFinalizador() => new(new SemFinalizador());

sealed class ComFinalizador
{
    public static int Finalizados;
    public byte[] Carga = new byte[256];
    ~ComFinalizador() { Interlocked.Increment(ref Finalizados); Ressuscitado = this; }
    public static ComFinalizador? Ressuscitado;
}

sealed class SemFinalizador { public byte[] Carga = new byte[256]; }

sealed class ContextoDeUmaThread : SynchronizationContext
{
    private readonly System.Collections.Concurrent.BlockingCollection<(SendOrPostCallback, object?)> fila = new();
    public override void Post(SendOrPostCallback d, object? state)
    {
        if (!fila.IsAddingCompleted) fila.Add((d, state));
    }
    public void Loop()
    {
        foreach (var (d, s) in fila.GetConsumingEnumerable()) d(s);
    }
    public void Encerrar() => fila.CompleteAdding();
}
