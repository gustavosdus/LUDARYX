# Data Sources and Attribution

LUDARYX uses third-party services only to identify installed games and enrich the local library. The application is not affiliated with these services.

## Source policy

- **Wikidata**: structured factual data only. Wikidata structured data is released under CC0.
- **Wikipedia**: short summaries may be used as a fallback. Wikipedia text is generally CC BY-SA; attribution should include the article/source and license when redistributed.
- **PCGamingWiki**: only structured/factual fields are consumed by LUDARYX (developer, publisher, release year, genres, store IDs). PCGamingWiki content is CC BY-NC-SA unless otherwise noted. Re-review this source before any commercial distribution.
- **Steam / GOG / IGDB**: used to query game metadata when available. Full storefront pages are not bundled with the installer. Descriptions are sanitized and capped to a short excerpt in the local library.
- **SteamGridDB / Steam CDN**: artwork is downloaded on the user's machine and is not pre-bundled with the LUDARYX installer. Artwork rights remain with their respective owners.

## Storefront scraping

LUDARYX does not intentionally scrape Nintendo, PlayStation, or Xbox storefront web pages. If an official/licensed API is adopted later, its terms should be reviewed before integration.

## Attribution in the UI

Each game's Details window displays the metadata source(s) that contributed to the cached record.

## Commercial release checklist

Before monetizing or broadly distributing LUDARYX:
1. Re-check the current terms/licenses of every external data source.
2. Review use of the name “LUDARYX” and any relevant trademark registrations.
3. Keep THIRD-PARTY-NOTICES.txt and upstream license files in the installer.
4. Do not bundle downloaded game artwork unless you have a clear license to redistribute it.
5. Review the privacy notice if telemetry, crash reporting, accounts, or the planned updater begins sending user data to a server controlled by the project.
