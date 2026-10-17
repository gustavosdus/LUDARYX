# SEO e indexação do site oficial do LUDARYX

URL canônica planejada:

- https://gustavosdus.github.io/LUDARYX/

## Estrutura já preparada

O site está em `site/` e é publicado pelo workflow `.github/workflows/pages.yml`.

Arquivos de descoberta:

- `site/robots.txt`
- `site/sitemap.xml`
- `site/manifest.webmanifest`
- metadados Open Graph e Twitter Card em `site/index.html`
- JSON-LD `SoftwareApplication` em `site/index.html`
- URL canônica em `site/index.html`
- página 404 com retorno para a raiz do site

O workflow copia a logo e as capturas existentes do repositório para o artefato do GitHub Pages. Não mantenha cópias binárias duplicadas dentro de `site/`.

## Ativação inicial do GitHub Pages

A primeira ativação do GitHub Pages exige uma configuração do proprietário do repositório:

1. Abra **Settings → Pages** no repositório.
2. Em **Build and deployment**, selecione **GitHub Actions** como Source.
3. Execute novamente o workflow **Deploy LUDARYX website** em **Actions**, ou faça um novo commit que altere `site/**`.

Depois da primeira publicação, pushes futuros que afetem o site fazem deploy automaticamente.

## Google Search Console

Depois que o site estiver acessível:

1. Adicione uma propriedade de prefixo de URL para `https://gustavosdus.github.io/LUDARYX/`.
2. Use o método de verificação que o Search Console oferecer para a propriedade.
3. Se for escolhido o método de arquivo HTML, adicione o arquivo exato fornecido pelo Google dentro de `site/` sem alterar nome ou conteúdo.
4. Envie o sitemap:
   `https://gustavosdus.github.io/LUDARYX/sitemap.xml`
5. Use a inspeção de URL para a página inicial e solicite indexação.

Não adicione um arquivo de verificação inventado: ele precisa ser emitido pela conta do Search Console.

## Conteúdo e nome do projeto

O nome público deve permanecer **LUDARYX** em letras maiúsculas.

Termos descritivos usados naturalmente no site incluem:

- LUDARYX
- unified PC game launcher
- Windows game launcher
- launcher unificado de jogos
- Steam
- Epic Games
- GOG
- Xbox / Microsoft Store
- EA app
- Ubisoft Connect
- Battle.net
- Riot Client

Evite repetição artificial de palavras-chave. O conteúdo principal deve continuar descrevendo o produto para pessoas primeiro.

## Ao publicar uma nova versão

Atualize:

- `softwareVersion` no JSON-LD do `site/index.html`;
- texto do botão principal, se ele exibir uma versão explícita;
- conteúdo do site caso plataformas ou recursos suportados mudem.

Os links `/releases/latest` não precisam ser alterados a cada versão.
