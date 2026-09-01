namespace SafeSpeak.App.Contracts.Tests;

public sealed class MobileWorkflowIsolationContractTests
{
    [Fact]
    public void AndroidWorkflow_IsBranchIsolatedAndNonPublishing()
    {
        string workflow = Source(".github", "workflows", "android-build.yml");

        Assert.Contains("android/develop", workflow);
        Assert.Contains("android/main", workflow);
        Assert.DoesNotContain("branches: [develop]", workflow);
        Assert.DoesNotContain("branches: [main]", workflow);
        Assert.Contains("AndroidPackageFormats=apk", workflow);
        Assert.Contains("AndroidKeyStore=false", workflow);
        Assert.Contains("permissions:\n  contents: read", Normalize(workflow));
        AssertNonPublishing(workflow);
    }

    [Fact]
    public void IosWorkflow_IsBranchIsolatedUnsignedAndNonPublishing()
    {
        string workflow = Source(".github", "workflows", "ios-build.yml");

        Assert.Contains("ios/develop", workflow);
        Assert.Contains("ios/main", workflow);
        Assert.DoesNotContain("branches: [develop]", workflow);
        Assert.DoesNotContain("branches: [main]", workflow);
        Assert.Contains("iossimulator-arm64", workflow);
        Assert.Contains("CodesignKey=", workflow);
        Assert.Contains("CodesignProvision=", workflow);
        Assert.Contains("permissions:\n  contents: read", Normalize(workflow));
        AssertNonPublishing(workflow);
    }

    [Fact]
    public void DesktopWorkflows_DoNotWatchMobileBranches()
    {
        string desktop = Source(".github", "workflows", "desktop-build.yml");
        string development = Source(".github", "workflows", "development-build.yml");

        Assert.DoesNotContain("android/", desktop, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("ios/", desktop, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("android/", development, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("ios/", development, StringComparison.OrdinalIgnoreCase);
    }

    private static void AssertNonPublishing(string workflow)
    {
        Assert.DoesNotContain("secrets.", workflow, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("environment:", workflow, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("partner center", workflow, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("app store connect", workflow, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("google play", workflow, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("gh release", workflow, StringComparison.OrdinalIgnoreCase);
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
