using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Input;
using UnifiedGameLauncher.Models;
using UnifiedGameLauncher.Services;

namespace UnifiedGameLauncher;

public partial class ParentalPinWindow : Window
{
    private enum PinFlow
    {
        Setup,
        Unlock,
        ChangeCurrent,
        ChangeNew
    }

    private readonly LauncherSettings _settings;
    private PinFlow _flow;

    public bool PinChanged { get; private set; }

    public ParentalPinWindow(LauncherSettings settings, bool changePinMode = false)
    {
        InitializeComponent();
        _settings = settings;
        _flow = changePinMode
            ? PinFlow.ChangeCurrent
            : ParentalControlService.HasPin(settings)
                ? PinFlow.Unlock
                : PinFlow.Setup;

        LudaryxThemeService.Apply(settings);
        LocalizationService.SetLanguage(settings.Language);
        LocalizationService.Apply(this);
        TitleText.Text = $"LUDARYX  /  {LocalizationService.Translate("CONTROLE PARENTAL")}";
        ConfigureFlow();

        Loaded += (_, _) =>
        {
            PinBox.Focus();
            Keyboard.Focus(PinBox);
        };
    }

    private void ConfigureFlow()
    {
        ErrorText.Text = string.Empty;
        PinBox.Clear();
        ConfirmPinBox.Clear();

        switch (_flow)
        {
            case PinFlow.Setup:
                ConfirmPanel.Visibility = Visibility.Visible;
                HeadingText.Text = LocalizationService.Translate("Criar PIN do controle parental");
                DescriptionText.Text = LocalizationService.Translate(
                    "Crie um PIN de exatamente 4 números. Ele será solicitado novamente após encerrar e abrir o LUDARYX.");
                PinLabel.Text = LocalizationService.Translate("Novo PIN");
                ConfirmPinLabel.Text = LocalizationService.Translate("Confirmar novo PIN");
                ConfirmButton.Content = LocalizationService.Translate("CRIAR PIN");
                break;

            case PinFlow.Unlock:
                ConfirmPanel.Visibility = Visibility.Collapsed;
                HeadingText.Text = LocalizationService.Translate("Desbloquear controle parental");
                DescriptionText.Text = LocalizationService.Translate(
                    "Digite o PIN de 4 números para alterar a faixa etária durante esta sessão.");
                PinLabel.Text = LocalizationService.Translate("PIN atual");
                ConfirmButton.Content = LocalizationService.Translate("DESBLOQUEAR");
                break;

            case PinFlow.ChangeCurrent:
                ConfirmPanel.Visibility = Visibility.Visible;
                HeadingText.Text = LocalizationService.Translate("Alterar PIN");
                DescriptionText.Text = LocalizationService.Translate(
                    "Digite o PIN atual e confirme-o novamente antes de escolher um novo PIN.");
                PinLabel.Text = LocalizationService.Translate("PIN atual");
                ConfirmPinLabel.Text = LocalizationService.Translate("Confirmar PIN atual");
                ConfirmButton.Content = LocalizationService.Translate("CONFIRMAR");
                break;

            case PinFlow.ChangeNew:
                ConfirmPanel.Visibility = Visibility.Visible;
                HeadingText.Text = LocalizationService.Translate("Criar novo PIN");
                DescriptionText.Text = LocalizationService.Translate(
                    "Digite o novo PIN de 4 números e confirme-o novamente.");
                PinLabel.Text = LocalizationService.Translate("Novo PIN");
                ConfirmPinLabel.Text = LocalizationService.Translate("Confirmar novo PIN");
                ConfirmButton.Content = LocalizationService.Translate("SALVAR PIN");
                break;
        }

        Dispatcher.BeginInvoke(() =>
        {
            PinBox.Focus();
            Keyboard.Focus(PinBox);
        });
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
            ErrorText.Text = LocalizationService.Translate("O PIN deve conter exatamente 4 números.");
            return;
        }

        switch (_flow)
        {
            case PinFlow.Setup:
                if (!TryConfirmMatchingPins(pin))
                    return;

                ParentalControlService.SetPin(_settings, pin);
                PinChanged = true;
                DialogResult = true;
                Close();
                return;

            case PinFlow.Unlock:
                if (!ParentalControlService.VerifyPin(_settings, pin))
                {
                    ShowIncorrectPin();
                    return;
                }

                ParentalControlService.UnlockSession();
                DialogResult = true;
                Close();
                return;

            case PinFlow.ChangeCurrent:
                if (!TryConfirmMatchingPins(pin))
                    return;

                if (!ParentalControlService.VerifyPin(_settings, pin))
                {
                    ShowIncorrectPin();
                    return;
                }

                ParentalControlService.UnlockSession();
                _flow = PinFlow.ChangeNew;
                TitleText.Text = $"LUDARYX  /  {LocalizationService.Translate("CONTROLE PARENTAL")}";
        ConfigureFlow();
                return;

            case PinFlow.ChangeNew:
                if (!TryConfirmMatchingPins(pin))
                    return;

                ParentalControlService.SetPin(_settings, pin);
                PinChanged = true;
                DialogResult = true;
                Close();
                return;
        }
    }

    private bool TryConfirmMatchingPins(string pin)
    {
        if (!ParentalControlService.IsValidPin(ConfirmPinBox.Password))
        {
            ErrorText.Text = LocalizationService.Translate("O PIN deve conter exatamente 4 números.");
            return false;
        }

        if (pin != ConfirmPinBox.Password)
        {
            ErrorText.Text = LocalizationService.Translate("Os PINs não são iguais.");
            return false;
        }

        return true;
    }

    private void ShowIncorrectPin()
    {
        ErrorText.Text = LocalizationService.Translate("PIN incorreto.");
        PinBox.Clear();
        ConfirmPinBox.Clear();
        PinBox.Focus();
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
