# LUDARYX 1.0.0

Primeira versão pública preparada para distribuição gratuita e open source.

## Destaques

- Biblioteca unificada de jogos instalados em múltiplos launchers.
- Steam, Epic Games, GOG, Xbox/Microsoft Store, EA app, Ubisoft Connect, Battle.net, Riot Client e jogos manuais.
- Metadados, capas, favoritos, itens ocultos, recentes e detecção de duplicados.
- Interface para teclado/mouse, XInput e DualSense.
- Português, inglês e espanhol com variantes regionais.
- Configurações locais e segredos protegidos por Windows DPAPI.
- Sem telemetria própria nesta versão.

## Download

Publique o instalador e/ou ZIP compilado nos assets desta release. Os binários não devem ser commitados no repositório.

## Integridade

Inclua `SHA256SUMS.txt`, gerado com:

```powershell
.\Scripts\Generate-ReleaseHashes.ps1 -Path .\pasta-da-release
```

## Assinatura digital

A build inicial pode ser publicada sem assinatura digital. Nesse caso, o Microsoft Defender SmartScreen pode exibir aviso de editor desconhecido. Isso não deve ser apresentado como assinatura ou certificação do executável.

## Observações

O LUDARYX é independente e não é afiliado às lojas, launchers, editoras ou desenvolvedoras mencionadas no aplicativo. Marcas e conteúdo de terceiros pertencem aos respectivos titulares.
