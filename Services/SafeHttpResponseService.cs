using System.Net.Http;
using System.Text;

namespace UnifiedGameLauncher.Services;

public static class SafeHttpResponseService
{
    public const int MaxJsonBytes = 4 * 1024 * 1024;

    public static async Task<string> ReadTextAsync(HttpResponseMessage response, int maxBytes = MaxJsonBytes, CancellationToken cancellationToken = default)
    {
        if (response.Content.Headers.ContentLength is long declared && declared > maxBytes)
            throw new InvalidOperationException($"A resposta remota excede o limite de {maxBytes / (1024 * 1024)} MB.");

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var memory = new MemoryStream();
        var buffer = new byte[81920];
        var total = 0;
        while (true)
        {
            var read = await stream.ReadAsync(buffer.AsMemory(0, buffer.Length), cancellationToken);
            if (read == 0) break;
            total += read;
            if (total > maxBytes)
                throw new InvalidOperationException($"A resposta remota excede o limite de {maxBytes / (1024 * 1024)} MB.");
            memory.Write(buffer, 0, read);
        }

        return Encoding.UTF8.GetString(memory.GetBuffer(), 0, checked((int)memory.Length));
    }
}
