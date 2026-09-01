using System.Runtime.InteropServices;

// Arbitragem: o red team acusou dois itens do eixo `hardware` de estarem ERRADOS.
// Em vez de acreditar na base ou no red team, medimos.

Console.WriteLine($"AMBIENTE runtime={RuntimeInformation.FrameworkDescription}");
Console.WriteLine();

// ---------- R1: Math.Abs(int.MinValue) em contexto unchecked ----------
// BASE alega: retorna int.MinValue (wrap silencioso do complemento de dois).
// RED TEAM alega: lança OverflowException mesmo em unchecked.
Console.WriteLine("== R1: Math.Abs(int.MinValue) ==");
int min = int.MinValue;
string r1u;
try { unchecked { r1u = $"retornou {Math.Abs(min)}"; } }
catch (OverflowException) { r1u = "lancou OverflowException"; }
Console.WriteLine($"R1 unchecked -> Math.Abs(int.MinValue) {r1u}");

string r1n;
try { r1n = $"retornou {-min}"; }
catch (OverflowException) { r1n = "lancou OverflowException"; }
Console.WriteLine($"R1 negacao unaria de variavel (unchecked padrao) -> -min {r1n}");

string r1c;
try { checked { r1c = $"retornou {-min}"; } }
catch (OverflowException) { r1c = "lancou OverflowException"; }
Console.WriteLine($"R1 checked -> -min {r1c}");
Console.WriteLine();

// ---------- R2: promoção numérica em comparação int × uint ----------
// BASE alega: o int é convertido para uint, entao negativo vira gigante e a comparacao inverte.
// RED TEAM alega: ambos sao promovidos para long, entao o negativo continua negativo.
Console.WriteLine("== R2: comparacao int x uint ==");
int neg = -1;
uint pos = 1u;
bool cmp = neg < pos;
Console.WriteLine($"R2 (int)-1 < (uint)1  ->  {cmp}");
Console.WriteLine($"R2 tipo inferido da soma (-1 + 1u): {((-1) + 1u).GetType().Name}");
Console.WriteLine($"R2 se promovesse para uint, -1 viraria {unchecked((uint)neg)} e a comparacao daria False");
Console.WriteLine($"R2 se promove para long, -1 continua -1 e a comparacao da True");
Console.WriteLine();

// contraste: com literal constante o compilador trata diferente
Console.WriteLine("== R2b: contraste com uint em contexto que forca conversao ==");
uint u = 1u;
int i = -1;
Console.WriteLine($"R2b (uint)1 > (int)-1 -> {u > i}");
unchecked { Console.WriteLine($"R2b conversao explicita (uint)(-1) = {(uint)i}"); }
Console.WriteLine();

Console.WriteLine("FIM");
