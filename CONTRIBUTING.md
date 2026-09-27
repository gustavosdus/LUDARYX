# Contribuindo com o LUDARYX

Obrigado pelo interesse em contribuir.

## Antes de começar

- Use Windows 10/11 x64 e .NET 8 SDK.
- Abra uma issue antes de mudanças grandes de arquitetura ou de comportamento.
- Nunca inclua API keys, tokens, credenciais, arquivos de configuração pessoais, caches ou caminhos privados em commits.
- Preserve as validações de segurança em `Services/`.

## Fluxo sugerido

1. Crie um fork e uma branch curta e descritiva.
2. Execute `dotnet restore` e `dotnet build -c Release` antes do commit.
3. Mantenha o código compatível com `.editorconfig`.
4. Teste as áreas alteradas e descreva os testes no pull request.
5. Não inclua binários de `bin/`, `obj/`, `Installer/` ou `publish/`.

## Organização

- `Providers/`: descoberta e lançamento específicos de cada plataforma.
- `Services/`: regras compartilhadas, rede, segurança, metadados, capas e persistência.
- `Models/`: modelos de domínio e configuração.
- XAML/code-behind: interface e interação; evite duplicar regras de negócio aqui.

Consulte também `Docs/ARCHITECTURE.md`, `Docs/MANUAL-EDITING.md` e `Docs/SECURITY.md`.

## Uso de inteligência artificial

O LUDARYX é um projeto desenvolvido com auxílio de ferramentas de inteligência artificial, incluindo o ChatGPT. Contribuições humanas ou auxiliadas por IA são bem-vindas, desde que o autor da contribuição revise, teste e assuma responsabilidade pelo código enviado.

Ao usar IA em uma contribuição, não envie para serviços externos API keys, tokens, credenciais, logs com dados pessoais ou qualquer outro segredo do projeto ou do usuário. Consulte `Docs/AI-ASSISTANCE.md` para a declaração completa do projeto.

## Relatos de bugs

Inclua a versão do LUDARYX, versão do Windows, passos para reproduzir, comportamento esperado e comportamento observado. Se houver erro, anexe somente logs revisados por você e remova qualquer dado pessoal que não queira compartilhar.
