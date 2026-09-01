using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Runtime;
using System.Text;

// FASE 4 em lote — eixos `runtime` e `linguagem`.
// Uma afirmacao por secao, cada uma com linha de saida discriminante propria.
// Regra: CONFIRMA exige valor que separe a hipotese da alternativa.

Console.WriteLine($"AMBIENTE runtime={RuntimeInformation.FrameworkDescription}");
Console.WriteLine($"AMBIENTE IsServerGC={GCSettings.IsServerGC} cores={Environment.ProcessorCount} latency={GCSettings.LatencyMode}");
Console.WriteLine();

// ---------- A1: limiar do LOH e 85.000 bytes DECIMAIS, nao 85 KiB (87.040) ----------
Console.WriteLine("== A1: limiar do LOH ==");
foreach (var n in new[] { 84_000, 84_960, 84_970, 84_984, 85_000, 87_040 })
{
    var arr = GC.AllocateUninitializedArray<byte>(n);
    Console.WriteLine($"A1 byte[{n}] -> geracao={GC.GetGeneration(arr)}");
    GC.KeepAlive(arr);
}
Console.WriteLine("A1 esperado: gen 0 abaixo do limiar, gen 2 a partir dele. Se o limiar fosse 85 KiB=87.040, byte[85000] ficaria em gen 0.");
Console.WriteLine();

// ---------- A2: chave struct sem IEquatable causa boxing por lookup ----------
Console.WriteLine("== A2: chave struct sem IEquatable ==");
var semEq = new Dictionary<ChaveSemEq, int>();
var comEq = new Dictionary<ChaveComEq, int>();
for (int i = 0; i < 64; i++) { semEq[new ChaveSemEq(i, i)] = i; comEq[new ChaveComEq(i, i)] = i; }
long a2Sem = Medir(() => { for (int i = 0; i < 200_000; i++) semEq.TryGetValue(new ChaveSemEq(i & 63, i & 63), out _); });
long a2Com = Medir(() => { for (int i = 0; i < 200_000; i++) comEq.TryGetValue(new ChaveComEq(i & 63, i & 63), out _); });
Console.WriteLine($"A2 sem IEquatable: {a2Sem} bytes alocados em 200k lookups");
Console.WriteLine($"A2 com IEquatable: {a2Com} bytes alocados em 200k lookups");
Console.WriteLine($"A2 razao={(a2Com == 0 ? double.PositiveInfinity : (double)a2Sem / a2Com):F1}x");
Console.WriteLine();

// ---------- A3: concatenacao em laco aloca quadraticamente ----------
Console.WriteLine("== A3: concat em laco vs StringBuilder ==");
long a3Concat = Medir(() => { var s = ""; for (int i = 0; i < 4_000; i++) s += "abcdefghij"; GC.KeepAlive(s); });
long a3Sb = Medir(() => { var sb = new StringBuilder(); for (int i = 0; i < 4_000; i++) sb.Append("abcdefghij"); GC.KeepAlive(sb.ToString()); });
Console.WriteLine($"A3 concat:        {a3Concat} bytes");
Console.WriteLine($"A3 StringBuilder: {a3Sb} bytes");
Console.WriteLine($"A3 razao={(double)a3Concat / a3Sb:F0}x  (quadratico vs amortizado)");
Console.WriteLine();

// ---------- A4: enumeracao multipla de IEnumerable reexecuta a fonte ----------
Console.WriteLine("== A4: enumeracao multipla ==");
int execucoes = 0;
IEnumerable<int> Fonte()
{
    execucoes++;
    for (int i = 0; i < 3; i++) yield return i;
}
var lazy = Fonte().Select(x => x * 2);
_ = lazy.Count();
_ = lazy.Sum();
_ = lazy.ToList();
Console.WriteLine($"A4 iterador executado {execucoes}x apos Count+Sum+ToList sobre a MESMA query (esperado 3)");
execucoes = 0;
var materializado = Fonte().Select(x => x * 2).ToList();
_ = materializado.Count; _ = materializado.Sum();
Console.WriteLine($"A4 apos ToList: iterador executado {execucoes}x (esperado 1)");
Console.WriteLine();

// ---------- A5: struct mutavel em List<T> perde a mutacao ----------
Console.WriteLine("== A5: struct mutavel em colecao ==");
var lista = new List<Contador> { new Contador(0) };
lista[0].Incrementar();
Console.WriteLine($"A5 List<struct> apos Incrementar via indexador: valor={lista[0].Valor} (esperado 0 = mutacao perdida)");
var arrStruct = new Contador[1];
arrStruct[0].Incrementar();
Console.WriteLine($"A5 Contador[] apos Incrementar via indice:      valor={arrStruct[0].Valor} (esperado 1 = array da acesso por referencia)");
var listaClasse = new List<ContadorClasse> { new ContadorClasse() };
listaClasse[0].Incrementar();
Console.WriteLine($"A5 List<classe> apos Incrementar:               valor={listaClasse[0].Valor} (esperado 1)");
Console.WriteLine();

// ---------- A6: GC.GetGeneration em literal de string (heap NonGC no .NET 8) ----------
Console.WriteLine("== A6: geracao de literal congelado ==");
const string literal = "literal-congelado-para-teste";
var runtime = new string("construida-em-runtime".ToCharArray());
int genLiteral = GC.GetGeneration(literal);
Console.WriteLine($"A6 literal  -> geracao={genLiteral}{(genLiteral == int.MaxValue ? " (int.MaxValue = heap NonGC)" : "")}");
Console.WriteLine($"A6 runtime  -> geracao={GC.GetGeneration(runtime)}");
Console.WriteLine($"A6 int.MaxValue = {int.MaxValue}");
Console.WriteLine();

// ---------- A7: boxing em lock sobre tipo valor ----------
Console.WriteLine("== A7: interpolacao de struct causa boxing? ==");
long a7 = Medir(() => { for (int i = 0; i < 100_000; i++) { object o = i; GC.KeepAlive(o); } });
long a7b = Medir(() => { long acc = 0; for (int i = 0; i < 100_000; i++) acc += i; GC.KeepAlive(acc); });
Console.WriteLine($"A7 boxing explicito de int: {a7} bytes em 100k ({(double)a7 / 100_000:F0} bytes/box)");
Console.WriteLine($"A7 sem boxing:              {a7b} bytes em 100k");
Console.WriteLine();

Console.WriteLine("FIM");

static long Medir(Action acao)
{
    acao(); // aquece: JIT e caches fora da medicao
    var antes = GC.GetAllocatedBytesForCurrentThread();
    acao();
    return GC.GetAllocatedBytesForCurrentThread() - antes;
}

struct ChaveSemEq
{
    public int A, B;
    public ChaveSemEq(int a, int b) { A = a; B = b; }
}

struct ChaveComEq : IEquatable<ChaveComEq>
{
    public int A, B;
    public ChaveComEq(int a, int b) { A = a; B = b; }
    public bool Equals(ChaveComEq o) => A == o.A && B == o.B;
    public override bool Equals(object? o) => o is ChaveComEq k && Equals(k);
    public override int GetHashCode() => HashCode.Combine(A, B);
}

struct Contador
{
    public int Valor;
    public Contador(int v) { Valor = v; }
    public void Incrementar() => Valor++;
}

class ContadorClasse
{
    public int Valor;
    public void Incrementar() => Valor++;
}
