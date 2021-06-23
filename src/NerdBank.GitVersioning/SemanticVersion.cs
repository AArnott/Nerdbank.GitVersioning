namespace Nerdbank.GitVersioning
{
    using System;
    using System.Diagnostics;
    using System.Text.RegularExpressions;
    using Validation;

    /// <summary>
    /// Describes a version with an optional unstable tag.
    /// </summary>
    [DebuggerDisplay("{DebuggerDisplay,nq}")]
    public class SemanticVersion : IEquatable<SemanticVersion>
    {
        /// <summary>
        /// The regular expression with capture groups for semantic versioning.
        /// It considers PATCH to be optional and permits the 4th Revision component.
        /// </summary>
        /// <remarks>
        /// Parts of this regex inspired by https://github.com/sindresorhus/semver-regex/blob/master/index.js
        /// </remarks>
        private static readonly Regex FullSemVerPattern = new Regex(@"^v?(?<major>0|[1-9][0-9]*)\.(?<minor>0|[1-9][0-9]*)(?:\.(?<patch>0|[1-9][0-9]*)(?:\.(?<revision>0|[1-9][0-9]*))?)?(?<prerelease>-[\da-z\-]+(?:\.[\da-z\-]+)*)?(?<buildMetadata>\+[\da-z\-]+(?:\.[\da-z\-]+)*)?$", RegexOptions.IgnoreCase);

        /// <summary>
        /// The regex pattern that a prerelease must match.
        /// </summary>
        /// <remarks>
        /// Keep in sync with the regex for the version field found in the version.schema.json file.
        /// </remarks>
        private static readonly Regex PrereleasePattern = new Regex("-(?:[\\da-z\\-]+|\\{height\\})(?:\\.(?:[\\da-z\\-]+|\\{height\\}))*", RegexOptions.IgnoreCase);

        /// <summary>
        /// The regex pattern that build metadata must match.
        /// </summary>
        /// <remarks>
        /// Keep in sync with the regex for the version field found in the version.schema.json file.
        /// </remarks>
        private static readonly Regex BuildMetadataPattern = new Regex("\\+(?:[\\da-z\\-]+|\\{height\\})(?:\\.(?:[\\da-z\\-]+|\\{height\\}))*", RegexOptions.IgnoreCase);

        /// <summary>
        /// The regular expression with capture groups for semantic versioning,
        /// allowing for macros such as {height}.
        /// </summary>
        /// <remarks>
        /// Keep in sync with the regex for the version field found in the version.schema.json file.
        /// </remarks>
        private static readonly Regex FullSemVerWithMacrosPattern = new Regex("^v?(?<major>0|[1-9][0-9]*)\\.(?<minor>0|[1-9][0-9]*)(?:\\.(?<patch>0|[1-9][0-9]*)(?:\\.(?<revision>0|[1-9][0-9]*))?)?(?<prerelease>" + PrereleasePattern + ")?(?<buildMetadata>" + BuildMetadataPattern + ")?$", RegexOptions.IgnoreCase);

        /// <summary>
        /// Initializes a new instance of the <see cref="SemanticVersion"/> class.
        /// </summary>
        /// <param name="version">The numeric version.</param>
        /// <param name="prerelease">The prerelease, with leading - character.</param>
        /// <param name="buildMetadata">The build metadata, with leading + character.</param>
        public SemanticVersion(Version version, string prerelease = null, string buildMetadata = null)
        {
            Requires.NotNull(version, nameof(version));
            VerifyPatternMatch(prerelease, PrereleasePattern, nameof(prerelease));
            VerifyPatternMatch(buildMetadata, BuildMetadataPattern, nameof(buildMetadata));

            this.Version = version;
            this.Prerelease = prerelease ?? string.Empty;
            this.BuildMetadata = buildMetadata ?? string.Empty;
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="SemanticVersion"/> class.
        /// </summary>
        /// <param name="version">The x.y.z numeric version.</param>
        /// <param name="prerelease">The prerelease, with leading - character.</param>
        /// <param name="buildMetadata">The build metadata, with leading + character.</param>
        public SemanticVersion(string version, string prerelease = null, string buildMetadata = null)
            : this(new Version(version), prerelease, buildMetadata)
        {
        }

        /// <summary>
        /// Identifies the various positions in a semantic version.
        /// </summary>
        public enum Position
        {
            /// <summary>
            /// The <see cref="Version.Major"/> component.
            /// </summary>
            Major,

            /// <summary>
            /// The <see cref="Version.Minor"/> component.
            /// </summary>
            Minor,

            /// <summary>
            /// The <see cref="Version.Build"/> component.
            /// </summary>
            Build,

            /// <summary>
            /// The <see cref="Version.Revision"/> component.
            /// </summary>
            Revision,

            /// <summary>
            /// The <see cref="Prerelease"/> portion of the version.
            /// </summary>
            Prerelease,

            /// <summary>
            /// The <see cref="BuildMetadata"/> portion of the version.
            /// </summary>
            BuildMetadata,
        }

        /// <summary>
        /// Gets the version.
        /// </summary>
        public Version Version { get; }

        /// <summary>
        /// Gets an unstable tag (with the leading hyphen), if applicable.
        /// </summary>
        /// <value>A string with a leading hyphen or the empty string.</value>
        public string Prerelease { get; }

        /// <summary>
        /// Gets the build metadata (with the leading plus), if applicable.
        /// </summary>
        /// <value>A string with a leading plus or the empty string.</value>
        public string BuildMetadata { get; }

        /// <summary>
        /// Gets the position in a computed version that the version height should appear.
        /// </summary>
        internal SemanticVersion.Position? VersionHeightPosition
        {
            get
            {
                if (this.Prerelease?.Contains(VersionOptions.VersionHeightPlaceholder) ?? false)
                {
                    return SemanticVersion.Position.Prerelease;
                }
                else if (this.Version.Build == -1)
                {
                    return SemanticVersion.Position.Build;
                }
                else if (this.Version.Revision == -1)
                {
                    return SemanticVersion.Position.Revision;
                }
                else
                {
                    return null;
                }
            }
        }

        /// <summary>
        /// Gets a value indicating whether this instance is the default "0.0" instance.
        /// </summary>
        internal bool IsDefault => this.Version?.Major == 0 && this.Version.Minor == 0 && this.Version.Build == -1 && this.Version.Revision == -1 && this.Prerelease == null && this.BuildMetadata == null;

        /// <summary>
        /// Gets the debugger display for this instance.
        /// </summary>
        private string DebuggerDisplay => this.ToString();

        /// <summary>
        /// Parses a semantic version from the given string.
        /// </summary>
        /// <param name="semanticVersion">The value which must wholly constitute a semantic version to succeed.</param>
        /// <param name="version">Receives the semantic version, if found.</param>
        /// <returns><c>true</c> if a semantic version is found; <c>false</c> otherwise.</returns>
        public static bool TryParse(string semanticVersion, out SemanticVersion version)
        {
            Requires.NotNullOrEmpty(semanticVersion, nameof(semanticVersion));

            Match m = FullSemVerWithMacrosPattern.Match(semanticVersion);
            if (m.Success)
            {
                var major = int.Parse(m.Groups["major"].Value);
                var minor = int.Parse(m.Groups["minor"].Value);
                var patch = m.Groups["patch"].Value;
                var revision = m.Groups["revision"].Value;
                var systemVersion = patch.Length > 0
                    ? revision.Length > 0 ? new Version(major, minor, int.Parse(patch), int.Parse(revision)) : new Version(major, minor, int.Parse(patch))
                    : new Version(major, minor);
                var prerelease = m.Groups["prerelease"].Value;
                var buildMetadata = m.Groups["buildMetadata"].Value;
                version = new SemanticVersion(systemVersion, prerelease, buildMetadata);
                return true;
            }

            version = null;
            return false;
        }

        /// <summary>
        /// Parses a semantic version from the given string.
        /// </summary>
        /// <param name="semanticVersion">The value which must wholly constitute a semantic version to succeed.</param>
        /// <returns>An instance of <see cref="SemanticVersion"/>, initialized to the value specified in <paramref name="semanticVersion"/>.</returns>
        public static SemanticVersion Parse(string semanticVersion)
        {
            SemanticVersion result;
            Requires.Argument(TryParse(semanticVersion, out result), nameof(semanticVersion), "Unrecognized or unsupported semantic version.");
            return result;
        }

        /// <summary>
        /// Checks equality against another object.
        /// </summary>
        /// <param name="obj">The other instance.</param>
        /// <returns><c>true</c> if the instances have equal values; <c>false</c> otherwise.</returns>
        public override bool Equals(object obj)
        {
            return this.Equals(obj as SemanticVersion);
        }

        /// <summary>
        /// Gets a hash code for this instance.
        /// </summary>
        /// <returns>The hash code.</returns>
        public override int GetHashCode()
        {
            return this.Version.GetHashCode() + this.Prerelease.GetHashCode();
        }

        /// <summary>
        /// Prints this instance as a string.
        /// </summary>
        /// <returns>A string representation of this object.</returns>
        public override string ToString()
        {
            return this.Version + this.Prerelease + this.BuildMetadata;
        }

        /// <summary>
        /// Checks equality against another instance of this class.
        /// </summary>
        /// <param name="other">The other instance.</param>
        /// <returns><c>true</c> if the instances have equal values; <c>false</c> otherwise.</returns>
        public bool Equals(SemanticVersion other)
        {
            if (other == null)
            {
                return false;
            }

            return this.Version == other.Version
                && this.Prerelease == other.Prerelease
                && this.BuildMetadata == other.BuildMetadata;
        }

        /// <summary>
        /// Tests whether two <see cref="SemanticVersion" /> instances are compatible enough that version height is not reset
        /// when progressing from one to the next.
        /// </summary>
        /// <param name="first">The first semantic version.</param>
        /// <param name="second">The second semantic version.</param>
        /// <param name="versionHeightPosition">The position within the version where height is tracked.</param>
        /// <returns><c>true</c> if transitioning from one version to the next should reset the version height; <c>false</c> otherwise.</returns>
        internal static bool WillVersionChangeResetVersionHeight(SemanticVersion first, SemanticVersion second, SemanticVersion.Position versionHeightPosition)
        {
            Requires.NotNull(first, nameof(first));
            Requires.NotNull(second, nameof(second));

            if (first == second)
            {
                return false;
            }

            if (versionHeightPosition == SemanticVersion.Position.Prerelease)
            {
                // The entire version spec must match exactly.
                return !first.Equals(second);
            }

            for (SemanticVersion.Position position = SemanticVersion.Position.Major; position <= versionHeightPosition; position++)
            {
                int expectedValue = ReadVersionPosition(second.Version, position);
                int actualValue = ReadVersionPosition(first.Version, position);
                if (expectedValue != actualValue)
                {
                    return true;
                }
            }

            return false;
        }

        internal static int ReadVersionPosition(Version version, SemanticVersion.Position position)
        {
            Requires.NotNull(version, nameof(version));

            switch (position)
            {
                case SemanticVersion.Position.Major:
                    return version.Major;
                case SemanticVersion.Position.Minor:
                    return version.Minor;
                case SemanticVersion.Position.Build:
                    return version.Build;
                case SemanticVersion.Position.Revision:
                    return version.Revision;
                default:
                    throw new ArgumentOutOfRangeException(nameof(position), position, "Must be one of the 4 integer parts.");
            }
        }

        internal int ReadVersionPosition(SemanticVersion.Position position)
        {
            switch (position)
            {
                case SemanticVersion.Position.Major:
                    return this.Version.Major;
                case SemanticVersion.Position.Minor:
                    return this.Version.Minor;
                case SemanticVersion.Position.Build:
                    return this.Version.Build;
                case SemanticVersion.Position.Revision:
                    return this.Version.Revision;
                default:
                    throw new ArgumentOutOfRangeException(nameof(position), position, "Must be one of the 4 integer parts.");
            }
        }

        /// <summary>
        /// Checks whether a particular version number
        /// belongs to the set of versions represented by this semantic version spec.
        /// </summary>
        /// <param name="version">A version, with major and minor components, and possibly build and/or revision components.</param>
        /// <returns><c>true</c> if <paramref name="version"/> may have been produced by this semantic version; <c>false</c> otherwise.</returns>
        internal bool Contains(Version version)
        {
            return
                this.Version.Major == version.Major &&
                this.Version.Minor == version.Minor &&
                (this.Version.Build == -1 || this.Version.Build == version.Build) &&
                (this.Version.Revision == -1 || this.Version.Revision == version.Revision);
        }

        /// <summary>
        /// Verifies that the prerelease tag follows semver rules.
        /// </summary>
        /// <param name="input">The input string to test.</param>
        /// <param name="pattern">The regex that the string must conform to.</param>
        /// <param name="parameterName">The name of the parameter supplying the <paramref name="input"/>.</param>
        /// <exception cref="ArgumentException">
        /// Thrown if the <paramref name="input"/> does not match the required <paramref name="pattern"/>.
        /// </exception>
        private static void VerifyPatternMatch(string input, Regex pattern, string parameterName)
        {
            Requires.NotNull(pattern, nameof(pattern));

            if (string.IsNullOrEmpty(input))
            {
                return;
            }

            Requires.Argument(pattern.IsMatch(input), parameterName, $"The prerelease must match the pattern \"{pattern}\".");
        }
    }
}
