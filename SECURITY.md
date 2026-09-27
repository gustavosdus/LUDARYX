# Política de segurança

## Reportando uma vulnerabilidade

Não publique credenciais, tokens, dados pessoais ou detalhes exploráveis em uma issue pública.

Para vulnerabilidades que ainda não possuem um canal privado dedicado no repositório, abra uma issue pública apenas informando que encontrou um problema de segurança e solicite um meio privado de contato, sem incluir detalhes técnicos sensíveis.

## Boas práticas do projeto

- Segredos configurados pelo usuário são persistidos usando Windows DPAPI.
- Downloads remotos de imagens passam por validação de URL, rede, tamanho e conteúdo.
- Destinos de execução e caminhos locais possuem validações centralizadas.
- O LUDARYX não deve registrar API keys, tokens ou conteúdo completo de configurações em logs.

Consulte `Docs/SECURITY.md` para detalhes de implementação.
