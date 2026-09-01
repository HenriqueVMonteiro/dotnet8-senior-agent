# pontes.md — síntese cruzada entre eixos

Onde os cinco livros falam do mesmo mecanismo por nomes diferentes. Cada ponte muda uma decisão real — não é analogia decorativa.

Gerado por síntese sobre 459 itens consolidados dos cinco eixos, uma vez que todos os eixos atingiram cobertura completa (linguagem 21/21, query 11/11, runtime 15/15, dados 11/11, hardware 10/10 unidades).

## PONTE-01: Paginação/working set e comportamento do GC sob pressão de memória
CAMADAS: hardware → runtime
MECANISMO UNICO: O working set é a fração de memória virtual residente fisicamente; sob pressão o SO pagina, e o GC não enxerga isso diretamente — precisa de sinal explícito (memory pressure/GCHeapHardLimit) para reagir antes que paginação degrade o processo
CONSEQUENCIA: Um sênior não diagnostica vazamento pelo GC Heap Size nem pelo working set isoladamente: mede memória commitada privada e configura limites de heap do GC (GCHeapHardLimit) alinhados ao limite de container/SO, evitando que o GC só reaja depois que o SO já começou a paginar
IDS: KOKOSA-02-B-23, KOKOSA-02-B-24, KOKOSA-04-B-13, KOKOSA-04-B-34, CSAPP-09-B-2, CSAPP-09-C-10

## PONTE-02: Isolamento SNAPSHOT/READ COMMITTED e IsolationLevel em transações .NET/T-SQL
CAMADAS: dados → query
MECANISMO UNICO: O nível de isolamento é implementado no motor via locks (2PL) ou versionamento (row versioning/version store), e a sintaxe SET TRANSACTION ISOLATION LEVEL / IsolationLevel do ADO.NET/EF Core é apenas a superfície que seleciona esse mecanismo físico
CONSEQUENCIA: Um sênior não escolhe IsolationLevel.Snapshot achando que resolve write skew — sabe que precisa habilitar ALLOW_SNAPSHOT_ISOLATION previamente no banco e que a proteção real contra invariantes multi-linha exige SERIALIZABLE (com custo de deadlock) ou lock explícito, não apenas trocar o enum do lado do código
IDS: DDIA-07-A-9, DDIA-07-B-2, DDIA-07-A-16, DDIA-07-B-5, DDIA-07-A-30, DDIA-07-B-16, TSQL-10-B-11, TSQL-10-B-13

## PONTE-03: B-tree/índice físico e sargabilidade/desenho de índice em T-SQL
CAMADAS: dados → query
MECANISMO UNICO: A ordem física das chaves na B-tree é o que permite busca por intervalo eficiente; um predicado não-sargável (função sobre coluna, tipo incompatível) impede o otimizador de usar essa ordenação e força scan
CONSEQUENCIA: Um sênior desenha o índice e escreve o predicado juntos — sabe que um índice hash (memory-optimized) não serve para range scan, e que CAST/função na coluna do WHERE anula o benefício do B-tree mesmo com índice presente
IDS: DDIA-03-A-2, DDIA-03-C-1, DDIA-03-A-13, TSQL-02-A-21, TSQL-02-C-9

## PONTE-04: Linha de cache/alinhamento de memória e layout de struct/StructLayout em C#
CAMADAS: hardware → linguagem
MECANISMO UNICO: O processador acessa tipos maiores apenas em endereços múltiplos de seu tamanho (alinhamento), e o compilador insere padding em structs para respeitar essa restrição; StructLayout.Sequential só controla isso para tipos sem referências gerenciadas
CONSEQUENCIA: Um sênior não soma o tamanho dos campos para prever o tamanho de uma struct nem confia em StructLayout.Sequential quando há campo de referência — sabe que o CLR reordena/adiciona padding e que só tipos blittable puros têm layout previsível para interop/FieldOffset
IDS: CSAPP-03B-B-1, KOKOSA-02-A-11, KOKOSA-13-A-23, CSAPP-03B-A-5, CSAPP-03B-C-12

## PONTE-05: Deslocamento aritmético com sinal (hardware) e >> vs >>> em C#
CAMADAS: hardware → linguagem
MECANISMO UNICO: A instrução de shift aritmético do processador replica o bit de sinal ao deslocar à direita valores em complemento de dois; o operador >> do C# sobre tipo signed usa exatamente essa instrução, enquanto >>> força shift lógico
CONSEQUENCIA: Um sênior usa >>> deliberadamente ao manipular hash/bits de objeto para evitar que a extensão de sinal introduza 1s espúrios, e não trata >> como equivalente a divisão por potência de 2 quando o valor pode ser negativo
IDS: CSAPP-02A-A-12, CSAPP-03A-A-1, CS12-02-A-24, CS12-02-B-47

## PONTE-06: Overflow silencioso da ULA (complemento de dois) e checked/unchecked em C#/aritmética T-SQL
CAMADAS: hardware → linguagem
MECANISMO UNICO: A ULA sempre calcula um bit de overflow em soma/subtração/multiplicação de complemento de dois, mas nenhuma instrução examina essa flag automaticamente; unchecked (padrão do C#) nunca a consulta, então o valor 'dá a volta' silenciosamente do mesmo jeito que no hardware
CONSEQUENCIA: Um sênior sabe que trocar int por uint, ou multiplicar em contexto unchecked, não elimina o overflow — só muda o padrão de wrap — e explicitamente envolve cálculos de tamanho de alocação e conversões estreitas em checked{} quando a entrada não é confiável
IDS: CSAPP-03A-A-3, CSAPP-02A-A-9, CSAPP-02A-A-10, CS12-02-A-19, CS12-02-B-4, CSAPP-02A-A-16

## PONTE-07: Ponto flutuante IEEE 754 (hardware) e Nunca use float/double para dinheiro (C#) e FLOAT do T-SQL
CAMADAS: hardware → linguagem → query
MECANISMO UNICO: A mantissa binária só representa exatamente somas de potências de dois; frações decimais comuns (0.1, 0.3) não têm representação exata em nenhuma largura de ponto flutuante do hardware, e essa limitação atravessa float/double do C# e FLOAT do SQL Server igualmente
CONSEQUENCIA: Um sênior usa decimal (C#) e decimal/numeric (T-SQL) para dinheiro em vez de float/double/FLOAT, e não espera que aumentar a precisão declarada de FLOAT(n) no SQL Server resolva o problema, porque a granularidade real ainda é ditada pelo hardware IEEE 754 binário
IDS: CSAPP-02B-A-1, CSAPP-02B-C-1, CSAPP-02B-C-12, CS12-02-A-13, CS12-02-B-11

## PONTE-08: Pin de objeto gerenciado (GC) e ponteiro nativo/fixed em interop
CAMADAS: runtime → linguagem
MECANISMO UNICO: O GC compactante move objetos e atualiza referências apenas em locais que ele varre como raízes (pilha gerenciada, remembered set); um endereço obtido via fixed só é válido enquanto o pin dura, pois fora dele a compactação pode mover o objeto sem avisar o código nativo
CONSEQUENCIA: Um sênior nunca guarda um IntPtr obtido dentro de um bloco fixed para uso posterior, e mantém o pin ativo por toda a janela em que código nativo pode desreferenciar o endereço — inclusive avaliando GCHandle.Alloc(Pinned) quando o escopo lexical de fixed não é suficiente
IDS: KOKOSA-01-A-27, KOKOSA-01-B-14, KOKOSA-01-B-13, KOKOSA-08-C-10, KOKOSA-09-A-5, CSAPP-03B-B-11

## PONTE-09: Stack de execução física e stackalloc/recursão sem limite
CAMADAS: hardware → linguagem/runtime
MECANISMO UNICO: A pilha é uma região fixa e finita por thread reservada no início; tanto stackalloc dimensionado por entrada quanto recursão sem profundidade limitada consomem essa mesma região finita, e o estouro é sinalizado pelo SO como falha de página fora dos limites, não como exceção capturável
CONSEQUENCIA: Um sênior nunca dimensiona stackalloc nem profundidade de recursão a partir de dado de entrada externo — usa teto fixo com fallback para ArrayPool/heap, e trata StackOverflowException como fatal não capturável por design, limitando profundidade explicitamente antes de chamar
IDS: CSAPP-03A-A-10, CSAPP-03B-A-2, KOKOSA-01-A-5, KOKOSA-01-C-3, KOKOSA-04-A-43, CS12-04-B-4

## PONTE-10: Leitura parcial de stream (kernel/socket) e laço de Read até completar no C#
CAMADAS: hardware/SO → linguagem
MECANISMO UNICO: Uma chamada de leitura do SO sobre socket/pipe devolve apenas os bytes já disponíveis naquele instante, não o total pedido; Stream.Read do .NET herda exatamente esse contrato, e contagem menor que o buffer não é erro
CONSEQUENCIA: Um sênior nunca assume que um único Read enche o buffer — usa laço acumulando bytesRead (ou ReadExactly/ReadAtLeast no .NET 8), tratando retorno zero como fim de stream de verdade, não como erro transitório
IDS: CSAPP-10-A-1, CSAPP-10-B-1, CSAPP-10-B-9, CS12-05-A-8, CS12-05-B-8

## PONTE-11: Contador IDENTITY não-transacional (hardware/log) e lacunas em chave surrogate exposta como número de negócio
CAMADAS: dados → query
MECANISMO UNICO: O contador interno de IDENTITY avança mesmo quando o INSERT falha ou a transação sofre rollback, porque o avanço do contador não é desfeito pelo mecanismo de log transacional — é uma estrutura separada, não protegida pela mesma atomicidade da linha
CONSEQUENCIA: Um sênior nunca expõe IDENTITY como 'número de pedido' contíguo ao cliente nem o usa para inferir ordem de criação entre shards independentes — trata-o só como chave técnica, sabendo que contém lacunas por design
IDS: TSQL-08-B-10, TSQL-08-B-7, TSQL-08-C-6, DDIA-05-C-3, DDIA-09-B-9

## PONTE-12: NULL de três valores no motor relacional e lógica ?/HasValue em C#, bool? em T-SQL
CAMADAS: dados → linguagem → query
MECANISMO UNICO: Tanto o Nullable<T> do CLR (com HasValue/Value) quanto o NULL/UNKNOWN do T-SQL representam ausência de valor como um terceiro estado lógico que precisa ser testado explicitamente antes de operar — comparações comuns silenciosamente devolvem 'desconhecido' em vez de lançar erro ou propagar valor
CONSEQUENCIA: Um sênior usa & e | (não && / ||) para lógica de três estados em bool? do C# quando precisa espelhar exatamente o WHERE (a AND b) do SQL Server, e nunca confia em == entre colunas anuláveis sem tratar IS NULL/HasValue explicitamente dos dois lados da pilha
IDS: CS12-04-A-20, CS12-04-B-11, TSQL-01-C-3, TSQL-02-A-2, CS12-04-A-17, CS12-04-A-18

## PONTE-13: ArrayPool/buffer rentado (runtime) e stackalloc/aritmética de ponteiro (linguagem/hardware)
CAMADAS: runtime → linguagem
MECANISMO UNICO: O Length de um array rentado do pool é maior que o pedido (classes de tamanho do alocador), e o Span sobre stackalloc não carrega checagem de limites do CLR — em ambos os casos o tamanho lógico útil é diferente do tamanho físico alocado, e usar o físico como lógico vaza ou corrompe dados adjacentes
CONSEQUENCIA: Um sênior nunca usa o Length do buffer do pool como tamanho lógico (carrega o comprimento pedido à parte) e nunca deriva o tamanho de um stackalloc do dado de entrada sem teto fixo, tratando ambos como superfícies sem proteção automática de limites
IDS: KOKOSA-01-B-17, KOKOSA-06-A-21, KOKOSA-01-A-5, KOKOSA-01-C-5, CSAPP-03B-A-1, CSAPP-03B-B-12

## PONTE-14: Não-atomicidade entre escrita local (transação SQL) e efeito externo (fila/cache/webhook)
CAMADAS: dados → runtime/distribuído
MECANISMO UNICO: A transação do banco só controla o que o motor gerencia (linhas, log, locks); uma chamada de rede para fila, cache ou webhook dentro ou ao redor da transação não participa desse mesmo commit atômico, criando janela onde um lado persiste e o outro falha
CONSEQUENCIA: Um sênior nunca dispara efeito externo dentro do bloco transacional nem confia em ordem de chamadas (SaveChanges + PublishAsync) como atômica — implementa outbox transacional, gravando o evento a publicar na mesma transação SQL e publicando depois, de forma assíncrona e idempotente
IDS: DDIA-07-A-41, DDIA-07-B-22, DDIA-08-C-11, DDIA-09-A-8, DDIA-11-B-2

## PONTE-15: Compactação do GC (relocação) e checagem de índices/aritmética manual (Unsafe/ponteiro)
CAMADAS: runtime → linguagem
MECANISMO UNICO: O GC compactante corrige endereços apenas em locais que ele varre como raízes; ponteiros calculados manualmente via Unsafe.Add ou aritmética bruta não são atualizados por essa relocação e também não têm checagem de limites, então erro de índice vira corrupção silenciosa, não exceção
CONSEQUENCIA: Um sênior sabe que indexação via Unsafe.Add remove a checagem de limites do runtime E que o valor lido pode estar corrompido se um objeto foi movido sem o pin correto — as duas causas de bug convergem no mesmo tipo de sintoma difícil de reproduzir, então valida manualmente os índices nesse código
IDS: CSAPP-03B-C-4, KOKOSA-01-B-18, KOKOSA-01-A-33, KOKOSA-01-B-13
