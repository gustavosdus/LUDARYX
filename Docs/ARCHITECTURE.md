# Arquitetura resumida

O LUDARYX usa WPF em .NET 8 e segue uma separação simples entre interface, modelos, providers e serviços.

## Fluxo principal

1. `MainWindow` carrega `LauncherSettings`.
2. `LibraryService` consulta os providers habilitados.
3. Cada provider devolve objetos `Game`.
4. Metadados, capas e estado do usuário são aplicados por serviços dedicados.
5. A interface filtra e exibe a lista resultante.
6. O lançamento passa por `GameLaunchService`/`LibraryService` e pelas validações de segurança do `ProcessService`.

## Dependências entre pastas

- `Models` não deve depender da interface.
- `Providers` pode usar `Models` e `Services` compartilhados.
- `Services` deve concentrar infraestrutura reutilizável.
- Janelas WPF coordenam a interface, mas não devem duplicar regras de providers ou segurança.

## Convenção de manutenção

Ao adicionar um novo launcher, crie um novo provider e registre-o no serviço de biblioteca. Ao adicionar uma regra transversal (segurança, cache, download, persistência), coloque-a em `Services` para que todas as plataformas recebam o mesmo comportamento.


## Localização e ciclo de processo

- `Services/LocalizationService.cs`: idiomas e tradução centralizada da interface.
- `Services/SingleInstanceService.cs`: impede múltiplas instâncias e restaura a janela existente.

## Normalização de gêneros
`Services/GenreService.cs` é o ponto único para normalizar, filtrar, deduplicar e exibir gêneros. Não adicione tratamento de gênero diretamente em providers ou janelas; envie os valores ao `GenreService`.

## Metadados multiplataforma
`Services/MetadataService.cs` combina fontes parciais. Para launchers sem uma API pública estável, o fallback Wikidata/Wikipedia usa correspondência exata do título normalizado antes de aplicar qualquer dado.

## Fonte PCGamingWiki
`Services/PcGamingWikiService.cs` consulta a API MediaWiki do PCGamingWiki como fonte geral de metadados para qualquer launcher. A integração usa correspondência exata de título, cache em memória e limitação de requisições. O serviço é usado para desenvolvedora, publicadora, lançamento, gêneros e Steam App ID quando disponível. Descrições continuam priorizando fontes oficiais de loja.


## Atualização via GitHub

- `Services/GitHubUpdateService.cs` consulta a release mais recente, baixa o instalador e valida `SHA256SUMS.txt`.
- `UpdateProgressWindow` exibe o progresso de download e permite cancelamento.
- `Updater/LUDARYX.Updater.csproj` gera o processo auxiliar que aguarda o fechamento do LUDARYX antes de iniciar o instalador.
- `Scripts/Publish-Release.ps1` deve ser usado para publicar launcher e updater juntos.

O updater auxiliar é copiado para uma pasta temporária antes de ser executado, evitando que o próprio instalador precise substituir um arquivo que ainda esteja em uso.
