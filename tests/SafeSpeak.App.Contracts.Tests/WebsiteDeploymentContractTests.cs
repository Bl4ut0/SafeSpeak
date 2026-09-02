namespace SafeSpeak.App.Contracts.Tests;

public sealed class WebsiteDeploymentContractTests
{
    [Fact]
    public void WebsiteCredentials_AreIgnoredAndExampleContainsNoCredentialValues()
    {
        string ignore = Source(".gitignore");
        string example = Source("tools", "website-deploy", ".env.example");

        Assert.Contains("local-deployment/", ignore);
        Assert.Contains(".env.*", ignore);
        Assert.Contains("!**/.env.example", ignore);
        Assert.Contains("tools/website-deploy/.env", ignore);
        Assert.Contains("**/.ftpconfig", ignore);
        Assert.Contains("**/.sftp-config.json", ignore);
        Assert.Contains("*.key", ignore);
        Assert.Contains("*.p12", ignore);
        Assert.Contains("*.pfx", ignore);
        Assert.Contains("FTP_USER=\n", Normalize(example));
        Assert.Contains("FTP_PASS=\n", Normalize(example));
        Assert.Contains("FTP_SECURE=true", example);
        Assert.Contains("FTP_REJECT_UNAUTHORIZED=true", example);
        Assert.Contains("FTP_CA_FILE=server-ca.pem", example);
    }

    [Fact]
    public void WebsiteDeployment_IsAllowlistedVerifiedAndNonDeleting()
    {
        string script = Source("tools", "website-deploy", "deploy.js");

        Assert.Contains("Plain FTP is disabled", script);
        Assert.Contains("Unverified TLS is disabled", script);
        Assert.Contains("rejectUnauthorized: true", script);
        Assert.DoesNotContain("rejectUnauthorized: false", script);
        Assert.Contains("FTP_REMOTE_DIR must not contain '..'", script);
        Assert.Contains("isSymbolicLink", script);
        Assert.Contains("publicFiles", script);
        Assert.DoesNotContain("\"README-UPLOAD.txt\"", script);
        Assert.DoesNotContain("removeDir", script);
        Assert.DoesNotContain("removeDirectory", script);
        Assert.DoesNotContain("client.remove(", script);
        Assert.True(
            script.IndexOf("\"robots.txt\"", StringComparison.Ordinal) <
            script.IndexOf("\"index.html\"", StringComparison.Ordinal));
        Assert.Contains("Public verification passed", script);
    }

    [Fact]
    public void WebsiteDeploymentCertificate_IsPublicCertificateOnly()
    {
        string certificate = Source("tools", "website-deploy", "server-ca.pem");

        Assert.Contains("BEGIN CERTIFICATE", certificate);
        Assert.DoesNotContain("PRIVATE KEY", certificate);
    }

    private static string Normalize(string value) => value.Replace("\r\n", "\n");

    private static string Source(params string[] segments) =>
        File.ReadAllText(RepositoryFile(segments));

    private static string RepositoryFile(params string[] segments)
    {
        DirectoryInfo? directory = new(AppContext.BaseDirectory);
        while (directory is not null &&
               !File.Exists(Path.Combine(directory.FullName, "SafeSpeak.sln")))
        {
            directory = directory.Parent;
        }

        Assert.NotNull(directory);
        return Path.Combine(new[] { directory!.FullName }.Concat(segments).ToArray());
    }
}
