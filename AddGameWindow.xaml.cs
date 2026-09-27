using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;
using UnifiedGameLauncher.Models;
using UnifiedGameLauncher.Services;
using Forms = System.Windows.Forms;

namespace UnifiedGameLauncher;

public partial class AddGameWindow : Window
{
    private readonly ManualGameDefinition? _existing;
    private readonly List<ManualLaunchProfile> _profiles = new();
    private ManualLaunchProfile? _currentProfile;
    private bool _loadingProfile;
    private string? _pendingIconSource;

    public ManualGameDefinition? Result { get; private set; }

    public AddGameWindow(ManualGameDefinition? existing = null)
    {
        InitializeComponent();
        _existing = existing;

        if (existing is not null)
        {
            Title = "Editar inicialização";
            HeaderText.Text = "EDITAR CONFIGURAÇÕES DE INICIALIZAÇÃO";
            SaveButton.Content = "SALVAR";
            NameBox.Text = existing.Name;
            IconPathBox.Text = existing.IconPath ?? "";

            foreach (var profile in existing.LaunchProfiles ?? new())
                _profiles.Add(CloneProfile(profile));

            if (_profiles.Count == 0)
            {
                _profiles.Add(new ManualLaunchProfile
                {
                    Name = "Padrão",
                    Executable = existing.Executable,
                    Arguments = existing.Arguments,
                    LaunchUri = existing.LaunchUri,
                    WorkingDirectory = existing.WorkingDirectory,
                    RunAsAdministrator = existing.RunAsAdministrator
                });
            }

            RefreshProfiles(existing.PreferredLaunchProfileId);
        }
        else
        {
            var profile = new ManualLaunchProfile { Name = "Padrão" };
            _profiles.Add(profile);
            RefreshProfiles(profile.Id);
        }

        LocalizationService.Apply(this);
        Loaded += (_, _) => LocalizationService.Apply(this);
    }

    private void RefreshProfiles(string? selectedId)
    {
        _loadingProfile = true;
        ProfileCombo.ItemsSource = null;
        ProfileCombo.ItemsSource = _profiles;
        ProfileCombo.DisplayMemberPath = nameof(ManualLaunchProfile.Name);
        ProfileCombo.SelectedItem = _profiles.FirstOrDefault(profile =>
            profile.Id.Equals(selectedId, StringComparison.OrdinalIgnoreCase)) ?? _profiles.FirstOrDefault();
        _loadingProfile = false;
        LoadSelectedProfile();
    }

    private void ProfileCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_loadingProfile)
            return;

        SaveCurrentProfileFields();
        LoadSelectedProfile();
    }

    private void LoadSelectedProfile()
    {
        _currentProfile = ProfileCombo.SelectedItem as ManualLaunchProfile;
        if (_currentProfile is null)
            return;

        _loadingProfile = true;
        ProfileNameBox.Text = _currentProfile.Name;
        ExeBox.Text = _currentProfile.Executable ?? "";
        ArgumentsBox.Text = _currentProfile.Arguments ?? "";
        UriBox.Text = _currentProfile.LaunchUri ?? "";
        WorkingDirectoryBox.Text = _currentProfile.WorkingDirectory ?? "";
        RunAsAdministratorCheck.IsChecked = _currentProfile.RunAsAdministrator;
        _loadingProfile = false;
    }

    private void SaveCurrentProfileFields()
    {
        if (_loadingProfile || _currentProfile is null)
            return;

        _currentProfile.Name = string.IsNullOrWhiteSpace(ProfileNameBox.Text)
            ? "Perfil"
            : ProfileNameBox.Text.Trim();
        _currentProfile.Executable = string.IsNullOrWhiteSpace(ExeBox.Text) ? null : ExeBox.Text.Trim();
        _currentProfile.Arguments = string.IsNullOrWhiteSpace(ArgumentsBox.Text) ? null : ArgumentsBox.Text.Trim();
        _currentProfile.LaunchUri = string.IsNullOrWhiteSpace(UriBox.Text) ? null : UriBox.Text.Trim();
        _currentProfile.WorkingDirectory = string.IsNullOrWhiteSpace(WorkingDirectoryBox.Text) ? null : WorkingDirectoryBox.Text.Trim();
        _currentProfile.RunAsAdministrator = RunAsAdministratorCheck.IsChecked == true;
    }

    private void NewProfile_Click(object sender, RoutedEventArgs e)
    {
        SaveCurrentProfileFields();
        var profile = new ManualLaunchProfile { Name = $"Perfil {_profiles.Count + 1}" };
        _profiles.Add(profile);
        RefreshProfiles(profile.Id);
    }

    private void DeleteProfile_Click(object sender, RoutedEventArgs e)
    {
        if (_profiles.Count <= 1 || ProfileCombo.SelectedItem is not ManualLaunchProfile selected)
        {
            MessageBox.Show(this, "O jogo precisa manter pelo menos um perfil de inicialização.",
                "LUDARYX", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        _profiles.Remove(selected);
        RefreshProfiles(_profiles[0].Id);
    }

    private void BrowseExe_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog { Filter = "Executáveis (*.exe)|*.exe" };
        if (dialog.ShowDialog(this) == true)
        {
            ExeBox.Text = dialog.FileName;
            if (string.IsNullOrWhiteSpace(NameBox.Text))
                NameBox.Text = Path.GetFileNameWithoutExtension(dialog.FileName);
            if (string.IsNullOrWhiteSpace(WorkingDirectoryBox.Text))
                WorkingDirectoryBox.Text = Path.GetDirectoryName(dialog.FileName) ?? "";
        }
    }

    private void BrowseIcon_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog
        {
            Filter = "Ícones e imagens (*.ico;*.exe;*.png;*.jpg;*.jpeg)|*.ico;*.exe;*.png;*.jpg;*.jpeg"
        };

        if (dialog.ShowDialog(this) == true)
        {
            _pendingIconSource = dialog.FileName;
            IconPathBox.Text = dialog.FileName;
        }
    }

    private void BrowseWorkingDirectory_Click(object sender, RoutedEventArgs e)
    {
        using var dialog = new Forms.FolderBrowserDialog
        {
            Description = "Selecione a pasta de trabalho do jogo",
            UseDescriptionForTitle = true
        };
        if (dialog.ShowDialog() == Forms.DialogResult.OK)
            WorkingDirectoryBox.Text = dialog.SelectedPath;
    }

    private void Add_Click(object sender, RoutedEventArgs e)
    {
        SaveCurrentProfileFields();
        var name = NameBox.Text.Trim();

        if (string.IsNullOrWhiteSpace(name))
        {
            MessageBox.Show(this, "Informe o nome do jogo.", "LUDARYX",
                MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        foreach (var profile in _profiles)
        {
            if (!ValidateProfile(profile))
                return;
        }

        var preferred = ProfileCombo.SelectedItem as ManualLaunchProfile ?? _profiles[0];

        var resultId = _existing?.Id ?? Guid.NewGuid().ToString("N");
        var iconPath = _existing?.IconPath;
        if (!string.IsNullOrWhiteSpace(_pendingIconSource))
        {
            try
            {
                iconPath = ManualIconService.SaveIconCopy(_pendingIconSource, resultId);
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, $"Não foi possível salvar o ícone personalizado.\n\n{ex.Message}",
                    "LUDARYX", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
        }

        Result = new ManualGameDefinition
        {
            Id = resultId,
            Name = name,
            Executable = preferred.Executable,
            Arguments = preferred.Arguments,
            LaunchUri = preferred.LaunchUri,
            WorkingDirectory = preferred.WorkingDirectory,
            RunAsAdministrator = preferred.RunAsAdministrator,
            IconPath = iconPath,
            CoverPath = _existing?.CoverPath,
            LaunchProfiles = _profiles.Select(CloneProfile).ToList(),
            PreferredLaunchProfileId = preferred.Id
        };

        DialogResult = true;
    }

    private bool ValidateProfile(ManualLaunchProfile profile)
    {
        if (string.IsNullOrWhiteSpace(profile.Executable) && string.IsNullOrWhiteSpace(profile.LaunchUri))
        {
            MessageBox.Show(this, $"O perfil '{profile.Name}' precisa de um executável ou URI.",
                "LUDARYX", MessageBoxButton.OK, MessageBoxImage.Information);
            return false;
        }

        string? safeExe = null;
        if (!string.IsNullOrWhiteSpace(profile.Executable) &&
            !LaunchTargetValidator.TryValidateExecutable(profile.Executable, out safeExe, out var exeError))
        {
            MessageBox.Show(this, $"{profile.Name}: {exeError}", "LUDARYX",
                MessageBoxButton.OK, MessageBoxImage.Warning);
            return false;
        }

        profile.Executable = safeExe;

        string? safeUri = null;
        if (!string.IsNullOrWhiteSpace(profile.LaunchUri) &&
            !LaunchTargetValidator.TryValidateUri(profile.LaunchUri, out safeUri, out var uriError))
        {
            MessageBox.Show(this, $"{profile.Name}: {uriError}", "LUDARYX",
                MessageBoxButton.OK, MessageBoxImage.Warning);
            return false;
        }

        profile.LaunchUri = safeUri;

        if (profile.Arguments is { Length: > 4096 })
        {
            MessageBox.Show(this, $"{profile.Name}: argumentos maiores que o limite permitido.",
                "LUDARYX", MessageBoxButton.OK, MessageBoxImage.Warning);
            return false;
        }

        if (!string.IsNullOrWhiteSpace(profile.WorkingDirectory))
        {
            try
            {
                profile.WorkingDirectory = Path.GetFullPath(profile.WorkingDirectory.Trim());
                if (!Directory.Exists(profile.WorkingDirectory))
                    throw new DirectoryNotFoundException();
            }
            catch
            {
                MessageBox.Show(this, $"{profile.Name}: a pasta de trabalho não existe ou é inválida.",
                    "LUDARYX", MessageBoxButton.OK, MessageBoxImage.Warning);
                return false;
            }
        }

        return true;
    }

    private static ManualLaunchProfile CloneProfile(ManualLaunchProfile profile) => new()
    {
        Id = profile.Id,
        Name = profile.Name,
        Executable = profile.Executable,
        Arguments = profile.Arguments,
        LaunchUri = profile.LaunchUri,
        WorkingDirectory = profile.WorkingDirectory,
        RunAsAdministrator = profile.RunAsAdministrator
    };

    private void Cancel_Click(object sender, RoutedEventArgs e) => DialogResult = false;
}