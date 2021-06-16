#nullable enable

using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Linq;
using Validation;

namespace Nerdbank.GitVersioning
{
    /// <summary>
    /// Represents a location and commit within a git repo and provides access to some version-related git activities.
    /// </summary>
    public abstract class GitContext : IDisposable
    {
        /// <summary>
        /// The 0.0 semver.
        /// </summary>
        private protected static readonly SemanticVersion SemVer0 = SemanticVersion.Parse("0.0");

        /// <summary>
        /// The 0.0 version.
        /// </summary>
        private protected static readonly Version Version0 = new Version(0, 0);

        /// <summary>
        /// Maximum allowable value for the <see cref="Version.Build"/>
        /// and <see cref="Version.Revision"/> components.
        /// </summary>
        private protected const ushort MaximumBuildNumberOrRevisionComponent = 0xfffe;

        private string repoRelativeProjectDirectory;

        /// <summary>Initializes a new instance of the <see cref="GitContext"/> class.</summary>
        /// <param name="workingTreePath">The absolute path to the root of the working tree.</param>
        /// <param name="dotGitPath">The path to the .git folder.</param>
        protected GitContext(string workingTreePath, string? dotGitPath)
        {
            this.WorkingTreePath = workingTreePath;
            this.repoRelativeProjectDirectory = string.Empty;
            this.DotGitPath = dotGitPath;
        }

        /// <summary>
        /// Gets the absolute path to the base directory of the git working tree.
        /// </summary>
        public string WorkingTreePath { get; }

        /// <summary>
        /// Gets the path to the directory to read version information from, relative to the <see cref="WorkingTreePath"/>.
        /// </summary>
        public string RepoRelativeProjectDirectory
        {
            get => this.repoRelativeProjectDirectory;
            set
            {
                Requires.NotNull(value, nameof(value));
                Requires.Argument(!Path.IsPathRooted(value), nameof(value), "Path must be relative to " + nameof(this.WorkingTreePath) + ".");
                this.repoRelativeProjectDirectory = value;
            }
        }

        /// <summary>
        /// Gets the absolute path to the directory to read version information from.
        /// </summary>
        public string AbsoluteProjectDirectory => Path.Combine(this.WorkingTreePath, this.RepoRelativeProjectDirectory);

        /// <summary>
        /// Gets an instance of <see cref="GitVersioning.VersionFile"/> that will read version information from the context identified by this instance.
        /// </summary>
        public abstract VersionFile VersionFile { get; }

        /// <summary>
        /// Gets a value indicating whether a git repository was found at <see cref="WorkingTreePath"/>;
        /// </summary>
        public bool IsRepository => this.DotGitPath is object;

        /// <summary>
        /// Gets the full SHA-1 id of the commit to be read.
        /// </summary>
        public abstract string? GitCommitId { get; }

        /// <summary>
        /// Gets a value indicating whether <see cref="GitCommitId"/> refers to the commit at HEAD.
        /// </summary>
        public abstract bool IsHead { get; }

        /// <summary>
        /// Gets a value indicating whether the repo is a shallow repo.
        /// </summary>
        public bool IsShallow => this.DotGitPath is object && File.Exists(Path.Combine(this.DotGitPath, "shallow"));

        /// <summary>
        /// Gets the date that the commit identified by <see cref="GitCommitId"/> was created.
        /// </summary>
        public abstract DateTimeOffset? GitCommitDate { get; }

        /// <summary>
        /// Gets the canonical name for HEAD's position (e.g. <c>refs/heads/master</c>)
        /// </summary>
        public abstract string? HeadCanonicalName { get; }

        /// <summary>
        /// Gets the path to the .git folder.
        /// </summary>
        protected string? DotGitPath { get; }

        /// <inheritdoc />
        public void Dispose()
        {
            this.Dispose(true);
            GC.SuppressFinalize(this);
        }

        /// <summary>
        /// Creates a context for reading/writing version information at a given path and committish.
        /// </summary>
        /// <param name="path">The path to a directory for which version information is required.</param>
        /// <param name="committish">The SHA-1 or ref for a git commit.</param>
        /// <param name="writable"><see langword="true"/> if mutating the git repository may be required; <see langword="false" /> otherwise.</param>
        /// <param name="disabled"><see langword="true"/> if git operations should be disabled (implies <paramref name="writable"/> false); <see langword="false" /> otherwise.</param>
        /// <returns></returns>
        public static GitContext Create(string path, string? committish = null, bool writable = false, bool disabled = false)
        {
            if (TryFindGitPaths(path, out string? gitDirectory, out string? workingTreeDirectory, out string? workingTreeRelativePath))
            {
                GitContext result =
                    disabled ? new DisabledGitContext(workingTreeDirectory) :
                    writable ? (GitContext)new LibGit2.LibGit2Context(workingTreeDirectory, gitDirectory, committish) :
                    new Managed.ManagedGitContext(workingTreeDirectory, gitDirectory, committish);
                result.RepoRelativeProjectDirectory = workingTreeRelativePath;
                return result;
            }
            else
            {
                // Consider the working tree to be the entire volume.
                string workingTree = path;
                string? candidate;
                while ((candidate = Path.GetDirectoryName(workingTree)) is object)
                {
                    workingTree = candidate;
                }

                return new NoGitContext(workingTree)
                {
                    RepoRelativeProjectDirectory = path.Substring(workingTree.Length),
                };
            }
        }

        /// <summary>
        /// Sets the context to represent a particular git commit.
        /// </summary>
        /// <param name="committish">Any committish string (e.g. commit id, branch, tag).</param>
        /// <returns><see langword="true" /> if the string was successfully parsed into a commit; <see langword="false" /> otherwise.</returns>
        public abstract bool TrySelectCommit(string committish);

        /// <summary>
        /// Adds a tag with the given name to the commit identified by <see cref="GitCommitId"/>.
        /// </summary>
        /// <param name="name">The name of the tag.</param>
        /// <exception cref="NotSupportedException">May be thrown if the context was created without specifying write access was required.</exception>
        /// <exception cref="InvalidOperationException">Thrown if <see cref="GitCommitId"/> is <see langword="null"/>.</exception>
        public abstract void ApplyTag(string name);

        /// <summary>
        /// Adds the specified path to the stage for the working tree.
        /// </summary>
        /// <param name="path">The path to be staged.</param>
        /// <exception cref="NotSupportedException">May be thrown if the context was created without specifying write access was required.</exception>
        public abstract void Stage(string path);

        /// <summary>
        /// Gets the shortest string that uniquely identifies the <see cref="GitCommitId"/>.
        /// </summary>
        /// <param name="minLength">A minimum length.</param>
        /// <returns>A string that is at least <paramref name="minLength"/> in length but may be more as required to uniquely identify the git object identified by <see cref="GitCommitId"/>.</returns>
        public abstract string GetShortUniqueCommitId(int minLength);

        internal abstract int CalculateVersionHeight(VersionOptions? committedVersion, VersionOptions? workingVersion);

        internal abstract Version GetIdAsVersion(VersionOptions? committedVersion, VersionOptions? workingVersion, int versionHeight);

        internal string GetRepoRelativePath(string absolutePath)
        {
            var repoRoot = this.WorkingTreePath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

            if (!absolutePath.StartsWith(repoRoot, StringComparison.OrdinalIgnoreCase))
            {
                throw new ArgumentException($"Path '{absolutePath}' is not within repository '{repoRoot}'", nameof(absolutePath));
            }

            return absolutePath.Substring(repoRoot.Length)
                .TrimStart(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        }

        /// <summary>
        /// Gets a value indicating whether the version file has changed in the working tree.
        /// </summary>
        /// <param name="committedVersion">
        /// The commited <see cref="VersionOptions"/>.
        /// </param>
        /// <param name="workingVersion">
        /// The working version of <see cref="VersionOptions"/>.
        /// </param>
        /// <returns><see langword="true" /> if the version file is dirty; <see langword="false"/> otherwise.</returns>
        protected static bool IsVersionFileChangedInWorkingTree(VersionOptions? committedVersion, VersionOptions? workingVersion)
        {
            if (workingVersion is object)
            {
                return !EqualityComparer<VersionOptions?>.Default.Equals(workingVersion, committedVersion);
            }

            // A missing working version is a change only if it was previously committed.
            return committedVersion is object;
        }

        internal static bool TryFindGitPaths(string? path, [NotNullWhen(true)] out string? gitDirectory, [NotNullWhen(true)] out string? workingTreeDirectory, [NotNullWhen(true)] out string? workingTreeRelativePath)
        {
            if (path is null || path.Length == 0)
            {
                gitDirectory = null;
                workingTreeDirectory = null;
                workingTreeRelativePath = null;
                return false;
            }

            path = Path.GetFullPath(path);
            var gitDirs = FindGitDir(path);
            if (gitDirs is null)
            {
                gitDirectory = null;
                workingTreeDirectory = null;
                workingTreeRelativePath = null;
                return false;
            }

            gitDirectory = gitDirs.Value.GitDirectory;
            workingTreeDirectory = gitDirs.Value.WorkingTreeDirectory;
            workingTreeRelativePath = path.Substring(gitDirs.Value.WorkingTreeDirectory.Length).TrimStart(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            return true;
        }

        private protected static void FindGitPaths(string path, out string gitDirectory, out string workingTreeDirectory, out string workingTreeRelativePath)
        {
            if (TryFindGitPaths(path, out string? gitDirectoryLocal, out string? workingTreeDirectoryLocal, out string? workingTreeRelativePathLocal))
            {
                gitDirectory = gitDirectoryLocal;
                workingTreeDirectory = workingTreeDirectoryLocal;
                workingTreeRelativePath = workingTreeRelativePathLocal;
            }
            else
            {
                throw new ArgumentException("Path is not within a git directory.", nameof(path));
            }
        }

        /// <summary>
        /// Disposes of native and managed resources associated by this object.
        /// </summary>
        /// <param name="disposing"><see langword="true" /> to dispose managed and native resources; <see langword="false" /> to only dispose of native resources.</param>
        protected virtual void Dispose(bool disposing)
        {
        }

        /// <summary>
        /// Searches a path and its ancestors for a directory with a .git subdirectory.
        /// </summary>
        /// <param name="path">The absolute path to start the search from.</param>
        /// <returns>The path to the .git folder and working tree, or <see langword="null"/> if not found.</returns>
        private static (string GitDirectory, string WorkingTreeDirectory)? FindGitDir(string path)
        {
            string? startingDir = path;
            while (startingDir is object)
            {
                var dirOrFilePath = Path.Combine(startingDir, ".git");
                if (Directory.Exists(dirOrFilePath))
                {
                    return (dirOrFilePath, Path.GetDirectoryName(dirOrFilePath)!);
                }
                else if (File.Exists(dirOrFilePath))
                {
                    string? relativeGitDirPath = ReadGitDirFromFile(dirOrFilePath);
                    if (!string.IsNullOrWhiteSpace(relativeGitDirPath))
                    {
                        var fullGitDirPath = Path.GetFullPath(Path.Combine(startingDir, relativeGitDirPath));
                        if (Directory.Exists(fullGitDirPath))
                        {
                            return (fullGitDirPath, Path.GetDirectoryName(dirOrFilePath)!);
                        }
                    }
                }

                startingDir = Path.GetDirectoryName(startingDir);
            }

            return null;
        }

        private static string? ReadGitDirFromFile(string fileName)
        {
            const string expectedPrefix = "gitdir: ";
            var firstLineOfFile = File.ReadLines(fileName).FirstOrDefault();
            if (firstLineOfFile?.StartsWith(expectedPrefix) ?? false)
            {
                return firstLineOfFile.Substring(expectedPrefix.Length); // strip off the prefix, leaving just the path
            }

            return null;
        }
    }
}
