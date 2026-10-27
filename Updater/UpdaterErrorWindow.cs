using System.Text.Json;

namespace LUDARYX.Updater;

/// <summary>Standalone error UI: the launcher may already be closed during an update.</summary>
internal static class UpdaterErrorWindow
{
    public static void Show(Exception error)
    {
        var language = "pt-BR";
        var light = false;
        try
        {
            var path = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "LUDARYX", "settings.json");
            using var settings = JsonDocument.Parse(File.ReadAllText(path));
            if (settings.RootElement.TryGetProperty("Language", out var locale)) language = locale.GetString() ?? language;
            if (settings.RootElement.TryGetProperty("Theme", out var theme)) light = theme.GetString() == "Light";
        }
        catch { /* Settings may be unavailable when reporting a startup failure. */ }
        var message = language switch
        {
            "en-US" or "en-GB" => "The LUDARYX update could not be started. Please open LUDARYX and try again.",
            "es-419" or "es-ES" => "No se pudo iniciar la actualización de LUDARYX. Abre LUDARYX e inténtalo de nuevo.",
            "ja-JP" => "LUDARYXの更新を開始できませんでした。LUDARYXを開いて、もう一度お試しください。",
            "pt-PT" => "Não foi possível iniciar a atualização do LUDARYX. Abra o LUDARYX e tente novamente.",
            _ => "Não foi possível iniciar a atualização do LUDARYX. Abra o LUDARYX e tente novamente."
        };
        var detailsLabel = language switch
        {
            "en-US" or "en-GB" => "Technical details",
            "es-419" or "es-ES" => "Detalles técnicos",
            "ja-JP" => "技術的な詳細",
            _ => "Detalhes técnicos"
        };
        var background = light ? Color.FromArgb(245, 247, 250) : Color.FromArgb(11, 18, 29);
        var foreground = light ? Color.FromArgb(24, 32, 44) : Color.White;
        using var window = new Form
        {
            Text = "LUDARYX", FormBorderStyle = FormBorderStyle.None,
            StartPosition = FormStartPosition.CenterScreen, ClientSize = new Size(540, 250),
            BackColor = background, ForeColor = foreground, Font = new Font("Segoe UI", 10),
            AutoScaleMode = AutoScaleMode.Dpi, Padding = new Padding(20)
        };
        window.Paint += (_, e) => e.Graphics.DrawRectangle(Pens.SteelBlue, 0, 0, window.ClientSize.Width - 1, window.ClientSize.Height - 1);
        var title = new Label { Text = "LUDARYX", AutoSize = true, Location = new Point(24, 20), Font = new Font("Segoe UI", 12, FontStyle.Bold) };
        var text = new Label { Text = message, Location = new Point(24, 60), Size = new Size(490, 90) };
        var ok = new Button { Text = "OK", DialogResult = DialogResult.OK, FlatStyle = FlatStyle.Flat, Location = new Point(410, 195), Size = new Size(105, 34), BackColor = background, ForeColor = foreground };
        var details = new Button { Text = detailsLabel, FlatStyle = FlatStyle.Flat, Location = new Point(24, 195), Size = new Size(220, 34), BackColor = background, ForeColor = foreground };
        var diagnostic = new TextBox { Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Vertical, Text = error.ToString(), Visible = false, Location = new Point(24, 250), Size = new Size(490, 145), BackColor = background, ForeColor = foreground };
        details.Click += (_, _) => { diagnostic.Visible = !diagnostic.Visible; window.ClientSize = new Size(540, diagnostic.Visible ? 420 : 250); };
        window.Controls.AddRange(new Control[] { title, text, ok, details, diagnostic });
        window.AcceptButton = ok;
        window.CancelButton = ok;
        window.ShowDialog();
    }
}
