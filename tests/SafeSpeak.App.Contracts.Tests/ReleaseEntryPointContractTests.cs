using System.Text.RegularExpressions;

namespace SafeSpeak.App.Contracts.Tests;

public sealed class ReleaseEntryPointContractTests
{
    private const string CoreTestsPath =
        @"tests\SafeSpeak.Core.Tests\SafeSpeak.Core.Tests.csproj";
    private const string AppContractsPath =
        @"tests\SafeSpeak.App.Contracts.Tests\SafeSpeak.App.Contracts.Tests.csproj";

    [Fact]
    public void Solution_IncludesBothReleaseTestProjects()
    {
        string solution = Source("SafeSpeak.sln");

        AssertSolutionProject(solution, "SafeSpeak.Core.Tests", CoreTestsPath);
        AssertSolutionProject(solution, "SafeSpeak.App.Contracts.Tests", AppContractsPath);
    }

    [Fact]
    public void ReleaseScript_RestoresAndRunsBothSuitesByDefault()
    {
        string script = ReleaseScript();

        Assert.Contains(
            @"$testProject = Join-Path $repoRoot 'tests\SafeSpeak.Core.Tests\SafeSpeak.Core.Tests.csproj'",
            script);
        Assert.Contains(
            @"$appContractsTestProject = Join-Path $repoRoot 'tests\SafeSpeak.App.Contracts.Tests\SafeSpeak.App.Contracts.Tests.csproj'",
            script);

        AssertCommandOccursExactlyOnce(script, "restore", "$testProject");
        AssertCommandOccursExactlyOnce(script, "restore", "$appContractsTestProject");
        AssertCommandOccursExactlyOnce(script, "test", "$testProject");
        AssertCommandOccursExactlyOnce(script, "test", "$appContractsTestProject");

        string skipTestsBlock = IfBlock(script, "if (-not $SkipTests)");
        Assert.Contains("@('test', $testProject, '-c', 'Release', '--no-restore')", skipTestsBlock);
        Assert.Contains(
            "@('test', $appContractsTestProject, '-c', 'Release', '--no-restore')",
            skipTestsBlock);
    }

    [Fact]
    public void SkipTests_SkipsBothSuitesThroughOneSharedGuard()
    {
        string script = ReleaseScript();
        string skipTestsBlock = IfBlock(script, "if (-not $SkipTests)");

        Assert.Equal(1, Count(script, "if (-not $SkipTests)"));
        Assert.Equal(2, Count(skipTestsBlock, "Invoke-CheckedCommand -FilePath 'dotnet' -ArgumentList @('test'"));

        string outsideGuard = script.Remove(
            script.IndexOf(skipTestsBlock, StringComparison.Ordinal),
            skipTestsBlock.Length);
        Assert.DoesNotContain("-ArgumentList @('test'", outsideGuard);
        Assert.DoesNotContain("dotnet test", outsideGuard, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ReleasePackaging_DoesNotBuildOrLaunchTikFinityEmulator()
    {
        string scriptWithoutComments = RemovePowerShellComments(ReleaseScript());

        Assert.DoesNotContain("TikFinityEmulator", scriptWithoutComments, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("tools\\TikFinityEmulator", scriptWithoutComments, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("SafeSpeak.sln", scriptWithoutComments, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Start-Process", scriptWithoutComments, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotMatch(
            new Regex("(?im)-ArgumentList\\s+@\\(\\s*['\"](?:build|run)['\"]"),
            scriptWithoutComments);
    }

    [Fact]
    public void DesktopBuildWorkflow_UsesTheReleaseEntryPoint()
    {
        string workflow = Source(".github", "workflows", "desktop-build.yml");

        Assert.Contains(
            "run: ./installer/Build-Release.ps1 -Architecture ${{ matrix.architecture }} -Format Both",
            workflow);
        Assert.Equal(1, Count(workflow, "./installer/Build-Release.ps1"));
        Assert.DoesNotContain("dotnet restore", workflow, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("dotnet test", workflow, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("dotnet publish", workflow, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("TikFinityEmulator", workflow, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void InstallerReadme_DocumentsBothDefaultSuitesAndSkipTestsScope()
    {
        string readme = Source("installer", "README.md");

        Assert.Contains(
            "The release build runs both the Core suite and the WPF accessibility-contract suite by default.",
            readme);
        Assert.Contains(
            "Use `-SkipTests` only after both suites have already passed in the same clean source revision.",
            readme);
        Assert.Contains("It never starts or owns the TikFinity emulator.", readme);
        Assert.Contains("`installer/Build-Release.ps1`", readme);
    }

    [Fact]
    public void RepositoryLicense_IsProprietaryWhileOfficialBinariesRemainFreeToUse()
    {
        string license = Source("LICENSE");
        string readme = Source("README.md");

        Assert.Contains("SafeSpeak Proprietary Source-Visible License", license);
        Assert.Contains("Copyright (c) 2026 Alex Mammen. All rights reserved.", license);
        Assert.Contains("Free use of official binary releases", license);
        Assert.Contains("Source visibility does not grant permission", license);
        Assert.Contains("previous MIT license", license);
        Assert.Contains("Third-Party Materials remain governed by their respective licenses", license);
        Assert.DoesNotContain("MIT License\n\nCopyright (c) 2026 SafeSpeak contributors", license);
        Assert.Contains("it is not open source", readme);
        Assert.Contains("THIRD-PARTY-NOTICES.md", readme);
    }

    [Fact]
    public void ReleasePackages_IncludeProprietaryAndThirdPartyLegalNotices()
    {
        string script = ReleaseScript();

        Assert.Contains("Join-Path $repoRoot 'LICENSE'", script);
        Assert.Contains("Join-Path $repoRoot 'THIRD-PARTY-NOTICES.md'", script);
        Assert.Contains("Join-Path $publishDirectory 'LICENSE.txt'", script);
        Assert.Contains("Join-Path $publishDirectory 'THIRD-PARTY-NOTICES.md'", script);
        Assert.Contains("ONNXRuntime-ThirdPartyNotices.txt", script);
        Assert.Contains("ONNXRuntime-LICENSE.txt", script);
        Assert.Contains("NAudio-LICENSE.txt", script);
        Assert.Contains("Apache-2.0.txt", script);
        Assert.Contains("$requiredPackagedLegalFiles", script);
        Assert.Contains("MSIX verification failed: packaged legal notice is missing", script);
    }

    [Fact]
    public void HashVerifiedModerationAssets_UseDeterministicGitAttributes()
    {
        string attributes = Source(".gitattributes");

        Assert.Contains(
            "src/SafeSpeak.Core/AI/Models/LocalModeration/model.onnx binary",
            attributes);
        Assert.Contains(
            "src/SafeSpeak.Core/AI/Models/LocalModeration/tokenizer.json text eol=lf",
            attributes);
    }

    [Fact]
    public void StoreBundleBuilder_UsesBothArchitecturesAndStoreGuards()
    {
        string script = Source("installer", "Build-StoreBundle.ps1");

        Assert.Contains("-Architecture x64", script);
        Assert.Contains("-Architecture arm64", script);
        Assert.Equal(2, Count(script, "-StoreSubmission"));
        Assert.Contains("-SkipTests:$SkipTests", script);
        Assert.Contains("-Architecture arm64", script);
        Assert.Contains("'bundle', '/d'", script);
        Assert.Contains("'unbundle', '/p'", script);
        Assert.Contains("Microsoft Store reserves the fourth package version component", script);
        Assert.Contains("requires the assigned Partner Center identity and publisher", script);
        Assert.DoesNotContain("Start-Process", script, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("TikFinityEmulator", script, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void StorePublisherWorkflow_IsManualProtectedAndDraftByDefault()
    {
        string workflow = Source(".github", "workflows", "store-publisher.yml");

        Assert.Contains("workflow_dispatch:", workflow);
        Assert.DoesNotContain("pull_request:", workflow);
        Assert.DoesNotContain("push:", workflow);
        Assert.Contains("default: false", workflow);
        Assert.Contains("./installer/Build-StoreBundle.ps1", workflow);
        Assert.Contains("PACKAGE_VERSION: ${{ inputs.package_version }}", workflow);
        Assert.Contains("-PackageVersion $env:PACKAGE_VERSION", workflow);
        Assert.DoesNotContain("-PackageVersion '${{ inputs.package_version }}'", workflow);
        Assert.Contains("name: microsoft-store-production", workflow);
        Assert.Contains("microsoft/microsoft-store-apppublisher@v1.1", workflow);
        Assert.Contains("$arguments += '--noCommit'", workflow);
        Assert.Contains("PARTNER_CENTER_CLIENT_SECRET: ${{ secrets.PARTNER_CENTER_CLIENT_SECRET }}", workflow);
        Assert.DoesNotContain("PARTNER_CENTER_CLIENT_SECRET: ${{ vars.", workflow);
    }

    private static void AssertSolutionProject(string solution, string name, string path)
    {
        string expected = $"= \"{name}\", \"{path}\",";
        Assert.Contains(expected, solution);
        Assert.Equal(1, Count(solution, expected));
    }

    private static void AssertCommandOccursExactlyOnce(
        string script,
        string verb,
        string projectVariable)
    {
        string marker = $"-ArgumentList @('{verb}', {projectVariable}";
        Assert.Equal(1, Count(script, marker));
    }

    private static string IfBlock(string source, string signature)
    {
        int signatureStart = source.IndexOf(signature, StringComparison.Ordinal);
        Assert.True(signatureStart >= 0, $"Conditional not found: {signature}");
        int bodyStart = source.IndexOf('{', signatureStart);
        Assert.True(bodyStart >= 0, $"Conditional body not found: {signature}");

        int depth = 0;
        for (int index = bodyStart; index < source.Length; index++)
        {
            if (source[index] == '{') depth++;
            if (source[index] != '}') continue;
            depth--;
            if (depth == 0) return source[signatureStart..(index + 1)];
        }

        throw new InvalidOperationException($"Unterminated conditional: {signature}");
    }

    private static string RemovePowerShellComments(string source) =>
        string.Join(
            Environment.NewLine,
            source.Split(new[] { "\r\n", "\n" }, StringSplitOptions.None)
                .Where(line => !line.TrimStart().StartsWith('#')));

    private static int Count(string source, string value) =>
        Regex.Matches(source, Regex.Escape(value)).Count;

    private static string ReleaseScript() =>
        Source("installer", "Build-Release.ps1");

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
        return Path.Combine(
            new[] { directory!.FullName }.Concat(segments).ToArray());
    }
}
