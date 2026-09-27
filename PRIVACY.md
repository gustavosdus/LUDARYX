# Privacy Notice — LUDARYX 1.0.2

This notice describes the current LUDARYX 1.0.2 source build. It should be reviewed whenever networking, diagnostics, update, or telemetry behavior changes.

## Local data

LUDARYX stores library preferences, play counts, hidden/favorite items, manual games and metadata, API credentials protected with Windows DPAPI, cached metadata, downloaded artwork, custom artwork, backups selected by the user, and diagnostic logs locally on the user's computer.

The default application-data directory is:

`%LOCALAPPDATA%\LUDARYX`

## Network requests

LUDARYX may contact third-party services for documented application features:

- Steam, GOG, PCGamingWiki, Wikidata/Wikipedia and other documented sources for game metadata;
- SteamGridDB and other documented artwork sources when artwork features are used;
- GitHub Releases to check for LUDARYX updates and, after user confirmation, download release assets.

These services receive ordinary network information such as the user's IP address and request metadata under their own privacy policies.

See [DATA-SOURCES.md](DATA-SOURCES.md) for the project's documented external data sources.

## Update checks

LUDARYX can check the official GitHub repository for newer releases. Startup update checks can be enabled or disabled in Settings.

The updater does not need to transmit the user's game library, play history, API keys, or custom metadata to the LUDARYX project.

When an update is downloaded, LUDARYX validates the installer against the release's published SHA-256 information before automatic installation is allowed.

## No first-party telemetry

LUDARYX 1.0.2 does not include first-party analytics or telemetry that uploads the user's game library or usage history to a LUDARYX-operated server.

## Diagnostics

Diagnostic logs are stored locally. The application provides a sanitized diagnostic report intended for troubleshooting. Users should still review any diagnostic information before posting it publicly.

## User control

Users can export and import local backups from the application. Automatic artwork cache files are excluded from backups because they can be regenerated.

Users can remove the application's local settings and cache from their Windows profile after closing LUDARYX.
