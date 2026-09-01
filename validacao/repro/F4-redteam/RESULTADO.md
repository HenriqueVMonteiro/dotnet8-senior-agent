# F4-redteam — arbitragem entre a base e o red team

O red team acusou dois itens do eixo `hardware` de estarem **errados**. Em vez de
acreditar em qualquer um dos lados, medimos.

COMO RODAR: `cd validacao/repro/F4-redteam && dotnet run -c Release`

AMBIENTE: .NET 8.0.28

SAIDA OBSERVADA:
```
== R1: Math.Abs(int.MinValue) ==
R1 unchecked -> Math.Abs(int.MinValue) lancou OverflowException
R1 negacao unaria de variavel (unchecked padrao) -> -min retornou -2147483648
R1 checked -> -min lancou OverflowException

== R2: comparacao int x uint ==
R2 (int)-1 < (uint)1  ->  True
R2 tipo inferido da soma (-1 + 1u): Int64
R2 se promovesse para uint, -1 viraria 4294967295 e a comparacao daria False
R2 se promove para long, -1 continua -1 e a comparacao da True

== R2b: contraste ==
R2b (uint)1 > (int)-1 -> True
R2b conversao explicita (uint)(-1) = 4294967295
```

## Vereditos

| # | Base alegava | Red team alegava | Vencedor |
|---|---|---|---|
| R1 | `Math.Abs(int.MinValue)` faz wrap silencioso e devolve `int.MinValue` | lança `OverflowException` mesmo em `unchecked` | **RED TEAM** |
| R2 | `int` comparado com `uint` é convertido para `uint`, invertendo a comparação | ambos são promovidos para `long`, o negativo continua negativo | **RED TEAM** |

## Evidência

**R1 — a base estava errada, com uma nuance que nenhum dos dois enunciou.**
`Math.Abs(int.MinValue)` **lança `OverflowException` mesmo dentro de `unchecked`**,
porque a checagem é feita dentro do método da BCL, não pelo contexto do chamador —
`unchecked` só governa operadores aritméticos do próprio código, não o corpo de um
método já compilado.

Mas a negação unária `-min` sobre uma **variável** devolveu `-2147483648`
silenciosamente em contexto padrão, e lançou em `checked`. Ou seja: os dois
caminhos que parecem equivalentes têm comportamento oposto sob `unchecked`.
Essa distinção é mais útil que qualquer das duas versões originais.

**R2 — a base estava errada e o red team certo, confirmado por dupla via.**
`(int)-1 < (uint)1` devolveu **`True`**. Se houvesse conversão para `uint`, o `-1`
viraria `4294967295` e o resultado seria `False`. A confirmação independente veio
do tipo inferido: `(-1 + 1u).GetType().Name` devolveu **`Int64`** — promoção
binária para `long`, exatamente como a especificação do C# manda quando um
operando é `int` e o outro `uint`.

## Consequência

Os dois itens foram corrigidos na base e passaram a carregar `EVIDENCIA`. O caso
importa além do conteúdo: **o red team pegou dois erros que sobreviveram a três
extrações independentes e ao filtro mecânico**. Consenso não substitui ataque, e
ataque não substitui medição — os três são camadas diferentes.
