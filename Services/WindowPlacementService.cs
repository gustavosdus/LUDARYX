using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using FormsScreen = System.Windows.Forms.Screen;

namespace UnifiedGameLauncher.Services;

/// <summary>
/// Mantém janelas dentro da área útil do monitor atual, respeitando a barra de tarefas
/// e a escala de DPI do Windows. Centraliza janelas auxiliares no monitor do proprietário.
/// </summary>
public static class WindowPlacementService
{
    public static void FitToWorkingArea(
        Window window,
        Window? relativeTo = null,
        double margin = 10,
        bool center = true)
    {
        if (window.WindowState != WindowState.Normal)
            return;

        var reference = relativeTo ?? window;
        var referenceHandle = new WindowInteropHelper(reference).Handle;
        if (referenceHandle == IntPtr.Zero)
            referenceHandle = new WindowInteropHelper(window).Handle;

        var screen = referenceHandle != IntPtr.Zero
            ? FormsScreen.FromHandle(referenceHandle)
            : FormsScreen.PrimaryScreen;

        if (screen is null)
            return;

        var workingArea = ToDeviceIndependentRect(window, screen.WorkingArea);
        var availableWidth = Math.Max(window.MinWidth, workingArea.Width - (margin * 2));
        var availableHeight = Math.Max(window.MinHeight, workingArea.Height - (margin * 2));

        var targetWidth = ResolveDimension(window.Width, window.ActualWidth, availableWidth);
        var targetHeight = ResolveDimension(window.Height, window.ActualHeight, availableHeight);

        targetWidth = Math.Min(targetWidth, availableWidth);
        targetHeight = Math.Min(targetHeight, availableHeight);

        window.Width = targetWidth;
        window.Height = targetHeight;

        if (center)
        {
            window.WindowStartupLocation = WindowStartupLocation.Manual;
            window.Left = workingArea.Left + Math.Max(margin, (workingArea.Width - targetWidth) / 2);
            window.Top = workingArea.Top + Math.Max(margin, (workingArea.Height - targetHeight) / 2);
            return;
        }

        window.Left = Math.Clamp(
            window.Left,
            workingArea.Left + margin,
            workingArea.Right - targetWidth - margin);
        window.Top = Math.Clamp(
            window.Top,
            workingArea.Top + margin,
            workingArea.Bottom - targetHeight - margin);
    }

    private static double ResolveDimension(double requested, double actual, double available)
    {
        if (!double.IsNaN(requested) && !double.IsInfinity(requested) && requested > 0)
            return requested;
        if (actual > 0)
            return actual;
        return available;
    }

    private static Rect ToDeviceIndependentRect(Window window, System.Drawing.Rectangle rectangle)
    {
        var source = PresentationSource.FromVisual(window);
        if (source?.CompositionTarget is null)
            return new Rect(rectangle.Left, rectangle.Top, rectangle.Width, rectangle.Height);

        var transform = source.CompositionTarget.TransformFromDevice;
        var topLeft = transform.Transform(new System.Windows.Point(rectangle.Left, rectangle.Top));
        var bottomRight = transform.Transform(new System.Windows.Point(rectangle.Right, rectangle.Bottom));
        return new Rect(topLeft, bottomRight);
    }
}
