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

    [Fact]
    public void AndroidPublisher_IsManualProtectedAndInternalOnly()
    {
        string workflow = Source(".github", "workflows", "android-publisher.yml");

        Assert.Contains("workflow_dispatch:", workflow);
        Assert.DoesNotContain("pull_request:", workflow);
        Assert.DoesNotContain("push:", workflow);
        Assert.Contains("google-play-production", workflow);
        Assert.Contains("android/main", workflow);
        Assert.Contains("AndroidPackageFormats=aab", workflow);
        Assert.Contains("AndroidKeyStore=true", workflow);
        Assert.Contains("tracks: internal", workflow);
        Assert.Contains("status: draft", workflow);
        Assert.DoesNotContain("tracks: production", workflow);
        Assert.Contains("upload_to_play", workflow);
    }

    [Fact]
    public void IosPublisher_IsManualProtectedAndTestFlightOnly()
    {
        string workflow = Source(".github", "workflows", "ios-publisher.yml");

        Assert.Contains("workflow_dispatch:", workflow);
        Assert.DoesNotContain("pull_request:", workflow);
        Assert.DoesNotContain("push:", workflow);
        Assert.Contains("apple-app-store-production", workflow);
        Assert.Contains("ios/main", workflow);
        Assert.Contains("ArchiveOnBuild=true", workflow);
        Assert.Contains("RuntimeIdentifier=ios-arm64", workflow);
        Assert.Contains("upload-testflight-build", workflow);
        Assert.Contains("upload_to_testflight", workflow);
        Assert.DoesNotContain("App Store release", workflow, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void IosProject_IncludesRequiredPrivacyManifestFoundation()
    {
        string project = Source("src", "SafeSpeak.Mobile", "SafeSpeak.Mobile.csproj");
        string manifest = Source("src", "SafeSpeak.Mobile", "Platforms", "iOS", "PrivacyInfo.xcprivacy");

        Assert.Contains("PrivacyInfo.xcprivacy", project);
        Assert.Contains("NSPrivacyAccessedAPICategoryFileTimestamp", manifest);
        Assert.Contains("C617.1", manifest);
        Assert.Contains("NSPrivacyAccessedAPICategorySystemBootTime", manifest);
        Assert.Contains("35F9.1", manifest);
        Assert.Contains("NSPrivacyAccessedAPICategoryDiskSpace", manifest);
        Assert.Contains("E174.1", manifest);
        Assert.Contains("NSPrivacyTracking", manifest);
        Assert.Contains("<false/>", manifest);
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
