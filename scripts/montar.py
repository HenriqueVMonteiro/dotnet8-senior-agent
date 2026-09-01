#!/usr/bin/env python3
"""Monta o prompt final do agente, resolvendo o include e escolhendo eixos.

  python montar.py                      -> nucleo + todos os eixos (~110k tokens)
  python montar.py query runtime        -> nucleo + esses eixos
  python montar.py --auto "minha duvida"-> escolhe o eixo pelas palavras da pergunta
  python montar.py query --traps        -> inclui tambem a camada B (armadilhas)

Saida: prompt.md (colar como system prompt) e um resumo de tamanho no terminal.
"""
import pathlib, sys

ROOT = pathlib.Path(__file__).parent
EIXOS = ["linguagem", "query", "runtime", "dados", "hardware"]

PISTAS = {
    "query": ["sql", "t-sql", "tsql", "query", "consulta", "index", "índice", "indice", "join",
              "where", "plano", "select", "cte", "sargav", "deadlock", "lock", "isolation",
              "isolamento", "transa", "rowversion", "ef core", "efcore", "migration",
              "savechanges", "executeupdate", "concorren", "count(", "not in", "udf"],
    "runtime": ["gc", "garbage", "memoria", "memória", "aloca", "loh", "poh", "heap", "gen0",
                "gen2", "finaliz", "dispose", "span", "arraypool", "boxing", "struct", "pinning",
                "fragmenta", "working set", "dump", "vazamento", "leak"],
    "linguagem": ["c#", "csharp", "async", "await", "task", "linq", "ienumerable", "yield",
                  "closure", "regex", "string", "httpclient", "nullable", "record", "generic",
                  "delegate", "event", "reflection", "collection", "dicion"],
    "dados": ["transa", "isolamento", "snapshot", "serializable", "replica", "réplica",
              "particion", "consenso", "consistenc", "outbox", "idempot", "write skew",
              "lost update", "durabil", "commit", "cap ", "eventual", "concorren",
              "savechanges", "save changes", "perdendo update", "perde update", "rollback",
              "dbcontext", "transactionscope", "race", "corrida"],
    "hardware": ["cache", "linha de cache", "false sharing", "float", "double", "decimal",
                 "ponto flutuante", "ieee", "bit", "byte", "overflow", "endian", "alinhamento",
                 "desmontagem", "assembly", "x86", "jit", "branch", "localidade",
                 "arredond", "precisao", "precisão", "truncad", "casas decimais"],
}


def escolher(pergunta: str) -> list[str]:
    import re
    p = pergunta.lower()
    pontos = {e: sum(1 for k in ks if k in p) for e, ks in PISTAS.items()}
    # literal decimal em conta aritmetica/comparacao -> ponto flutuante (eixo hardware)
    if re.search(r"\d+[.,]\d+", p) and re.search(r"[+\-*/]|==|igual|soma|arredond|total|precis", p):
        pontos["hardware"] += 2
    vivos = [e for e, n in sorted(pontos.items(), key=lambda x: -x[1]) if n > 0]
    return vivos[:2] or ["linguagem"]


def main() -> int:
    args = sys.argv[1:]
    traps = "--traps" in args
    args = [a for a in args if a != "--traps"]

    if args and args[0] == "--auto":
        pergunta = " ".join(args[1:])
        if not pergunta:
            print("uso: python montar.py --auto \"sua duvida\"")
            return 2
        eixos = escolher(pergunta)
        print(f"eixo(s) escolhido(s) pela pergunta: {', '.join(eixos)}")
    else:
        eixos = args or EIXOS
        invalidos = [e for e in eixos if e not in EIXOS]
        if invalidos:
            print(f"eixo desconhecido: {', '.join(invalidos)}\nvalidos: {', '.join(EIXOS)}")
            return 2

    agente = (ROOT / "AGENTE.md").read_text(encoding="utf-8")
    nucleo = (ROOT / "base" / "nucleo.md").read_text(encoding="utf-8")
    agente = agente.replace("{incluir: base/nucleo.md}", nucleo)

    partes = [agente, "\n\n# BASE CARREGADA\n"]
    for e in eixos:
        partes.append("\n" + (ROOT / "base" / "principios" / f"{e}.md").read_text(encoding="utf-8"))
    if traps:
        ref = ROOT / "base" / "referencia" / "dados.md"
        if ref.exists():
            partes.append("\n\n# CAMADA B — ARMADILHAS\n\n" + ref.read_text(encoding="utf-8"))

    prompt = "".join(partes)
    (ROOT / "prompt.md").write_text(prompt, encoding="utf-8")

    tok = len(prompt) // 4
    try:
        print(f"prompt.md escrito: {len(prompt):,} chars, ~{tok:,} tokens")
        print(f"eixos: {', '.join(eixos)}" + (" + camada B" if traps else ""))
        if tok > 100_000:
            print("AVISO: acima de 100k tokens. Carregue menos eixos para deixar espaco de conversa.")
    except OSError:
        pass  # stdout fechado por pipe (head/tail)
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
