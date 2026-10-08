using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace UnifiedGameLauncher.Services;

/// <summary>
/// Navegação genérica por controle para janelas modais do LUDARYX.
/// D-pad/analógico navegam, A ativa, B volta/fecha. Comboboxes podem
/// ser alterados diretamente pelo controle sem depender do mouse.
/// </summary>
public sealed class WindowGamepadNavigationService : IDisposable
{
    private readonly Window _window;
    private readonly Action _backAction;
    private readonly GamepadService _gamepad;
    private readonly ScrollViewer? _scrollViewer;
    private DateTime _nextNavigationAllowedUtc = DateTime.MinValue;
    private bool _disposed;

    public WindowGamepadNavigationService(
        Window window,
        Action backAction,
        ScrollViewer? scrollViewer = null)
    {
        _window = window;
        _backAction = backAction;
        _scrollViewer = scrollViewer;
        _gamepad = new GamepadService(window);
        _gamepad.StateChanged += Gamepad_StateChanged;

        _window.Loaded += Window_Loaded;
        _window.Closed += Window_Closed;
    }

    private void Window_Loaded(object sender, RoutedEventArgs e)
    {
        _window.Dispatcher.BeginInvoke(() =>
        {
            if (Keyboard.FocusedElement is null)
                _window.MoveFocus(new TraversalRequest(FocusNavigationDirection.First));

            BringFocusedIntoView();
        }, System.Windows.Threading.DispatcherPriority.Input);
    }

    private void Gamepad_StateChanged(object? sender, GamepadState state)
    {
        if (_disposed || !_window.IsActive || !state.Connected)
            return;

        if (_gamepad.WasPressed(GamepadButtons.B, state))
        {
            _backAction();
            return;
        }

        if (_gamepad.WasPressed(GamepadButtons.A, state))
        {
            ActivateFocusedControl();
            return;
        }

        const short axisThreshold = 22000;
        var up = state.Buttons.HasFlag(GamepadButtons.DPadUp) || state.LeftY > axisThreshold;
        var down = state.Buttons.HasFlag(GamepadButtons.DPadDown) || state.LeftY < -axisThreshold;
        var left = state.Buttons.HasFlag(GamepadButtons.DPadLeft) || state.LeftX < -axisThreshold;
        var right = state.Buttons.HasFlag(GamepadButtons.DPadRight) || state.LeftX > axisThreshold;

        if (DateTime.UtcNow < _nextNavigationAllowedUtc)
            return;

        var focused = Keyboard.FocusedElement as FrameworkElement;

        if (_scrollViewer is not null && (up || down))
        {
            var delta = Math.Max(48, _scrollViewer.ViewportHeight * 0.14);
            _scrollViewer.ScrollToVerticalOffset(
                Math.Clamp(
                    _scrollViewer.VerticalOffset + (down ? delta : -delta),
                    0,
                    Math.Max(0, _scrollViewer.ScrollableHeight)));

            _nextNavigationAllowedUtc = DateTime.UtcNow.AddMilliseconds(140);
            return;
        }

        if (focused is ComboBox combo)
        {
            if (combo.IsDropDownOpen)
            {
                if (up) ChangeComboSelection(combo, -1);
                else if (down) ChangeComboSelection(combo, 1);
                else if (left || right) combo.IsDropDownOpen = false;

                if (up || down || left || right)
                    _nextNavigationAllowedUtc = DateTime.UtcNow.AddMilliseconds(170);
                return;
            }

            if (left)
            {
                ChangeComboSelection(combo, -1);
                _nextNavigationAllowedUtc = DateTime.UtcNow.AddMilliseconds(170);
                return;
            }

            if (right)
            {
                ChangeComboSelection(combo, 1);
                _nextNavigationAllowedUtc = DateTime.UtcNow.AddMilliseconds(170);
                return;
            }
        }

        if (up || left)
        {
            MoveFocus(FocusNavigationDirection.Previous);
            _nextNavigationAllowedUtc = DateTime.UtcNow.AddMilliseconds(160);
        }
        else if (down || right)
        {
            MoveFocus(FocusNavigationDirection.Next);
            _nextNavigationAllowedUtc = DateTime.UtcNow.AddMilliseconds(160);
        }
    }

    private static void ChangeComboSelection(ComboBox combo, int delta)
    {
        if (combo.Items.Count <= 0)
            return;

        var current = combo.SelectedIndex < 0 ? 0 : combo.SelectedIndex;
        combo.SelectedIndex = Math.Clamp(current + delta, 0, combo.Items.Count - 1);
        combo.BringIntoView();
    }

    private void MoveFocus(FocusNavigationDirection direction)
    {
        if (Keyboard.FocusedElement is UIElement current)
        {
            if (!current.MoveFocus(new TraversalRequest(direction)))
                _window.MoveFocus(new TraversalRequest(
                    direction == FocusNavigationDirection.Next
                        ? FocusNavigationDirection.First
                        : FocusNavigationDirection.Last));
        }
        else
        {
            _window.MoveFocus(new TraversalRequest(
                direction == FocusNavigationDirection.Next
                    ? FocusNavigationDirection.First
                    : FocusNavigationDirection.Last));
        }

        BringFocusedIntoView();
    }

    private static void BringFocusedIntoView()
    {
        if (Keyboard.FocusedElement is FrameworkElement element)
            element.BringIntoView(new Rect(0, 0, Math.Max(1, element.ActualWidth), Math.Max(1, element.ActualHeight)));
    }

    private static void ActivateFocusedControl()
    {
        switch (Keyboard.FocusedElement)
        {
            case Button button when button.IsEnabled:
                button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                break;

            case System.Windows.Controls.CheckBox checkBox when checkBox.IsEnabled:
                checkBox.IsChecked = !(checkBox.IsChecked == true);
                break;

            case ComboBox combo when combo.IsEnabled:
                combo.IsDropDownOpen = !combo.IsDropDownOpen;
                break;

            case ListBoxItem item when item.IsEnabled:
                item.IsSelected = true;
                item.BringIntoView();
                break;

            case TextBox textBox when textBox.IsEnabled:
                textBox.Focus();
                Keyboard.Focus(textBox);
                break;

            case PasswordBox passwordBox when passwordBox.IsEnabled:
                passwordBox.Focus();
                Keyboard.Focus(passwordBox);
                break;
        }
    }

    private void Window_Closed(object? sender, EventArgs e) => Dispose();

    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;
        _window.Loaded -= Window_Loaded;
        _window.Closed -= Window_Closed;
        _gamepad.StateChanged -= Gamepad_StateChanged;
        _gamepad.Dispose();
    }
}
