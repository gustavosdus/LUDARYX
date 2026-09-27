# Notas de segurança para manutenção

Estas regras fazem parte do hardening atual do LUDARYX e devem ser preservadas em edições manuais.

- Não iniciar executáveis detectados sem passar pela validação central de caminhos.
- Não aceitar caminhos UNC/rede para executáveis manuais.
- Validar reparse points/junctions em toda a cadeia do caminho.
- Não liberar protocolos genéricos quando um formato específico é suficiente.
- Não baixar imagens diretamente com `BitmapImage` a partir de URLs externas.
- Manter limites de tamanho para respostas HTTP, imagens e arquivos locais analisados.
- Manter segredos protegidos com DPAPI e fora do JSON em texto puro.
- Evitar `Process.Start` com executável localizado apenas por `PATH`.
- Não executar o aplicativo elevado após a instalação.

Qualquer alteração que contorne um desses pontos deve receber nova revisão de segurança.
