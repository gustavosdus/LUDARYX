using System.Text.Json;
using UnifiedGameLauncher.Models;

namespace UnifiedGameLauncher.Services;

public sealed class JsonSettingsService
{
    private readonly string _file;
    private readonly string _backupFile;
    private static readonly JsonSerializerOptions Options = new() { WriteIndented = true };

    public JsonSettingsService()
    {
        AppDataService.EnsureMigrated();
        var dir = AppDataService.RootDirectory;
        Directory.CreateDirectory(dir);
        _file = Path.Combine(dir, "settings.json");
        _backupFile = Path.Combine(dir, "settings.backup.json");
    }

    public LauncherSettings Load()
    {
        // Tenta primeiro o arquivo principal. Se ele estiver corrompido ou incompleto,
        // recupera automaticamente a última cópia de segurança válida.
        if (TryLoadFile(_file, out var settings, out var sourceJson))
        {
            MigrateLegacySecrets(settings, sourceJson);
            return settings;
        }

        if (TryLoadFile(_backupFile, out settings, out sourceJson))
        {
            DiagnosticLogService.LogInfo("settings.json could not be loaded; restored settings from settings.backup.json.");
            MigrateLegacySecrets(settings, sourceJson);
            try
            {
                // Restaura o principal para que os próximos carregamentos voltem ao caminho normal.
                File.Copy(_backupFile, _file, true);
            }
            catch (Exception ex)
            {
                DiagnosticLogService.LogException("Could not restore settings.json from backup", ex);
            }
            return settings;
        }

        if (File.Exists(_file) || File.Exists(_backupFile))
            DiagnosticLogService.LogInfo("Both settings.json and settings.backup.json were unavailable or invalid; defaults will be used.");

        return new LauncherSettings();
    }

    private static bool TryLoadFile(string path, out LauncherSettings settings, out string json)
    {
        settings = new LauncherSettings();
        json = string.Empty;
        try
        {
            if (!File.Exists(path)) return false;
            json = SafeFileReadService.ReadAllText(path, SafeFileReadService.SettingsMaxBytes, "configurações");
            settings = JsonSerializer.Deserialize<LauncherSettings>(json, Options) ?? new LauncherSettings();

            // Garante coleções válidas mesmo se uma versão antiga tiver gravado null.
            settings.HiddenGameIds ??= new();
            settings.ExcludedGameIds ??= new();
            settings.FavoriteGameIds ??= new();
            settings.LastPlayedUtc ??= new();
            settings.PlayCounts ??= new();
            settings.TotalPlayTimeSeconds ??= new();
            settings.ManualMetadata ??= new();
            settings.ManualGames ??= new();
            settings.SteamGridDbGameIds ??= new();
            settings.DuplicatePlatformPriority ??= new();
            if (settings.DuplicatePlatformPriority.Count == 0)
            {
                settings.DuplicatePlatformPriority = new()
                {
                    "Steam", "GOG", "Epic", "Xbox", "EAApp", "UbisoftConnect", "BattleNet", "RiotClient", "Manual"
                };
            }
            settings.Shortcuts ??= new ShortcutSettings();

            foreach (var manualGame in settings.ManualGames)
            {
                manualGame.LaunchProfiles ??= new();
                if (manualGame.LaunchProfiles.Count == 0)
                {
                    var legacyProfile = new ManualLaunchProfile
                    {
                        Name = "Padrão",
                        Executable = manualGame.Executable,
                        Arguments = manualGame.Arguments,
                        LaunchUri = manualGame.LaunchUri,
                        WorkingDirectory = manualGame.WorkingDirectory,
                        RunAsAdministrator = manualGame.RunAsAdministrator
                    };
                    manualGame.LaunchProfiles.Add(legacyProfile);
                    manualGame.PreferredLaunchProfileId = legacyProfile.Id;
                }
                else if (string.IsNullOrWhiteSpace(manualGame.PreferredLaunchProfileId))
                {
                    manualGame.PreferredLaunchProfileId = manualGame.LaunchProfiles[0].Id;
                }
            }
            settings.PreferredDuplicateProviders = new Dictionary<string, string>(
                settings.PreferredDuplicateProviders ?? new Dictionary<string, string>(),
                StringComparer.OrdinalIgnoreCase);
            settings.MaxCacheSizeMb = Math.Clamp(settings.MaxCacheSizeMb <= 0 ? 1024 : settings.MaxCacheSizeMb, 128, 16384);

            settings.IgdbClientId = SecretProtectionService.Unprotect(settings.ProtectedIgdbClientId);
            settings.IgdbClientSecret = SecretProtectionService.Unprotect(settings.ProtectedIgdbClientSecret);
            settings.SteamGridDbApiKey = SecretProtectionService.Unprotect(settings.ProtectedSteamGridDbApiKey);
            return true;
        }
        catch (Exception ex)
        {
            DiagnosticLogService.LogException($"Could not load settings file {Path.GetFileName(path)}", ex);
            return false;
        }
    }

    private void MigrateLegacySecrets(LauncherSettings settings, string json)
    {
        try
        {
            var migrated = false;
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            if (string.IsNullOrWhiteSpace(settings.IgdbClientId) && TryReadLegacy(root, "IgdbClientId", out var legacyClientId))
            {
                settings.IgdbClientId = legacyClientId;
                migrated = true;
            }
            if (string.IsNullOrWhiteSpace(settings.IgdbClientSecret) && TryReadLegacy(root, "IgdbClientSecret", out var legacySecret))
            {
                settings.IgdbClientSecret = legacySecret;
                migrated = true;
            }
            if (string.IsNullOrWhiteSpace(settings.SteamGridDbApiKey) && TryReadLegacy(root, "SteamGridDbApiKey", out var legacyGridKey))
            {
                settings.SteamGridDbApiKey = legacyGridKey;
                migrated = true;
            }

            if (migrated) SaveAfterLegacySecretMigration(settings);
        }
        catch { }
    }

    private void SaveAfterLegacySecretMigration(LauncherSettings settings)
    {
        // Não copia o JSON legado para o backup: ele pode conter segredos em texto puro.
        PrepareProtectedSecrets(settings);
        var sanitizedJson = JsonSerializer.Serialize(settings, Options);
        // Sobrescreve primeiro o backup, que é o local onde um segredo legado
        // poderia sobreviver após a migração.
        WriteAtomically(_backupFile, sanitizedJson);
        WriteAtomically(_file, sanitizedJson);
    }

    private static void PrepareProtectedSecrets(LauncherSettings settings)
    {
        settings.ProtectedIgdbClientId = SecretProtectionService.Protect(settings.IgdbClientId);
        settings.ProtectedIgdbClientSecret = SecretProtectionService.Protect(settings.IgdbClientSecret);
        settings.ProtectedSteamGridDbApiKey = SecretProtectionService.Protect(settings.SteamGridDbApiKey);
    }

    private static void WriteAtomically(string path, string json)
    {
        var temp = path + ".tmp";
        File.WriteAllText(temp, json);
        File.Move(temp, path, true);
    }

    public void Save(LauncherSettings settings)
    {
        PrepareProtectedSecrets(settings);

        // Mantém uma cópia da última configuração válida antes de substituir o arquivo principal.
        // Assim preferências como itens ocultos, favoritos e jogos manuais sobrevivem a uma
        // gravação interrompida ou a um arquivo principal corrompido.
        if (File.Exists(_file))
        {
            try { File.Copy(_file, _backupFile, true); } catch { }
        }

        // Grava primeiro em arquivo temporário e só então substitui o original para reduzir risco de corrupção.
        var json = JsonSerializer.Serialize(settings, Options);
        WriteAtomically(_file, json);

        // Na primeira gravação, cria também uma cópia de segurança inicial.
        if (!File.Exists(_backupFile))
        {
            try { File.Copy(_file, _backupFile, true); } catch { }
        }
    }

    private static bool TryReadLegacy(JsonElement root, string propertyName, out string? value)
    {
        value = null;
        if (!root.TryGetProperty(propertyName, out var property) || property.ValueKind != JsonValueKind.String)
            return false;
        value = property.GetString();
        return !string.IsNullOrWhiteSpace(value);
    }
}
