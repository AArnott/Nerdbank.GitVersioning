using System;
using System.IO;
using System.Linq;
using LibGit2Sharp;
using Nerdbank.GitVersioning;
using Xunit;
using Xunit.Abstractions;
using Version = System.Version;

[Trait("Engine", "Managed")]
public class VersionOracleManagedTests : VersionOracleTests
{
    public VersionOracleManagedTests(ITestOutputHelper logger)
        : base(logger)
    {
    }

    protected override GitContext CreateGitContext(string path, string committish = null)
        => GitContext.Create(path, committish, writable: false);
}

[Trait("Engine", "LibGit2")]
public class VersionOracleLibGit2Tests : VersionOracleTests
{
    public VersionOracleLibGit2Tests(ITestOutputHelper logger)
        : base(logger)
    {
    }

    protected override GitContext CreateGitContext(string path, string committish = null)
        => GitContext.Create(path, committish, writable: true);
}

public abstract class VersionOracleTests : RepoTestBase
{
    public VersionOracleTests(ITestOutputHelper logger)
        : base(logger)
    {
    }

    private string CommitIdShort => this.Context.GitCommitId?.Substring(0, VersionOptions.DefaultGitCommitIdShortFixedLength);

    [Fact]
    public void NotRepo()
    {
        // Seems safe to assume a temporary path is not a Git directory.
        var context = this.CreateGitContext(Path.GetTempPath());
        var oracle = new VersionOracle(context);
        Assert.Equal(0, oracle.VersionHeight);
    }

    [Fact]
    public void Submodule_RecognizedWithCorrectVersion()
    {
        using (var expandedRepo = TestUtilities.ExtractRepoArchive("submodules"))
        {
            this.Context = this.CreateGitContext(Path.Combine(expandedRepo.RepoPath, "a"));
            var oracleA = new VersionOracle(this.Context);
            Assert.Equal("1.3.1", oracleA.SimpleVersion.ToString());
            Assert.Equal("e238b03e75", oracleA.GitCommitIdShort);

            this.Context = this.CreateGitContext(Path.Combine(expandedRepo.RepoPath, "b", "projB"));
            var oracleB = new VersionOracle(this.Context);
            Assert.Equal("2.5.2", oracleB.SimpleVersion.ToString());
            Assert.Equal("3ea7f010c3", oracleB.GitCommitIdShort);
        }
    }

    [Fact]
    public void Informational_version_has_four_components_when_three_component_version_is_used()
    {
        var versionOptions = new VersionOptions { Version = SemanticVersion.Parse("1.2.3") };

        this.WriteVersionFile(versionOptions);
        this.InitializeSourceControl();
        this.AddCommits(20);
        var oracle = new VersionOracle(this.Context);
        Assert.StartsWith("1.2.3.21+", oracle.AssemblyInformationalVersion);
    }

    [Fact]
    public void Informational_version_has_three_components_when_two_component_version_is_used()
    {
        var versionOptions = new VersionOptions { Version = SemanticVersion.Parse("1.2") };

        this.WriteVersionFile(versionOptions);
        this.InitializeSourceControl();
        this.AddCommits(20);
        var oracle = new VersionOracle(this.Context);
        Assert.StartsWith("1.2.21+", oracle.AssemblyInformationalVersion);
    }

    [Fact]
    public void MajorMinorPrereleaseBuildMetadata()
    {
        VersionOptions workingCopyVersion = new VersionOptions
        {
            Version = SemanticVersion.Parse("7.8-beta.3+metadata.4"),
        };
        this.WriteVersionFile(workingCopyVersion);
        this.InitializeSourceControl();
        var oracle = new VersionOracle(this.Context);
        Assert.Equal("7.8", oracle.MajorMinorVersion.ToString());
        Assert.Equal(oracle.VersionHeight, oracle.BuildNumber);

        Assert.Equal("-beta.3", oracle.PrereleaseVersion);
        ////Assert.Equal("+metadata.4", oracle.BuildMetadataFragment);

        Assert.Equal(1, oracle.VersionHeight);
        Assert.Equal(0, oracle.VersionHeightOffset);
    }

    [Fact]
    public void MajorMinorBuildPrereleaseBuildMetadata()
    {
        VersionOptions workingCopyVersion = new VersionOptions
        {
            Version = SemanticVersion.Parse("7.8.9-beta.3+metadata.4"),
        };
        this.WriteVersionFile(workingCopyVersion);
        this.InitializeSourceControl();
        var oracle = new VersionOracle(this.Context);
        Assert.Equal("7.8", oracle.MajorMinorVersion.ToString());
        Assert.Equal(9, oracle.BuildNumber);
        Assert.Equal(oracle.VersionHeight + oracle.VersionHeightOffset, oracle.Version.Revision);

        Assert.Equal("-beta.3", oracle.PrereleaseVersion);
        ////Assert.Equal("+metadata.4", oracle.BuildMetadataFragment);

        Assert.Equal(1, oracle.VersionHeight);
        Assert.Equal(0, oracle.VersionHeightOffset);
    }

    [Theory]
    [InlineData("0.1", "0.2")]
    [InlineData("0.1.0+{height}", "0.1.5+{height}")]
    [InlineData("0.1.5-alpha0.{height}", "0.1.5-alpha1.{height}")]
    [InlineData("0.1.5-beta.{height}", "0.1.5-beta1.{height}")]
    [InlineData("0.1.5-alpha.{height}", "0.1.5-beta.{height}")]
    [InlineData("0.1.5-alpha.1.{height}", "0.1.5-beta.1.{height}")]
    public void VersionHeightResetsWithVersionSpecChanges(string initial, string next)
    {
        var options = new VersionOptions
        {
            Version = SemanticVersion.Parse(initial),
        };
        this.WriteVersionFile(options);
        this.InitializeSourceControl();
        this.AddCommits(10);

        var oracle = new VersionOracle(this.Context);
        Assert.Equal(11, oracle.VersionHeight);

        options.Version = SemanticVersion.Parse(next);

        this.WriteVersionFile(options);
        this.SetContextToHead();
        oracle = new VersionOracle(this.Context);
        Assert.Equal(1, oracle.VersionHeight);

        if (this.Context is Nerdbank.GitVersioning.LibGit2.LibGit2Context libgit2Context)
        {
            foreach (var commit in libgit2Context.Repository.Head.Commits)
            {
                var versionFromId = this.GetVersion(committish: commit.Sha);
                Assert.Contains(commit, Nerdbank.GitVersioning.LibGit2.LibGit2GitExtensions.GetCommitsFromVersion(libgit2Context, versionFromId));
            }
        }
    }

    [Fact]
    public void HeightInPrerelease()
    {
        VersionOptions workingCopyVersion = new VersionOptions
        {
            Version = SemanticVersion.Parse("7.8.9-beta.{height}.foo"),
            VersionHeightOffset = 2,
        };
        this.WriteVersionFile(workingCopyVersion);
        this.InitializeSourceControl();
        var oracle = new VersionOracle(this.Context);
        Assert.Equal("7.8", oracle.MajorMinorVersion.ToString());
        Assert.Equal(9, oracle.BuildNumber);
        Assert.Equal(-1, oracle.Version.Revision);

        Assert.Equal("-beta." + (oracle.VersionHeight + oracle.VersionHeightOffset) + ".foo", oracle.PrereleaseVersion);

        Assert.Equal(1, oracle.VersionHeight);
        Assert.Equal(2, oracle.VersionHeightOffset);
    }

    [Fact(Skip = "Build metadata not yet retained from version.json")]
    public void HeightInBuildMetadata()
    {
        VersionOptions workingCopyVersion = new VersionOptions
        {
            Version = SemanticVersion.Parse("7.8.9-beta+another.{height}.foo"),
            VersionHeightOffset = 2,
        };
        this.WriteVersionFile(workingCopyVersion);
        this.InitializeSourceControl();
        var oracle = new VersionOracle(this.Context);
        Assert.Equal("7.8", oracle.MajorMinorVersion.ToString());
        Assert.Equal(9, oracle.BuildNumber);
        Assert.Equal(oracle.VersionHeight + oracle.VersionHeightOffset, oracle.Version.Revision);

        Assert.Equal("-beta", oracle.PrereleaseVersion);
        Assert.Equal("+another." + (oracle.VersionHeight + oracle.VersionHeightOffset) + ".foo", oracle.BuildMetadataFragment);

        Assert.Equal(1, oracle.VersionHeight);
        Assert.Equal(2, oracle.VersionHeightOffset);
    }

    [Theory]
    [InlineData("7.8.9-foo.25", "7.8.9-foo-0025")]
    [InlineData("7.8.9-foo.25s", "7.8.9-foo-25s")]
    [InlineData("7.8.9-foo.s25", "7.8.9-foo-s25")]
    [InlineData("7.8.9-foo.25.bar-24.13-11", "7.8.9-foo-0025-bar-24-13-11")]
    [InlineData("7.8.9-25.bar.baz-25", "7.8.9-0025-bar-baz-25")]
    [InlineData("7.8.9-foo.5.bar.1.43.baz", "7.8.9-foo-0005-bar-0001-0043-baz")]
    public void SemVer1PrereleaseConversion(string semVer2, string semVer1)
    {
        VersionOptions workingCopyVersion = new VersionOptions
        {
            Version = SemanticVersion.Parse(semVer2),
            VersionHeightOffset = 2,
        };
        this.WriteVersionFile(workingCopyVersion);
        this.InitializeSourceControl();
        var oracle = new VersionOracle(this.Context);
        oracle.PublicRelease = true;
        Assert.Equal(semVer1, oracle.SemVer1);
    }

    [Fact]
    public void SemVer1PrereleaseConversionPadding()
    {
        VersionOptions workingCopyVersion = new VersionOptions
        {
            Version = SemanticVersion.Parse("7.8.9-foo.25"),
            VersionHeightOffset = 2,
            SemVer1NumericIdentifierPadding = 3,
        };
        this.WriteVersionFile(workingCopyVersion);
        this.InitializeSourceControl();
        var oracle = new VersionOracle(this.Context);
        oracle.PublicRelease = true;
        Assert.Equal("7.8.9-foo-025", oracle.SemVer1);
    }

    [Fact]
    public void SemVerStableNonPublicVersion()
    {
        var workingCopyVersion = new VersionOptions
        {
            Version = SemanticVersion.Parse("2.3"),
        };
        this.WriteVersionFile(workingCopyVersion);
        this.InitializeSourceControl();
        var oracle = new VersionOracle(this.Context);
        oracle.PublicRelease = false;
        Assert.Matches(@"^2.3.1-[^g]{10}$", oracle.SemVer1);
        Assert.Matches(@"^2.3.1-g[a-f0-9]{10}$", oracle.SemVer2);
        Assert.Matches(@"^2.3.1-g[a-f0-9]{10}$", oracle.NuGetPackageVersion);
        Assert.Matches(@"^2.3.1-g[a-f0-9]{10}$", oracle.ChocolateyPackageVersion);
    }

    [Fact]
    public void SemVerStableNonPublicVersionShortened()
    {
        var workingCopyVersion = new VersionOptions
        {
            Version = SemanticVersion.Parse("2.3"),
            GitCommitIdShortFixedLength = 7
        };
        this.WriteVersionFile(workingCopyVersion);
        this.InitializeSourceControl();
        var oracle = new VersionOracle(this.Context);
        oracle.PublicRelease = false;
        Assert.Matches(@"^2.3.1-[^g]{7}$", oracle.SemVer1);
        Assert.Matches(@"^2.3.1-g[a-f0-9]{7}$", oracle.SemVer2);
        Assert.Matches(@"^2.3.1-g[a-f0-9]{7}$", oracle.NuGetPackageVersion);
        Assert.Matches(@"^2.3.1-g[a-f0-9]{7}$", oracle.ChocolateyPackageVersion);
    }

    [Fact]
    public void DefaultNuGetPackageVersionIsSemVer1PublicRelease()
    {
        VersionOptions workingCopyVersion = new VersionOptions
        {
            Version = SemanticVersion.Parse("7.8.9-foo.25"),
            SemVer1NumericIdentifierPadding = 2,
        };
        this.WriteVersionFile(workingCopyVersion);
        this.InitializeSourceControl();
        var oracle = new VersionOracle(this.Context);
        oracle.PublicRelease = true;
        Assert.Equal($"7.8.9-foo-25", oracle.NuGetPackageVersion);
    }

    [Fact]
    public void DefaultNuGetPackageVersionIsSemVer1NonPublicRelease()
    {
        VersionOptions workingCopyVersion = new VersionOptions
        {
            Version = SemanticVersion.Parse("7.8.9-foo.25"),
            SemVer1NumericIdentifierPadding = 2,
        };
        this.WriteVersionFile(workingCopyVersion);
        this.InitializeSourceControl();
        var oracle = new VersionOracle(this.Context);
        oracle.PublicRelease = false;
        Assert.Equal($"7.8.9-foo-25-g{this.CommitIdShort}", oracle.NuGetPackageVersion);
    }

    [Fact]
    public void NpmPackageVersionIsSemVer2()
    {
        VersionOptions workingCopyVersion = new VersionOptions
        {
            Version = SemanticVersion.Parse("7.8.9-foo.25"),
            SemVer1NumericIdentifierPadding = 2
        };
        this.WriteVersionFile(workingCopyVersion);
        this.InitializeSourceControl();
        var oracle = new VersionOracle(this.Context);
        oracle.PublicRelease = true;
        Assert.Equal("7.8.9-foo.25", oracle.NpmPackageVersion);
    }

    [Fact]
    public void CanSetSemVer2ForNuGetPackageVersionPublicRelease()
    {
        VersionOptions workingCopyVersion = new VersionOptions
        {
            Version = SemanticVersion.Parse("7.8.9-foo.25"),
            NuGetPackageVersion = new VersionOptions.NuGetPackageVersionOptions
            {
                SemVer = 2,
            }
        };
        this.WriteVersionFile(workingCopyVersion);
        this.InitializeSourceControl();
        var oracle = new VersionOracle(this.Context);
        oracle.PublicRelease = true;
        Assert.Equal($"7.8.9-foo.25", oracle.NuGetPackageVersion);
    }

    [Fact]
    public void CanSetSemVer2ForNuGetPackageVersionNonPublicRelease()
    {
        VersionOptions workingCopyVersion = new VersionOptions
        {
            Version = SemanticVersion.Parse("7.8.9-foo.25"),
            NuGetPackageVersion = new VersionOptions.NuGetPackageVersionOptions
            {
                SemVer = 2,
            }
        };
        this.WriteVersionFile(workingCopyVersion);
        this.InitializeSourceControl();
        var oracle = new VersionOracle(this.Context);
        oracle.PublicRelease = false;
        Assert.Equal($"7.8.9-foo.25.g{this.CommitIdShort}", oracle.NuGetPackageVersion);
    }

    [Fact]
    public void CanSetGitCommitIdPrefixNonPublicRelease()
    {
        VersionOptions workingCopyVersion = new VersionOptions
        {
            Version = SemanticVersion.Parse("7.8.9-foo.25"),
            NuGetPackageVersion = new VersionOptions.NuGetPackageVersionOptions
            {
                SemVer = 2,
            },
            GitCommitIdPrefix = "git",
        };
        this.WriteVersionFile(workingCopyVersion);
        this.InitializeSourceControl();
        var oracle = new VersionOracle(this.Context);
        oracle.PublicRelease = false;
        Assert.Equal($"7.8.9-foo.25.git{this.CommitIdShort}", oracle.NuGetPackageVersion);
    }

    [Fact]
    public void CanUseGitProjectRelativePathWithGitRepoRoot()
    {
        VersionOptions rootVersion = new VersionOptions
        {
            Version = SemanticVersion.Parse("1.1"),
        };

        VersionOptions projectVersion = new VersionOptions
        {
            Version = SemanticVersion.Parse("2.2"),
        };

        string childProjectRelativeDir = "ChildProject1";
        string childProjectAbsoluteDir = Path.Combine(this.RepoPath, childProjectRelativeDir);
        this.WriteVersionFile(rootVersion);
        this.WriteVersionFile(projectVersion, childProjectRelativeDir);

        this.InitializeSourceControl();

        // Check Root Version. Root version will be used
        var oracle = new VersionOracle(this.Context);
        Assert.Equal("1.1", oracle.MajorMinorVersion.ToString());

        // Check ChildProject with projectRelativeDir, with version file. Child project version will be used.
        this.Context.RepoRelativeProjectDirectory = childProjectRelativeDir;
        oracle = new VersionOracle(this.Context);
        Assert.Equal("2.2", oracle.MajorMinorVersion.ToString());

        // Check ChildProject withOUT Version file. Root version will be used.
        this.Context.RepoRelativeProjectDirectory = "otherChildProject";
        oracle = new VersionOracle(this.Context);
        Assert.Equal("1.1", oracle.MajorMinorVersion.ToString());
    }

    [Fact]
    public void VersionJsonWithoutVersion()
    {
        File.WriteAllText(Path.Combine(this.RepoPath, VersionFile.JsonFileName), "{}");
        this.InitializeSourceControl();
        var oracle = new VersionOracle(this.Context);
        Assert.Equal(0, oracle.Version.Major);
        Assert.Equal(0, oracle.Version.Minor);
    }

    [Fact]
    public void VersionJsonWithSingleIntegerForVersion()
    {
        File.WriteAllText(Path.Combine(this.RepoPath, VersionFile.JsonFileName), @"{""version"":""3""}");
        this.InitializeSourceControl();
        var ex = Assert.Throws<FormatException>(() => new VersionOracle(this.Context));
        Assert.Contains(this.Context.GitCommitId, ex.Message);
        Assert.Contains("\"3\"", ex.InnerException.Message);
        this.Logger.WriteLine(ex.ToString());
    }

    [Theory, CombinatorialData]
    public void Worktree_Support(bool detachedHead)
    {
        var workingCopyVersion = new VersionOptions
        {
            Version = SemanticVersion.Parse("2.3"),
        };
        this.WriteVersionFile(workingCopyVersion);
        this.InitializeSourceControl();
        var oracleOriginal = new VersionOracle(this.Context);
        this.AddCommits();

        string workTreePath = this.CreateDirectoryForNewRepo();
        Directory.Delete(workTreePath);
        if (detachedHead)
        {
            this.LibGit2Repository.Worktrees.Add("HEAD~1", "myworktree", workTreePath, isLocked: false);
        }
        else
        {
            this.LibGit2Repository.Branches.Add("wtbranch", "HEAD~1");
            this.LibGit2Repository.Worktrees.Add("wtbranch", "myworktree", workTreePath, isLocked: false);
        }

        var context = this.CreateGitContext(workTreePath);
        var oracleWorkTree = new VersionOracle(context);
        Assert.Equal(oracleOriginal.Version, oracleWorkTree.Version);

        Assert.True(context.TrySelectCommit("HEAD"));
        Assert.True(context.TrySelectCommit(this.LibGit2Repository.Head.Tip.Sha));
    }

    [Fact]
    public void GetVersionHeight_Test()
    {
        this.InitializeSourceControl();

        var first = this.LibGit2Repository.Commit("First", this.Signer, this.Signer, new CommitOptions { AllowEmptyCommit = true });
        var second = this.LibGit2Repository.Commit("Second", this.Signer, this.Signer, new CommitOptions { AllowEmptyCommit = true });
        this.WriteVersionFile();
        var third = this.LibGit2Repository.Commit("Third", this.Signer, this.Signer, new CommitOptions { AllowEmptyCommit = true });
        Assert.Equal(2, this.GetVersionHeight());
    }

    [Fact]
    public void GetVersionHeight_VersionJsonHasUnrelatedHistory()
    {
        this.InitializeSourceControl();

        // Emulate a repo that used version.json for something else.
        string versionJsonPath = Path.Combine(this.RepoPath, "version.json");
        File.WriteAllText(versionJsonPath, @"{ ""unrelated"": false }");
        Assert.Equal(0, this.GetVersionHeight()); // exercise code that handles the file not yet checked in.
        Commands.Stage(this.LibGit2Repository, versionJsonPath);
        this.LibGit2Repository.Commit("Add unrelated version.json file.", this.Signer, this.Signer);
        Assert.Equal(0, this.GetVersionHeight()); // exercise code that handles a checked in file.

        // And now the repo has decided to use this package.
        this.WriteVersionFile();

        Assert.Equal(1, this.GetVersionHeight());

        // Also emulate case of where the related version.json was just changed to conform,
        // but not yet checked in.
        this.LibGit2Repository.Reset(ResetMode.Mixed, this.LibGit2Repository.Head.Tip.Parents.Single());
        Assert.Equal(0, this.GetVersionHeight());
    }

    [Fact]
    public void GetVersionHeight_VersionJsonHasParsingErrorsInHistory()
    {
        this.InitializeSourceControl();
        this.WriteVersionFile();
        Assert.Equal(1, this.GetVersionHeight());

        // Now introduce a parsing error.
        string versionJsonPath = Path.Combine(this.RepoPath, "version.json");
        File.WriteAllText(versionJsonPath, @"{ ""version"": ""1.0"""); // no closing curly brace for parsing error
        Assert.Equal(0, this.GetVersionHeight());
        Commands.Stage(this.LibGit2Repository, versionJsonPath);
        this.LibGit2Repository.Commit("Add broken version.json file.", this.Signer, this.Signer);
        Assert.Equal(0, this.GetVersionHeight());

        // Now fix it.
        this.WriteVersionFile();
        Assert.Equal(1, this.GetVersionHeight());

        // And emulate fixing it without having checked in yet.
        this.LibGit2Repository.Reset(ResetMode.Mixed, this.LibGit2Repository.Head.Tip.Parents.Single());
        Assert.Equal(0, this.GetVersionHeight());
    }

    [Fact]
    public void GetVersionHeight_IntroducingFiltersIncrementsHeight()
    {
        this.InitializeSourceControl();

        string relativeDirectory = "some-sub-dir";

        this.WriteVersionFile(relativeDirectory: relativeDirectory);
        Assert.Equal(1, this.GetVersionHeight(relativeDirectory));

        var versionData = VersionOptions.FromVersion(new Version("1.2"));
        versionData.PathFilters = new[] { new FilterPath("./", relativeDirectory) };
        this.WriteVersionFile(versionData, relativeDirectory);
        Assert.Equal(2, this.GetVersionHeight(relativeDirectory));
    }

    [Theory]
    [InlineData("./")]
    [InlineData("../some-sub-dir")]
    [InlineData("/some-sub-dir")]
    [InlineData(":/some-sub-dir")]
    public void GetVersionHeight_IncludeFilter(string includeFilter)
    {
        this.InitializeSourceControl();

        string relativeDirectory = "some-sub-dir";

        var versionData = VersionOptions.FromVersion(new Version("1.2"));
        versionData.PathFilters = new[] { new FilterPath(includeFilter, relativeDirectory) };
        this.WriteVersionFile(versionData, relativeDirectory);
        Assert.Equal(1, this.GetVersionHeight(relativeDirectory));

        // Expect commit outside of project tree to not affect version height
        var otherFilePath = Path.Combine(this.RepoPath, "my-file.txt");
        File.WriteAllText(otherFilePath, "hello");
        Commands.Stage(this.LibGit2Repository, otherFilePath);
        this.LibGit2Repository.Commit("Add other file outside of project root", this.Signer, this.Signer);
        Assert.Equal(1, this.GetVersionHeight(relativeDirectory));

        // Expect commit inside project tree to affect version height
        var containedFilePath = Path.Combine(this.RepoPath, relativeDirectory, "another-file.txt");
        File.WriteAllText(containedFilePath, "hello");
        Commands.Stage(this.LibGit2Repository, containedFilePath);
        this.LibGit2Repository.Commit("Add file within project root", this.Signer, this.Signer);
        Assert.Equal(2, this.GetVersionHeight(relativeDirectory));
    }

    [Fact]
    public void GetVersionHeight_IncludeExcludeFilter()
    {
        this.InitializeSourceControl();

        string relativeDirectory = "some-sub-dir";

        var versionData = VersionOptions.FromVersion(new Version("1.2"));
        versionData.PathFilters = new[]
        {
            new FilterPath("./", relativeDirectory),
            new FilterPath(":^/some-sub-dir/ignore.txt", relativeDirectory),
            new FilterPath(":^excluded-dir", relativeDirectory)
        };
        this.WriteVersionFile(versionData, relativeDirectory);
        Assert.Equal(1, this.GetVersionHeight(relativeDirectory));

        // Commit touching excluded path does not affect version height
        var ignoredFilePath = Path.Combine(this.RepoPath, relativeDirectory, "ignore.txt");
        File.WriteAllText(ignoredFilePath, "hello");
        Commands.Stage(this.LibGit2Repository, ignoredFilePath);
        this.LibGit2Repository.Commit("Add excluded file", this.Signer, this.Signer);
        Assert.Equal(1, this.GetVersionHeight(relativeDirectory));

        // Commit touching both excluded and included path does affect height
        var includedFilePath = Path.Combine(this.RepoPath, relativeDirectory, "another-file.txt");
        File.WriteAllText(includedFilePath, "hello");
        File.WriteAllText(ignoredFilePath, "changed");
        Commands.Stage(this.LibGit2Repository, includedFilePath);
        Commands.Stage(this.LibGit2Repository, ignoredFilePath);
        this.LibGit2Repository.Commit("Change both excluded and included file", this.Signer, this.Signer);
        Assert.Equal(2, this.GetVersionHeight(relativeDirectory));

        // Commit touching excluded directory does not affect version height
        var fileInExcludedDirPath = Path.Combine(this.RepoPath, relativeDirectory, "excluded-dir", "ignore.txt");
        Directory.CreateDirectory(Path.GetDirectoryName(fileInExcludedDirPath));
        File.WriteAllText(fileInExcludedDirPath, "hello");
        Commands.Stage(this.LibGit2Repository, fileInExcludedDirPath);
        this.LibGit2Repository.Commit("Add file to excluded dir", this.Signer, this.Signer);
        Assert.Equal(2, this.GetVersionHeight(relativeDirectory));
    }

    [Fact]
    public void GetVersionHeight_IncludeExcludeFilter_NoProjectDirectory()
    {
        this.InitializeSourceControl();

        var versionData = VersionOptions.FromVersion(new Version("1.2"));
        versionData.PathFilters = new[]
        {
            new FilterPath("./", "."),
            new FilterPath(":^/some-sub-dir/ignore.txt", "."),
            new FilterPath(":^/excluded-dir", ".")
        };
        this.WriteVersionFile(versionData);
        Assert.Equal(1, this.GetVersionHeight());

        // Commit touching excluded path does not affect version height
        var ignoredFilePath = Path.Combine(this.RepoPath, "some-sub-dir", "ignore.txt");
        Directory.CreateDirectory(Path.GetDirectoryName(ignoredFilePath));
        File.WriteAllText(ignoredFilePath, "hello");
        Commands.Stage(this.LibGit2Repository, ignoredFilePath);
        this.LibGit2Repository.Commit("Add excluded file", this.Signer, this.Signer);
        Assert.Equal(1, this.GetVersionHeight());

        // Commit touching both excluded and included path does affect height
        var includedFilePath = Path.Combine(this.RepoPath, "some-sub-dir", "another-file.txt");
        File.WriteAllText(includedFilePath, "hello");
        File.WriteAllText(ignoredFilePath, "changed");
        Commands.Stage(this.LibGit2Repository, includedFilePath);
        Commands.Stage(this.LibGit2Repository, ignoredFilePath);
        this.LibGit2Repository.Commit("Change both excluded and included file", this.Signer, this.Signer);
        Assert.Equal(2, this.GetVersionHeight());

        // Commit touching excluded directory does not affect version height
        var fileInExcludedDirPath = Path.Combine(this.RepoPath, "excluded-dir", "ignore.txt");
        Directory.CreateDirectory(Path.GetDirectoryName(fileInExcludedDirPath));
        File.WriteAllText(fileInExcludedDirPath, "hello");
        Commands.Stage(this.LibGit2Repository, fileInExcludedDirPath);
        this.LibGit2Repository.Commit("Add file to excluded dir", this.Signer, this.Signer);
        Assert.Equal(2, this.GetVersionHeight());
    }

    [Theory]
    [InlineData(":^/excluded-dir")]
    [InlineData(":^../excluded-dir")]
    public void GetVersionHeight_AddingExcludeDoesNotLowerHeight(string excludePathFilter)
    {
        this.InitializeSourceControl();

        string relativeDirectory = "some-sub-dir";

        var versionData = VersionOptions.FromVersion(new Version("1.2"));
        this.WriteVersionFile(versionData, relativeDirectory);
        Assert.Equal(1, this.GetVersionHeight(relativeDirectory));

        // Commit a file which will later be ignored
        var ignoredFilePath = Path.Combine(this.RepoPath, "excluded-dir", "ignore.txt");
        Directory.CreateDirectory(Path.GetDirectoryName(ignoredFilePath));
        File.WriteAllText(ignoredFilePath, "hello");
        Commands.Stage(this.LibGit2Repository, ignoredFilePath);
        this.LibGit2Repository.Commit("Add file which will later be excluded", this.Signer, this.Signer);
        Assert.Equal(2, this.GetVersionHeight(relativeDirectory));

        versionData.PathFilters = new[] { new FilterPath(excludePathFilter, relativeDirectory), };
        this.WriteVersionFile(versionData, relativeDirectory);
        Assert.Equal(3, this.GetVersionHeight(relativeDirectory));

        // Committing a change to an ignored file does not increment the version height
        File.WriteAllText(ignoredFilePath, "changed");
        Commands.Stage(this.LibGit2Repository, ignoredFilePath);
        this.LibGit2Repository.Commit("Change now excluded file", this.Signer, this.Signer);
        Assert.Equal(3, this.GetVersionHeight(relativeDirectory));
    }

    [Fact]
    public void GetVersionHeight_IncludeRoot()
    {
        this.InitializeSourceControl();

        string relativeDirectory = "some-sub-dir";

        var versionData = VersionOptions.FromVersion(new Version("1.2"));
        versionData.PathFilters = new[] { new FilterPath(":/", relativeDirectory) };
        this.WriteVersionFile(versionData, relativeDirectory);
        Assert.Equal(1, this.GetVersionHeight(relativeDirectory));

        // Expect commit outside of project tree to affect version height
        var otherFilePath = Path.Combine(this.RepoPath, "my-file.txt");
        File.WriteAllText(otherFilePath, "hello");
        Commands.Stage(this.LibGit2Repository, otherFilePath);
        this.LibGit2Repository.Commit("Add other file outside of project root", this.Signer, this.Signer);
        Assert.Equal(2, this.GetVersionHeight(relativeDirectory));

        // Expect commit inside project tree to affect version height
        var containedFilePath = Path.Combine(this.RepoPath, relativeDirectory, "another-file.txt");
        File.WriteAllText(containedFilePath, "hello");
        Commands.Stage(this.LibGit2Repository, containedFilePath);
        this.LibGit2Repository.Commit("Add file within project root", this.Signer, this.Signer);
        Assert.Equal(3, this.GetVersionHeight(relativeDirectory));
    }

    [Fact]
    public void GetVersionHeight_IncludeRootExcludeSome()
    {
        this.InitializeSourceControl();

        string relativeDirectory = "some-sub-dir";

        var versionData = VersionOptions.FromVersion(new Version("1.2"));
        versionData.PathFilters = new[]
        {
            new FilterPath(":/", relativeDirectory),
            new FilterPath(":^/excluded-dir", relativeDirectory),
        };
        this.WriteVersionFile(versionData, relativeDirectory);
        Assert.Equal(1, this.GetVersionHeight(relativeDirectory));

        // Expect commit in an excluded directory to not affect version height
        var ignoredFilePath = Path.Combine(this.RepoPath, "excluded-dir", "my-file.txt");
        Directory.CreateDirectory(Path.GetDirectoryName(ignoredFilePath));
        File.WriteAllText(ignoredFilePath, "hello");
        Commands.Stage(this.LibGit2Repository, ignoredFilePath);
        this.LibGit2Repository.Commit("Add other file to excluded directory", this.Signer, this.Signer);
        Assert.Equal(1, this.GetVersionHeight(relativeDirectory));

        // Expect commit within another directory to affect version height
        var otherFilePath = Path.Combine(this.RepoPath, "another-dir", "another-file.txt");
        Directory.CreateDirectory(Path.GetDirectoryName(otherFilePath));
        File.WriteAllText(otherFilePath, "hello");
        Commands.Stage(this.LibGit2Repository, otherFilePath);
        this.LibGit2Repository.Commit("Add file within project root", this.Signer, this.Signer);
        Assert.Equal(2, this.GetVersionHeight(relativeDirectory));
    }

    [Fact]
    public void GetVersion_PathFilterInTwoDeepSubDirAndVersionBump()
    {
        this.InitializeSourceControl();

        const string relativeDirectory = "src/lib";
        var versionOptions = new VersionOptions
        {
            Version = new SemanticVersion("1.1"),
            PathFilters = new FilterPath[]
            {
                new FilterPath(".", relativeDirectory),
            },
        };
        this.WriteVersionFile(versionOptions, relativeDirectory);
        Assert.Equal(1, this.GetVersionHeight(relativeDirectory));

        versionOptions.Version = new SemanticVersion("1.2");
        this.WriteVersionFile(versionOptions, relativeDirectory);
        Assert.Equal(1, this.GetVersionHeight(relativeDirectory));
    }

    [Fact]
    public void GetVersionHeight_ProjectDirectoryDifferentToVersionJsonDirectory()
    {
        this.InitializeSourceControl();

        string relativeDirectory = "some-sub-dir";

        var versionData = VersionOptions.FromVersion(new Version("1.2"));
        versionData.PathFilters = new[]
        {
            new FilterPath(".", "")
        };
        this.WriteVersionFile(versionData, "");
        Assert.Equal(1, this.GetVersionHeight(relativeDirectory));

        // Expect commit in an excluded directory to not affect version height
        var ignoredFilePath = Path.Combine(this.RepoPath, "other-dir", "my-file.txt");
        Directory.CreateDirectory(Path.GetDirectoryName(ignoredFilePath));
        File.WriteAllText(ignoredFilePath, "hello");
        Commands.Stage(this.LibGit2Repository, ignoredFilePath);
        this.LibGit2Repository.Commit("Add file to other directory", this.Signer, this.Signer);
        Assert.Equal(2, this.GetVersionHeight(relativeDirectory));
    }

    [Fact]
    public void GetVersionHeight_ProjectDirectoryIsMoved()
    {
        this.InitializeSourceControl();

        string relativeDirectory = "some-sub-dir";

        var versionData = VersionOptions.FromVersion(new Version("1.2"));
        versionData.PathFilters = new[]
        {
            new FilterPath("./", relativeDirectory),
            new FilterPath(":^/some-sub-dir/ignore.txt", relativeDirectory),
            new FilterPath(":^excluded-dir", relativeDirectory),
        };
        this.WriteVersionFile(versionData, relativeDirectory);
        Assert.Equal(1, this.GetVersionHeight(relativeDirectory));

        // Commit touching excluded path does not affect version height
        var ignoredFilePath = Path.Combine(this.RepoPath, relativeDirectory, "ignore.txt");
        File.WriteAllText(ignoredFilePath, "hello");
        Commands.Stage(this.LibGit2Repository, ignoredFilePath);
        this.LibGit2Repository.Commit("Add excluded file", this.Signer, this.Signer);
        Assert.Equal(1, this.GetVersionHeight(relativeDirectory));

        // Commit touching both excluded and included path does affect height
        var includedFilePath = Path.Combine(this.RepoPath, relativeDirectory, "another-file.txt");
        File.WriteAllText(includedFilePath, "hello");
        File.WriteAllText(ignoredFilePath, "changed");
        Commands.Stage(this.LibGit2Repository, includedFilePath);
        Commands.Stage(this.LibGit2Repository, ignoredFilePath);
        this.LibGit2Repository.Commit("Change both excluded and included file", this.Signer, this.Signer);
        Assert.Equal(2, this.GetVersionHeight(relativeDirectory));

        // Commit touching excluded directory does not affect version height
        var fileInExcludedDirPath = Path.Combine(this.RepoPath, relativeDirectory, "excluded-dir", "ignore.txt");
        Directory.CreateDirectory(Path.GetDirectoryName(fileInExcludedDirPath));
        File.WriteAllText(fileInExcludedDirPath, "hello");
        Commands.Stage(this.LibGit2Repository, fileInExcludedDirPath);
        this.LibGit2Repository.Commit("Add file to excluded dir", this.Signer, this.Signer);
        Assert.Equal(2, this.GetVersionHeight(relativeDirectory));

        // Rename the project directory
        Directory.Move(Path.Combine(this.RepoPath, relativeDirectory), Path.Combine(this.RepoPath, "new-project-dir"));
        Commands.Stage(this.LibGit2Repository, relativeDirectory);
        Commands.Stage(this.LibGit2Repository, "new-project-dir");
        this.LibGit2Repository.Commit("Move project directory", this.Signer, this.Signer);

        // Version is reset as project directory cannot be find in the ancestor commit
        Assert.Equal(1, this.GetVersionHeight("new-project-dir"));
    }

    [Fact]
    public void GitCommitIdShort()
    {
        this.WriteVersionFile(new VersionOptions { Version = SemanticVersion.Parse("1.2"), GitCommitIdShortAutoMinimum = 4 });
        this.InitializeSourceControl();
        this.AddCommits(1);
        var oracle = new VersionOracle(this.Context);

        if (this.Context is Nerdbank.GitVersioning.LibGit2.LibGit2Context)
        {
            // I'm not sure why libgit2 returns 7 as the minimum length when clearly a two commit repo would need fewer.
            Assert.Equal(7, oracle.GitCommitIdShort.Length);
        }
        else
        {
            Assert.Equal(4, oracle.GitCommitIdShort.Length);
        }
    }

    [Fact(Skip = "Slow test")]
    public void GetVersionHeight_VeryLongHistory()
    {
        this.WriteVersionFile();

        // Make a *lot* of commits
        this.AddCommits(2000);

        this.GetVersionHeight();
    }

}
