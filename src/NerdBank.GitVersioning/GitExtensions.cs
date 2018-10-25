﻿namespace Nerdbank.GitVersioning
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Linq;
    using System.Runtime.InteropServices;
    using System.Text;
    using LibGit2Sharp;
    using Validation;
    using Version = System.Version;

    /// <summary>
    /// Git extension methods.
    /// </summary>
    public static class GitExtensions
    {
        /// <summary>
        /// The 0.0 version.
        /// </summary>
        private static readonly Version Version0 = new Version(0, 0);

        /// <summary>
        /// Maximum allowable value for the <see cref="Version.Build"/>
        /// and <see cref="Version.Revision"/> components.
        /// </summary>
        private const ushort MaximumBuildNumberOrRevisionComponent = 0xfffe;

        /// <summary>
        /// Gets the number of commits in the longest single path between
        /// the specified commit and the most distant ancestor (inclusive)
        /// that set the version to the value at <paramref name="commit"/>.
        /// </summary>
        /// <param name="commit">The commit to measure the height of.</param>
        /// <param name="repoRelativeProjectDirectory">The repo-relative project directory for which to calculate the version.</param>
        /// <param name="baseVersion">Optional base version to calculate the height. If not specified, the base version will be calculated by scanning the repository.</param>
        /// <returns>The height of the commit. Always a positive integer.</returns>
        public static int GetVersionHeight(this Commit commit, string repoRelativeProjectDirectory = null, Version baseVersion = null)
        {
            Requires.NotNull(commit, nameof(commit));
            Requires.Argument(repoRelativeProjectDirectory == null || !Path.IsPathRooted(repoRelativeProjectDirectory), nameof(repoRelativeProjectDirectory), "Path should be relative to repo root.");

            if (baseVersion == null)
            {
                baseVersion = VersionFile.GetVersion(commit, repoRelativeProjectDirectory)?.Version?.Version ?? Version0;
            }

            int height = commit.GetHeight(c => CommitMatchesMajorMinorVersion(c, baseVersion, repoRelativeProjectDirectory));
            return height;
        }

        /// <summary>
        /// Gets the number of commits in the longest single path between
        /// HEAD in a repo and the most distant ancestor (inclusive)
        /// that set the version to the value in the working copy
        /// (or HEAD for bare repositories).
        /// </summary>
        /// <param name="repo">The repo with the working copy / HEAD to measure the height of.</param>
        /// <param name="repoRelativeProjectDirectory">The repo-relative project directory for which to calculate the version.</param>
        /// <returns>The height of the repo at HEAD. Always a positive integer.</returns>
        public static int GetVersionHeight(this Repository repo, string repoRelativeProjectDirectory = null)
        {
            if (repo == null)
            {
                return 0;
            }

            VersionOptions workingCopyVersionOptions, committedVersionOptions;
            if (IsVersionFileChangedInWorkingCopy(repo, repoRelativeProjectDirectory, out committedVersionOptions, out workingCopyVersionOptions))
            {
                Version workingCopyVersion = workingCopyVersionOptions?.Version?.Version;
                Version headCommitVersion = committedVersionOptions?.Version?.Version ?? Version0;
                if (workingCopyVersion == null || !workingCopyVersion.Equals(headCommitVersion))
                {
                    // The working copy has changed the major.minor version.
                    // So by definition the version height is 0, since no commit represents it yet.
                    return 0;
                }
            }

            // No special changes in the working directory, so apply regular logic.
            return GetVersionHeight(repo.Head, repoRelativeProjectDirectory);
        }

        /// <summary>
        /// Gets the number of commits in the longest single path between
        /// the specified commit and the most distant ancestor (inclusive)
        /// that set the version to the value at the tip of the <paramref name="branch"/>.
        /// </summary>
        /// <param name="branch">The branch to measure the height of.</param>
        /// <param name="repoRelativeProjectDirectory">The repo-relative project directory for which to calculate the version.</param>
        /// <returns>The height of the branch till the version is changed.</returns>
        public static int GetVersionHeight(this Branch branch, string repoRelativeProjectDirectory = null)
        {
            return GetVersionHeight(branch.Commits.First(), repoRelativeProjectDirectory);
        }

        /// <summary>
        /// Gets the number of commits in the longest single path between
        /// the specified commit and the most distant ancestor (inclusive).
        /// </summary>
        /// <param name="commit">The commit to measure the height of.</param>
        /// <param name="continueStepping">
        /// A function that returns <c>false</c> when we reach a commit that
        /// should not be included in the height calculation.
        /// May be null to count the height to the original commit.
        /// </param>
        /// <returns>The height of the commit. Always a positive integer.</returns>
        public static int GetHeight(this Commit commit, Func<Commit, bool> continueStepping = null)
        {
            Requires.NotNull(commit, nameof(commit));

            var heights = new Dictionary<ObjectId, int>();
            return GetCommitHeight(commit, heights, continueStepping);
        }

        /// <summary>
        /// Gets the number of commits in the longest single path between
        /// the specified branch's head and the most distant ancestor (inclusive).
        /// </summary>
        /// <param name="branch">The branch to measure the height of.</param>
        /// <param name="continueStepping">
        /// A function that returns <c>false</c> when we reach a commit that
        /// should not be included in the height calculation.
        /// May be null to count the height to the original commit.
        /// </param>
        /// <returns>The height of the branch.</returns>
        public static int GetHeight(this Branch branch, Func<Commit, bool> continueStepping = null)
        {
            return GetHeight(branch.Commits.First(), continueStepping);
        }

        /// <summary>
        /// Takes the first 4 bytes of a commit ID (i.e. first 8 characters of its hex-encoded SHA)
        /// and returns them as an integer.
        /// </summary>
        /// <param name="commit">The commit to identify with an integer.</param>
        /// <returns>The integer which identifies a commit.</returns>
        public static int GetTruncatedCommitIdAsInt32(this Commit commit)
        {
            Requires.NotNull(commit, nameof(commit));
            return BitConverter.ToInt32(commit.Id.RawId, 0);
        }

        /// <summary>
        /// Takes the first 2 bytes of a commit ID (i.e. first 4 characters of its hex-encoded SHA)
        /// and returns them as an 16-bit unsigned integer.
        /// </summary>
        /// <param name="commit">The commit to identify with an integer.</param>
        /// <returns>The unsigned integer which identifies a commit.</returns>
        public static ushort GetTruncatedCommitIdAsUInt16(this Commit commit)
        {
            Requires.NotNull(commit, nameof(commit));
            return BitConverter.ToUInt16(commit.Id.RawId, 0);
        }

        /// <summary>
        /// Looks up a commit by an integer that captures the first for bytes of its ID.
        /// </summary>
        /// <param name="repo">The repo to search for a matching commit.</param>
        /// <param name="truncatedId">The value returned from <see cref="GetTruncatedCommitIdAsInt32(Commit)"/>.</param>
        /// <returns>A matching commit.</returns>
        public static Commit GetCommitFromTruncatedIdInteger(this Repository repo, int truncatedId)
        {
            Requires.NotNull(repo, nameof(repo));

            byte[] rawId = BitConverter.GetBytes(truncatedId);
            return repo.Lookup<Commit>(EncodeAsHex(rawId));
        }

        /// <summary>
        /// Encodes a commit from history in a <see cref="Version"/>
        /// so that the original commit can be found later.
        /// </summary>
        /// <param name="commit">The commit whose ID and position in history is to be encoded.</param>
        /// <param name="repoRelativeProjectDirectory">The repo-relative project directory for which to calculate the version.</param>
        /// <param name="versionHeight">
        /// The version height, previously calculated by a call to <see cref="GetVersionHeight(Commit, string, Version)"/>
        /// with the same value for <paramref name="repoRelativeProjectDirectory"/>.
        /// </param>
        /// <returns>
        /// A version whose <see cref="Version.Build"/> and
        /// <see cref="Version.Revision"/> components are calculated based on the commit.
        /// </returns>
        /// <remarks>
        /// In the returned version, the <see cref="Version.Build"/> component is
        /// the height of the git commit while the <see cref="Version.Revision"/>
        /// component is the first four bytes of the git commit id (forced to be a positive integer).
        /// </remarks>
        public static Version GetIdAsVersion(this Commit commit, string repoRelativeProjectDirectory = null, int? versionHeight = null)
        {
            Requires.NotNull(commit, nameof(commit));
            Requires.Argument(repoRelativeProjectDirectory == null || !Path.IsPathRooted(repoRelativeProjectDirectory), nameof(repoRelativeProjectDirectory), "Path should be relative to repo root.");

            var versionOptions = VersionFile.GetVersion(commit, repoRelativeProjectDirectory);

            if (!versionHeight.HasValue)
            {
                versionHeight = GetVersionHeight(commit, repoRelativeProjectDirectory);
            }

            return GetIdAsVersionHelper(commit, versionOptions, versionHeight.Value);
        }

        /// <summary>
        /// Encodes HEAD (or a modified working copy) from history in a <see cref="Version"/>
        /// so that the original commit can be found later.
        /// </summary>
        /// <param name="repo">The repo whose ID and position in history is to be encoded.</param>
        /// <param name="repoRelativeProjectDirectory">The repo-relative project directory for which to calculate the version.</param>
        /// <param name="versionHeight">
        /// The version height, previously calculated by a call to <see cref="GetVersionHeight(Commit, string, Version)"/>
        /// with the same value for <paramref name="repoRelativeProjectDirectory"/>.
        /// </param>
        /// <returns>
        /// A version whose <see cref="Version.Build"/> and
        /// <see cref="Version.Revision"/> components are calculated based on the commit.
        /// </returns>
        /// <remarks>
        /// In the returned version, the <see cref="Version.Build"/> component is
        /// the height of the git commit while the <see cref="Version.Revision"/>
        /// component is the first four bytes of the git commit id (forced to be a positive integer).
        /// </remarks>
        public static Version GetIdAsVersion(this Repository repo, string repoRelativeProjectDirectory = null, int? versionHeight = null)
        {
            Requires.NotNull(repo, nameof(repo));

            var headCommit = repo.Head.Commits.FirstOrDefault();
            VersionOptions workingCopyVersionOptions, committedVersionOptions;
            if (IsVersionFileChangedInWorkingCopy(repo, repoRelativeProjectDirectory, out committedVersionOptions, out workingCopyVersionOptions))
            {
                // Apply ordinary logic, but to the working copy version info.
                if (!versionHeight.HasValue)
                {
                    var baseVersion = workingCopyVersionOptions?.Version?.Version;
                    versionHeight = GetVersionHeight(headCommit, repoRelativeProjectDirectory, baseVersion);
                }

                Version result = GetIdAsVersionHelper(headCommit, workingCopyVersionOptions, versionHeight.Value);
                return result;
            }

            return GetIdAsVersion(headCommit, repoRelativeProjectDirectory);
        }

        /// <summary>
        /// Looks up the commit that matches a specified version number.
        /// </summary>
        /// <param name="repo">The repository to search for a matching commit.</param>
        /// <param name="version">The version previously obtained from <see cref="GetIdAsVersion(Commit, string, int?)"/>.</param>
        /// <param name="repoRelativeProjectDirectory">
        /// The repo-relative project directory from which <paramref name="version"/> was originally calculated.
        /// </param>
        /// <returns>The matching commit, or <c>null</c> if no match is found.</returns>
        /// <exception cref="InvalidOperationException">
        /// Thrown in the very rare situation that more than one matching commit is found.
        /// </exception>
        public static Commit GetCommitFromVersion(this Repository repo, Version version, string repoRelativeProjectDirectory = null)
        {
            // Note we'll accept no match, or one match. But we throw if there is more than one match.
            return GetCommitsFromVersion(repo, version, repoRelativeProjectDirectory).SingleOrDefault();
        }

        /// <summary>
        /// Looks up the commits that match a specified version number.
        /// </summary>
        /// <param name="repo">The repository to search for a matching commit.</param>
        /// <param name="version">The version previously obtained from <see cref="GetIdAsVersion(Commit, string, int?)"/>.</param>
        /// <param name="repoRelativeProjectDirectory">The repo-relative project directory from which <paramref name="version"/> was originally calculated.</param>
        /// <returns>The matching commits, or an empty enumeration if no match is found.</returns>
        public static IEnumerable<Commit> GetCommitsFromVersion(this Repository repo, Version version, string repoRelativeProjectDirectory = null)
        {
            Requires.NotNull(repo, nameof(repo));
            Requires.NotNull(version, nameof(version));

            // The revision is a 16-bit unsigned integer, but is not allowed to be 0xffff.
            // So if the value is 0xfffe, consider that the actual last bit is insignificant
            // since the original git commit ID could have been either 0xffff or 0xfffe.
            ushort objectIdLeadingValue = (ushort)version.Revision;
            ushort objectIdMask = (ushort)(version.Revision == MaximumBuildNumberOrRevisionComponent ? 0xfffe : 0xffff);

            var possibleCommits = from commit in GetCommitsReachableFromRefs(repo)
                                  where version.Revision == -1 || commit.Id.StartsWith(objectIdLeadingValue, objectIdMask)
                                  let buildNumberOffset = VersionFile.GetVersion(commit)?.BuildNumberOffsetOrDefault ?? 0
                                  let versionHeight = commit.GetHeight(c => CommitMatchesMajorMinorVersion(c, version, repoRelativeProjectDirectory))
                                  where versionHeight == version.Build - buildNumberOffset
                                  select commit;

            return possibleCommits;
        }

#if NET461
        /// <summary>
        /// Assists the operating system in finding the appropriate native libgit2 module.
        /// </summary>
        /// <param name="basePath">The path to the directory that contains the lib folder.</param>
        /// <exception cref="ArgumentException">Thrown if the provided path does not lead to an existing directory.</exception>
        public static void HelpFindLibGit2NativeBinaries(string basePath)
        {
            if (!TryHelpFindLibGit2NativeBinaries(basePath, out string attemptedDirectory))
            {
                throw new ArgumentException($"Unable to find native binaries under directory: \"{attemptedDirectory}\".");
            }
        }

        /// <summary>
        /// Assists the operating system in finding the appropriate native libgit2 module.
        /// </summary>
        /// <param name="basePath">The path to the directory that contains the lib folder.</param>
        /// <returns><c>true</c> if the libgit2 native binaries have been found; <c>false</c> otherwise.</returns>
        public static bool TryHelpFindLibGit2NativeBinaries(string basePath)
        {
            return TryHelpFindLibGit2NativeBinaries(basePath, out string attemptedDirectory);
        }

        /// <summary>
        /// Assists the operating system in finding the appropriate native libgit2 module.
        /// </summary>
        /// <param name="basePath">The path to the directory that contains the lib folder.</param>
        /// <param name="attemptedDirectory">Receives the directory that native binaries are expected.</param>
        /// <returns><c>true</c> if the libgit2 native binaries have been found; <c>false</c> otherwise.</returns>
        public static bool TryHelpFindLibGit2NativeBinaries(string basePath, out string attemptedDirectory)
        {
            attemptedDirectory = FindLibGit2NativeBinaries(basePath);
            if (Directory.Exists(attemptedDirectory))
            {
                AddDirectoryToPath(attemptedDirectory);
                return true;
            }

            return false;
        }

        /// <summary>
        /// Add a directory to the PATH environment variable if it isn't already present.
        /// </summary>
        /// <param name="directory">The directory to be added.</param>
        public static void AddDirectoryToPath(string directory)
        {
            Requires.NotNullOrEmpty(directory, nameof(directory));

            string pathEnvVar = Environment.GetEnvironmentVariable("PATH");
            string[] searchPaths = pathEnvVar.Split(Path.PathSeparator);
            if (!searchPaths.Contains(directory, StringComparer.OrdinalIgnoreCase))
            {
                pathEnvVar += Path.PathSeparator + directory;
                Environment.SetEnvironmentVariable("PATH", pathEnvVar);
            }
        }
#endif

        /// <summary>
        /// Finds the directory that contains the appropriate native libgit2 module.
        /// </summary>
        /// <param name="basePath">The path to the directory that contains the lib folder.</param>
        /// <returns>Receives the directory that native binaries are expected.</returns>
        public static string FindLibGit2NativeBinaries(string basePath)
        {
#if !NET461
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
#endif
            {
                return Path.Combine(basePath, "lib", "win32", IntPtr.Size == 4 ? "x86" : "x64");
            }
#if !NET461
            else if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
            {
                return Path.Combine(basePath, "lib", "linux", IntPtr.Size == 4 ? "x86" : "x86_64");
            }
            else if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
            {
                return Path.Combine(basePath, "lib", "osx");
            }

            return null;
#endif
        }

        /// <summary>
        /// Opens a <see cref="Repository"/> found at or above a specified path.
        /// </summary>
        /// <param name="pathUnderGitRepo">The path at or beneath the git repo root.</param>
        /// <returns>The <see cref="Repository"/> found for the specified path, or <c>null</c> if no git repo is found.</returns>
        public static Repository OpenGitRepo(string pathUnderGitRepo)
        {
            Requires.NotNullOrEmpty(pathUnderGitRepo, nameof(pathUnderGitRepo));
            var gitDir = FindGitDir(pathUnderGitRepo);

            // Override Config Search paths to empty path to avoid new Repository instance to lookup for Global\System .gitconfig file
            GlobalSettings.SetConfigSearchPaths(ConfigurationLevel.Global, string.Empty);
            GlobalSettings.SetConfigSearchPaths(ConfigurationLevel.System, string.Empty);

            return gitDir == null ? null : new Repository(gitDir);
        }

        /// <summary>
        /// Tests whether a commit is of a specified version, comparing major and minor components
        /// with the version.txt file defined by that commit.
        /// </summary>
        /// <param name="commit">The commit to test.</param>
        /// <param name="expectedVersion">The version to test for in the commit</param>
        /// <param name="repoRelativeProjectDirectory">The repo-relative directory from which <paramref name="expectedVersion"/> was originally calculated.</param>
        /// <returns><c>true</c> if the <paramref name="commit"/> matches the major and minor components of <paramref name="expectedVersion"/>.</returns>
        internal static bool CommitMatchesMajorMinorVersion(this Commit commit, Version expectedVersion, string repoRelativeProjectDirectory)
        {
            Requires.NotNull(commit, nameof(commit));
            Requires.NotNull(expectedVersion, nameof(expectedVersion));

            var commitVersionData = VersionFile.GetVersion(commit, repoRelativeProjectDirectory);
            Version majorMinorFromFile = commitVersionData?.Version?.Version ?? Version0;
            return majorMinorFromFile?.Major == expectedVersion.Major && majorMinorFromFile?.Minor == expectedVersion.Minor;
        }

        internal static bool CommitMatchesFormat(this Commit commit, SemanticVersion expectedVersion, string repoRelativeProjectDirectory)
        {
            Requires.NotNull(commit, nameof(commit));
            Requires.NotNull(expectedVersion, nameof(expectedVersion));

            var commitVersionData = VersionFile.GetVersion(commit, repoRelativeProjectDirectory);
            var majorMinorFromFile = commitVersionData?.Version;
            return majorMinorFromFile.Equals(expectedVersion);
        }


        private static string FindGitDir(string startingDir)
        {
            while (startingDir != null)
            {
                var dirOrFilePath = Path.Combine(startingDir, ".git");
                if (Directory.Exists(dirOrFilePath))
                {
                    return dirOrFilePath;
                }
                else if (File.Exists(dirOrFilePath))
                {
                    var relativeGitDirPath = ReadGitDirFromFile(dirOrFilePath);
                    if (!string.IsNullOrWhiteSpace(relativeGitDirPath))
                    {
                        var fullGitDirPath = Path.GetFullPath(Path.Combine(startingDir, relativeGitDirPath));
                        if (Directory.Exists(fullGitDirPath))
                        {
                            return fullGitDirPath;
                        }
                    }
                }

                startingDir = Path.GetDirectoryName(startingDir);
            }

            return null;
        }

        private static string ReadGitDirFromFile(string fileName)
        {
            const string expectedPrefix = "gitdir: ";
            var firstLineOfFile = File.ReadLines(fileName).FirstOrDefault();
            if (firstLineOfFile?.StartsWith(expectedPrefix) ?? false)
            {
                return firstLineOfFile.Substring(expectedPrefix.Length); // strip off the prefix, leaving just the path
            }

            return null;
        }

        /// <summary>
        /// Tests whether an object's ID starts with the specified 16-bits, or a subset of them.
        /// </summary>
        /// <param name="object">The object whose ID is to be tested.</param>
        /// <param name="leadingBytes">The leading 16-bits to be tested.</param>
        /// <param name="bitMask">The mask that indicates which bits should be compared.</param>
        /// <returns><c>True</c> if the object's ID starts with <paramref name="leadingBytes"/> after applying the <paramref name="bitMask"/>.</returns>
        private static bool StartsWith(this ObjectId @object, ushort leadingBytes, ushort bitMask = 0xffff)
        {
            ushort truncatedObjectId = BitConverter.ToUInt16(@object.RawId, 0);
            return (truncatedObjectId & bitMask) == leadingBytes;
        }

        /// <summary>
        /// Encodes a byte array as hex.
        /// </summary>
        /// <param name="buffer">The buffer to encode.</param>
        /// <returns>A hexidecimal string.</returns>
        private static string EncodeAsHex(byte[] buffer)
        {
            Requires.NotNull(buffer, nameof(buffer));

            var sb = new StringBuilder();
            for (int i = 0; i < buffer.Length; i++)
            {
                sb.AppendFormat("{0:x2}", buffer[i]);
            }

            return sb.ToString();
        }

        /// <summary>
        /// Gets the number of commits in the longest single path between
        /// the specified branch's head and the most distant ancestor (inclusive).
        /// </summary>
        /// <param name="commit">The commit to measure the height of.</param>
        /// <param name="heights">A cache of commits and their heights.</param>
        /// <param name="continueStepping">
        /// A function that returns <c>false</c> when we reach a commit that
        /// should not be included in the height calculation.
        /// May be null to count the height to the original commit.
        /// </param>
        /// <returns>The height of the branch.</returns>
        private static int GetCommitHeight(Commit commit, Dictionary<ObjectId, int> heights, Func<Commit, bool> continueStepping)
        {
            Requires.NotNull(commit, nameof(commit));
            Requires.NotNull(heights, nameof(heights));

            int height;
            if (!heights.TryGetValue(commit.Id, out height))
            {
                height = 0;
                if (continueStepping == null || continueStepping(commit))
                {
                    height = 1;
                    if (commit.Parents.Any())
                    {
                        height += commit.Parents.Max(p => GetCommitHeight(p, heights, continueStepping));
                    }
                }

                heights[commit.Id] = height;
            }

            return height;
        }

        /// <summary>
        /// Enumerates over the set of commits in the repository that are reachable from any named reference.
        /// </summary>
        /// <param name="repo">The repo to search.</param>
        /// <returns>An enumerate of commits.</returns>
        private static IEnumerable<Commit> GetCommitsReachableFromRefs(Repository repo)
        {
            Requires.NotNull(repo, nameof(repo));

            var commits = new HashSet<Commit>();
            foreach (var reference in repo.Refs)
            {
                var commit = reference.ResolveToDirectReference()?.Target as Commit;
                if (commit != null)
                {
                    AddReachableCommitsFrom(commit, commits);
                }
            }

            return commits;
        }

        /// <summary>
        /// Adds a commit and all its ancestors to a set.
        /// </summary>
        /// <param name="startingCommit">The starting commit to add.</param>
        /// <param name="set">
        /// The set into which the <paramref name="startingCommit"/>
        /// and all its ancestors are to be added.
        /// </param>
        private static void AddReachableCommitsFrom(Commit startingCommit, HashSet<Commit> set)
        {
            Requires.NotNull(startingCommit, nameof(startingCommit));
            Requires.NotNull(set, nameof(set));

            if (set.Add(startingCommit))
            {
                foreach (var parent in startingCommit.Parents)
                {
                    AddReachableCommitsFrom(parent, set);
                }
            }
        }

        /// <summary>
        /// Encodes a commit from history in a <see cref="Version"/>
        /// so that the original commit can be found later.
        /// </summary>
        /// <param name="commit">The commit whose ID and position in history is to be encoded.</param>
        /// <param name="versionOptions">The version options applicable at this point (either from commit or working copy).</param>
        /// <param name="versionHeight">The version height, previously calculated by a call to <see cref="GetVersionHeight(Commit, string, Version)"/>.</param>
        /// <returns>
        /// A version whose <see cref="Version.Build"/> and
        /// <see cref="Version.Revision"/> components are calculated based on the commit.
        /// </returns>
        /// <remarks>
        /// In the returned version, the <see cref="Version.Build"/> component is
        /// the height of the git commit while the <see cref="Version.Revision"/>
        /// component is the first four bytes of the git commit id (forced to be a positive integer).
        /// </remarks>
        /// <returns></returns>
        internal static Version GetIdAsVersionHelper(this Commit commit, VersionOptions versionOptions, int versionHeight)
        {
            var baseVersion = versionOptions?.Version?.Version ?? Version0;
            int buildNumber = baseVersion.Build;
            int revision = baseVersion.Revision;

            if (revision < 0)
            {
                // The compiler (due to WinPE header requirements) only allows 16-bit version components,
                // and forbids 0xffff as a value.
                // The build number is set to the git height. This helps ensure that
                // within a major.minor release, each patch has an incrementing integer.
                // The revision is set to the first two bytes of the git commit ID.

                int adjustedVersionHeight = versionHeight == 0 ? 0 : versionHeight + (versionOptions?.BuildNumberOffset ?? 0);
                Verify.Operation(adjustedVersionHeight <= MaximumBuildNumberOrRevisionComponent, "Git height is {0}, which is greater than the maximum allowed {0}.", adjustedVersionHeight, MaximumBuildNumberOrRevisionComponent);

                if (buildNumber < 0)
                {
                    buildNumber = adjustedVersionHeight;
                    revision = commit != null
                        ? Math.Min(MaximumBuildNumberOrRevisionComponent, commit.GetTruncatedCommitIdAsUInt16())
                        : 0;
                }
                else
                {
                    revision = adjustedVersionHeight;
                }
            }

            return new Version(baseVersion.Major, baseVersion.Minor, buildNumber, revision);
        }

        /// <summary>
        /// Gets the version options from HEAD and the working copy (if applicable),
        /// and tests their equality.
        /// </summary>
        /// <param name="repo">The repo to scan for version info.</param>
        /// <param name="repoRelativeProjectDirectory">The path to the directory of the project whose version is being queried, relative to the repo root.</param>
        /// <param name="committedVersion">Receives the version options from the HEAD commit.</param>
        /// <param name="workingCopyVersion">Receives the version options from the working copy, when applicable.</param>
        /// <returns><c>true</c> if <paramref name="committedVersion"/> and <paramref name="workingCopyVersion"/> are not equal.</returns>
        private static bool IsVersionFileChangedInWorkingCopy(Repository repo, string repoRelativeProjectDirectory, out VersionOptions committedVersion, out VersionOptions workingCopyVersion)
        {
            Requires.NotNull(repo, nameof(repo));
            Commit headCommit = repo.Head.Commits.FirstOrDefault();
            committedVersion = VersionFile.GetVersion(headCommit, repoRelativeProjectDirectory);

            if (!repo.Info.IsBare)
            {
                string fullDirectory = Path.Combine(repo.Info.WorkingDirectory, repoRelativeProjectDirectory ?? string.Empty);
                workingCopyVersion = VersionFile.GetVersion(fullDirectory);
                return !EqualityComparer<VersionOptions>.Default.Equals(workingCopyVersion, committedVersion);
            }

            workingCopyVersion = null;
            return false;
        }
    }
}
