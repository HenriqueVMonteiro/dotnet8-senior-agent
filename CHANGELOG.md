# Changelog

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
