using System.Buffers.Binary;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;

namespace UnifiedGameLauncher.Services;

/// <summary>
/// Baixa e valida imagens com HTTPS obrigatório, limite de bytes, assinatura e dimensões.
/// </summary>
public static class SafeImageDownloadService
{
    public const int MaxImageBytes = 15 * 1024 * 1024;
    public const int MaxImageDimension = 12000;
    public const long MaxImagePixels = 40_000_000;

    private const int MaxRedirects = 5;

    public static async Task<byte[]> DownloadAsync(HttpClient http, string url, CancellationToken cancellationToken = default)
    {
        // O HttpClient recebido é mantido na assinatura por compatibilidade com os serviços
        // existentes, mas downloads de imagens usam um cliente próprio sem redirects automáticos.
        _ = http;

        if (!Uri.TryCreate(url, UriKind.Absolute, out var currentUri))
            throw new InvalidOperationException("A URL da imagem é inválida.");

        for (var redirect = 0; redirect <= MaxRedirects; redirect++)
        {
            var validatedAddresses = await ValidateRemoteUriAsync(currentUri, cancellationToken);

            using var handler = CreatePinnedHandler(validatedAddresses);
            using var client = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(30) };
            using var request = new HttpRequestMessage(HttpMethod.Get, currentUri);
            using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);

            if (IsRedirect(response.StatusCode))
            {
                if (redirect == MaxRedirects)
                    throw new InvalidOperationException("A imagem excedeu o limite de redirects permitidos.");

                var location = response.Headers.Location
                    ?? throw new InvalidOperationException("O servidor retornou um redirect sem endereço de destino.");
                currentUri = location.IsAbsoluteUri ? location : new Uri(currentUri, location);
                // O próximo endereço será validado ANTES da próxima requisição.
                continue;
            }

            if (!response.IsSuccessStatusCode)
                throw new HttpRequestException($"Falha ao baixar imagem (HTTP {(int)response.StatusCode}).");

            var finalUri = response.RequestMessage?.RequestUri
                ?? throw new InvalidOperationException("Não foi possível validar o endereço final da imagem.");
            if (Uri.Compare(finalUri, currentUri, UriComponents.HttpRequestUrl, UriFormat.SafeUnescaped,
                    StringComparison.OrdinalIgnoreCase) != 0)
                throw new InvalidOperationException("O endereço final da imagem mudou de forma inesperada.");

            var mediaType = response.Content.Headers.ContentType?.MediaType;
            if (string.IsNullOrWhiteSpace(mediaType) || !mediaType.StartsWith("image/", StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("O servidor não retornou um tipo de conteúdo de imagem válido.");

            if (response.Content.Headers.ContentLength is long length && length > MaxImageBytes)
                throw new InvalidOperationException("A imagem excede o limite de 15 MB.");

            await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
            using var memory = new MemoryStream();
            var buffer = new byte[81920];
            var total = 0;
            while (true)
            {
                var read = await stream.ReadAsync(buffer.AsMemory(0, buffer.Length), cancellationToken);
                if (read == 0) break;
                total += read;
                if (total > MaxImageBytes)
                    throw new InvalidOperationException("A imagem excede o limite de 15 MB.");
                memory.Write(buffer, 0, read);
            }

            var bytes = memory.ToArray();
            ValidateImageBytes(bytes);
            return bytes;
        }

        throw new InvalidOperationException("Não foi possível concluir o download seguro da imagem.");
    }

    private static SocketsHttpHandler CreatePinnedHandler(IReadOnlyList<IPAddress> addresses)
    {
        return new SocketsHttpHandler
        {
            AllowAutoRedirect = false,
            UseProxy = false,
            AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate,
            ConnectCallback = async (context, cancellationToken) =>
            {
                Exception? lastError = null;
                foreach (var address in addresses)
                {
                    var socket = new Socket(address.AddressFamily, SocketType.Stream, ProtocolType.Tcp);
                    try
                    {
                        await socket.ConnectAsync(new IPEndPoint(address, context.DnsEndPoint.Port), cancellationToken);
                        return new NetworkStream(socket, ownsSocket: true);
                    }
                    catch (Exception ex)
                    {
                        lastError = ex;
                        socket.Dispose();
                    }
                }

                throw new HttpRequestException("Não foi possível conectar ao endereço validado da imagem.", lastError);
            }
        };
    }

    private static bool IsRedirect(System.Net.HttpStatusCode statusCode) =>
        statusCode is System.Net.HttpStatusCode.MovedPermanently
            or System.Net.HttpStatusCode.Redirect
            or System.Net.HttpStatusCode.RedirectMethod
            or System.Net.HttpStatusCode.TemporaryRedirect
            or System.Net.HttpStatusCode.PermanentRedirect;

    private static async Task<IPAddress[]> ValidateRemoteUriAsync(Uri uri, CancellationToken cancellationToken)
    {
        if (!uri.IsAbsoluteUri || uri.Scheme != Uri.UriSchemeHttps || !string.IsNullOrEmpty(uri.UserInfo))
            throw new InvalidOperationException("A imagem remota precisa usar HTTPS e não pode conter credenciais na URL.");
        if (!uri.IsDefaultPort && uri.Port != 443)
            throw new InvalidOperationException("Downloads de capas só podem usar a porta HTTPS padrão (443).");

        if (uri.IsLoopback || uri.Host.Equals("localhost", StringComparison.OrdinalIgnoreCase) ||
            uri.Host.EndsWith(".localhost", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Endereços locais não são permitidos para download de capas.");

        IPAddress[] addresses;
        if (IPAddress.TryParse(uri.Host, out var literal))
            addresses = new[] { literal };
        else
            addresses = await Dns.GetHostAddressesAsync(uri.Host, cancellationToken);

        if (addresses.Length == 0 || addresses.Length > 32 || addresses.Any(IsPrivateOrLocalAddress))
            throw new InvalidOperationException("O endereço da imagem aponta para uma rede local/privada e foi bloqueado.");

        return addresses;
    }

    private static bool IsPrivateOrLocalAddress(IPAddress address)
    {
        if (IPAddress.IsLoopback(address) || address.Equals(IPAddress.Any) || address.Equals(IPAddress.IPv6Any) ||
            address.Equals(IPAddress.None) || address.Equals(IPAddress.IPv6None)) return true;

        var bytes = address.GetAddressBytes();
        if (address.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork)
        {
            return bytes[0] == 10 || bytes[0] == 127 || bytes[0] == 0 ||
                   (bytes[0] == 169 && bytes[1] == 254) ||
                   (bytes[0] == 172 && bytes[1] >= 16 && bytes[1] <= 31) ||
                   (bytes[0] == 192 && bytes[1] == 168) ||
                   bytes[0] >= 224;
        }

        if (address.AddressFamily == System.Net.Sockets.AddressFamily.InterNetworkV6)
        {
            return address.IsIPv6LinkLocal || address.IsIPv6SiteLocal || address.IsIPv6Multicast ||
                   (bytes.Length > 0 && (bytes[0] & 0xFE) == 0xFC);
        }

        return true;
    }

    public static byte[] ReadAndValidateLocalImage(string path)
    {
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
            throw new InvalidOperationException("O arquivo de imagem não existe.");
        if (!LaunchTargetValidator.IsSafeLocalFilePath(path))
            throw new InvalidOperationException("A imagem precisa estar em um caminho local sem links/junctions.");

        var info = new FileInfo(path);
        if (info.Length <= 0 || info.Length > MaxImageBytes)
            throw new InvalidOperationException("A imagem precisa ter no máximo 15 MB.");

        var bytes = File.ReadAllBytes(path);
        ValidateImageBytes(bytes);
        return bytes;
    }

    public static void ValidateImageBytes(byte[] bytes)
    {
        if (bytes.Length == 0 || !LooksLikeImage(bytes))
            throw new InvalidOperationException("O conteúdo não possui uma assinatura de imagem reconhecida.");

        if (!TryGetImageDimensions(bytes, out var width, out var height) || width <= 0 || height <= 0)
            throw new InvalidOperationException("Não foi possível validar as dimensões da imagem.");

        if (width > MaxImageDimension || height > MaxImageDimension || (long)width * height > MaxImagePixels)
            throw new InvalidOperationException(
                $"A imagem é grande demais para ser carregada com segurança ({width}x{height}).");
    }

    private static bool LooksLikeImage(byte[] b)
    {
        if (b.Length >= 8 && b[0] == 0x89 && b[1] == 0x50 && b[2] == 0x4E && b[3] == 0x47 && b[4] == 0x0D && b[5] == 0x0A && b[6] == 0x1A && b[7] == 0x0A) return true;
        if (b.Length >= 3 && b[0] == 0xFF && b[1] == 0xD8 && b[2] == 0xFF) return true;
        if (b.Length >= 10 && b[0] == (byte)'G' && b[1] == (byte)'I' && b[2] == (byte)'F' && b[3] == (byte)'8' && (b[4] == (byte)'7' || b[4] == (byte)'9') && b[5] == (byte)'a') return true;
        if (b.Length >= 26 && b[0] == (byte)'B' && b[1] == (byte)'M') return true;
        if (b.Length >= 30 && b[0] == (byte)'R' && b[1] == (byte)'I' && b[2] == (byte)'F' && b[3] == (byte)'F' && b[8] == (byte)'W' && b[9] == (byte)'E' && b[10] == (byte)'B' && b[11] == (byte)'P') return true;
        return false;
    }

    private static bool TryGetImageDimensions(byte[] b, out int width, out int height)
    {
        width = height = 0;

        // PNG IHDR
        if (b.Length >= 24 && b[0] == 0x89 && b[1] == 0x50 && b[2] == 0x4E && b[3] == 0x47)
        {
            width = BinaryPrimitives.ReadInt32BigEndian(b.AsSpan(16, 4));
            height = BinaryPrimitives.ReadInt32BigEndian(b.AsSpan(20, 4));
            return true;
        }

        // GIF
        if (b.Length >= 10 && b[0] == (byte)'G' && b[1] == (byte)'I' && b[2] == (byte)'F')
        {
            width = BinaryPrimitives.ReadUInt16LittleEndian(b.AsSpan(6, 2));
            height = BinaryPrimitives.ReadUInt16LittleEndian(b.AsSpan(8, 2));
            return true;
        }

        // BMP (BITMAPINFOHEADER e derivados)
        if (b.Length >= 26 && b[0] == (byte)'B' && b[1] == (byte)'M')
        {
            width = Math.Abs(BinaryPrimitives.ReadInt32LittleEndian(b.AsSpan(18, 4)));
            height = Math.Abs(BinaryPrimitives.ReadInt32LittleEndian(b.AsSpan(22, 4)));
            return width > 0 && height > 0;
        }

        // JPEG: procura um marcador SOF que contenha largura e altura.
        if (b.Length >= 4 && b[0] == 0xFF && b[1] == 0xD8)
        {
            var i = 2;
            while (i + 8 < b.Length)
            {
                while (i < b.Length && b[i] != 0xFF) i++;
                while (i < b.Length && b[i] == 0xFF) i++;
                if (i >= b.Length) break;

                var marker = b[i++];
                if (marker is 0xD8 or 0xD9) continue;
                if (marker == 0xDA || i + 1 >= b.Length) break;

                var segmentLength = (b[i] << 8) | b[i + 1];
                if (segmentLength < 2 || i + segmentLength > b.Length) break;

                var isSof = marker is 0xC0 or 0xC1 or 0xC2 or 0xC3 or 0xC5 or 0xC6 or 0xC7
                                      or 0xC9 or 0xCA or 0xCB or 0xCD or 0xCE or 0xCF;
                if (isSof && segmentLength >= 7)
                {
                    height = (b[i + 3] << 8) | b[i + 4];
                    width = (b[i + 5] << 8) | b[i + 6];
                    return width > 0 && height > 0;
                }
                i += segmentLength;
            }
            return false;
        }

        // WebP VP8X / VP8 / VP8L.
        if (b.Length >= 30 && b[0] == (byte)'R' && b[1] == (byte)'I' && b[8] == (byte)'W' && b[9] == (byte)'E')
        {
            var fourCc = System.Text.Encoding.ASCII.GetString(b, 12, 4);
            if (fourCc == "VP8X" && b.Length >= 30)
            {
                width = 1 + b[24] + (b[25] << 8) + (b[26] << 16);
                height = 1 + b[27] + (b[28] << 8) + (b[29] << 16);
                return true;
            }
            if (fourCc == "VP8 " && b.Length >= 30 && b[23] == 0x9D && b[24] == 0x01 && b[25] == 0x2A)
            {
                width = BinaryPrimitives.ReadUInt16LittleEndian(b.AsSpan(26, 2)) & 0x3FFF;
                height = BinaryPrimitives.ReadUInt16LittleEndian(b.AsSpan(28, 2)) & 0x3FFF;
                return width > 0 && height > 0;
            }
            if (fourCc == "VP8L" && b.Length >= 25 && b[20] == 0x2F)
            {
                uint bits = (uint)(b[21] | (b[22] << 8) | (b[23] << 16) | (b[24] << 24));
                width = (int)(bits & 0x3FFF) + 1;
                height = (int)((bits >> 14) & 0x3FFF) + 1;
                return true;
            }
        }

        return false;
    }
}
