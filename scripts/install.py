#!/usr/bin/env python3
"""Instala o dotnet8-senior nos CLIs suportados, apontando a base para este clone.

  python scripts/install.py                 detecta e instala no que existir
  python scripts/install.py --check         so mostra o que faria
  python scripts/install.py --target omp    forca um alvo (omp | claude | cursor | agents-md)

Nao existe formato universal de agente entre CLIs. O que existe e um arquivo
canonico (agent/dotnet8-senior.md) e uma traducao por harness:

  omp        ~/.omp/agent/agents/         frontmatter OMP, tools minusculo, model @role
  claude     ~/.claude/agents/            frontmatter Claude Code, tools Capitalizado
  cursor     .cursor/rules/               .mdc com cabecalho de rule
  agents-md  ./AGENTS.md                  sem frontmatter, contexto puro (Codex e afins)

Notas apuradas na pratica, nao na documentacao:
  - O OMP ignora `.claude/agents` de proposito; le `.omp/agents` e raizes de
    plugin do marketplace Claude. Instalar nos dois lugares e o caminho.
  - `model: inherit` funciona no Claude Code mas quebra no despacho de subagente
    do OMP com "No model selected". No OMP use um papel concreto (`@default`).
  - A lista de agentes que o modelo LE e memoizada no inicio da sessao. Agente
    criado no meio da sessao nao aparece na lista, mas O DESPACHO FUNCIONA
    porque a execucao redescobre. Reinicie o CLI para ve-lo listado.
"""
import pathlib
import re
import shutil
import sys

REPO = pathlib.Path(__file__).resolve().parent.parent
SRC = REPO / "agent" / "dotnet8-senior.md"
HOME = pathlib.Path.home()

OBRIGATORIOS = [
    "base/nucleo.md", "base/pontes.md",
    "base/principios/linguagem.md", "base/principios/query.md",
    "base/principios/runtime.md", "base/principios/dados.md",
    "base/principios/hardware.md",
]

TOOLS_CLAUDE = "Read, Grep, Glob, Bash, Write, Edit"


def carregar():
    conteudo = SRC.read_text(encoding="utf-8")
    base_fwd = str(REPO).replace("\\", "/")
    conteudo, n1 = re.subn(r"(?m)^2\. `.*?/base/principios/` — base de referência",
                           f"2. `{base_fwd}/base/principios/` — base de referência", conteudo)
    conteudo, n2 = re.subn(r"\(ou `.*?/base/pontes\.md`\)",
                           f"(ou `{base_fwd}/base/pontes.md`)", conteudo)
    return conteudo, n1 + n2


def partir(conteudo):
    """separa frontmatter do corpo"""
    m = re.match(r"(?s)^---\n(.*?)\n---\n(.*)$", conteudo)
    if not m:
        return "", conteudo
    return m.group(1), m.group(2).lstrip("\n")


def alvo_omp(conteudo):
    return HOME / ".omp" / "agent" / "agents" / "dotnet8-senior.md", conteudo


def alvo_claude(conteudo):
    """Claude Code: tools Capitalizado, model inherit."""
    fm, corpo = partir(conteudo)
    fm = re.sub(r"(?m)^tools:.*$", f"tools: {TOOLS_CLAUDE}", fm)
    fm = re.sub(r'(?m)^model:.*$', "model: inherit", fm)
    fm = re.sub(r"(?m)^read-summarize:.*\n?", "", fm)
    return HOME / ".claude" / "agents" / "dotnet8-senior.md", f"---\n{fm}\n---\n\n{corpo}"


def alvo_cursor(conteudo):
    """Cursor: .mdc com cabecalho de rule, sempre aplicada."""
    _, corpo = partir(conteudo)
    cab = ("---\n"
           "description: Engenheiro senior de backend .NET 8 com base verificada\n"
           "globs: [\"**/*.cs\", \"**/*.sql\", \"**/*.csproj\"]\n"
           "alwaysApply: false\n"
           "---\n\n")
    return pathlib.Path.cwd() / ".cursor" / "rules" / "dotnet8-senior.mdc", cab + corpo


def alvo_agents_md(conteudo):
    """Codex e afins: sem frontmatter, so contexto."""
    _, corpo = partir(conteudo)
    return pathlib.Path.cwd() / "AGENTS.md", corpo


ALVOS = {
    "omp": (alvo_omp, lambda: (HOME / ".omp").exists()),
    "claude": (alvo_claude, lambda: (HOME / ".claude").exists()),
    "cursor": (alvo_cursor, lambda: (pathlib.Path.cwd() / ".cursor").exists()),
    "agents-md": (alvo_agents_md, lambda: False),  # so sob --target explicito
}


def main() -> int:
    args = sys.argv[1:]
    check = "--check" in args
    forcado = None
    if "--target" in args:
        i = args.index("--target")
        if i + 1 >= len(args):
            print(f"uso: --target <{' | '.join(ALVOS)}>"); return 2
        forcado = args[i + 1]
        if forcado not in ALVOS:
            print(f"alvo desconhecido: {forcado}\nvalidos: {', '.join(ALVOS)}"); return 2

    if not SRC.exists():
        print(f"erro: {SRC} nao encontrado"); return 1
    faltando = [f for f in OBRIGATORIOS if not (REPO / f).exists()]
    if faltando:
        print("erro: base incompleta, faltam:")
        for f in faltando: print(f"  {f}")
        return 1

    conteudo, subs = carregar()
    if subs < 2:
        print(f"aviso: {2 - subs} caminho(s) de fallback nao reescrito(s) — confira o agente")

    escolhidos = [forcado] if forcado else [n for n, (_, detecta) in ALVOS.items() if detecta()]
    if not escolhidos:
        print("nenhum CLI detectado. Use --target para forcar:")
        for n in ALVOS: print(f"  python scripts/install.py --target {n}")
        return 1

    for nome in escolhidos:
        construir, _ = ALVOS[nome]
        destino, texto = construir(conteudo)
        if check:
            print(f"[{nome}] instalaria em: {destino}")
            continue
        destino.parent.mkdir(parents=True, exist_ok=True)
        if destino.exists():
            shutil.copy(destino, destino.with_suffix(destino.suffix + ".bak"))
        destino.write_text(texto, encoding="utf-8")
        print(f"[{nome}] instalado: {destino}")

    if not check:
        print()
        print(f"base apontada: {str(REPO).replace(chr(92), '/')}/base")
        print()
        print("IMPORTANTE: reinicie o CLI. A lista de agentes que o modelo le e")
        print("memoizada no inicio da sessao — sem reiniciar, o agente nao aparece")
        print("listado (embora o despacho pelo nome ja funcione).")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
