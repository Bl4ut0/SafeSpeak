using System.Net.Http.Json;
using System.Text;
using System.Globalization;
using System.Security.Cryptography;
using System.Text.Json.Serialization;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace SafeSpeak.Core.Logging;

/// <summary>Explicit app-signed diagnostic uploads. The embedded signing key is clonable, not proof of authenticity.</summary>
public sealed class DiagnosticUploadService(HttpClient client)
{
    public static readonly Uri DefaultEndpoint = new("https://safespeak.bl4ut0.dev/diagnostics/index.php");
    public const long MaximumArchiveBytes = 25 * 1024 * 1024;

    public async Task<string> UploadAsync(string archivePath, string hostId,
        CancellationToken cancellationToken = default, Uri? endpoint = null)
    {
        endpoint ??= DefaultEndpoint;
        if (endpoint.Scheme != Uri.UriSchemeHttps || !string.IsNullOrEmpty(endpoint.UserInfo) || !string.IsNullOrEmpty(endpoint.Query) || !string.IsNullOrEmpty(endpoint.Fragment))
            throw new ArgumentException("Diagnostics requires an HTTPS endpoint without credentials or query parameters.");
        if (!Guid.TryParse(hostId, out Guid host) || host == Guid.Empty)
            throw new ArgumentException("Diagnostics requires a valid installation host ID.");
        hostId = host.ToString("D");
        await using var archive = new FileStream(archivePath, FileMode.Open, FileAccess.Read, FileShare.Read,
            81920, FileOptions.Asynchronous | FileOptions.SequentialScan);
        if (archive.Length is < 1 or > MaximumArchiveBytes)
            throw new InvalidDataException("The support ZIP must be no larger than 25 MB.");
        string hash = Convert.ToHexString(await SHA256.HashDataAsync(archive, cancellationToken)).ToLowerInvariant();
        archive.Position = 0;
        string signedData = hostId + "\n" + archive.Length.ToString(CultureInfo.InvariantCulture) + "\n" + hash;
        // Deliberately public app marker. This can be reproduced by other clients.
        string signature = Convert.ToHexString(HMACSHA256.HashData(
            Encoding.UTF8.GetBytes("SafeSpeak-diagnostics-app-signature-v1"), Encoding.UTF8.GetBytes(signedData))).ToLowerInvariant();
        using var handshakeRequest = new HttpRequestMessage(HttpMethod.Post, new Uri(endpoint + "?action=handshake"))
        {
            Content = JsonContent.Create(new { hostId, bytes = archive.Length, sha256 = hash, signatureVersion = 1, signature })
        };
        using HttpResponseMessage handshake = await client.SendAsync(handshakeRequest, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        if (handshake.StatusCode == System.Net.HttpStatusCode.ServiceUnavailable)
            throw new HttpRequestException("SafeSpeak diagnostics intake is closed or unavailable. Contact support before retrying.");
        if (handshake.StatusCode == System.Net.HttpStatusCode.TooManyRequests)
            throw new HttpRequestException("Diagnostics rate limit reached. Try again later.");
        if (!handshake.IsSuccessStatusCode)
            throw new HttpRequestException($"Upload authorization failed (HTTP {(int)handshake.StatusCode}). Try again or contact support.");
        Ticket? ticket = await ReadBoundedResponseAsync<Ticket>(handshake, cancellationToken);
        if (ticket is null || !Regex.IsMatch(ticket.Token ?? "", "\\A[a-f0-9]{64}\\z"))
            throw new InvalidDataException("The server returned an invalid upload authorization.");
        using var request = new HttpRequestMessage(HttpMethod.Post, new Uri(endpoint + "?action=upload"));
        request.Headers.Authorization = new("Bearer", ticket.Token);
        request.Headers.Add("X-SafeSpeak-Upload-Ticket", ticket.Token);
        request.Content = new StreamContent(archive);
        request.Content.Headers.ContentType = new("application/zip");
        using HttpResponseMessage response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        if (response.StatusCode == System.Net.HttpStatusCode.TooManyRequests)
            throw new HttpRequestException("Diagnostics rate limit reached. Try again later.");
        if (!response.IsSuccessStatusCode)
            throw new HttpRequestException($"Diagnostics upload failed (HTTP {(int)response.StatusCode}). The package was not confirmed as received.");
        Receipt? receipt = await ReadBoundedResponseAsync<Receipt>(response, cancellationToken);
        if (receipt is null || !Guid.TryParse(receipt.Id, out _)) throw new InvalidDataException("The server did not return a valid receipt.");
        return receipt.Id;
    }

    private sealed record Ticket([property: JsonPropertyName("token")] string Token);
    private sealed record Receipt([property: JsonPropertyName("id")] string Id);

    private static async Task<T?> ReadBoundedResponseAsync<T>(HttpResponseMessage response, CancellationToken token)
    {
        await using Stream input = await response.Content.ReadAsStreamAsync(token);
        byte[] buffer = new byte[4097];
        int count = 0;
        while (count < buffer.Length)
        {
            int read = await input.ReadAsync(buffer.AsMemory(count), token);
            if (read == 0) break;
            count += read;
        }
        if (count > 4096) throw new InvalidDataException("Diagnostics server response exceeds its size limit.");
        return JsonSerializer.Deserialize<T>(buffer.AsSpan(0, count));
    }
}
