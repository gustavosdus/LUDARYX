# Metadados

O launcher mantém automaticamente um cache em:

`%LOCALAPPDATA%\\UnifiedGameLauncher\\metadata.json`

Capas baixadas ficam em:

`%LOCALAPPDATA%\\UnifiedGameLauncher\\covers`

## Steam

Para jogos Steam, o launcher consulta a Steam Store API sem exigir uma chave do usuário e obtém nome, gêneros, descrição, desenvolvedor, publicador, ano e capa quando disponíveis.

## IGDB opcional

O IGDB fornece campos de jogos, gêneros, plataformas e capas. Para habilitar:

- defina `UseIgdbMetadata` como `true` em `settings.json`; e
- configure `IGDB_CLIENT_ID` e `IGDB_CLIENT_SECRET` como variáveis de ambiente, ou preencha os campos correspondentes no arquivo de configurações.

É preferível usar variáveis de ambiente para não deixar segredo no arquivo do launcher.
