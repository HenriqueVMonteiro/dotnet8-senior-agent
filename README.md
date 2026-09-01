# dotnet8-senior

Agente de revisão de código .NET 8 apoiado numa base de conhecimento destilada de
cinco livros técnicos — com **24 itens verificados por execução real**, não por
opinião de modelo.

Alvo fixo: **.NET 8 · C# 12 · SQL Server · EF Core 8**.

```
Você: por que essa struct com bool + long gasta mais que a soma dos campos?

dotnet8-senior:
  PONTE-04 (hardware→linguagem): o CLR alinha o long ao seu próprio tamanho,
  inserindo padding — a struct fica com 16 bytes, não 9.
  Meça com Marshal.SizeOf<T>() e Marshal.OffsetOf para o padding por campo.
```

---

## O que faz diferente

**Recusa dar número sem medição.** Pergunte "quanto mais rápido fica com Dapper"
e ele responde o mecanismo, aponta em que camada do diagnóstico você está
olhando errado, e diz exatamente o que medir. Não estima percentual.

**Pede o contexto que falta.** Volume real das tabelas, índices existentes,
nível de isolamento, Server ou Workstation GC, p99 aceitável. Sem poder
perguntar, declara a suposição na primeira linha.

**Não emite recurso fora do alvo.** Pediu `field` keyword ou
`System.Threading.Lock` (C# 13)? Ele entrega o equivalente em C# 12 e menciona
em uma linha que existe algo melhor depois.

**Cita a origem.** Cada afirmação carrega o ID do item da base. Ao contrariar
um item, cita o ID e explica por que o caso é exceção.

---

## Instalação

### OMP

```bash
git clone https://github.com/<voce>/dotnet8-senior-agent.git
cd dotnet8-senior-agent
python scripts/install.py
```

Instala `agent/dotnet8-senior.md` em `~/.omp/agent/agents/` e aponta o caminho
da base para onde você clonou. Confira em `Alt+A` (hub de agentes).

Para usar: peça em prosa ao agente principal.

```
usa o dotnet8-senior pra revisar src/Pedidos/PedidoService.cs
manda o dotnet8-senior olhar essa query, tá lenta
```

### Claude Code / Cursor / qualquer ferramenta com arquivo de contexto

Copie a pasta `base/` para a raiz do seu projeto e o conteúdo de
`agent/dotnet8-senior.md` para `CLAUDE.md` (ou `AGENTS.md`, `.cursorrules`).
O agente procura `./base/principios/` antes de qualquer outro caminho.

### Chat web / API

```bash
python scripts/montar.py --auto "sua dúvida"   # escolhe o eixo pela pergunta
python scripts/montar.py query runtime          # eixos específicos
```

Gera `prompt.md` para colar como system prompt.

---

## A base

450 itens em cinco eixos, destilados de 6.297 extrações brutas. Cada eixo publica por **cota de impacto** — 55 correção, 25 performance, 10 manutenção — para que otimização não seja engolida por correção.

| Eixo | Fonte | Cobertura | Itens | Verificados |
|---|---|---|---|---|
| `linguagem` | C# 12 in a Nutshell | 21/21 caps | 90 | 6 |
| `query` | T-SQL Fundamentals 4ed | 11/11 caps | 90 | 5 |
| `runtime` | Pro .NET Memory Management 2ed | 15/15 caps | 90 | 4 |
| `dados` | Designing Data-Intensive Applications | 11/11 caps | 90 | 9 |
| `hardware` | Computer Systems: A Programmer's Perspective | 10/10 unidades | 90 | 2 |

Mais `base/pontes.md`: 15 sínteses cruzadas entre eixos, onde dois livros
descrevem o mesmo mecanismo com nomes diferentes.

O agente carrega **1 ou 2 eixos por pergunta** (~20–26k tokens cada), nunca a
base inteira. Ele mesmo classifica a pergunta e usa `grep` quando já sabe o termo.

### Marcas de confiança

| Marca | Significado |
|---|---|
| `EVIDENCIA` | provado por execução real em `validacao/repro/` — trate como fato |
| `ACORDO 3` | três extrações independentes convergiram |
| `ACORDO 1` | visto por uma só — use, mas sinalize a incerteza |
| `alta` / `media` / `baixa` | quão diretamente a fonte sustenta a ponte para .NET |

**Leia [`docs/LIMITACOES.md`](docs/LIMITACOES.md) antes de confiar nos itens sem
`EVIDENCIA`.** São 424 de 450. O ataque adversarial completo está em
[`docs/RED-TEAM.md`](docs/RED-TEAM.md): 165 problemas levantados, 2 arbitrados por medição.

---

## Os achados que valem sozinhos

Três casos em que a medição contrariou o senso comum — todos reproduzíveis em
`validacao/repro/`:

**"UDF escalar mata o paralelismo" é falso a partir do compatibility level 150.**
Mesma UDF, mesma tabela de 6.901.129 linhas:

| compatibility_level | elapsed | paralelismo |
|---|---|---|
| 140 | 56.471 ms | não |
| 160 (default do SQL Server 2022) | 321 ms | **sim** |

O scalar UDF inlining remove a UDF do plano. A recomendação de reescrever
sobrevive — o predicado sargável ainda é 5,4x mais rápido — mas o mecanismo que
todo mundo repete está errado.

**O limiar do LOH incide sobre o tamanho total do objeto.** A geração vira 2
entre `byte[84_970]` e `byte[84_984]`: com 24 bytes de cabeçalho em x64, o corte
real é ~84.976 elementos, não 85.000.

**O nível de isolamento vaza pelo pool de conexões.** Fora de qualquer escopo,
uma conexão pooled reportou `4/Serializable` enquanto `Pooling=false` reportou
`2/ReadCommitted` no mesmo instante. Um `TransactionScope` sem `TransactionOptions`
em um endpoint contamina requisições posteriores que nunca abriram escopo algum.

---

## Reproduzir a verificação

```bash
docker run -d --name sqlbase -e ACCEPT_EULA=Y \
  -e "MSSQL_SA_PASSWORD=Repro#2024pw" -e MSSQL_PID=Developer \
  -p 14333:1433 mcr.microsoft.com/mssql/server:2022-latest

cd validacao/repro/F4-runtime-linguagem && dotnet run -c Release
```

Cada pasta em `validacao/repro/` tem o código e um `RESULTADO.md` com a saída
observada verbatim, o veredito (`CONFIRMA` / `REFUTA` / `INCONCLUSIVO`) e o
ambiente. Um deles é `INCONCLUSIVO` de propósito — o experimento mediu o
otimizador do JIT em vez do mecanismo, e isso está registrado em vez de
escondido.

`validacao/casos.md` traz a suíte de regressão: entrada, resposta esperada e
critério de reprovação, incluindo metacasos que testam **honestidade** (recusar
número sem medição, declarar lacuna em vez de inventar).

---

## Estrutura

```
agent/dotnet8-senior.md      definição do agente (~2.4k tokens)
base/nucleo.md               regras duras + ordem de diagnóstico
base/principios/*.md         os cinco eixos, 90 itens cada
base/pontes.md               15 sínteses cruzadas
base/referencia/dados.md     camada B: armadilhas com código
validacao/casos.md           suíte de regressão
validacao/repro/             8 experimentos executáveis
docs/METODOLOGIA.md          como a base foi construída
docs/LIMITACOES.md           o que não confiar, e por quê
scripts/                     instalador e montador de prompt
```

---

## Licença e origem

Código, scripts e definição do agente: [MIT](LICENSE).

A base de conhecimento é **derivada de cinco livros com copyright**. Leia
[`NOTICE.md`](NOTICE.md). Os itens são reescritos com mecanismo próprio e
manifestação em .NET — nenhum trecho é transcrito — mas se você usa isso
profissionalmente, **compre os livros**. Eles são melhores que qualquer
destilação.
