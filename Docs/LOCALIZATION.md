# Localização do LUDARYX

A versão pública permanece **1.0.0**.

## Idiomas suportados

- `pt-BR` — Português (Brasil)
- `pt-PT` — Português (Portugal)
- `en-US` — English (US)
- `en-GB` — English (UK)
  - Esta opção usa inglês britânico (British English).
- `es-419` — Español (Latinoamérica)
- `es-ES` — Español (España)

## Onde editar traduções

As traduções da interface ficam centralizadas em:

`Services/LocalizationService.cs`

Cada entrada mantém o texto-base em português do Brasil e as traduções regionais correspondentes. O japonês usa um mapa explícito de traduções para os fluxos principais e fallback em inglês para textos legados ainda não mapeados. Para adicionar ou corrigir um texto, prefira editar esse arquivo em vez de espalhar condições de idioma pelas janelas.

A escolha do usuário é salva em `LauncherSettings.Language`.

## Instância única

A proteção contra múltiplas instâncias fica em:

`Services/SingleInstanceService.cs`

Uma segunda execução sinaliza a instância existente para restaurar a janela e encerra o novo processo.


## Japonês

- Código: `ja-JP`.
- Atalho da barra superior: `JP`.
- Metadados localizados: Steam `japanese/JP` e Wikipedia `ja`.
- Classificação indicativa prioritária: CERO.
- Textos legados sem tradução japonesa explícita usam inglês como fallback para evitar misturar português na interface.
