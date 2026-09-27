using System.Windows;
using Microsoft.Win32;
using UnifiedGameLauncher.Models;
using UnifiedGameLauncher.Services;

namespace UnifiedGameLauncher;

public partial class AddGameWindow : Window
{
    public ManualGameDefinition? Result { get; private set; }
    public AddGameWindow()
    {
        InitializeComponent();
        LocalizationService.Apply(this);
        Loaded += (_, _) => LocalizationService.Apply(this);
    }

    private void BrowseExe_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog { Filter = "Executáveis (*.exe)|*.exe" };
        if (dialog.ShowDialog(this) == true)
        {
            ExeBox.Text = dialog.FileName;
            if (string.IsNullOrWhiteSpace(NameBox.Text)) NameBox.Text = Path.GetFileNameWithoutExtension(dialog.FileName);
        }
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
        if (!LaunchTargetValidator.TryValidateExecutable(exe, out var safeExe, out var exeError))
        {
            MessageBox.Show(exeError, "LUDARYX", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }
        if (!LaunchTargetValidator.TryValidateUri(uri, out var safeUri, out var uriError))
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

        Result = new ManualGameDefinition
        {
            Name = name,
            Executable = safeExe,
            Arguments = arguments,
            LaunchUri = safeUri
        };
        DialogResult = true;
    }

    private void Cancel_Click(object sender, RoutedEventArgs e) => DialogResult = false;
}
