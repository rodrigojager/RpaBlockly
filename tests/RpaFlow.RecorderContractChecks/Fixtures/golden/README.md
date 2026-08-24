# Matriz de bundles golden do Recorder

Os bundles são gerados deterministicamente pelos testes da extensão e consumidos
sem extração pelos testes do importador. O conteúdo lógico de cada caso fica nas
fixtures JSON deste projeto para que C# e TypeScript validem os mesmos bytes.

| Golden | Conteúdo | Resultado esperado |
|---|---|---|
| `minimal.rpablockly.zip` | pacote V2, sessão e integridade, sem evidência | aceito |
| `complete.rpablockly.zip` | evidência, comentário, segredo cifrado e upload consentido | aceito após mappings |
| `invalid-contract.rpablockly.zip` | campo desconhecido ou enum inválido | rejeitado |
| `tampered.rpablockly.zip` | hash ou tamanho divergente | rejeitado antes do JSON |
| `zip-slip.rpablockly.zip` | entrada com `..`, caminho absoluto ou separador enganoso | rejeitado |
| `duplicate-case.rpablockly.zip` | nomes iguais sem diferença de caixa | rejeitado |
| `zip-bomb.rpablockly.zip` | razão ou tamanho acima dos limites | rejeitado |
| `symlink.rpablockly.zip` | entrada marcada como link simbólico | rejeitado |

Nenhum golden contém código de replay. Os arquivos ZIP não são mantidos como
binários manuais: o teste de build os recria com data fixa e compara checksum.
