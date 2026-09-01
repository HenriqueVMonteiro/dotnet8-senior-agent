#!/usr/bin/env node
/**
 * Instalador do dotnet8-senior. Zero dependências.
 *
 *   npx github:HenriqueVMonteiro/dotnet8-senior-agent      instala tudo que detectar
 *   npx github:...#main -- --check                          dry run
 *   npx github:... -- --target omp                          força um alvo
 *   npx github:... -- --from-clone                          aponta para o clone, não copia a base
 *
 * Não existe formato universal de agente entre CLIs. Existe um arquivo canônico
 * (agent/dotnet8-senior.md) e uma tradução por harness.
 *
 * Duas incompatibilidades apuradas na prática, não na documentação:
 *   - `model: inherit` funciona no Claude Code e QUEBRA no despacho de subagente
 *     do OMP com "No model selected". No OMP é preciso papel concreto (@default).
 *   - O OMP ignora `.claude/agents` de propósito; lê `.omp/agents` e raízes de
 *     plugin do marketplace Claude. Por isso os ce-* aparecem e um arquivo solto
 *     em .claude/agents não apareceria.
 */

const fs = require("node:fs");
const path = require("node:path");
const os = require("node:os");

const PKG = path.resolve(__dirname, "..");
const HOME = os.homedir();
const STABLE = path.join(HOME, ".dotnet8-senior");

const OBRIGATORIOS = [
  "base/nucleo.md",
  "base/pontes.md",
  "base/principios/linguagem.md",
  "base/principios/query.md",
  "base/principios/runtime.md",
  "base/principios/dados.md",
  "base/principios/hardware.md",
];

const TOOLS_CLAUDE = "Read, Grep, Glob, Bash, Write, Edit";

function log(msg = "") { process.stdout.write(msg + "\n"); }

function copiarDir(origem, destino) {
  fs.mkdirSync(destino, { recursive: true });
  for (const entrada of fs.readdirSync(origem, { withFileTypes: true })) {
    const de = path.join(origem, entrada.name);
    const para = path.join(destino, entrada.name);
    if (entrada.isDirectory()) copiarDir(de, para);
    else fs.copyFileSync(de, para);
  }
}

function partirFrontmatter(texto) {
  const m = texto.match(/^---\n([\s\S]*?)\n---\n([\s\S]*)$/);
  if (!m) return { fm: "", corpo: texto };
  return { fm: m[1], corpo: m[2].replace(/^\n+/, "") };
}

/** Reescreve os caminhos de fallback da base para onde ela realmente vai morar. */
function apontarBase(texto, baseDir) {
  const fwd = baseDir.split(path.sep).join("/");
  let n = 0;
  texto = texto.replace(
    /^2\. `.*?\/base\/principios\/` — base de referência/m,
    () => { n++; return `2. \`${fwd}/base/principios/\` — base de referência`; }
  );
  texto = texto.replace(
    /\(ou `.*?\/base\/pontes\.md`\)/,
    () => { n++; return `(ou \`${fwd}/base/pontes.md\`)`; }
  );
  return { texto, substituicoes: n };
}

const ALVOS = {
  omp: {
    detecta: () => fs.existsSync(path.join(HOME, ".omp")),
    destino: () => path.join(HOME, ".omp", "agent", "agents", "dotnet8-senior.md"),
    transforma: (t) => t,
  },
  claude: {
    detecta: () => fs.existsSync(path.join(HOME, ".claude")),
    destino: () => path.join(HOME, ".claude", "agents", "dotnet8-senior.md"),
    transforma: (t) => {
      let { fm, corpo } = partirFrontmatter(t);
      fm = fm.replace(/^tools:.*$/m, `tools: ${TOOLS_CLAUDE}`);
      fm = fm.replace(/^model:.*$/m, "model: inherit");
      fm = fm.replace(/^read-summarize:.*\n?/m, "");
      return `---\n${fm}\n---\n\n${corpo}`;
    },
  },
  cursor: {
    detecta: () => fs.existsSync(path.join(process.cwd(), ".cursor")),
    destino: () => path.join(process.cwd(), ".cursor", "rules", "dotnet8-senior.mdc"),
    transforma: (t) => {
      const { corpo } = partirFrontmatter(t);
      const cab =
        "---\n" +
        "description: Engenheiro senior de backend .NET 8 com base verificada\n" +
        'globs: ["**/*.cs", "**/*.sql", "**/*.csproj"]\n' +
        "alwaysApply: false\n" +
        "---\n\n";
      return cab + corpo;
    },
  },
  "agents-md": {
    detecta: () => false, // só sob --target explícito
    destino: () => path.join(process.cwd(), "AGENTS.md"),
    transforma: (t) => partirFrontmatter(t).corpo,
  },
};

function main() {
  const argv = process.argv.slice(2);
  const check = argv.includes("--check");
  const fromClone = argv.includes("--from-clone");
  const iTarget = argv.indexOf("--target");
  const forcado = iTarget >= 0 ? argv[iTarget + 1] : null;

  if (forcado && !ALVOS[forcado]) {
    log(`alvo desconhecido: ${forcado}`);
    log(`válidos: ${Object.keys(ALVOS).join(", ")}`);
    return 2;
  }

  const src = path.join(PKG, "agent", "dotnet8-senior.md");
  if (!fs.existsSync(src)) {
    log(`erro: ${src} não encontrado`);
    return 1;
  }
  const faltando = OBRIGATORIOS.filter((f) => !fs.existsSync(path.join(PKG, f)));
  if (faltando.length) {
    log("erro: base incompleta, faltam:");
    faltando.forEach((f) => log(`  ${f}`));
    return 1;
  }

  // Onde a base vai morar. Rodando por npx, o pacote está num cache temporário
  // que o npm apaga — copiar para um diretório estável é obrigatório.
  let baseDir;
  if (fromClone) {
    baseDir = PKG;
    log(`base: usando o clone em ${baseDir}`);
  } else {
    baseDir = STABLE;
    if (!check) {
      fs.rmSync(path.join(STABLE, "base"), { recursive: true, force: true });
      copiarDir(path.join(PKG, "base"), path.join(STABLE, "base"));
      log(`base copiada para ${path.join(STABLE, "base")}`);
    } else {
      log(`base seria copiada para ${path.join(STABLE, "base")}`);
    }
  }

  const bruto = fs.readFileSync(src, "utf8");
  const { texto, substituicoes } = apontarBase(bruto, baseDir);
  if (substituicoes < 2) {
    log(`aviso: ${2 - substituicoes} caminho(s) de fallback não reescrito(s) — confira o agente`);
  }

  const escolhidos = forcado
    ? [forcado]
    : Object.keys(ALVOS).filter((n) => ALVOS[n].detecta());

  if (!escolhidos.length) {
    log("nenhum CLI detectado. Use --target para forçar:");
    Object.keys(ALVOS).forEach((n) => log(`  --target ${n}`));
    return 1;
  }

  for (const nome of escolhidos) {
    const alvo = ALVOS[nome];
    const destino = alvo.destino();
    if (check) {
      log(`[${nome}] instalaria em: ${destino}`);
      continue;
    }
    fs.mkdirSync(path.dirname(destino), { recursive: true });
    if (fs.existsSync(destino)) fs.copyFileSync(destino, destino + ".bak");
    fs.writeFileSync(destino, alvo.transforma(texto), "utf8");
    log(`[${nome}] instalado: ${destino}`);
  }

  if (!check) {
    log();
    log("REINICIE o CLI. A lista de agentes que o modelo lê é memoizada no início");
    log("da sessão — sem reiniciar, o agente não aparece listado (embora o");
    log("despacho pelo nome já funcione).");
    log();
    log('Uso: "usa o dotnet8-senior pra revisar src/Foo.cs"');
  }
  return 0;
}

process.exit(main());
