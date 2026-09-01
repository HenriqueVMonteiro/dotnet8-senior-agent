#!/usr/bin/env python3
"""Instala o dotnet8-senior no OMP, apontando a base para este clone.

  python scripts/install.py            instala em ~/.omp/agent/agents/
  python scripts/install.py --check    so mostra o que faria
"""
import pathlib, re, sys

REPO = pathlib.Path(__file__).resolve().parent.parent
SRC = REPO / "agent" / "dotnet8-senior.md"
DEST_DIR = pathlib.Path.home() / ".omp" / "agent" / "agents"
DEST = DEST_DIR / "dotnet8-senior.md"

OBRIGATORIOS = ["base/nucleo.md", "base/pontes.md",
                "base/principios/linguagem.md", "base/principios/query.md",
                "base/principios/runtime.md", "base/principios/dados.md",
                "base/principios/hardware.md"]


def main() -> int:
    check = "--check" in sys.argv
    if not SRC.exists():
        print(f"erro: {SRC} nao encontrado"); return 1
    faltando = [f for f in OBRIGATORIOS if not (REPO / f).exists()]
    if faltando:
        print("erro: base incompleta, faltam:")
        for f in faltando: print(f"  {f}")
        return 1

    base_fwd = str(REPO).replace("\\", "/")
    conteudo = SRC.read_text(encoding="utf-8")
    conteudo, n1 = re.subn(r"(?m)^2\. `.*?/base/principios/` — base de referência",
                           f"2. `{base_fwd}/base/principios/` — base de referência", conteudo)
    conteudo, n2 = re.subn(r"\(ou `.*?/base/pontes\.md`\)",
                           f"(ou `{base_fwd}/base/pontes.md`)", conteudo)
    if n1 == 0: print("aviso: caminho de fallback dos principios nao encontrado")
    if n2 == 0: print("aviso: caminho de fallback de pontes.md nao encontrado")

    if check:
        print(f"instalaria em: {DEST}")
        print(f"base apontada: {base_fwd}/base")
        print(f"substituicoes: principios={n1} pontes={n2}")
        return 0

    DEST_DIR.mkdir(parents=True, exist_ok=True)
    DEST.write_text(conteudo, encoding="utf-8")
    print(f"instalado: {DEST}")
    print(f"base:      {base_fwd}/base")
    print()
    print("Abra o OMP e confira em Alt+A (hub de agentes).")
    print('Uso: "usa o dotnet8-senior pra revisar src/Foo.cs"')
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
