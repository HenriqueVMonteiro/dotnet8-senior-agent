# Changelog

## 1.2.0

### Adicionado
- Instalação por `npx skills add HenriqueVMonteiro/dotnet8-senior-agent`, via
  [vercel-labs/skills](https://github.com/vercel-labs/skills) — 77 agentes
  suportados, repo privado com a credencial git existente, `npx skills update`
- `skills/dotnet8-senior/SKILL.md`: gate de carga da base, regras duras e tabela
  de eixos, ~1,6k tokens

### Alterado
- A base vive em `skills/dotnet8-senior/base/` e é referenciada por caminho
  **relativo**. O `skills add` leva o diretório inteiro, então nada precisa ser
  reescrito no destino — verificado: os 8 arquivos chegam intactos

### Corrigido
- `agent/dotnet8-senior.md` apontava para `Livros_Agente/base/principios/`,
  caminho que deixou de existir com o move. O instalador reescrevia na
  instalação, mas a fonte no repo mentia para quem lesse ou rodasse
  `--from-clone`

## 1.1.1

### Corrigido
- `.gitignore` continha `bin/` para ignorar saída de build do .NET e capturou
  também o `bin/install.js` do Node. O `git add -A` pulou o arquivo em silêncio,
  o repo subiu com `package.json` apontando para um bin inexistente, e o
  `npx github:...` falhava com *"'dotnet8-senior-agent' não é reconhecido como um
  comando"*. Padrões agora são específicos: `**/[Bb]in/[Dd]ebug/`,
  `**/[Bb]in/[Rr]elease/`, `**/obj/`

## 1.1.0

### Adicionado
- Instalador Node de uma linha, zero dependências, multi-CLI
  (`npx github:HenriqueVMonteiro/dotnet8-senior-agent`)
- README reescrito com os sete avisos importantes no corpo, não em doc separado

### Corrigido
- A carga da base virou **gate na primeira ação** em vez de instrução no rodapé
  (linha 164 de 215). Um agente despachado com contrato mecânico pulava a carga;
  os testes anteriores mascaravam o defeito porque reforçavam a instrução na task

### Removido
- `scripts/install.py` — duas implementações do mesmo instalador divergem

## 1.0.0

Primeira versão pública.

### Base
- 450 itens de camada A em cinco eixos (`linguagem`, `query`, `runtime`,
  `dados`, `hardware`), 90 por eixo
- Destilados de 6.297 extrações brutas — 204 execuções de extração
  (68 unidades × 3 framings independentes)
- 24 itens marcados `EVIDENCIA`, verificados por execução real
- `base/pontes.md`: 15 sínteses cruzadas entre eixos
- `base/referencia/dados.md`: 24 armadilhas com código antes/depois

### Verificação
- 8 repros executáveis com saída observada verbatim
- Suíte de regressão com metacasos de honestidade
- Um veredito `INCONCLUSIVO` mantido no repositório por transparência

### Achados que contrariaram o senso comum
- "UDF escalar mata o paralelismo" é falso a partir do compatibility level 150
  (56.471 ms serial × 321 ms paralelo, mesma UDF)
- O limiar do LOH incide sobre o tamanho total do objeto (~84.976 elementos
  para `byte[]` em x64), não sobre o comprimento do array
- O nível de isolamento sobrevive ao retorno da conexão ao pool
- Struct mutável perde mutação em `List<T>` mas não em array

### Agente
- Definição para OMP (~2.4k tokens) com carga de eixo sob demanda
- Protocolo que consulta `pontes.md` antes dos eixos em perguntas
  que atravessam camadas
