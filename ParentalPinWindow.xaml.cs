using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Input;
using UnifiedGameLauncher.Models;
using UnifiedGameLauncher.Services;

namespace UnifiedGameLauncher;

public partial class ParentalPinWindow : Window
{
    private readonly LauncherSettings _settings;
    private readonly bool _setupMode;

    public bool PinChanged { get; private set; }

    public ParentalPinWindow(LauncherSettings settings)
    {
        InitializeComponent();
        _settings = settings;
        _setupMode = !ParentalControlService.HasPin(settings);

        LudaryxThemeService.Apply(settings);

        ConfirmPanel.Visibility = _setupMode ? Visibility.Visible : Visibility.Collapsed;
        HeadingText.Text = _setupMode
            ? "Criar PIN do controle parental"
            : "Desbloquear controle parental";
        DescriptionText.Text = _setupMode
            ? "Crie um PIN de exatamente 4 números. Ele será solicitado novamente após encerrar e abrir o LUDARYX."
            : "Digite o PIN de 4 números para alterar a faixa etária durante esta sessão.";
        ConfirmButton.Content = _setupMode ? "CRIAR PIN" : "DESBLOQUEAR";

        Loaded += (_, _) =>
        {
            PinBox.Focus();
            Keyboard.Focus(PinBox);
        };
    }

    private void PinBox_PreviewTextInput(object sender, TextCompositionEventArgs e)
    {
        if (!Regex.IsMatch(e.Text, @"^\d+$"))
            e.Handled = true;
    }

    private void Confirm_Click(object sender, RoutedEventArgs e)
    {
        var pin = PinBox.Password;

        if (!ParentalControlService.IsValidPin(pin))
        {
            ErrorText.Text = "O PIN deve conter exatamente 4 números.";
            return;
        }

        if (_setupMode)
        {
            if (pin != ConfirmPinBox.Password)
            {
                ErrorText.Text = "Os PINs não são iguais.";
                return;
            }

            ParentalControlService.SetPin(_settings, pin);
            PinChanged = true;
            DialogResult = true;
            Close();
            return;
        }

        if (!ParentalControlService.VerifyPin(_settings, pin))
        {
            ErrorText.Text = "PIN incorreto.";
            PinBox.Clear();
            PinBox.Focus();
            return;
        }

        ParentalControlService.UnlockSession();
        DialogResult = true;
        Close();
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }

    private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.LeftButton == MouseButtonState.Pressed)
            DragMove();
    }
}
