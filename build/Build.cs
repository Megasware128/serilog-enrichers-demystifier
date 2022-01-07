using Nuke.Common;
using Nuke.Common.CI;
using Nuke.Common.CI.AppVeyor;
using Nuke.Common.CI.GitHubActions;
using Nuke.Common.Execution;
using Nuke.Common.Git;
using Nuke.Common.IO;
using Nuke.Common.ProjectModel;
using Nuke.Common.Tooling;
using Nuke.Common.Tools.DotNet;
using Nuke.Common.Tools.GitVersion;
using Nuke.Common.Utilities.Collections;
using static Nuke.Common.IO.FileSystemTasks;
using static Nuke.Common.IO.PathConstruction;
using static Nuke.Common.Tools.DotNet.DotNetTasks;

[CheckBuildProjectConfigurations]
[ShutdownDotNetAfterServerBuild]
[AppVeyor(AppVeyorImage.VisualStudio2019, BranchesOnly = new[] { "master", "dev" }, InvokedTargets = new[] { nameof(Test) })]
[GitHubActions("ci",
                GitHubActionsImage.WindowsLatest,
                InvokedTargets = new[] { nameof(Test), nameof(Pack) },
                OnPushBranches = new[] { "master", "dev" },
                PublishArtifacts = true)]
[GitHubActions("publish", GitHubActionsImage.UbuntuLatest, InvokedTargets = new[] { nameof(Pack), nameof(Publish) }, OnPushTags = new[] { "v*" }, ImportSecrets = new[] { nameof(NuGetApiKey) })]
class Build : NukeBuild
{
    /// Support plugins are available for:
    ///   - JetBrains ReSharper        https://nuke.build/resharper
    ///   - JetBrains Rider            https://nuke.build/rider
    ///   - Microsoft VisualStudio     https://nuke.build/visualstudio
    ///   - Microsoft VSCode           https://nuke.build/vscode

    public static int Main() => Execute<Build>(x => x.Compile);

    [Parameter("Configuration to build - Default is 'Debug' (local) or 'Release' (server)")]
    readonly Configuration Configuration = IsLocalBuild ? Configuration.Debug : Configuration.Release;

    [Parameter] readonly string NuGetSource = "https://api.nuget.org/v3/index.json";
    [Parameter] readonly string NuGetApiKey;

    [Solution(GenerateProjects = true)] readonly Solution Solution;
    [GitRepository] readonly GitRepository GitRepository;
    [GitVersion(Framework = "net5.0")] readonly GitVersion GitVersion;

    [PathExecutable("nuke.exe")] readonly Tool Nuke;
    [PackageExecutable("NuKeeper", "NuKeeper.dll", Framework = "net5.0")] readonly Tool NuKeeper;
    [PackageExecutable("upgrade-assistant", "Microsoft.DotNet.UpgradeAssistant.Cli.dll", Framework = "net6.0")] readonly Tool UpgradeAssistant;

    AbsolutePath SourceDirectory => RootDirectory / "src";
    AbsolutePath TestsDirectory => RootDirectory / "test";
    AbsolutePath ArtifactsDirectory => RootDirectory / "artifacts";

    Target Clean => _ => _
        .Before(Restore)
        .Executes(() =>
        {
            SourceDirectory.GlobDirectories("**/bin", "**/obj").ForEach(DeleteDirectory);
            TestsDirectory.GlobDirectories("**/bin", "**/obj").ForEach(DeleteDirectory);
            EnsureCleanDirectory(ArtifactsDirectory);
        });

    Target Restore => _ => _
        .Executes(() =>
        {
            DotNetRestore(s => s
                .SetProjectFile(Solution));
        });

    Target Compile => _ => _
        .DependsOn(Restore)
        .Executes(() =>
        {
            DotNetBuild(s => s
                .SetProjectFile(Solution)
                .SetConfiguration(Configuration)
                .SetAssemblyVersion(GitVersion.AssemblySemVer)
                .SetFileVersion(GitVersion.AssemblySemFileVer)
                .SetInformationalVersion(GitVersion.InformationalVersion)
                .EnableNoRestore());
        });

    Target Test => _ => _
        .DependsOn(Compile)
        .Executes(() =>
        {
            DotNetTest(s => s
                .SetProjectFile(Solution.test.Serilog_Enrichers_Demystifier_Tests)
                .SetConfiguration(Configuration)
                .EnableNoBuild());
        });

    Target Pack => _ => _
        .DependsOn(Compile)
        .Produces(ArtifactsDirectory / "*.nupkg")
        .Executes(() =>
        {
            DotNetPack(s => s
                .SetProject(Solution.src.Serilog_Enrichers_Demystifier)
                .SetConfiguration(Configuration)
                .SetVersion(GitVersion.NuGetVersion)
                .SetOutputDirectory(ArtifactsDirectory)
                .EnableNoBuild());
        });

    Target Publish => _ => _
        .After(Pack)
        .Consumes(Pack)
        .Requires(() => NuGetSource)
        .Requires(() => NuGetApiKey)
        .Executes(() =>
        {
            DotNetNuGetPush(s => s
                .SetTargetPath(ArtifactsDirectory / "*.nupkg")
                .SetSource(NuGetSource)
                .SetApiKey(NuGetApiKey));
        });

    Target Update => _ => _
        .Executes(() =>
        {
            Nuke(":update");
            UpgradeAssistant($"upgrade {Solution} -e * --skip-backup --non-interactive");
            NuKeeper("update -m 10 -a 0");
        });
}
