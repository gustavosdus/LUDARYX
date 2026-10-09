# Privacy Notice — LUDARYX 1.2.0

This notice describes the current LUDARYX 1.2.0 source build. It should be reviewed whenever networking, diagnostics, update, or telemetry behavior changes.

## Local data

LUDARYX stores library preferences, favorites/hidden items, manual games and launch profiles, local play counts and recorded session time, duplicate preferences, keyboard shortcuts, manual metadata, API credentials protected with Windows DPAPI, cached metadata, downloaded artwork, custom artwork, user-selected backups, and diagnostic logs locally on the user's computer.

The default application-data directory is:

`%LOCALAPPDATA%\LUDARYX`

Local statistics are calculated from data stored on the user's computer. LUDARYX does not operate a project analytics service for these statistics.

## Network requests

LUDARYX may contact third-party services for documented application features:

- Steam, GOG, PCGamingWiki, Wikidata/Wikipedia and other documented sources for game metadata;
- Steam may also provide regional age-rating metadata when available;
- the MJSP open-data portal (`dados.mj.gov.br`) may be contacted to obtain the official Brazilian ClassInd game dataset; the downloaded dataset is cached locally;
- Wikidata may provide structured age-rating facts for non-Brazilian rating systems; LUDARYX does not intentionally scrape the public ESRB, PEGI, USK, CERO, ACB or BBFC sites;
- SteamGridDB and other documented artwork sources when artwork features are used;
- GitHub Releases to check for LUDARYX updates and, after user confirmation, download release assets.

These services receive ordinary network information such as the user's IP address and request metadata under their own privacy policies.

See [DATA-SOURCES.md](DATA-SOURCES.md) for the project's documented external data sources.

## Update checks

LUDARYX can check the official GitHub repository for newer releases. Startup update checks can be enabled or disabled in Settings.

The updater does not need to transmit the user's game library, play history, local statistics, API keys, or custom metadata to the LUDARYX project.

When an update is downloaded, LUDARYX validates the installer against the release's published SHA-256 information before automatic installation is allowed.

## No first-party telemetry

LUDARYX 1.2.0 does not include first-party analytics or telemetry that uploads the user's game library, play history, recorded session time, or usage statistics to a LUDARYX-operated server.

## Diagnostics

Diagnostic logs are stored locally. The application provides a sanitized diagnostic report intended for troubleshooting. Users should still review diagnostic information before posting it publicly.

## User control

Users can export selected categories of local data through the backup feature. Automatic artwork cache files are excluded from backups because they can be regenerated.

Users can remove the application's local settings and cache from their Windows profile after closing LUDARYX.
