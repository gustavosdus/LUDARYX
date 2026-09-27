# Checklist de publicação no GitHub

## Antes do push

- [ ] `dotnet restore`
- [ ] `dotnet build -c Release`
- [ ] Confirmar que `git status` não contém `bin/`, `obj/`, `Installer/`, `publish/`, logs ou configurações locais.
- [ ] Procurar segredos e caminhos pessoais antes do commit.
- [ ] Confirmar que o nome público aparece como `LUDARYX`.
- [ ] Revisar `README.md`, `PRIVACY.md`, `LEGAL-NOTICE.md` e `THIRD-PARTY-NOTICES.txt`.

## Antes da release

- [ ] Gerar a publicação com `dotnet publish`.
- [ ] Testar instalação, primeira abertura, atualização da biblioteca, abertura de jogo e desinstalação.
- [ ] Criar o instalador Inno Setup.
- [ ] Gerar `SHA256SUMS.txt` com `Scripts/Generate-ReleaseHashes.ps1`.
- [ ] Publicar binários apenas em GitHub Releases.
- [ ] Informar claramente se a build não possui assinatura digital.
- [ ] Nunca incluir API keys de desenvolvimento em assets, exemplos ou logs.

## Pesquisa rápida antes do commit

Exemplos de termos a revisar manualmente:

```text
PC Gamer
PCGamer
C:\Users\
SteamGridDBApiKey
IgdbClientSecret
password
secret
token
BEGIN PRIVATE KEY
```

Ocorrências desses termos no código podem ser legítimas; procure principalmente valores literais reais e dados locais.
