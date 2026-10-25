# Edição manual do LUDARYX

Este documento indica os pontos principais do projeto para que alterações manuais sejam feitas sem precisar procurar lógica espalhada pelo código.

## Estrutura

- `MainWindow.xaml`: layout da janela principal, barra superior, filtros, biblioteca e estilos visuais.
- `MainWindow.xaml.cs`: comportamento da janela principal. Os arquivos parciais separam biblioteca, filtros, ações, controles, tema e tela cheia.
- `SettingsWindow.xaml` / `.xaml.cs`: tela e persistência das opções do usuário.
- `DetailsWindow.xaml` / `.xaml.cs`: detalhes e personalização de cada jogo.
- `AddGameWindow.xaml` / `.xaml.cs`: cadastro manual de jogos.
- `Models/`: modelos de dados usados pela aplicação.
- `Providers/`: descoberta e lançamento específico de cada plataforma.
- `Services/`: serviços compartilhados, segurança, capas, controle, configurações e processos.
- `Assets/`: ícone, logo e sons incorporados ao aplicativo.
- `Docs/Releases/`: histórico das notas de versão.

## Arquivos mais importantes

### Interface e navegação

Edite `MainWindow.xaml` para aparência e `MainWindow.xaml.cs` para comportamento. As regiões do code-behind separam carregamento da biblioteca, filtros, controle, barra superior, ações de jogos, tela cheia, tema e teclado.

### Plataformas

Cada plataforma deve ficar em um provider dentro de `Providers/`. Evite colocar detecção específica de launcher diretamente na `MainWindow`.

### Execução segura

Toda abertura de executáveis deve passar por `Services/ProcessService.cs` e pelas validações de `Services/LaunchTargetValidator.cs`. Não use `Process.Start` diretamente em novos providers sem uma necessidade específica e revisada.

### Rede e imagens

Downloads de capas devem passar por `SafeImageDownloadService`. Respostas HTTP textuais devem respeitar os limites definidos em `SafeHttpResponseService`.

### Configurações

Novas opções persistentes devem ser adicionadas em `Models/LauncherSettings.cs` e salvas por `JsonSettingsService`. Segredos devem continuar usando `SecretProtectionService` em vez de texto puro no JSON.

### Controle

`Services/GamepadService.cs` concentra XInput, DualSense/HID e SDL. O arquivo está dividido em regiões para facilitar alterações manuais sem misturar as diferentes APIs.

## Formatação

O projeto inclui `.editorconfig`. Visual Studio e VS Code reconhecem esse arquivo automaticamente.

Antes de gerar uma build, quando o SDK estiver disponível, você pode executar o script incluído no projeto:

```powershell
.\Format-Code.ps1
```

Ele executa `dotnet format` e depois `dotnet build -c Release`.

Para conferir somente formatação sem alterar os arquivos:

```powershell
.\Format-Code.ps1 -Verify
```

## Regra para novas alterações

Prefira métodos pequenos, nomes descritivos e um único local responsável por cada tarefa. Se um recurso for compartilhado por dois providers ou duas janelas, ele deve ir para `Services/` em vez de ser duplicado.


## Dicas de entrada no rodapé

Os ícones de **Selecionar** e **Personalizar** ficam em `MainWindow.xaml`, na região do rodapé.
A troca automática entre teclado, Xbox e PlayStation é controlada por `FooterInputMode` em `MainWindow.xaml.cs`.
A identificação da família do controle fica em `Services/GamepadService.cs`, por meio de `GamepadDeviceKind`.
Os textos `SELECIONAR` e `PERSONALIZAR` são traduzidos em `Services/LocalizationService.cs`.

### Metadados multiplataforma
A lógica principal fica em `Services/MetadataService.cs`. Para jogos não-Steam, preserve a ordem de fallback: fonte nativa quando disponível (GOG), Steam por correspondência exata e IGDB opcional. Não faça correspondência aproximada automática de títulos.

## Organização revisada na 1.2.3

- `MainWindow.Library.cs`: descoberta, atualização, capas e filtro de gêneros.
- `MainWindow.Filters.cs`: seleção de filtros, ordenação, marcadores e apresentação da seleção.
- `MainWindow.GameActions.cs`: seleção/navegação na grade, detalhes e abertura de jogos.
- `Services/LocalizationService.cs`: seleção de idioma, tradução e aplicação nos controles.
- `Services/LocalizationService.Catalog.cs`: textos com campos nomeados para os sete idiomas. A tabela suplementar tem prioridade para aliases; não repita um texto-base na tabela principal.
- `Tests/`: verificações de catálogo, troca de idioma, atualização visual dos controles WPF e leitura de campos antigos de configuração.

Para alterar uma tradução, procure seu texto-base no catálogo e edite o campo do idioma correspondente. Preserve os marcadores `{0}`, `{1}` e seus formatos. Nunca percorra a árvore visual para traduzir os TextBlocks gerados pelos ContentPresenters: isso impede a atualização de seleções e ícones.

Execute `dotnet run --project Tests/LUDARYX.RegressionTests.csproj -c Release` no Windows. O GitHub Actions também executa essas verificações. `./Format-Code.ps1` formata e compila o aplicativo, o updater e as verificações; `-Verify` verifica a formatação sem modificar arquivos.

## Publicação

O workflow `Publish LUDARYX` (`.github/workflows/release.yml`) é acionado manualmente na branch `main`, somente quando a publicação for solicitada. Informe a versão que já consta no aplicativo, updater, instalador e notas. O workflow valida a versão, executa as verificações, gera o instalador e os hashes e publica a release. Commits comuns não publicam versões.

As migrações de pasta antiga, backups, chaves protegidas, perfis manuais e preferências de duplicatas continuam necessárias para usuários existentes. Não remova essas rotinas apenas por conterem a palavra “legado”.
