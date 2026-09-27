using System.Windows;
using Microsoft.Win32;
using UnifiedGameLauncher.Models;
using UnifiedGameLauncher.Services;
using Forms = System.Windows.Forms;

namespace UnifiedGameLauncher;

public partial class AddGameWindow : Window
{
    private readonly ManualGameDefinition? _existing;
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
            ExeBox.Text = existing.Executable ?? "";
            ArgumentsBox.Text = existing.Arguments ?? "";
            UriBox.Text = existing.LaunchUri ?? "";
            WorkingDirectoryBox.Text = existing.WorkingDirectory ?? "";
            RunAsAdministratorCheck.IsChecked = existing.RunAsAdministrator;
        }

        LocalizationService.Apply(this);
        Loaded += (_, _) => LocalizationService.Apply(this);
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
        var name = NameBox.Text.Trim();
        var exe = ExeBox.Text.Trim();
        var uri = UriBox.Text.Trim();

        if (string.IsNullOrWhiteSpace(name) || (string.IsNullOrWhiteSpace(exe) && string.IsNullOrWhiteSpace(uri)))
        {
            MessageBox.Show("Informe o nome e um executável ou URI.", "LUDARYX", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        string? safeExe = null;
        if (!string.IsNullOrWhiteSpace(exe) && !LaunchTargetValidator.TryValidateExecutable(exe, out safeExe, out var exeError))
        {
            MessageBox.Show(exeError, "LUDARYX", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        string? safeUri = null;
        if (!string.IsNullOrWhiteSpace(uri) && !LaunchTargetValidator.TryValidateUri(uri, out safeUri, out var uriError))
        {
            MessageBox.Show(uriError, "LUDARYX", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        var arguments = string.IsNullOrWhiteSpace(ArgumentsBox.Text) ? null : ArgumentsBox.Text.Trim();
        if (arguments is { Length: > 4096 })
        {
            MessageBox.Show("Os argumentos de inicialização são maiores que o limite permitido.", "LUDARYX", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        string? workingDirectory = null;
        if (!string.IsNullOrWhiteSpace(WorkingDirectoryBox.Text))
        {
            try
            {
                workingDirectory = Path.GetFullPath(WorkingDirectoryBox.Text.Trim());
                if (!Directory.Exists(workingDirectory))
                {
                    MessageBox.Show("A pasta de trabalho informada não existe.", "LUDARYX", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }
            }
            catch
            {
                MessageBox.Show("A pasta de trabalho informada é inválida.", "LUDARYX", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
        }

        Result = new ManualGameDefinition
        {
            Id = _existing?.Id ?? Guid.NewGuid().ToString("N"),
            Name = name,
            Executable = safeExe,
            Arguments = arguments,
            LaunchUri = safeUri,
            WorkingDirectory = workingDirectory,
            RunAsAdministrator = RunAsAdministratorCheck.IsChecked == true,
            CoverPath = _existing?.CoverPath
        };
        DialogResult = true;
    }

    private void Cancel_Click(object sender, RoutedEventArgs e) => DialogResult = false;
}