using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;
using System.Xml;
using LibGit2Sharp;
using Microsoft.Build.Construction;
using Microsoft.Build.Evaluation;
using Microsoft.Build.Execution;
using Microsoft.Build.Framework;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Nerdbank.GitVersioning;
using Validation;
using Xunit;
using Xunit.Abstractions;
using Version = System.Version;

[Trait("Engine", "Managed")]
[Collection("Build")] // msbuild sets current directory in the process, so we can't have it be concurrent with other build tests.
public class BuildIntegrationManagedTests : BuildIntegrationTests
{
    public BuildIntegrationManagedTests(ITestOutputHelper logger)
        : base(logger)
    {
    }

    protected override GitContext CreateGitContext(string path, string committish = null)
        => GitContext.Create(path, committish, writable: false);

    protected override void ApplyGlobalProperties(IDictionary<string, string> globalProperties)
        => globalProperties["NBGV_GitEngine"] = "Managed";
}

[Trait("Engine", "LibGit2")]
[Collection("Build")] // msbuild sets current directory in the process, so we can't have it be concurrent with other build tests.
public class BuildIntegrationLibGit2Tests : BuildIntegrationTests
{
    public BuildIntegrationLibGit2Tests(ITestOutputHelper logger)
        : base(logger)
    {
    }

    protected override GitContext CreateGitContext(string path, string committish = null)
        => GitContext.Create(path, committish, writable: true);

    protected override void ApplyGlobalProperties(IDictionary<string, string> globalProperties)
        => globalProperties["NBGV_GitEngine"] = "LibGit2";
}

public abstract class BuildIntegrationTests : RepoTestBase, IClassFixture<MSBuildFixture>
{
    private const string GitVersioningTargetsFileName = "NerdBank.GitVersioning.targets";
    private const string UnitTestCloudBuildPrefix = "UnitTest: ";
    private static readonly string[] ToxicEnvironmentVariablePrefixes = new string[]
    {
        "APPVEYOR",
        "SYSTEM_",
        "BUILD_",
        "NBGV_GitEngine",
    };
    private BuildManager buildManager;
    private ProjectCollection projectCollection;
    private string projectDirectory;
    private ProjectRootElement testProject;
    private Dictionary<string, string> globalProperties = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
    {
        // Set global properties to neutralize environment variables
        // that might actually be defined by a CI that is building and running these tests.
        { "PublicRelease", string.Empty },
    };
    private Random random;

    public BuildIntegrationTests(ITestOutputHelper logger)
        : base(logger)
    {
        // MSBuildExtensions.LoadMSBuild will be called as part of the base constructor, because this class
        // implements the IClassFixture<MSBuildFixture> interface. LoadMSBuild will load the MSBuild assemblies.
        // This must happen _before_ any method that directly references types in the Microsoft.Build namespace has been called.
        // Net, don't init MSBuild-related fields in the constructor, but in a method that is called by the constructor.
        this.Init();
    }

    private void Init()
    {
        int seed = (int)DateTime.Now.Ticks;
        this.random = new Random(seed);
        this.Logger.WriteLine("Random seed: {0}", seed);
        this.buildManager = new BuildManager();
        this.projectCollection = new ProjectCollection();
        this.projectDirectory = Path.Combine(this.RepoPath, "projdir");
        Directory.CreateDirectory(this.projectDirectory);
        this.LoadTargetsIntoProjectCollection();
        this.testProject = this.CreateProjectRootElement(this.projectDirectory, "test.prj");
        this.globalProperties.Add("NerdbankGitVersioningTasksPath", Environment.CurrentDirectory + "\\");
        Environment.SetEnvironmentVariable("_NBGV_UnitTest", "true");

        // Sterilize the test of any environment variables.
        foreach (System.Collections.DictionaryEntry variable in Environment.GetEnvironmentVariables())
        {
            string name = (string)variable.Key;
            if (ToxicEnvironmentVariablePrefixes.Any(toxic => name.StartsWith(toxic, StringComparison.OrdinalIgnoreCase)))
            {
                this.globalProperties[name] = string.Empty;
            }
        }
    }

    private string CommitIdShort => this.Context.GitCommitId?.Substring(0, VersionOptions.DefaultGitCommitIdShortFixedLength);

    protected abstract void ApplyGlobalProperties(IDictionary<string, string> globalProperties);

    protected override void Dispose(bool disposing)
    {
        Environment.SetEnvironmentVariable("_NBGV_UnitTest", string.Empty);
        base.Dispose(disposing);
    }

    [Fact]
    public async Task GetBuildVersion_Returns_BuildVersion_Property()
    {
        this.WriteVersionFile();
        this.InitializeSourceControl();
        var buildResult = await this.BuildAsync();
        Assert.Equal(
            buildResult.BuildVersion,
            buildResult.BuildResult.ResultsByTarget[Targets.GetBuildVersion].Items.Single().ItemSpec);
    }

    [Fact]
    public async Task GetBuildVersion_Without_Git()
    {
        this.WriteVersionFile("3.4");
        var buildResult = await this.BuildAsync();
        Assert.Equal("3.4", buildResult.BuildVersion);
        Assert.Equal("3.4.0", buildResult.AssemblyInformationalVersion);
    }

    [Fact]
    public async Task GetBuildVersion_WithThreeVersionIntegers()
    {
        VersionOptions workingCopyVersion = new VersionOptions
        {
            Version = SemanticVersion.Parse("7.8.9-beta.3"),
            SemVer1NumericIdentifierPadding = 1,
        };
        this.WriteVersionFile(workingCopyVersion);
        this.InitializeSourceControl();
        var buildResult = await this.BuildAsync();
        this.AssertStandardProperties(workingCopyVersion, buildResult);
    }

    [Fact]
    public async Task GetBuildVersion_Without_Git_HighPrecisionAssemblyVersion()
    {
        this.WriteVersionFile(new VersionOptions
        {
            Version = SemanticVersion.Parse("3.4"),
            AssemblyVersion = new VersionOptions.AssemblyVersionOptions
            {
                Precision = VersionOptions.VersionPrecision.Revision,
            },
        });
        var buildResult = await this.BuildAsync();
        Assert.Equal("3.4", buildResult.BuildVersion);
        Assert.Equal("3.4.0", buildResult.AssemblyInformationalVersion);
    }

    [Fact]
    public async Task GetBuildVersion_OutsideGit_PointingToGit()
    {
        // Write a version file to the 'virtualized' repo.
        string version = "3.4";
        this.WriteVersionFile(version);

        string repoRelativeProjectPath = this.testProject.FullPath.Substring(this.RepoPath.Length + 1);

        // Update the repo path so we create the 'normal' one elsewhere
        this.RepoPath = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
        this.InitializeSourceControl();

        // Write the same version file to the 'real' repo
        this.WriteVersionFile(version);

        // Point the project to the 'real' repo
        this.testProject.AddProperty("GitRepoRoot", this.RepoPath);
        this.testProject.AddProperty("ProjectPathRelativeToGitRepoRoot", repoRelativeProjectPath);

        var buildResult = await this.BuildAsync();

        var workingCopyVersion = VersionOptions.FromVersion(new Version(version));

        this.AssertStandardProperties(workingCopyVersion, buildResult);
    }

    [Fact]
    public async Task GetBuildVersion_In_Git_But_Without_Commits()
    {
        Repository.Init(this.RepoPath);
        var repo = new Repository(this.RepoPath); // do not assign Repo property to avoid commits being generated later
        this.WriteVersionFile("3.4");
        Assumes.False(repo.Head.Commits.Any()); // verification that the test is doing what it claims
        var buildResult = await this.BuildAsync();
        Assert.Equal("3.4.0.0", buildResult.BuildVersion);
        Assert.Equal("3.4.0", buildResult.AssemblyInformationalVersion);
    }

    [Fact]
    public async Task GetBuildVersion_In_Git_But_Head_Lacks_VersionFile()
    {
        Repository.Init(this.RepoPath);
        var repo = new Repository(this.RepoPath); // do not assign Repo property to avoid commits being generated later
        repo.Commit("empty", this.Signer, this.Signer, new CommitOptions { AllowEmptyCommit = true });
        this.WriteVersionFile("3.4");
        Assumes.True(repo.Index[VersionFile.JsonFileName] == null);
        var buildResult = await this.BuildAsync();
        Assert.Equal("3.4.0." + this.GetVersion().Revision, buildResult.BuildVersion);
        Assert.Equal("3.4.0+" + repo.Head.Tip.Id.Sha.Substring(0, VersionOptions.DefaultGitCommitIdShortFixedLength), buildResult.AssemblyInformationalVersion);
    }

    [Fact]
    public async Task GetBuildVersion_In_Git_But_WorkingCopy_Has_Changes()
    {
        const string majorMinorVersion = "5.8";
        const string prerelease = "";

        this.WriteVersionFile(majorMinorVersion, prerelease);
        this.InitializeSourceControl();
        var workingCopyVersion = VersionOptions.FromVersion(new Version("6.0"));
        this.Context.VersionFile.SetVersion(this.RepoPath, workingCopyVersion);
        var buildResult = await this.BuildAsync();
        this.AssertStandardProperties(workingCopyVersion, buildResult);
    }

    [Fact]
    public async Task GetBuildVersion_In_Git_No_VersionFile_At_All()
    {
        Repository.Init(this.RepoPath);
        var repo = new Repository(this.RepoPath); // do not assign Repo property to avoid commits being generated later
        repo.Commit("empty", this.Signer, this.Signer, new CommitOptions { AllowEmptyCommit = true });
        var buildResult = await this.BuildAsync();
        Assert.Equal("0.0.0." + this.GetVersion().Revision, buildResult.BuildVersion);
        Assert.Equal("0.0.0+" + repo.Head.Tip.Id.Sha.Substring(0, VersionOptions.DefaultGitCommitIdShortFixedLength), buildResult.AssemblyInformationalVersion);
    }

    [Fact]
    public async Task GetBuildVersion_In_Git_With_Version_File_In_Subdirectory_Works()
    {
        const string majorMinorVersion = "5.8";
        const string prerelease = "";
        const string subdirectory = "projdir";

        this.WriteVersionFile(majorMinorVersion, prerelease, subdirectory);
        this.InitializeSourceControl();
        this.AddCommits(this.random.Next(15));
        var buildResult = await this.BuildAsync();
        this.AssertStandardProperties(VersionOptions.FromVersion(new Version(majorMinorVersion)), buildResult, subdirectory);
    }

    [Fact]
    public async Task GetBuildVersion_In_Git_With_Version_File_In_Root_And_Subdirectory_Works()
    {
        var rootVersionSpec = new VersionOptions
        {
            Version = SemanticVersion.Parse("14.1"),
            AssemblyVersion = new VersionOptions.AssemblyVersionOptions(new Version(14, 0)),
        };
        var subdirVersionSpec = new VersionOptions { Version = SemanticVersion.Parse("11.0") };
        const string subdirectory = "projdir";

        this.WriteVersionFile(rootVersionSpec);
        this.WriteVersionFile(subdirVersionSpec, subdirectory);
        this.InitializeSourceControl();
        this.AddCommits(this.random.Next(15));
        var buildResult = await this.BuildAsync();
        this.AssertStandardProperties(subdirVersionSpec, buildResult, subdirectory);
    }

    [Fact]
    public async Task GetBuildVersion_In_Git_With_Version_File_In_Root_And_Project_In_Root_Works()
    {
        var rootVersionSpec = new VersionOptions
        {
            Version = SemanticVersion.Parse("14.1"),
            AssemblyVersion = new VersionOptions.AssemblyVersionOptions(new Version(14, 0)),
        };

        this.WriteVersionFile(rootVersionSpec);
        this.InitializeSourceControl();
        this.AddCommits(this.random.Next(15));
        this.testProject = this.CreateProjectRootElement(this.RepoPath, "root.proj");
        var buildResult = await this.BuildAsync();
        this.AssertStandardProperties(rootVersionSpec, buildResult);
    }

    [Fact]
    public async Task GetBuildVersion_StablePreRelease()
    {
        const string majorMinorVersion = "5.8";
        const string prerelease = "";

        this.WriteVersionFile(majorMinorVersion, prerelease);
        this.InitializeSourceControl();
        this.AddCommits(this.random.Next(15));
        var buildResult = await this.BuildAsync();
        this.AssertStandardProperties(VersionOptions.FromVersion(new Version(majorMinorVersion)), buildResult);
    }

    [Fact]
    public async Task GetBuildVersion_StableRelease()
    {
        const string majorMinorVersion = "5.8";
        const string prerelease = "";

        this.WriteVersionFile(majorMinorVersion, prerelease);
        this.InitializeSourceControl();
        this.AddCommits(this.random.Next(15));
        this.globalProperties["PublicRelease"] = "true";
        var buildResult = await this.BuildAsync();
        this.AssertStandardProperties(VersionOptions.FromVersion(new Version(majorMinorVersion)), buildResult);

        Version version = this.GetVersion();
        Assert.Equal($"{version.Major}.{version.Minor}.{buildResult.GitVersionHeight}", buildResult.NuGetPackageVersion);
    }

    [Fact]
    public async Task GetBuildVersion_UnstablePreRelease()
    {
        const string majorMinorVersion = "5.8";
        const string prerelease = "-beta";

        this.WriteVersionFile(majorMinorVersion, prerelease);
        this.InitializeSourceControl();
        this.AddCommits(this.random.Next(15));
        var buildResult = await this.BuildAsync();
        this.AssertStandardProperties(VersionOptions.FromVersion(new Version(majorMinorVersion), prerelease), buildResult);
    }

    [Fact]
    public async Task GetBuildVersion_UnstableRelease()
    {
        const string majorMinorVersion = "5.8";
        const string prerelease = "-beta";

        this.WriteVersionFile(majorMinorVersion, prerelease);
        this.InitializeSourceControl();
        this.AddCommits(this.random.Next(15));
        this.globalProperties["PublicRelease"] = "true";
        var buildResult = await this.BuildAsync();
        this.AssertStandardProperties(VersionOptions.FromVersion(new Version(majorMinorVersion), prerelease), buildResult);
    }

    [Fact]
    public async Task GetBuildVersion_CustomAssemblyVersion()
    {
        this.WriteVersionFile("14.0");
        this.InitializeSourceControl();
        var versionOptions = new VersionOptions
        {
            Version = new SemanticVersion(new Version(14, 1)),
            AssemblyVersion = new VersionOptions.AssemblyVersionOptions(new Version(14, 0)),
        };
        this.WriteVersionFile(versionOptions);
        var buildResult = await this.BuildAsync();
        this.AssertStandardProperties(versionOptions, buildResult);
    }

    [Theory]
    [InlineData(VersionOptions.VersionPrecision.Major)]
    [InlineData(VersionOptions.VersionPrecision.Build)]
    [InlineData(VersionOptions.VersionPrecision.Revision)]
    public async Task GetBuildVersion_CustomAssemblyVersionWithPrecision(VersionOptions.VersionPrecision precision)
    {
        var versionOptions = new VersionOptions
        {
            Version = new SemanticVersion("14.1"),
            AssemblyVersion = new VersionOptions.AssemblyVersionOptions
            {
                Version = new Version("15.2"),
                Precision = precision,
            },
        };
        this.WriteVersionFile(versionOptions);
        this.InitializeSourceControl();
        var buildResult = await this.BuildAsync();
        this.AssertStandardProperties(versionOptions, buildResult);
    }

    [Theory]
    [InlineData(VersionOptions.VersionPrecision.Major)]
    [InlineData(VersionOptions.VersionPrecision.Build)]
    [InlineData(VersionOptions.VersionPrecision.Revision)]
    public async Task GetBuildVersion_CustomAssemblyVersionPrecision(VersionOptions.VersionPrecision precision)
    {
        var versionOptions = new VersionOptions
        {
            Version = new SemanticVersion("14.1"),
            AssemblyVersion = new VersionOptions.AssemblyVersionOptions
            {
                Precision = precision,
            },
        };
        this.WriteVersionFile(versionOptions);
        this.InitializeSourceControl();
        var buildResult = await this.BuildAsync();
        this.AssertStandardProperties(versionOptions, buildResult);
    }

    [Fact]
    public async Task GetBuildVersion_CustomBuildNumberOffset()
    {
        this.WriteVersionFile("14.0");
        this.InitializeSourceControl();
        var versionOptions = new VersionOptions
        {
            Version = new SemanticVersion(new Version(14, 1)),
            VersionHeightOffset = 5,
        };
        this.WriteVersionFile(versionOptions);
        var buildResult = await this.BuildAsync();
        this.AssertStandardProperties(versionOptions, buildResult);
    }

    [Fact]
    public async Task GetBuildVersion_OverrideBuildNumberOffset()
    {
        this.WriteVersionFile("14.0");
        this.InitializeSourceControl();
        var versionOptions = new VersionOptions
        {
            Version = new SemanticVersion(new Version(14, 1))
        };
        this.WriteVersionFile(versionOptions);
        this.testProject.AddProperty("OverrideBuildNumberOffset", "10");
        var buildResult = await this.BuildAsync();
        Assert.StartsWith("14.1.11.", buildResult.AssemblyFileVersion);
    }

    [Fact]
    public async Task GetBuildVersion_Minus1BuildOffset_NotYetCommitted()
    {
        this.WriteVersionFile("14.0");
        this.InitializeSourceControl();
        var versionOptions = new VersionOptions
        {
            Version = new SemanticVersion(new Version(14, 1)),
            VersionHeightOffset = -1,
        };
        this.Context.VersionFile.SetVersion(this.RepoPath, versionOptions);
        var buildResult = await this.BuildAsync();
        this.AssertStandardProperties(versionOptions, buildResult);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(21)]
    public async Task GetBuildVersion_BuildNumberSpecifiedInVersionJson(int buildNumber)
    {
        var versionOptions = new VersionOptions
        {
            Version = SemanticVersion.Parse("14.0." + buildNumber),
        };
        this.WriteVersionFile(versionOptions);
        this.InitializeSourceControl();
        var buildResult = await this.BuildAsync();
        this.AssertStandardProperties(versionOptions, buildResult);
    }

    [Fact]
    public async Task PublicRelease_RegEx_Unsatisfied()
    {
        var versionOptions = new VersionOptions
        {
            Version = SemanticVersion.Parse("1.0"),
            PublicReleaseRefSpec = new string[] { "^refs/heads/release$" },
        };
        this.WriteVersionFile(versionOptions);
        this.InitializeSourceControl();

        // Just build "master", which doesn't conform to the regex.
        var buildResult = await this.BuildAsync();
        Assert.False(buildResult.PublicRelease);
        this.AssertStandardProperties(versionOptions, buildResult);
    }

    public static IEnumerable<object[]> CloudBuildOfBranch(string branchName)
    {
        return new object[][]
        {
            new object[] { CloudBuild.AppVeyor.SetItem("APPVEYOR_REPO_BRANCH", branchName) },
            new object[] { CloudBuild.VSTS.SetItem("BUILD_SOURCEBRANCH", $"refs/heads/{branchName}") },
            new object[] { CloudBuild.VSTS.SetItem("BUILD_SOURCEBRANCH", $"refs/tags/{branchName}") },
            new object[] { CloudBuild.Teamcity.SetItem("BUILD_GIT_BRANCH", $"refs/heads/{branchName}") },
            new object[] { CloudBuild.Teamcity.SetItem("BUILD_GIT_BRANCH", $"refs/tags/{branchName}") },
        };
    }

    [Theory]
    [MemberData(nameof(CloudBuildOfBranch), "release")]
    public async Task PublicRelease_RegEx_SatisfiedByCI(IReadOnlyDictionary<string, string> serverProperties)
    {
        var versionOptions = new VersionOptions
        {
            Version = SemanticVersion.Parse("1.0"),
            PublicReleaseRefSpec = new string[] 
            { 
                "^refs/heads/release$",
                "^refs/tags/release$"
            },
        };
        this.WriteVersionFile(versionOptions);
        this.InitializeSourceControl();

        // Don't actually switch the checked out branch in git. CI environment variables
        // should take precedence over actual git configuration. (Why? because these variables may
        // retain information about which tag was checked out on a detached head).
        using (ApplyEnvironmentVariables(serverProperties))
        {
            var buildResult = await this.BuildAsync();
            Assert.True(buildResult.PublicRelease);
            this.AssertStandardProperties(versionOptions, buildResult);
        }
    }

    public static object[][] CloudBuildVariablesData
    {
        get
        {
            return new object[][]
            {
                new object[] { CloudBuild.VSTS, "##vso[task.setvariable variable={NAME};]{VALUE}", false },
                new object[] { CloudBuild.VSTS, "##vso[task.setvariable variable={NAME};]{VALUE}", true },
            };
        }
    }

    [Theory]
    [Trait("TestCategory", "FailsOnAzurePipelines")]
    [MemberData(nameof(CloudBuildVariablesData))]
    public async Task CloudBuildVariables_SetInCI(IReadOnlyDictionary<string, string> properties, string expectedMessage, bool setAllVariables)
    {
        using (ApplyEnvironmentVariables(properties))
        {
            string keyName = "n1";
            string value = "v1";
            this.testProject.AddItem("CloudBuildVersionVars", keyName, new Dictionary<string, string> { { "Value", value } });

            string alwaysExpectedMessage = UnitTestCloudBuildPrefix + expectedMessage
                .Replace("{NAME}", keyName)
                .Replace("{VALUE}", value);

            var versionOptions = new VersionOptions
            {
                Version = SemanticVersion.Parse("1.0"),
                CloudBuild = new VersionOptions.CloudBuildOptions { SetAllVariables = setAllVariables, SetVersionVariables = true },
            };
            this.WriteVersionFile(versionOptions);
            this.InitializeSourceControl();

            var buildResult = await this.BuildAsync();
            this.AssertStandardProperties(versionOptions, buildResult);

            // Assert GitBuildVersion was set
            string conditionallyExpectedMessage = UnitTestCloudBuildPrefix + expectedMessage
                .Replace("{NAME}", "GitBuildVersion")
                .Replace("{VALUE}", buildResult.BuildVersion);
            Assert.Contains(alwaysExpectedMessage, buildResult.LoggedEvents.Select(e => e.Message.TrimEnd()));
            Assert.Contains(conditionallyExpectedMessage, buildResult.LoggedEvents.Select(e => e.Message.TrimEnd()));

            // Assert GitBuildVersionSimple was set
            conditionallyExpectedMessage = UnitTestCloudBuildPrefix + expectedMessage
                .Replace("{NAME}", "GitBuildVersionSimple")
                .Replace("{VALUE}", buildResult.BuildVersionSimple);
            Assert.Contains(alwaysExpectedMessage, buildResult.LoggedEvents.Select(e => e.Message.TrimEnd()));
            Assert.Contains(conditionallyExpectedMessage, buildResult.LoggedEvents.Select(e => e.Message.TrimEnd()));

            // Assert that project properties are also set.
            Assert.Equal(buildResult.BuildVersion, buildResult.GitBuildVersion);
            Assert.Equal(buildResult.BuildVersionSimple, buildResult.GitBuildVersionSimple);
            Assert.Equal(buildResult.AssemblyInformationalVersion, buildResult.GitAssemblyInformationalVersion);

            if (setAllVariables)
            {
                // Assert that some project properties were set as build properties prefaced with "NBGV_".
                Assert.Equal(buildResult.GitCommitIdShort, buildResult.NBGV_GitCommitIdShort);
                Assert.Equal(buildResult.NuGetPackageVersion, buildResult.NBGV_NuGetPackageVersion);
            }
            else
            {
                // Assert that the NBGV_ prefixed properties are *not* set.
                Assert.Equal(string.Empty, buildResult.NBGV_GitCommitIdShort);
                Assert.Equal(string.Empty, buildResult.NBGV_NuGetPackageVersion);
            }

            // Assert that env variables were also set in context of the build.
            Assert.Contains(buildResult.LoggedEvents, e => string.Equals(e.Message, $"n1=v1", StringComparison.OrdinalIgnoreCase));

            versionOptions.CloudBuild.SetVersionVariables = false;
            this.WriteVersionFile(versionOptions);
            this.SetContextToHead();
            buildResult = await this.BuildAsync();
            this.AssertStandardProperties(versionOptions, buildResult);

            // Assert GitBuildVersion was not set
            conditionallyExpectedMessage = UnitTestCloudBuildPrefix + expectedMessage
                .Replace("{NAME}", "GitBuildVersion")
                .Replace("{VALUE}", buildResult.BuildVersion);
            Assert.Contains(alwaysExpectedMessage, buildResult.LoggedEvents.Select(e => e.Message.TrimEnd()));
            Assert.DoesNotContain(conditionallyExpectedMessage, buildResult.LoggedEvents.Select(e => e.Message.TrimEnd()));
            Assert.NotEqual(buildResult.BuildVersion, buildResult.BuildResult.ProjectStateAfterBuild.GetPropertyValue("GitBuildVersion"));

            // Assert GitBuildVersionSimple was not set
            conditionallyExpectedMessage = UnitTestCloudBuildPrefix + expectedMessage
                .Replace("{NAME}", "GitBuildVersionSimple")
                .Replace("{VALUE}", buildResult.BuildVersionSimple);
            Assert.Contains(alwaysExpectedMessage, buildResult.LoggedEvents.Select(e => e.Message.TrimEnd()));
            Assert.DoesNotContain(conditionallyExpectedMessage, buildResult.LoggedEvents.Select(e => e.Message.TrimEnd()));
            Assert.NotEqual(buildResult.BuildVersionSimple, buildResult.BuildResult.ProjectStateAfterBuild.GetPropertyValue("GitBuildVersionSimple"));
        }
    }

    private static VersionOptions BuildNumberVersionOptionsBasis
    {
        get
        {
            return new VersionOptions
            {
                Version = SemanticVersion.Parse("1.0"),
                CloudBuild = new VersionOptions.CloudBuildOptions
                {
                    BuildNumber = new VersionOptions.CloudBuildNumberOptions
                    {
                        Enabled = true,
                        IncludeCommitId = new VersionOptions.CloudBuildNumberCommitIdOptions(),
                    }
                },
            };
        }
    }

    public static object[][] BuildNumberData
    {
        get
        {
            return new object[][]
            {
                new object[] { BuildNumberVersionOptionsBasis, CloudBuild.VSTS, "##vso[build.updatebuildnumber]{CLOUDBUILDNUMBER}" },
            };
        }
    }

    [Theory]
    [MemberData(nameof(BuildNumberData))]
    public async Task BuildNumber_SetInCI(VersionOptions versionOptions, IReadOnlyDictionary<string, string> properties, string expectedBuildNumberMessage)
    {
        this.WriteVersionFile(versionOptions);
        this.InitializeSourceControl();
        using (ApplyEnvironmentVariables(properties))
        {
            var buildResult = await this.BuildAsync();
            this.AssertStandardProperties(versionOptions, buildResult);
            expectedBuildNumberMessage = expectedBuildNumberMessage.Replace("{CLOUDBUILDNUMBER}", buildResult.CloudBuildNumber);
            Assert.Contains(UnitTestCloudBuildPrefix + expectedBuildNumberMessage, buildResult.LoggedEvents.Select(e => e.Message.TrimEnd()));
        }

        versionOptions.CloudBuild.BuildNumber.Enabled = false;
        this.WriteVersionFile(versionOptions);
        using (ApplyEnvironmentVariables(properties))
        {
            var buildResult = await this.BuildAsync();
            this.AssertStandardProperties(versionOptions, buildResult);
            expectedBuildNumberMessage = expectedBuildNumberMessage.Replace("{CLOUDBUILDNUMBER}", buildResult.CloudBuildNumber);
            Assert.DoesNotContain(UnitTestCloudBuildPrefix + expectedBuildNumberMessage, buildResult.LoggedEvents.Select(e => e.Message.TrimEnd()));
        }
    }

    [Theory]
    [PairwiseData]
    public async Task BuildNumber_VariousOptions(bool isPublic, VersionOptions.CloudBuildNumberCommitWhere where, VersionOptions.CloudBuildNumberCommitWhen when, [CombinatorialValues(0, 1, 2)] int extraBuildMetadataCount, [CombinatorialValues(1, 2)] int semVer)
    {
        var versionOptions = BuildNumberVersionOptionsBasis;
        versionOptions.CloudBuild.BuildNumber.IncludeCommitId.Where = where;
        versionOptions.CloudBuild.BuildNumber.IncludeCommitId.When = when;
        versionOptions.NuGetPackageVersion = new VersionOptions.NuGetPackageVersionOptions
        {
            SemVer = semVer,
        };
        this.WriteVersionFile(versionOptions);
        this.InitializeSourceControl();

        this.globalProperties["PublicRelease"] = isPublic.ToString();
        for (int i = 0; i < extraBuildMetadataCount; i++)
        {
            this.testProject.AddItem("BuildMetadata", $"A{i}");
        }

        var buildResult = await this.BuildAsync();
        this.AssertStandardProperties(versionOptions, buildResult);
    }

    [Fact]
    public void GitLab_BuildTag()
    {
        // Based on the values defined in https://docs.gitlab.com/ee/ci/variables/#syntax-of-environment-variables-in-job-scripts
        using (ApplyEnvironmentVariables(
            CloudBuild.SuppressEnvironment.SetItems(
                new Dictionary<string, string>()
                {
                    { "CI_COMMIT_TAG", "1.0.0" },
                    { "CI_COMMIT_SHA", "1ecfd275763eff1d6b4844ea3168962458c9f27a" },
                    { "GITLAB_CI", "true" },
                    { "SYSTEM_TEAMPROJECTID", string.Empty }
                })))
        {
            var activeCloudBuild = Nerdbank.GitVersioning.CloudBuild.Active;
            Assert.NotNull(activeCloudBuild);
            Assert.Null(activeCloudBuild.BuildingBranch);
            Assert.Equal("refs/tags/1.0.0", activeCloudBuild.BuildingTag);
            Assert.Equal("1ecfd275763eff1d6b4844ea3168962458c9f27a", activeCloudBuild.GitCommitId);
            Assert.True(activeCloudBuild.IsApplicable);
            Assert.False(activeCloudBuild.IsPullRequest);
        }
    }

    [Fact]
    public void GitLab_BuildBranch()
    {
        // Based on the values defined in https://docs.gitlab.com/ee/ci/variables/#syntax-of-environment-variables-in-job-scripts
        using (ApplyEnvironmentVariables(
            CloudBuild.SuppressEnvironment.SetItems(
                new Dictionary<string, string>()
                {
                    { "CI_COMMIT_REF_NAME", "master" },
                    { "CI_COMMIT_SHA", "1ecfd275763eff1d6b4844ea3168962458c9f27a" },
                    { "GITLAB_CI", "true" },
            })))
        {
            var activeCloudBuild = Nerdbank.GitVersioning.CloudBuild.Active;
            Assert.NotNull(activeCloudBuild);
            Assert.Equal("refs/heads/master", activeCloudBuild.BuildingBranch);
            Assert.Null(activeCloudBuild.BuildingTag);
            Assert.Equal("1ecfd275763eff1d6b4844ea3168962458c9f27a", activeCloudBuild.GitCommitId);
            Assert.True(activeCloudBuild.IsApplicable);
            Assert.False(activeCloudBuild.IsPullRequest);
        }
    }

    [Fact]
    public async Task PublicRelease_RegEx_SatisfiedByCheckedOutBranch()
    {
        var versionOptions = new VersionOptions
        {
            Version = SemanticVersion.Parse("1.0"),
            PublicReleaseRefSpec = new string[] { "^refs/heads/release$" },
        };
        this.WriteVersionFile(versionOptions);
        this.InitializeSourceControl();

        using (ApplyEnvironmentVariables(CloudBuild.SuppressEnvironment))
        {
            // Check out a branch that conforms.
            var releaseBranch = this.LibGit2Repository.CreateBranch("release");
            Commands.Checkout(this.LibGit2Repository, releaseBranch);
            var buildResult = await this.BuildAsync();
            Assert.True(buildResult.PublicRelease);
            this.AssertStandardProperties(versionOptions, buildResult);
        }
    }

    // This test builds projects using 'classic' MSBuild projects, which target net45.
    // This is not supported on Linux.
    [WindowsTheory]
    [PairwiseData]
    public async Task AssemblyInfo(bool isVB, bool includeNonVersionAttributes, bool gitRepo, bool isPrerelease, bool isPublicRelease)
    {
        this.WriteVersionFile(prerelease: isPrerelease ? "-beta" : string.Empty);
        if (gitRepo)
        {
            this.InitializeSourceControl();
        }

        if (isVB)
        {
            this.MakeItAVBProject();
        }

        if (includeNonVersionAttributes)
        {
            this.testProject.AddProperty("NBGV_EmitNonVersionCustomAttributes", "true");
        }

        this.globalProperties["PublicRelease"] = isPublicRelease ? "true" : "false";

        var result = await this.BuildAsync("Build", logVerbosity: LoggerVerbosity.Minimal);
        string assemblyPath = result.BuildResult.ProjectStateAfterBuild.GetPropertyValue("TargetPath");
        string versionFileContent = File.ReadAllText(Path.Combine(this.projectDirectory, result.BuildResult.ProjectStateAfterBuild.GetPropertyValue("VersionSourceFile")));
        this.Logger.WriteLine(versionFileContent);

        var assembly = Assembly.LoadFile(assemblyPath);

        var assemblyFileVersion = assembly.GetCustomAttribute<AssemblyFileVersionAttribute>();
        var assemblyInformationalVersion = assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>();
        var assemblyTitle = assembly.GetCustomAttribute<AssemblyTitleAttribute>();
        var assemblyProduct = assembly.GetCustomAttribute<AssemblyProductAttribute>();
        var assemblyCompany = assembly.GetCustomAttribute<AssemblyCompanyAttribute>();
        var assemblyCopyright = assembly.GetCustomAttribute<AssemblyCopyrightAttribute>();
        var thisAssemblyClass = assembly.GetType("ThisAssembly") ?? assembly.GetType("TestNamespace.ThisAssembly");
        Assert.NotNull(thisAssemblyClass);

        Assert.Equal(new Version(result.AssemblyVersion), assembly.GetName().Version);
        Assert.Equal(result.AssemblyFileVersion, assemblyFileVersion.Version);
        Assert.Equal(result.AssemblyInformationalVersion, assemblyInformationalVersion.InformationalVersion);
        if (includeNonVersionAttributes)
        {
            Assert.Equal(result.AssemblyTitle, assemblyTitle.Title);
            Assert.Equal(result.AssemblyProduct, assemblyProduct.Product);
            Assert.Equal(result.AssemblyCompany, assemblyCompany.Company);
            Assert.Equal(result.AssemblyCopyright, assemblyCopyright.Copyright);
        }
        else
        {
            Assert.Null(assemblyTitle);
            Assert.Null(assemblyProduct);
            Assert.Null(assemblyCompany);
            Assert.Null(assemblyCopyright);
        }

        const BindingFlags fieldFlags = BindingFlags.Static | BindingFlags.NonPublic;
        Assert.Equal(result.AssemblyVersion, thisAssemblyClass.GetField("AssemblyVersion", fieldFlags).GetValue(null));
        Assert.Equal(result.AssemblyFileVersion, thisAssemblyClass.GetField("AssemblyFileVersion", fieldFlags).GetValue(null));
        Assert.Equal(result.AssemblyInformationalVersion, thisAssemblyClass.GetField("AssemblyInformationalVersion", fieldFlags).GetValue(null));
        Assert.Equal(result.AssemblyName, thisAssemblyClass.GetField("AssemblyName", fieldFlags).GetValue(null));
        Assert.Equal(result.RootNamespace, thisAssemblyClass.GetField("RootNamespace", fieldFlags).GetValue(null));
        Assert.Equal(result.AssemblyConfiguration, thisAssemblyClass.GetField("AssemblyConfiguration", fieldFlags).GetValue(null));
        Assert.Equal(result.AssemblyTitle, thisAssemblyClass.GetField("AssemblyTitle", fieldFlags)?.GetValue(null));
        Assert.Equal(result.AssemblyProduct, thisAssemblyClass.GetField("AssemblyProduct", fieldFlags)?.GetValue(null));
        Assert.Equal(result.AssemblyCompany, thisAssemblyClass.GetField("AssemblyCompany", fieldFlags)?.GetValue(null));
        Assert.Equal(result.AssemblyCopyright, thisAssemblyClass.GetField("AssemblyCopyright", fieldFlags)?.GetValue(null));
        Assert.Equal(result.GitCommitId, thisAssemblyClass.GetField("GitCommitId", fieldFlags)?.GetValue(null) ?? string.Empty);
        Assert.Equal(result.PublicRelease, thisAssemblyClass.GetField("IsPublicRelease", fieldFlags)?.GetValue(null));
        Assert.Equal(!string.IsNullOrEmpty(result.PrereleaseVersion), thisAssemblyClass.GetField("IsPrerelease", fieldFlags)?.GetValue(null));

        if (gitRepo)
        {
            Assert.True(long.TryParse(result.GitCommitDateTicks, out _), $"Invalid value for GitCommitDateTicks: '{result.GitCommitDateTicks}'");
            var gitCommitDate = new DateTime(long.Parse(result.GitCommitDateTicks), DateTimeKind.Utc);
            Assert.Equal(gitCommitDate, thisAssemblyClass.GetProperty("GitCommitDate", fieldFlags)?.GetValue(null) ?? thisAssemblyClass.GetField("GitCommitDate", fieldFlags)?.GetValue(null) ?? string.Empty);
        }
        else
        {
            Assert.Empty(result.GitCommitDateTicks);
            Assert.Null(thisAssemblyClass.GetProperty("GitCommitDate", fieldFlags));
        }

        // Verify that it doesn't have key fields
        Assert.Null(thisAssemblyClass.GetField("PublicKey", fieldFlags));
        Assert.Null(thisAssemblyClass.GetField("PublicKeyToken", fieldFlags));
    }

    // TODO: add key container test.
    [Theory]
    [InlineData("keypair.snk", false)]
    [InlineData("public.snk", true)]
    [InlineData("protectedPair.pfx", true)]
    public async Task AssemblyInfo_HasKeyData(string keyFile, bool delaySigned)
    {
        TestUtilities.ExtractEmbeddedResource($@"Keys\{keyFile}", Path.Combine(this.projectDirectory, keyFile));
        this.testProject.AddProperty("SignAssembly", "true");
        this.testProject.AddProperty("AssemblyOriginatorKeyFile", keyFile);
        this.testProject.AddProperty("DelaySign", delaySigned.ToString());

        this.WriteVersionFile();
        var result = await this.BuildAsync(Targets.GenerateAssemblyVersionInfo, logVerbosity: LoggerVerbosity.Minimal);
        string versionCsContent = File.ReadAllText(
            Path.GetFullPath(
                Path.Combine(
                    this.projectDirectory,
                    result.BuildResult.ProjectStateAfterBuild.GetPropertyValue("VersionSourceFile"))));
        this.Logger.WriteLine(versionCsContent);

        var sourceFile = CSharpSyntaxTree.ParseText(versionCsContent);
        var syntaxTree = await sourceFile.GetRootAsync();
        var fields = syntaxTree.DescendantNodes().OfType<VariableDeclaratorSyntax>();

        var publicKeyField = (LiteralExpressionSyntax)fields.SingleOrDefault(f => f.Identifier.ValueText == "PublicKey")?.Initializer.Value;
        var publicKeyTokenField = (LiteralExpressionSyntax)fields.SingleOrDefault(f => f.Identifier.ValueText == "PublicKeyToken")?.Initializer.Value;
        if (Path.GetExtension(keyFile) == ".pfx")
        {
            // No support for PFX (yet anyway), since they're encrypted.
            // Note for future: I think by this point, the user has typically already decrypted
            // the PFX and stored the key pair in a key container. If we knew how to find which one,
            // we could perhaps divert to that.
            Assert.Null(publicKeyField);
            Assert.Null(publicKeyTokenField);
        }
        else
        {
            Assert.Equal(
                "002400000480000094000000060200000024000052534131000400000100010067cea773679e0ecc114b7e1d442466a90bf77c755811a0d3962a546ed716525b6508abf9f78df132ffd3fb75fe604b3961e39c52d5dfc0e6c1fb233cb4fb56b1a9e3141513b23bea2cd156cb2ef7744e59ba6b663d1f5b2f9449550352248068e85b61c68681a6103cad91b3bf7a4b50d2fabf97e1d97ac34db65b25b58cd0dc",
                publicKeyField?.Token.ValueText);
            Assert.Equal("ca2d1515679318f5", publicKeyTokenField?.Token.ValueText);
        }
    }

    [Fact]
    [Trait("TestCategory", "FailsOnAzurePipelines")]
    public async Task AssemblyInfo_IncrementalBuild()
    {
        this.WriteVersionFile(prerelease: "-beta");
        await this.BuildAsync("Build", logVerbosity: LoggerVerbosity.Minimal);
        this.WriteVersionFile(prerelease: "-rc"); // two characters SHORTER, to test file truncation.
        await this.BuildAsync("Build", logVerbosity: LoggerVerbosity.Minimal);
    }

    /// <summary>
    /// Emulate a project with an unsupported language, and verify that
    /// one warning is emitted because the assembly info file couldn't be generated.
    /// </summary>
    [Fact]
    public async Task AssemblyInfo_NotProducedWithoutCodeDomProvider()
    {
        var propertyGroup = this.testProject.CreatePropertyGroupElement();
        this.testProject.AppendChild(propertyGroup);
        propertyGroup.AddProperty("Language", "NoCodeDOMProviderForThisLanguage");

        this.WriteVersionFile();
        var result = await this.BuildAsync(Targets.GenerateAssemblyVersionInfo, logVerbosity: LoggerVerbosity.Minimal, assertSuccessfulBuild: false);
        Assert.Equal(BuildResultCode.Failure, result.BuildResult.OverallResult);
        string versionCsFilePath = Path.Combine(this.projectDirectory, result.BuildResult.ProjectStateAfterBuild.GetPropertyValue("VersionSourceFile"));
        Assert.False(File.Exists(versionCsFilePath));
        Assert.Single(result.LoggedEvents.OfType<BuildErrorEventArgs>());
    }

    /// <summary>
    /// Emulate a project with an unsupported language, and verify that
    /// no errors are emitted because the target is skipped.
    /// </summary>
    [Fact]
    public async Task AssemblyInfo_Suppressed()
    {
        var propertyGroup = this.testProject.CreatePropertyGroupElement();
        this.testProject.AppendChild(propertyGroup);
        propertyGroup.AddProperty("Language", "NoCodeDOMProviderForThisLanguage");
        propertyGroup.AddProperty(Targets.GenerateAssemblyVersionInfo, "false");

        this.WriteVersionFile();
        var result = await this.BuildAsync(Targets.GenerateAssemblyVersionInfo, logVerbosity: LoggerVerbosity.Minimal);
        string versionCsFilePath = Path.Combine(this.projectDirectory, result.BuildResult.ProjectStateAfterBuild.GetPropertyValue("VersionSourceFile"));
        Assert.False(File.Exists(versionCsFilePath));
        Assert.Empty(result.LoggedEvents.OfType<BuildErrorEventArgs>());
        Assert.Empty(result.LoggedEvents.OfType<BuildWarningEventArgs>());
    }

    /// <summary>
    /// Emulate a project with an unsupported language, and verify that
    /// no errors are emitted because the target is skipped.
    /// </summary>
    [Fact]
    public async Task AssemblyInfo_SuppressedImplicitlyByTargetExt()
    {
        var propertyGroup = this.testProject.CreatePropertyGroupElement();
        this.testProject.InsertAfterChild(propertyGroup, this.testProject.Imports.First()); // insert just after the Common.Targets import.
        propertyGroup.AddProperty("Language", "NoCodeDOMProviderForThisLanguage");
        propertyGroup.AddProperty("TargetExt", ".notdll");

        this.WriteVersionFile();
        var result = await this.BuildAsync(Targets.GenerateAssemblyVersionInfo, logVerbosity: LoggerVerbosity.Minimal);
        string versionCsFilePath = Path.Combine(this.projectDirectory, result.BuildResult.ProjectStateAfterBuild.GetPropertyValue("VersionSourceFile"));
        Assert.False(File.Exists(versionCsFilePath));
        Assert.Empty(result.LoggedEvents.OfType<BuildErrorEventArgs>());
        Assert.Empty(result.LoggedEvents.OfType<BuildWarningEventArgs>());
    }

    protected override GitContext CreateGitContext(string path, string committish = null) => throw new NotImplementedException();

#if !NETCOREAPP
    /// <summary>
    /// Create a native resource .dll and verify that its version
    ///  information is set correctly.
    /// </summary>
    [Fact]
    public async Task NativeVersionInfo_CreateNativeResourceDll()
    {
        this.testProject = this.CreateNativeProjectRootElement(this.projectDirectory, "test.vcxproj");
        this.WriteVersionFile();
        var result = await this.BuildAsync(Targets.Build, logVerbosity: LoggerVerbosity.Minimal);
        Assert.Empty(result.LoggedEvents.OfType<BuildErrorEventArgs>());

        string targetFile = Path.Combine(this.projectDirectory, result.BuildResult.ProjectStateAfterBuild.GetPropertyValue("TargetPath"));
        Assert.True(File.Exists(targetFile));

        var fileInfo = FileVersionInfo.GetVersionInfo(targetFile);
        Assert.Equal("1.2", fileInfo.FileVersion);
        Assert.Equal("1.2.0", fileInfo.ProductVersion);
        Assert.Equal("test", fileInfo.InternalName);
        Assert.Equal("NerdBank", fileInfo.CompanyName);
        Assert.Equal($"Copyright (c) {DateTime.Now.Year}. All rights reserved.", fileInfo.LegalCopyright);
    }
#endif

    private static Version GetExpectedAssemblyVersion(VersionOptions versionOptions, Version version)
    {
        // Function should be very similar to VersionOracle.GetAssemblyVersion()
        var assemblyVersion = (versionOptions?.AssemblyVersion?.Version ?? versionOptions.Version.Version).EnsureNonNegativeComponents();
        var precision = versionOptions?.AssemblyVersion?.Precision ?? VersionOptions.DefaultVersionPrecision;

        assemblyVersion = new System.Version(
            assemblyVersion.Major,
            precision >= VersionOptions.VersionPrecision.Minor ? assemblyVersion.Minor : 0,
            precision >= VersionOptions.VersionPrecision.Build ? version.Build : 0,
            precision >= VersionOptions.VersionPrecision.Revision ? version.Revision : 0);
        return assemblyVersion;
    }

    private static RestoreEnvironmentVariables ApplyEnvironmentVariables(IReadOnlyDictionary<string, string> variables)
    {
        Requires.NotNull(variables, nameof(variables));

        var oldValues = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var variable in variables)
        {
            oldValues[variable.Key] = Environment.GetEnvironmentVariable(variable.Key);
            Environment.SetEnvironmentVariable(variable.Key, variable.Value);
        }

        return new RestoreEnvironmentVariables(oldValues);
    }

    private void AssertStandardProperties(VersionOptions versionOptions, BuildResults buildResult, string relativeProjectDirectory = null)
    {
        int versionHeight = this.GetVersionHeight(relativeProjectDirectory);
        Version idAsVersion = this.GetVersion(relativeProjectDirectory);
        string commitIdShort = this.CommitIdShort;
        Version version = this.GetVersion(relativeProjectDirectory);
        Version assemblyVersion = GetExpectedAssemblyVersion(versionOptions, version);
        var additionalBuildMetadata = from item in buildResult.BuildResult.ProjectStateAfterBuild.GetItems("BuildMetadata")
                                      select item.EvaluatedInclude;
        var expectedBuildMetadata = $"+{commitIdShort}";
        if (additionalBuildMetadata.Any())
        {
            expectedBuildMetadata += "." + string.Join(".", additionalBuildMetadata);
        }

        string expectedBuildMetadataWithoutCommitId = additionalBuildMetadata.Any() ? $"+{string.Join(".", additionalBuildMetadata)}" : string.Empty;

        var optionalFourthComponent = versionOptions.VersionHeightPosition switch
        {
            SemanticVersion.Position.Revision => $".{idAsVersion.Revision}",
            _ => ""
        };

        Assert.Equal($"{version}", buildResult.AssemblyFileVersion);
        Assert.Equal($"{idAsVersion.Major}.{idAsVersion.Minor}.{idAsVersion.Build}{optionalFourthComponent}{versionOptions.Version.Prerelease}{expectedBuildMetadata}", buildResult.AssemblyInformationalVersion);

        // The assembly version property should always have four integer components to it,
        // per bug https://github.com/dotnet/Nerdbank.GitVersioning/issues/26
        Assert.Equal($"{assemblyVersion.Major}.{assemblyVersion.Minor}.{assemblyVersion.Build}.{assemblyVersion.Revision}", buildResult.AssemblyVersion);

        Assert.Equal(idAsVersion.Build.ToString(), buildResult.BuildNumber);
        Assert.Equal($"{version}", buildResult.BuildVersion);
        Assert.Equal($"{idAsVersion.Major}.{idAsVersion.Minor}.{idAsVersion.Build}", buildResult.BuildVersion3Components);
        Assert.Equal(idAsVersion.Build.ToString(), buildResult.BuildVersionNumberComponent);
        Assert.Equal($"{idAsVersion.Major}.{idAsVersion.Minor}.{idAsVersion.Build}", buildResult.BuildVersionSimple);
        Assert.Equal(this.LibGit2Repository.Head.Tip.Id.Sha, buildResult.GitCommitId);
        Assert.Equal(this.LibGit2Repository.Head.Tip.Author.When.UtcTicks.ToString(CultureInfo.InvariantCulture), buildResult.GitCommitDateTicks);
        Assert.Equal(commitIdShort, buildResult.GitCommitIdShort);
        Assert.Equal(versionHeight.ToString(), buildResult.GitVersionHeight);
        Assert.Equal($"{version.Major}.{version.Minor}", buildResult.MajorMinorVersion);
        Assert.Equal(versionOptions.Version.Prerelease, buildResult.PrereleaseVersion);
        Assert.Equal(expectedBuildMetadata, buildResult.SemVerBuildSuffix);

        string GetPkgVersionSuffix(bool useSemVer2)
        {
            string pkgVersionSuffix = buildResult.PublicRelease ? string.Empty : $"-g{commitIdShort}";
            if (useSemVer2)
            {
                pkgVersionSuffix += expectedBuildMetadataWithoutCommitId;
            }

            return pkgVersionSuffix;
        }

        // NuGet is now SemVer 2.0 and will pass additional build metadata if provided
        string nugetPkgVersionSuffix = GetPkgVersionSuffix(useSemVer2: versionOptions?.NuGetPackageVersionOrDefault.SemVer == 2);
        Assert.Equal($"{idAsVersion.Major}.{idAsVersion.Minor}.{idAsVersion.Build}{GetSemVerAppropriatePrereleaseTag(versionOptions)}{nugetPkgVersionSuffix}", buildResult.NuGetPackageVersion);

        // Chocolatey only supports SemVer 1.0
        string chocolateyPkgVersionSuffix = GetPkgVersionSuffix(useSemVer2: false);
        Assert.Equal($"{idAsVersion.Major}.{idAsVersion.Minor}.{idAsVersion.Build}{GetSemVerAppropriatePrereleaseTag(versionOptions)}{chocolateyPkgVersionSuffix}", buildResult.ChocolateyPackageVersion);

        var buildNumberOptions = versionOptions.CloudBuildOrDefault.BuildNumberOrDefault;
        if (buildNumberOptions.EnabledOrDefault)
        {
            var commitIdOptions = buildNumberOptions.IncludeCommitIdOrDefault;
            var buildNumberSemVer = SemanticVersion.Parse(buildResult.CloudBuildNumber);
            bool hasCommitData = commitIdOptions.WhenOrDefault == VersionOptions.CloudBuildNumberCommitWhen.Always
                || (commitIdOptions.WhenOrDefault == VersionOptions.CloudBuildNumberCommitWhen.NonPublicReleaseOnly && !buildResult.PublicRelease);
            Version expectedVersion = hasCommitData && commitIdOptions.WhereOrDefault == VersionOptions.CloudBuildNumberCommitWhere.FourthVersionComponent
                ? idAsVersion
                : new Version(version.Major, version.Minor, version.Build);
            Assert.Equal(expectedVersion, buildNumberSemVer.Version);
            Assert.Equal(buildResult.PrereleaseVersion, buildNumberSemVer.Prerelease);
            string expectedBuildNumberMetadata = hasCommitData && commitIdOptions.WhereOrDefault == VersionOptions.CloudBuildNumberCommitWhere.BuildMetadata
                ? $"+{commitIdShort}"
                : string.Empty;
            if (additionalBuildMetadata.Any())
            {
                expectedBuildNumberMetadata = expectedBuildNumberMetadata.Length == 0
                    ? "+" + string.Join(".", additionalBuildMetadata)
                    : expectedBuildNumberMetadata + "." + string.Join(".", additionalBuildMetadata);
            }

            Assert.Equal(expectedBuildNumberMetadata, buildNumberSemVer.BuildMetadata);
        }
        else
        {
            Assert.Equal(string.Empty, buildResult.CloudBuildNumber);
        }
    }

    private static string GetSemVerAppropriatePrereleaseTag(VersionOptions versionOptions)
    {
        return versionOptions.NuGetPackageVersionOrDefault.SemVer == 1
            ? versionOptions.Version.Prerelease?.Replace('.', '-')
            : versionOptions.Version.Prerelease;
    }

    private async Task<BuildResults> BuildAsync(string target = Targets.GetBuildVersion, LoggerVerbosity logVerbosity = LoggerVerbosity.Detailed, bool assertSuccessfulBuild = true)
    {
        var eventLogger = new MSBuildLogger { Verbosity = LoggerVerbosity.Minimal };
        var loggers = new ILogger[] { eventLogger };
        this.testProject.Save(); // persist generated project on disk for analysis
        this.ApplyGlobalProperties(this.globalProperties);
        var buildResult = await this.buildManager.BuildAsync(
            this.Logger,
            this.projectCollection,
            this.testProject,
            target,
            this.globalProperties,
            logVerbosity,
            loggers);
        var result = new BuildResults(buildResult, eventLogger.LoggedEvents);
        this.Logger.WriteLine(result.ToString());
        if (assertSuccessfulBuild)
        {
            Assert.Equal(BuildResultCode.Success, buildResult.OverallResult);
        }

        return result;
    }

    private void LoadTargetsIntoProjectCollection()
    {
        string prefix = $"{ThisAssembly.RootNamespace}.Targets.";

        var streamNames = from name in Assembly.GetExecutingAssembly().GetManifestResourceNames()
                          where name.StartsWith(prefix, StringComparison.Ordinal)
                          select name;
        foreach (string name in streamNames)
        {
            using (var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(name))
            {
                var targetsFile = ProjectRootElement.Create(XmlReader.Create(stream), this.projectCollection);
                targetsFile.FullPath = Path.Combine(this.RepoPath, name.Substring(prefix.Length));
                targetsFile.Save(); // persist files on disk
            }
        }
    }

    private ProjectRootElement CreateNativeProjectRootElement(string projectDirectory, string projectName)
    {
        using (var reader = XmlReader.Create(Assembly.GetExecutingAssembly().GetManifestResourceStream($"{ThisAssembly.RootNamespace}.test.vcprj")))
        {
            var pre = ProjectRootElement.Create(reader, this.projectCollection);
            pre.FullPath = Path.Combine(projectDirectory, projectName);
            pre.AddImport(Path.Combine(this.RepoPath, GitVersioningTargetsFileName));
            return pre;
        }
    }

    private ProjectRootElement CreateProjectRootElement(string projectDirectory, string projectName)
    {
        using (var reader = XmlReader.Create(Assembly.GetExecutingAssembly().GetManifestResourceStream($"{ThisAssembly.RootNamespace}.test.prj")))
        {
            var pre = ProjectRootElement.Create(reader, this.projectCollection);
            pre.FullPath = Path.Combine(projectDirectory, projectName);
            pre.AddImport(Path.Combine(this.RepoPath, GitVersioningTargetsFileName));
            return pre;
        }
    }

    private void MakeItAVBProject()
    {
        var csharpImport = this.testProject.Imports.Single(i => i.Project.Contains("CSharp"));
        csharpImport.Project = "$(MSBuildToolsPath)/Microsoft.VisualBasic.targets";
        var isVbProperty = this.testProject.Properties.Single(p => p.Name == "IsVB");
        isVbProperty.Value = "true";
    }

    private struct RestoreEnvironmentVariables : IDisposable
    {
        private readonly IReadOnlyDictionary<string, string> applyVariables;

        internal RestoreEnvironmentVariables(IReadOnlyDictionary<string, string> applyVariables)
        {
            this.applyVariables = applyVariables;
        }

        public void Dispose()
        {
            ApplyEnvironmentVariables(this.applyVariables);
        }
    }

    private static class CloudBuild
    {
        public static readonly ImmutableDictionary<string, string> SuppressEnvironment = ImmutableDictionary<string, string>.Empty
            // AppVeyor
            .Add("APPVEYOR", string.Empty)
            .Add("APPVEYOR_REPO_TAG", string.Empty)
            .Add("APPVEYOR_REPO_TAG_NAME", string.Empty)
            .Add("APPVEYOR_PULL_REQUEST_NUMBER", string.Empty)
            // VSTS
            .Add("SYSTEM_TEAMPROJECTID", string.Empty)
            .Add("BUILD_SOURCEBRANCH", string.Empty)
            // Teamcity
            .Add("BUILD_VCS_NUMBER", string.Empty)
            .Add("BUILD_GIT_BRANCH", string.Empty);
        public static readonly ImmutableDictionary<string, string> VSTS = SuppressEnvironment
            .SetItem("SYSTEM_TEAMPROJECTID", "1");
        public static readonly ImmutableDictionary<string, string> AppVeyor = SuppressEnvironment
            .SetItem("APPVEYOR", "True");
        public static readonly ImmutableDictionary<string, string> Teamcity = SuppressEnvironment
            .SetItem("BUILD_VCS_NUMBER", "1");
    }

    private static class Targets
    {
        internal const string Build = "Build";
        internal const string GetBuildVersion = "GetBuildVersion";
        internal const string GetNuGetPackageVersion = "GetNuGetPackageVersion";
        internal const string GenerateAssemblyVersionInfo = "GenerateAssemblyVersionInfo";
        internal const string GenerateNativeVersionInfo = "GenerateNativeVersionInfo";
    }

    private class BuildResults
    {
        internal BuildResults(BuildResult buildResult, IReadOnlyList<BuildEventArgs> loggedEvents)
        {
            Requires.NotNull(buildResult, nameof(buildResult));
            this.BuildResult = buildResult;
            this.LoggedEvents = loggedEvents;
        }

        public BuildResult BuildResult { get; private set; }

        public IReadOnlyList<BuildEventArgs> LoggedEvents { get; private set; }

        public bool PublicRelease => string.Equals("true", this.BuildResult.ProjectStateAfterBuild.GetPropertyValue("PublicRelease"), StringComparison.OrdinalIgnoreCase);
        public string BuildNumber => this.BuildResult.ProjectStateAfterBuild.GetPropertyValue("BuildNumber");
        public string GitCommitId => this.BuildResult.ProjectStateAfterBuild.GetPropertyValue("GitCommitId");
        public string BuildVersion => this.BuildResult.ProjectStateAfterBuild.GetPropertyValue("BuildVersion");
        public string BuildVersionSimple => this.BuildResult.ProjectStateAfterBuild.GetPropertyValue("BuildVersionSimple");
        public string PrereleaseVersion => this.BuildResult.ProjectStateAfterBuild.GetPropertyValue("PrereleaseVersion");
        public string MajorMinorVersion => this.BuildResult.ProjectStateAfterBuild.GetPropertyValue("MajorMinorVersion");
        public string BuildVersionNumberComponent => this.BuildResult.ProjectStateAfterBuild.GetPropertyValue("BuildVersionNumberComponent");
        public string GitCommitIdShort => this.BuildResult.ProjectStateAfterBuild.GetPropertyValue("GitCommitIdShort");
        public string GitCommitDateTicks => this.BuildResult.ProjectStateAfterBuild.GetPropertyValue("GitCommitDateTicks");
        public string GitVersionHeight => this.BuildResult.ProjectStateAfterBuild.GetPropertyValue("GitVersionHeight");
        public string SemVerBuildSuffix => this.BuildResult.ProjectStateAfterBuild.GetPropertyValue("SemVerBuildSuffix");
        public string BuildVersion3Components => this.BuildResult.ProjectStateAfterBuild.GetPropertyValue("BuildVersion3Components");
        public string AssemblyInformationalVersion => this.BuildResult.ProjectStateAfterBuild.GetPropertyValue("AssemblyInformationalVersion");
        public string AssemblyFileVersion => this.BuildResult.ProjectStateAfterBuild.GetPropertyValue("AssemblyFileVersion");
        public string AssemblyVersion => this.BuildResult.ProjectStateAfterBuild.GetPropertyValue("AssemblyVersion");
        public string NuGetPackageVersion => this.BuildResult.ProjectStateAfterBuild.GetPropertyValue("NuGetPackageVersion");
        public string ChocolateyPackageVersion => this.BuildResult.ProjectStateAfterBuild.GetPropertyValue("ChocolateyPackageVersion");
        public string CloudBuildNumber => this.BuildResult.ProjectStateAfterBuild.GetPropertyValue("CloudBuildNumber");
        public string AssemblyName => this.BuildResult.ProjectStateAfterBuild.GetPropertyValue("AssemblyName");
        public string AssemblyTitle => this.BuildResult.ProjectStateAfterBuild.GetPropertyValue("AssemblyTitle");
        public string AssemblyProduct => this.BuildResult.ProjectStateAfterBuild.GetPropertyValue("AssemblyProduct");
        public string AssemblyCompany => this.BuildResult.ProjectStateAfterBuild.GetPropertyValue("AssemblyCompany");
        public string AssemblyCopyright => this.BuildResult.ProjectStateAfterBuild.GetPropertyValue("AssemblyCopyright");
        public string AssemblyConfiguration => this.BuildResult.ProjectStateAfterBuild.GetPropertyValue("Configuration");
        public string RootNamespace => this.BuildResult.ProjectStateAfterBuild.GetPropertyValue("RootNamespace");

        public string GitBuildVersion => this.BuildResult.ProjectStateAfterBuild.GetPropertyValue("GitBuildVersion");
        public string GitBuildVersionSimple => this.BuildResult.ProjectStateAfterBuild.GetPropertyValue("GitBuildVersionSimple");
        public string GitAssemblyInformationalVersion => this.BuildResult.ProjectStateAfterBuild.GetPropertyValue("GitAssemblyInformationalVersion");

        // Just a sampling of other properties optionally set in cloud build.
        public string NBGV_GitCommitIdShort => this.BuildResult.ProjectStateAfterBuild.GetPropertyValue("NBGV_GitCommitIdShort");
        public string NBGV_NuGetPackageVersion => this.BuildResult.ProjectStateAfterBuild.GetPropertyValue("NBGV_NuGetPackageVersion");

        public override string ToString()
        {
            var sb = new StringBuilder();

            foreach (var property in this.GetType().GetRuntimeProperties().OrderBy(p => p.Name, StringComparer.OrdinalIgnoreCase))
            {
                if (property.DeclaringType == this.GetType() && property.Name != nameof(this.BuildResult))
                {
                    sb.AppendLine($"{property.Name} = {property.GetValue(this)}");
                }
            }

            return sb.ToString();
        }
    }

    private class MSBuildLogger : ILogger
    {
        public string Parameters { get; set; }

        public LoggerVerbosity Verbosity { get; set; }

        public List<BuildEventArgs> LoggedEvents { get; } = new List<BuildEventArgs>();

        public void Initialize(IEventSource eventSource)
        {
            eventSource.AnyEventRaised += this.EventSource_AnyEventRaised;
        }

        public void Shutdown()
        {
        }

        private void EventSource_AnyEventRaised(object sender, BuildEventArgs e)
        {
            this.LoggedEvents.Add(e);
        }
    }
}
