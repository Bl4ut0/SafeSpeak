using System.Net;
using System.Text;
using System.Text.Json;
using SafeSpeak.Core.Logging;

namespace SafeSpeak.Core.Tests;

public sealed class DiagnosticUploadServiceTests
{
    [Fact]
    public async Task UploadBindsHandshakeToArchiveAndRequiresReceipt()
    {
        string path = Path.GetTempFileName();
        try
        {
            await File.WriteAllBytesAsync(path, [1, 2, 3]);
            string token = new('b', 64), receipt = Guid.NewGuid().ToString();
            var handler = new Handler(async request =>
            {
                if (request.RequestUri!.Query == "?action=handshake")
                {
                    using var body = JsonDocument.Parse(await request.Content!.ReadAsStringAsync());
                    Assert.False(body.RootElement.TryGetProperty("code", out _));
                    string canonical = "11111111-1111-1111-1111-111111111111\n3\n" + body.RootElement.GetProperty("sha256").GetString();
                    string expected = Convert.ToHexString(System.Security.Cryptography.HMACSHA256.HashData(
                        Encoding.UTF8.GetBytes("SafeSpeak-diagnostics-app-signature-v1"), Encoding.UTF8.GetBytes(canonical))).ToLowerInvariant();
                    Assert.Equal(expected, body.RootElement.GetProperty("signature").GetString());
                    Assert.Equal(3, body.RootElement.GetProperty("bytes").GetInt64());
                    Assert.Equal(64, body.RootElement.GetProperty("sha256").GetString()!.Length);
                    return Json(HttpStatusCode.OK, new { token });
                }
                Assert.Equal("Bearer", request.Headers.Authorization!.Scheme);
                Assert.Equal(token, request.Headers.Authorization.Parameter);
                Assert.Equal(new byte[] { 1, 2, 3 }, await request.Content!.ReadAsByteArrayAsync());
                return Json(HttpStatusCode.Created, new { id = receipt });
            });
            using var client = new HttpClient(handler);
            Assert.Equal(receipt, await new DiagnosticUploadService(client).UploadAsync(path, "11111111-1111-1111-1111-111111111111"));
            Assert.Equal(2, handler.Calls);
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public async Task RefusedHandshakeDoesNotSendArchive()
    {
        string path = Path.GetTempFileName();
        try
        {
            await File.WriteAllBytesAsync(path, [1]);
            var handler = new Handler(_ => Task.FromResult(Json(HttpStatusCode.Forbidden, new { error = "refused" })));
            using var client = new HttpClient(handler);
            await Assert.ThrowsAsync<HttpRequestException>(() => new DiagnosticUploadService(client).UploadAsync(path, "11111111-1111-1111-1111-111111111111"));
            Assert.Equal(1, handler.Calls);
        }
        finally { File.Delete(path); }
    }
    private static HttpResponseMessage Json(HttpStatusCode status, object value) => new(status)
    { Content = new StringContent(JsonSerializer.Serialize(value), Encoding.UTF8, "application/json") };
    private sealed class Handler(Func<HttpRequestMessage, Task<HttpResponseMessage>> send) : HttpMessageHandler
    {
        public int Calls;
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        { Calls++; return send(request); }
    }
}
