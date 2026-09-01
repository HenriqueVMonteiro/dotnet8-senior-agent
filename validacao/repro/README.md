# Ambiente de verificação empírica

Ambiente real, verificado, disponível para qualquer subagente da Fase 4.
Não presuma nada além do que está aqui. Se algo faltar, reporte em vez de simular.

## Runtime .NET

| item | valor |
|---|---|
| SDK instalado | 10.0.301 |
| Runtime alvo | Microsoft.NETCore.App **8.0.28** (instalado e verificado) |
| TargetFramework | `net8.0` — travado por `Directory.Build.props`, não sobrescreva |
| LangVersion | `12.0` — travado |

`dotnet run -c Release` dentro de qualquer pasta de repro compila contra net8.0 e
executa sobre o runtime 8.0.28. Confirmado por smoke test em `_smoke/`.

Verificação de que o processo realmente rodou em .NET 8, obrigatória em todo repro de runtime:

```csharp
Console.WriteLine(System.Runtime.InteropServices.RuntimeInformation.FrameworkDescription);
// esperado: .NET 8.0.28
```

## SQL Server

Container já em execução. Não crie outro; não derrube este.

| item | valor |
|---|---|
| container | `sqlbase` |
| imagem | `mcr.microsoft.com/mssql/server:2022-latest` |
| versão | 16.0.4265.3, Developer Edition (64-bit) |
| host porta | `localhost,14333` |
| usuário | `sa` |
| senha | `Repro#2024pw` |

Connection string a partir do host (use exatamente esta):

```
Server=localhost,14333;User Id=sa;Password=Repro#2024pw;TrustServerCertificate=True;Encrypt=False;Database=ReproDb
```

sqlcmd a partir do host:

```bash
docker exec sqlbase /opt/mssql-tools18/bin/sqlcmd -S localhost -U sa -P 'Repro#2024pw' -C -d ReproDb -Q "SELECT 1"
```

Banco `ReproDb` já criado. Regras:

- Crie seus próprios objetos com prefixo único por repro (ex.: `T_TRAP07_Contas`).
- Faça `DROP ... IF EXISTS` no início do seu script, não no fim: o estado após a
  falha é evidência.
- `ALTER DATABASE` é permitido em `ReproDb`, mas o repro DEVE registrar o estado
  anterior e restaurá-lo, porque outros repros compartilham o banco.
  Isso vale especialmente para `READ_COMMITTED_SNAPSHOT` e `ALLOW_SNAPSHOT_ISOLATION`.
  Se o experimento exige um estado global exclusivo, crie um banco próprio
  (`CREATE DATABASE ReproDb_<seu_id>`) e derrube no fim.

Estado inicial de `ReproDb` (verificado):

- `READ_COMMITTED_SNAPSHOT` = OFF
- `ALLOW_SNAPSHOT_ISOLATION` = OFF

## Pacotes NuGet permitidos

Fixe a versão 8.x na família EF Core. Nada de 9.x — quebra o alvo.

| pacote | versão |
|---|---|
| `Microsoft.EntityFrameworkCore.SqlServer` | `8.0.*` |
| `Microsoft.EntityFrameworkCore.Design` | `8.0.*` |
| `Microsoft.Data.SqlClient` | `5.*` |
| `BenchmarkDotNet` | `0.13.*` |

Cache local de pacotes: `work/nuget` (configurado em `Directory.Build.props`).

## Contrato de saída de todo repro

Cada repro vive em `validacao/repro/<ID-DO-ITEM>/` e produz, além do código,
um `RESULTADO.md` com exatamente estas seções:

```
# <ID-DO-ITEM>
AFIRMACAO: <a afirmação testada, uma frase>
COMO RODAR: <comando exato>
SAIDA OBSERVADA:
<colar a saída real do programa, verbatim, sem editar>
VEREDITO: CONFIRMA | REFUTA | INCONCLUSIVO
EVIDENCIA: <uma frase que só pode ser escrita por quem viu a saída acima>
AMBIENTE: <runtime observado, versão do SQL Server, se aplicável>
```

`INCONCLUSIVO` é uma resposta honesta e aceitável. `REFUTA` é resultado de alta
qualidade, não fracasso. Inventar `CONFIRMA` sem saída real é a única falha grave.

## Cuidado estatístico (eixo hardware e microbenchmark)

Não conclua nada de uma execução única em máquina compartilhada.

- Use BenchmarkDotNet com `MemoryDiagnoser` quando a afirmação for sobre alocação.
- Quando a afirmação for sobre efeito de cache ou de localidade, varie o tamanho do
  dado em pelo menos 5 pontos por potência de 2 e mostre a curva. O veredito
  `CONFIRMA` exige que o **joelho previsto** apareça na curva, não apenas que a
  variante A seja mais rápida que a B numa medição.
- Diferença abaixo de 15% em máquina de desenvolvimento não sustenta `CONFIRMA`.
  Marque `INCONCLUSIVO`.
