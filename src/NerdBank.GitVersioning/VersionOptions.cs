#nullable enable

namespace Nerdbank.GitVersioning
{
    using System;
    using System.Collections.Generic;
    using System.Collections.ObjectModel;
    using System.Diagnostics;
    using System.Linq;
    using System.Reflection;
    using Newtonsoft.Json;
    using Newtonsoft.Json.Converters;
    using Validation;
    using EditorBrowsableAttribute = System.ComponentModel.EditorBrowsableAttribute;
    using EditorBrowsableState = System.ComponentModel.EditorBrowsableState;

    /// <summary>
    /// Describes the various versions and options required for the build.
    /// </summary>
    [DebuggerDisplay("{DebuggerDisplay,nq}")]
    public class VersionOptions : IEquatable<VersionOptions>
    {
        /// <summary>
        /// A value indicating whether mutations of this instance are not allowed.
        /// </summary>
        [DebuggerBrowsable(DebuggerBrowsableState.Never)]
        private bool isFrozen;

        [DebuggerBrowsable(DebuggerBrowsableState.Never)]
        private string? gitCommitIdPrefix;

        /// <summary>
        /// Backing field for the <see cref="Version"/> property.
        /// </summary>
        [DebuggerBrowsable(DebuggerBrowsableState.Never)]
        private SemanticVersion? version;

        /// <summary>
        /// Backing field for the <see cref="AssemblyVersion"/> property.
        /// </summary>
        [DebuggerBrowsable(DebuggerBrowsableState.Never)]
        private AssemblyVersionOptions? assemblyVersion;

        /// <summary>
        /// Backing field for the <see cref="BuildNumberOffset"/> property.
        /// </summary>
        [DebuggerBrowsable(DebuggerBrowsableState.Never)]
        private int? buildNumberOffset;

        /// <summary>
        /// Backing field for the <see cref="SemVer1NumericIdentifierPadding"/> property.
        /// </summary>
        [DebuggerBrowsable(DebuggerBrowsableState.Never)]
        private int? semVer1NumericIdentifierPadding;

        /// <summary>
        /// Backing field for the <see cref="GitCommitIdShortFixedLength"/> property.
        /// </summary>
        [DebuggerBrowsable(DebuggerBrowsableState.Never)]
        private int? gitCommitIdShortFixedLength;

        /// <summary>
        /// Backing field for the <see cref="GitCommitIdShortAutoMinimum"/> property.
        /// </summary>
        [DebuggerBrowsable(DebuggerBrowsableState.Never)]
        private int? gitCommitIdShortAutoMinimum;

        /// <summary>
        /// Backing field for the <see cref="NuGetPackageVersion"/> property.
        /// </summary>
        [DebuggerBrowsable(DebuggerBrowsableState.Never)]
        private NuGetPackageVersionOptions? nuGetPackageVersion;

        /// <summary>
        /// Backing field for the <see cref="PublicReleaseRefSpec"/> property.
        /// </summary>
        [DebuggerBrowsable(DebuggerBrowsableState.Never)]
        private IReadOnlyList<string>? publicReleaseRefSpec;

        /// <summary>
        /// Backing field for the <see cref="CloudBuild"/> property.
        /// </summary>
        [DebuggerBrowsable(DebuggerBrowsableState.Never)]
        private CloudBuildOptions? cloudBuild;

        /// <summary>
        /// Backing field for the <see cref="Release"/> property.
        /// </summary>
        [DebuggerBrowsable(DebuggerBrowsableState.Never)]
        private ReleaseOptions? release;

        /// <summary>
        /// Backing field for the <see cref="PathFilters"/> property.
        /// </summary>
        [DebuggerBrowsable(DebuggerBrowsableState.Never)]
        private IReadOnlyList<FilterPath>? pathFilters;

        /// <summary>
        /// Backing field for the <see cref="Inherit"/> property.
        /// </summary>
        [DebuggerBrowsable(DebuggerBrowsableState.Never)]
        private bool inherit;

        /// <summary>
        /// Default value for <see cref="VersionPrecision"/>.
        /// </summary>
        public const VersionPrecision DefaultVersionPrecision = VersionPrecision.Minor;

        /// <summary>
        /// The placeholder that may appear in the <see cref="Version"/> property's <see cref="SemanticVersion.Prerelease"/>
        /// to specify where the version height should appear in a computed semantic version.
        /// </summary>
        /// <remarks>
        /// When this macro does not appear in the string, the version height is set as the first unspecified integer of the 4-integer version.
        /// If all 4 integers in a version are specified, and the macro does not appear, the version height isn't inserted anywhere.
        /// </remarks>
        public const string VersionHeightPlaceholder = "{height}";

        /// <summary>
        /// The default value for the <see cref="SemVer1NumericIdentifierPaddingOrDefault"/> property.
        /// </summary>
        private const int DefaultSemVer1NumericIdentifierPadding = 4;

        /// <summary>
        /// The default value for the <see cref="GitCommitIdShortFixedLength"/> property.
        /// </summary>
        public const int DefaultGitCommitIdShortFixedLength = 10;

        /// <summary>
        /// The $schema field that should be serialized when writing
        /// </summary>
        [JsonProperty(PropertyName = "$schema")]
        public string Schema => "https://raw.githubusercontent.com/dotnet/Nerdbank.GitVersioning/master/src/NerdBank.GitVersioning/version.schema.json";

        /// <summary>
        /// Initializes a new instance of the <see cref="VersionOptions"/> class.
        /// </summary>
        public VersionOptions()
        {
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="VersionOptions"/> class.
        /// </summary>
        /// <param name="copyFrom">Another instance to copy values from.</param>
        public VersionOptions(VersionOptions copyFrom)
        {
            Requires.NotNull(copyFrom, nameof(copyFrom));

            this.gitCommitIdPrefix = copyFrom.gitCommitIdPrefix;
            this.version = copyFrom.version;
            this.assemblyVersion = copyFrom.assemblyVersion is object ? new AssemblyVersionOptions(copyFrom.assemblyVersion) : null;
            this.buildNumberOffset = copyFrom.buildNumberOffset;
            this.semVer1NumericIdentifierPadding = copyFrom.semVer1NumericIdentifierPadding;
            this.gitCommitIdShortFixedLength = copyFrom.gitCommitIdShortFixedLength;
            this.gitCommitIdShortAutoMinimum = copyFrom.gitCommitIdShortAutoMinimum;
            this.nuGetPackageVersion = copyFrom.nuGetPackageVersion is object ? new NuGetPackageVersionOptions(copyFrom.nuGetPackageVersion) : null;
            this.publicReleaseRefSpec = copyFrom.publicReleaseRefSpec?.ToList();
            this.cloudBuild = copyFrom.cloudBuild is object ? new CloudBuildOptions(copyFrom.cloudBuild) : null;
            this.release = copyFrom.release is object ? new ReleaseOptions(copyFrom.release) : null;
            this.pathFilters = copyFrom.pathFilters?.ToList();
        }

        /// <summary>
        /// Gets or sets the default version to use.
        /// </summary>
        [JsonProperty(DefaultValueHandling = DefaultValueHandling.Ignore)]
        public SemanticVersion? Version
        {
            get => this.version;
            set => this.SetIfNotReadOnly(ref this.version, value);
        }

        /// <summary>
        /// Gets or sets the version to use particularly for the <see cref="AssemblyVersionAttribute"/>
        /// instead of the default <see cref="Version"/>.
        /// </summary>
        /// <value>An instance of <see cref="System.Version"/> or <c>null</c> to simply use the default <see cref="Version"/>.</value>
        [JsonProperty(DefaultValueHandling = DefaultValueHandling.Ignore)]
        public AssemblyVersionOptions? AssemblyVersion
        {
            get => this.assemblyVersion;
            set => this.SetIfNotReadOnly(ref this.assemblyVersion, value);
        }

        /// <summary>
        /// Gets or sets the prefix for git commit id in version.
        /// Because of semver rules the prefix must lead with a [A-z_] character (not a number) and it cannot be the empty string.
        /// If <c>null</c> 'g' will be used.
        /// </summary>
        /// <value>A prefix for git commit id.</value>
        [JsonProperty(DefaultValueHandling = DefaultValueHandling.Ignore)]
        public string? GitCommitIdPrefix
        {
            get => this.gitCommitIdPrefix;
            set
            {
                if (string.IsNullOrWhiteSpace(value))
                {
                    throw new ArgumentNullException(nameof(value), $"{nameof(this.GitCommitIdPrefix)} can't be empty");
                }
                char first = value![0];
                if (first < 'A' || (first > 'Z' && first < 'a' && first != '_') || first > 'z')
                {
                    throw new ArgumentException(nameof(value), $"{nameof(this.GitCommitIdPrefix)} must lead with a [A-z_] character (not a number)");
                }

                this.SetIfNotReadOnly(ref this.gitCommitIdPrefix, value);
            }
        }

        /// <summary>
        /// Gets the version to use particularly for the <see cref="AssemblyVersionAttribute"/>
        /// instead of the default <see cref="Version"/>.
        /// </summary>
        /// <value>An instance of <see cref="System.Version"/> or <c>null</c> to simply use the default <see cref="Version"/>.</value>
        [JsonIgnore]
        public AssemblyVersionOptions AssemblyVersionOrDefault => this.AssemblyVersion ?? AssemblyVersionOptions.DefaultInstance;

        /// <summary>
        /// Gets or sets a number to add to the git height when calculating the version height,
        /// which typically is used in the <see cref="Version.Build"/> portion of the computed version.
        /// </summary>
        /// <value>Any integer (0, positive, or negative).</value>
        /// <remarks>
        /// An error will result if this value is negative with such a magnitude as to exceed the git height,
        /// resulting in a negative build number.
        /// </remarks>
        [JsonProperty(DefaultValueHandling = DefaultValueHandling.Ignore)]
        [Obsolete("Use " + nameof(VersionHeightOffset) + " instead.")]
        [EditorBrowsable(EditorBrowsableState.Never)]
        public int? BuildNumberOffset
        {
            get => this.buildNumberOffset;
            set => this.SetIfNotReadOnly(ref this.buildNumberOffset, value);
        }

        /// <summary>
        /// Gets or sets a number to add to the git height when calculating the <see cref="Version.Build"/> number.
        /// </summary>
        /// <value>Any integer (0, positive, or negative).</value>
        /// <remarks>
        /// An error will result if this value is negative with such a magnitude as to exceed the git height,
        /// resulting in a negative build number.
        /// </remarks>
        [JsonProperty(DefaultValueHandling = DefaultValueHandling.Ignore)]
        public int? VersionHeightOffset
        {
#pragma warning disable CS0618
            get => this.BuildNumberOffset;
            set => this.BuildNumberOffset = value;
#pragma warning restore CS0618
        }

        /// <summary>
        /// Gets a number to add to the git height when calculating the <see cref="Version.Build"/> number.
        /// </summary>
        /// <value>Any integer (0, positive, or negative).</value>
        /// <remarks>
        /// An error will result if this value is negative with such a magnitude as to exceed the git height,
        /// resulting in a negative build number.
        /// </remarks>
        [JsonIgnore]
        [Obsolete("Use " + nameof(VersionHeightOffsetOrDefault) + " instead.")]
        [EditorBrowsable(EditorBrowsableState.Never)]
        public int BuildNumberOffsetOrDefault => this.BuildNumberOffset ?? 0;

        /// <summary>
        /// Gets a number to add to the git height when calculating the <see cref="Version.Build"/> number.
        /// </summary>
        /// <value>Any integer (0, positive, or negative).</value>
        /// <remarks>
        /// An error will result if this value is negative with such a magnitude as to exceed the git height,
        /// resulting in a negative build number.
        /// </remarks>
        [JsonIgnore]
        public int VersionHeightOffsetOrDefault
        {
#pragma warning disable CS0618
            get => this.BuildNumberOffsetOrDefault;
#pragma warning restore CS0618
        }

        /// <summary>
        /// Gets or sets the minimum number of digits to use for numeric identifiers in SemVer 1.
        /// </summary>
        [JsonProperty(DefaultValueHandling = DefaultValueHandling.Ignore)]
        public int? SemVer1NumericIdentifierPadding
        {
            get => this.semVer1NumericIdentifierPadding;
            set => this.SetIfNotReadOnly(ref this.semVer1NumericIdentifierPadding, value);
        }

        /// <summary>
        /// Gets the minimum number of digits to use for numeric identifiers in SemVer 1.
        /// </summary>
        [JsonIgnore]
        public int SemVer1NumericIdentifierPaddingOrDefault => this.SemVer1NumericIdentifierPadding ?? DefaultSemVer1NumericIdentifierPadding;

        /// <summary>
        /// Gets or sets the abbreviated git commit hash length.
        /// </summary>
        [JsonProperty(DefaultValueHandling = DefaultValueHandling.Ignore)]
        public int? GitCommitIdShortFixedLength
        {
            get => this.gitCommitIdShortFixedLength;
            set => this.SetIfNotReadOnly(ref this.gitCommitIdShortFixedLength, value);
        }

        /// <summary>
        /// Gets or sets the abbreviated git commit hash length minimum value.
        /// The git repository provides the value.
        /// If set to 0 or a git repository is not available, <see cref="GitCommitIdShortFixedLength"/> is used.
        /// The value is 0 by default.
        /// </summary>
        [JsonProperty(DefaultValueHandling = DefaultValueHandling.Ignore)]
        public int? GitCommitIdShortAutoMinimum
        {
            get => this.gitCommitIdShortAutoMinimum;
            set => this.SetIfNotReadOnly(ref this.gitCommitIdShortAutoMinimum, value);
        }

        /// <summary>
        /// Gets or sets the options around NuGet version strings
        /// </summary>
        [JsonProperty(DefaultValueHandling = DefaultValueHandling.Ignore)]
        public NuGetPackageVersionOptions? NuGetPackageVersion
        {
            get => this.nuGetPackageVersion;
            set => this.SetIfNotReadOnly(ref this.nuGetPackageVersion, value);
        }

        /// <summary>
        /// Gets the options around NuGet version strings
        /// </summary>
        [JsonIgnore]
        public NuGetPackageVersionOptions NuGetPackageVersionOrDefault => this.NuGetPackageVersion ?? NuGetPackageVersionOptions.DefaultInstance;

        /// <summary>
        /// Gets or sets an array of regular expressions that describes branch or tag names that should
        /// be built with PublicRelease=true as the default value on build servers.
        /// </summary>
        [JsonProperty(DefaultValueHandling = DefaultValueHandling.Ignore)]
        public IReadOnlyList<string>? PublicReleaseRefSpec
        {
            get => this.publicReleaseRefSpec;
            set => this.SetIfNotReadOnly(ref this.publicReleaseRefSpec, value);
        }

        /// <summary>
        /// Gets an array of regular expressions that describes branch or tag names that should
        /// be built with PublicRelease=true as the default value on build servers.
        /// </summary>
        [JsonIgnore]
        public IReadOnlyList<string> PublicReleaseRefSpecOrDefault => this.PublicReleaseRefSpec ?? Array.Empty<string>();

        /// <summary>
        /// Gets or sets the options around cloud build.
        /// </summary>
        [JsonProperty(DefaultValueHandling = DefaultValueHandling.Ignore)]
        public CloudBuildOptions? CloudBuild
        {
            get => this.cloudBuild;
            set => this.SetIfNotReadOnly(ref this.cloudBuild, value);
        }

        /// <summary>
        /// Gets the options around cloud build.
        /// </summary>
        [JsonIgnore]
        public CloudBuildOptions CloudBuildOrDefault => this.CloudBuild ?? CloudBuildOptions.DefaultInstance;

        /// <summary>
        /// Gets or sets the options for the prepare-release command
        /// </summary>
        [JsonProperty(DefaultValueHandling = DefaultValueHandling.Ignore)]
        public ReleaseOptions? Release
        {
            get => this.release;
            set => this.SetIfNotReadOnly(ref this.release, value);
        }

        /// <summary>
        /// Gets the options for the prepare-release command
        /// </summary>
        [JsonIgnore]
        public ReleaseOptions ReleaseOrDefault => this.Release ?? ReleaseOptions.DefaultInstance;

        /// <summary>
        /// Gets or sets a list of paths to use to filter commits when calculating version height.
        /// If a given commit does not affect any paths in this filter, it is ignored for version height calculations.
        /// Paths should be relative to the root of the repository.
        /// </summary>
        [JsonProperty(DefaultValueHandling = DefaultValueHandling.Ignore)]
        public IReadOnlyList<FilterPath>? PathFilters
        {
            get => this.pathFilters;
            set => this.SetIfNotReadOnly(ref this.pathFilters, value);
        }

        /// <summary>
        /// Gets or sets a value indicating whether this options object should inherit from an ancestor any settings that are not explicitly set in this one.
        /// </summary>
        /// <remarks>
        /// When this is <c>true</c>, this object may not completely describe the options to be applied.
        /// </remarks>
        [JsonProperty(DefaultValueHandling = DefaultValueHandling.Ignore)]
        public bool Inherit
        {
            get => this.inherit;
            set => this.SetIfNotReadOnly(ref this.inherit, value);
        }

        /// <summary>
        /// Gets a value indicating whether this instance rejects all attempts to mutate it.
        /// </summary>
        [JsonIgnore]
        public bool IsFrozen => this.isFrozen;

        /// <summary>
        /// Gets the position in a computed version that the version height should appear.
        /// </summary>
        [JsonIgnore]
        public SemanticVersion.Position? VersionHeightPosition
        {
            get
            {
                return this.version?.VersionHeightPosition;
            }
        }

        /// <summary>
        /// Gets the position in a computed version that the first 16 bits of a git commit ID should appear, if any.
        /// </summary>
        [JsonIgnore]
        internal SemanticVersion.Position? GitCommitIdPosition
        {
            get
            {
                // We can only store the git commit ID info after there was a place to put the version height.
                // We don't want to store the commit ID (which is effectively a random integer) in the revision slot
                // if the version height does not appear, or only appears later (in the -prerelease tag) since that
                // would mess up version ordering.
                if (this.VersionHeightPosition == SemanticVersion.Position.Build)
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
        /// Gets the debugger display for this instance.
        /// </summary>
        private string DebuggerDisplay => this.Version?.ToString() ?? (this.Inherit ? "Inheriting version info" : "(missing version)");

        /// <summary>
        /// Initializes a new instance of the <see cref="VersionOptions"/> class
        /// with <see cref="Version"/> initialized with the specified parameters.
        /// </summary>
        /// <param name="version">The version number.</param>
        /// <param name="unstableTag">The prerelease tag, if any.</param>
        /// <returns>The new instance of <see cref="VersionOptions"/>.</returns>
        public static VersionOptions FromVersion(Version version, string? unstableTag = null)
        {
            return new VersionOptions
            {
                Version = new SemanticVersion(version, unstableTag),
            };
        }

        /// <summary>
        /// Gets the <see cref="JsonSerializerSettings"/> to use based on certain requirements.
        /// The $schema property is not serialized when using this overload.
        /// </summary>
        /// <param name="includeDefaults">A value indicating whether default values should be serialized.</param>
        /// <returns>The serializer settings to use.</returns>
        public static JsonSerializerSettings GetJsonSettings(bool includeDefaults) => GetJsonSettings(includeDefaults, includeSchemaProperty: false);

        /// <summary>
        /// Gets the <see cref="JsonSerializerSettings"/> to use based on certain requirements.
        /// Path filters cannot be serialized or deserialized when using this overload.
        /// </summary>
        /// <param name="includeDefaults">A value indicating whether default values should be serialized.</param>
        /// <param name="includeSchemaProperty">A value indicating whether the $schema property should be serialized.</param>
        /// <returns>The serializer settings to use.</returns>
        public static JsonSerializerSettings GetJsonSettings(bool includeDefaults, bool includeSchemaProperty) => GetJsonSettings(includeDefaults, includeSchemaProperty, repoRelativeBaseDirectory: null);

        /// <summary>
        /// Gets the <see cref="JsonSerializerSettings"/> to use based on certain requirements.
        /// </summary>
        /// <param name="includeDefaults">A value indicating whether default values should be serialized.</param>
        /// <param name="includeSchemaProperty">A value indicating whether the $schema property should be serialized.</param>
        /// <param name="repoRelativeBaseDirectory">
        /// Directory (relative to the root of the repository) that path
        /// filters should be relative to.
        /// This should be the directory where the version.json file resides.
        /// An empty string represents the root of the repository.
        /// Passing <c>null</c> will mean path filters cannot be serialized.
        /// </param>
        /// <returns>The serializer settings to use.</returns>
        public static JsonSerializerSettings GetJsonSettings(bool includeDefaults = false, bool includeSchemaProperty = false, string? repoRelativeBaseDirectory = null)
        {
            return new JsonSerializerSettings
            {
                Converters = new JsonConverter[] {
                    new VersionConverter(),
                    new SemanticVersionJsonConverter(),
                    new AssemblyVersionOptionsConverter(includeDefaults),
                    new StringEnumConverter() { CamelCaseText = true },
                    new FilterPathJsonConverter(repoRelativeBaseDirectory),
                },
                ContractResolver = new VersionOptionsContractResolver
                {
                    IncludeDefaults = includeDefaults,
                    IncludeSchemaProperty = includeSchemaProperty,
                },
                Formatting = Formatting.Indented,
            };
        }

        /// <summary>
        /// Checks equality against another object.
        /// </summary>
        /// <param name="obj">The other instance.</param>
        /// <returns><c>true</c> if the instances have equal values; <c>false</c> otherwise.</returns>
        public override bool Equals(object? obj)
        {
            return this.Equals(obj as VersionOptions);
        }

        /// <summary>
        /// Gets a hash code for this instance.
        /// </summary>
        /// <returns>The hash code.</returns>
        public override int GetHashCode() => EqualWithDefaultsComparer.Singleton.GetHashCode(this);

        /// <summary>
        /// Checks equality against another instance of this class.
        /// </summary>
        /// <param name="other">The other instance.</param>
        /// <returns><c>true</c> if the instances have equal values; <c>false</c> otherwise.</returns>
        public bool Equals(VersionOptions? other) => EqualWithDefaultsComparer.Singleton.Equals(this, other);

        /// <summary>
        /// Freezes this instance so no more changes can be made to it.
        /// </summary>
        public void Freeze()
        {
            if (!this.isFrozen)
            {
                this.isFrozen = true;
                this.assemblyVersion?.Freeze();
                this.nuGetPackageVersion?.Freeze();
                this.publicReleaseRefSpec = this.publicReleaseRefSpec is object ? new ReadOnlyCollection<string>(this.publicReleaseRefSpec.ToList()) : null;
                this.cloudBuild?.Freeze();
                this.release?.Freeze();
                this.pathFilters = this.pathFilters is object ? new ReadOnlyCollection<FilterPath>(this.pathFilters.ToList()) : null;
            }
        }

        /// <summary>
        /// Gets a value indicating whether <see cref="Version"/> is
        /// set and the only property on this class that is set.
        /// </summary>
        internal bool IsDefaultVersionTheOnlyPropertySet
        {
            get
            {
                return this.Version != null
                    && this.AssemblyVersion == null
                    && (this.CloudBuild?.IsDefault ?? true)
                    && this.VersionHeightOffset == 0
                    && !this.SemVer1NumericIdentifierPadding.HasValue
                    && !this.Inherit;
            }
        }

        /// <summary>
        /// Sets the value of a field if this instance is not marked as read only.
        /// </summary>
        /// <typeparam name="T">The type of the value stored by the field.</typeparam>
        /// <param name="field">The field to change.</param>
        /// <param name="value">The value to set.</param>
        private void SetIfNotReadOnly<T>(ref T field, T value)
        {
            Verify.Operation(!this.isFrozen, "This instance is read only.");
            field = value;
        }

        /// <summary>
        /// The class that contains settings for the <see cref="NuGetPackageVersion" /> property.
        /// </summary>
        public class NuGetPackageVersionOptions : IEquatable<NuGetPackageVersionOptions>
        {
            /// <summary>
            /// The default (uninitialized) instance.
            /// </summary>
            internal static readonly NuGetPackageVersionOptions DefaultInstance = new NuGetPackageVersionOptions()
            {
                isFrozen = true,
                semVer = 1.0f,
            };

            [DebuggerBrowsable(DebuggerBrowsableState.Never)]
            private bool isFrozen;

            [DebuggerBrowsable(DebuggerBrowsableState.Never)]
            private float? semVer;

            /// <summary>
            /// Initializes a new instance of the <see cref="NuGetPackageVersionOptions" /> class.
            /// </summary>
            public NuGetPackageVersionOptions()
            {
            }

            /// <summary>
            /// Initializes a new instance of the <see cref="NuGetPackageVersionOptions" /> class.
            /// </summary>
            public NuGetPackageVersionOptions(NuGetPackageVersionOptions copyFrom)
            {
                this.semVer = copyFrom.semVer;
            }

            /// <summary>
            /// Gets or sets the version of SemVer (e.g. 1 or 2) that should be used when generating the package version.
            /// </summary>
            [JsonProperty(DefaultValueHandling = DefaultValueHandling.Ignore)]
            public float? SemVer
            {
                get => this.semVer;
                set => this.SetIfNotReadOnly(ref this.semVer, value);
            }

            /// <summary>
            /// Gets the version of SemVer (e.g. 1 or 2) that should be used when generating the package version.
            /// </summary>
            [JsonIgnore]
            public float? SemVerOrDefault => this.SemVer ?? DefaultInstance.SemVer;

            /// <summary>
            /// Gets a value indicating whether this instance rejects all attempts to mutate it.
            /// </summary>
            [JsonIgnore]
            public bool IsFrozen => this.isFrozen;

            /// <summary>
            /// Freezes this instance so no more changes can be made to it.
            /// </summary>
            public void Freeze() => this.isFrozen = true;

            /// <inheritdoc />
            public override bool Equals(object? obj) => this.Equals(obj as NuGetPackageVersionOptions);

            /// <inheritdoc />
            public bool Equals(NuGetPackageVersionOptions? other) => EqualWithDefaultsComparer.Singleton.Equals(this, other);

            /// <inheritdoc />
            public override int GetHashCode() => EqualWithDefaultsComparer.Singleton.GetHashCode(this);

            /// <summary>
            /// Gets a value indicating whether this instance is equivalent to the default instance.
            /// </summary>
            internal bool IsDefault => this.Equals(DefaultInstance);

            /// <summary>
            /// Sets the value of a field if this instance is not marked as read only.
            /// </summary>
            /// <typeparam name="T">The type of the value stored by the field.</typeparam>
            /// <param name="field">The field to change.</param>
            /// <param name="value">The value to set.</param>
            private void SetIfNotReadOnly<T>(ref T field, T value)
            {
                Verify.Operation(!this.isFrozen, "This instance is read only.");
                field = value;
            }

            internal class EqualWithDefaultsComparer : IEqualityComparer<NuGetPackageVersionOptions?>
            {
                internal static readonly EqualWithDefaultsComparer Singleton = new EqualWithDefaultsComparer();

                private EqualWithDefaultsComparer() { }

                /// <inheritdoc />
                public bool Equals(NuGetPackageVersionOptions? x, NuGetPackageVersionOptions? y)
                {
                    if (ReferenceEquals(x, y))
                    {
                        return true;
                    }

                    if (x == null || y == null)
                    {
                        return false;
                    }

                    return x.SemVerOrDefault == y.SemVerOrDefault;
                }

                /// <inheritdoc />
                public int GetHashCode(NuGetPackageVersionOptions? obj)
                {
                    return obj?.SemVerOrDefault.GetHashCode() ?? 0;
                }
            }
        }

        /// <summary>
        /// Describes the details of how the AssemblyVersion value will be calculated.
        /// </summary>
        public class AssemblyVersionOptions : IEquatable<AssemblyVersionOptions>
        {
            /// <summary>
            /// The default (uninitialized) instance.
            /// </summary>
            internal static readonly AssemblyVersionOptions DefaultInstance = new AssemblyVersionOptions()
            {
                isFrozen = true,
                precision = DefaultVersionPrecision,
            };

            [DebuggerBrowsable(DebuggerBrowsableState.Never)]
            private bool isFrozen;

            [DebuggerBrowsable(DebuggerBrowsableState.Never)]
            private Version? version;

            [DebuggerBrowsable(DebuggerBrowsableState.Never)]
            private VersionPrecision? precision;

            /// <summary>
            /// Initializes a new instance of the <see cref="AssemblyVersionOptions"/> class.
            /// </summary>
            public AssemblyVersionOptions()
            {
            }

            /// <summary>
            /// Initializes a new instance of the <see cref="AssemblyVersionOptions"/> class.
            /// </summary>
            /// <param name="version">The assembly version (with major.minor components).</param>
            /// <param name="precision">The additional version precision to add toward matching the AssemblyFileVersion.</param>
            public AssemblyVersionOptions(Version version, VersionPrecision? precision = null)
            {
                this.Version = version;
                this.Precision = precision;
            }

            /// <summary>
            /// Initializes a new instance of the <see cref="AssemblyVersionOptions"/> class.
            /// </summary>
            public AssemblyVersionOptions(AssemblyVersionOptions copyFrom)
            {
                this.version = copyFrom.version;
                this.precision = copyFrom.precision;
            }

            /// <summary>
            /// Gets or sets the major.minor components of the assembly version.
            /// </summary>
            [JsonProperty(DefaultValueHandling = DefaultValueHandling.Ignore)]
            public Version? Version
            {
                get => this.version;
                set => this.SetIfNotReadOnly(ref this.version, value);
            }

            /// <summary>
            /// Gets or sets the additional version precision to add toward matching the AssemblyFileVersion.
            /// </summary>
            [JsonProperty(DefaultValueHandling = DefaultValueHandling.Ignore)]
            public VersionPrecision? Precision
            {
                get => this.precision;
                set => this.SetIfNotReadOnly(ref this.precision, value);
            }

            /// <summary>
            /// Gets the additional version precision to add toward matching the AssemblyFileVersion.
            /// </summary>
            [JsonIgnore]
            public VersionPrecision PrecisionOrDefault => this.Precision ?? DefaultVersionPrecision;

            /// <summary>
            /// Gets a value indicating whether this instance rejects all attempts to mutate it.
            /// </summary>
            [JsonIgnore]
            public bool IsFrozen => this.isFrozen;

            /// <summary>
            /// Freezes this instance so no more changes can be made to it.
            /// </summary>
            public void Freeze() => this.isFrozen = true;

            /// <inheritdoc />
            public override bool Equals(object? obj) => this.Equals(obj as AssemblyVersionOptions);

            /// <inheritdoc />
            public bool Equals(AssemblyVersionOptions? other) => EqualWithDefaultsComparer.Singleton.Equals(this, other);

            /// <inheritdoc />
            public override int GetHashCode() => EqualWithDefaultsComparer.Singleton.GetHashCode(this);

            /// <summary>
            /// Gets a value indicating whether this instance is equivalent to the default instance.
            /// </summary>
            internal bool IsDefault => this.Equals(DefaultInstance);

            /// <summary>
            /// Sets the value of a field if this instance is not marked as read only.
            /// </summary>
            /// <typeparam name="T">The type of the value stored by the field.</typeparam>
            /// <param name="field">The field to change.</param>
            /// <param name="value">The value to set.</param>
            private void SetIfNotReadOnly<T>(ref T field, T value)
            {
                Verify.Operation(!this.isFrozen, "This instance is read only.");
                field = value;
            }

            internal class EqualWithDefaultsComparer : IEqualityComparer<AssemblyVersionOptions?>
            {
                internal static readonly EqualWithDefaultsComparer Singleton = new EqualWithDefaultsComparer();

                private EqualWithDefaultsComparer() { }

                /// <inheritdoc />
                public bool Equals(AssemblyVersionOptions? x, AssemblyVersionOptions? y)
                {
                    if (ReferenceEquals(x, y))
                    {
                        return true;
                    }

                    if (x is null || y is null)
                    {
                        return false;
                    }

                    return EqualityComparer<Version?>.Default.Equals(x.Version, y.Version)
                        && x.PrecisionOrDefault == y.PrecisionOrDefault;
                }

                /// <inheritdoc />
                public int GetHashCode(AssemblyVersionOptions? obj)
                {
                    if (obj is null)
                    {
                        return 0;
                    }

                    return (obj.Version?.GetHashCode() ?? 0) + (int)obj.PrecisionOrDefault;
                }
            }
        }

        /// <summary>
        /// Options that are applicable specifically to cloud builds (e.g. VSTS, AppVeyor, TeamCity)
        /// </summary>
        public class CloudBuildOptions : IEquatable<CloudBuildOptions>
        {
            /// <summary>
            /// The default (uninitialized) instance.
            /// </summary>
            internal static readonly CloudBuildOptions DefaultInstance = new CloudBuildOptions()
            {
                isFrozen = true,
                setAllVariables = false,
                setVersionVariables = true,
            };

            [DebuggerBrowsable(DebuggerBrowsableState.Never)]
            private bool isFrozen;

            [DebuggerBrowsable(DebuggerBrowsableState.Never)]
            private bool? setAllVariables;

            [DebuggerBrowsable(DebuggerBrowsableState.Never)]
            private bool? setVersionVariables;

            [DebuggerBrowsable(DebuggerBrowsableState.Never)]
            private CloudBuildNumberOptions? buildNumber;

            /// <summary>
            /// Initializes a new instance of the <see cref="CloudBuildOptions"/> class.
            /// </summary>
            public CloudBuildOptions()
            {
            }

            /// <summary>
            /// Initializes a new instance of the <see cref="CloudBuildOptions"/> class.
            /// </summary>
            /// <param name="copyFrom">Another instance to copy values from</param>
            public CloudBuildOptions(CloudBuildOptions copyFrom)
            {
                this.setAllVariables = copyFrom.setAllVariables;
                this.setVersionVariables = copyFrom.setVersionVariables;
                this.buildNumber = copyFrom.buildNumber is object ? new CloudBuildNumberOptions(copyFrom.buildNumber) : null;
            }

            /// <summary>
            /// Gets or sets a value indicating whether to elevate all build properties to cloud build variables prefaced with "NBGV_".
            /// </summary>
            public bool? SetAllVariables
            {
                get => this.setAllVariables;
                set => this.SetIfNotReadOnly(ref this.setAllVariables, value);
            }

            /// <summary>
            /// Gets or sets a value indicating whether to elevate certain calculated version build properties to cloud build variables.
            /// </summary>
            public bool? SetVersionVariables
            {
                get => this.setVersionVariables;
                set => this.SetIfNotReadOnly(ref this.setVersionVariables, value);
            }

            /// <summary>
            /// Gets a value indicating whether to elevate all build properties to cloud build variables prefaced with "NBGV_".
            /// </summary>
            [JsonIgnore]
            public bool SetAllVariablesOrDefault => this.SetAllVariables ?? DefaultInstance.SetAllVariables!.Value;

            /// <summary>
            /// Gets a value indicating whether to elevate certain calculated version build properties to cloud build variables.
            /// </summary>
            [JsonIgnore]
            public bool SetVersionVariablesOrDefault => this.SetVersionVariables ?? DefaultInstance.SetVersionVariables!.Value;

            /// <summary>
            /// Gets or sets options around how and whether to set the build number preset by the cloud build with one enriched with version information.
            /// </summary>
            public CloudBuildNumberOptions? BuildNumber
            {
                get => this.buildNumber;
                set => this.SetIfNotReadOnly(ref this.buildNumber, value);
            }

            /// <summary>
            /// Gets options around how and whether to set the build number preset by the cloud build with one enriched with version information.
            /// </summary>
            [JsonIgnore]
            public CloudBuildNumberOptions BuildNumberOrDefault => this.BuildNumber ?? CloudBuildNumberOptions.DefaultInstance;

            /// <summary>
            /// Gets a value indicating whether this instance rejects all attempts to mutate it.
            /// </summary>
            [JsonIgnore]
            public bool IsFrozen => this.isFrozen;

            /// <summary>
            /// Freezes this instance so no more changes can be made to it.
            /// </summary>
            public void Freeze()
            {
                if (!this.isFrozen)
                {
                    this.isFrozen = true;
                    this.buildNumber?.Freeze();
                }
            }

            /// <inheritdoc />
            public override bool Equals(object? obj) => this.Equals(obj as CloudBuildOptions);

            /// <inheritdoc />
            public bool Equals(CloudBuildOptions? other) => EqualWithDefaultsComparer.Singleton.Equals(this, other);

            /// <inheritdoc />
            public override int GetHashCode() => EqualWithDefaultsComparer.Singleton.GetHashCode(this);

            /// <summary>
            /// Gets a value indicating whether this instance is equivalent to the default instance.
            /// </summary>
            internal bool IsDefault => this.Equals(DefaultInstance);

            /// <summary>
            /// Sets the value of a field if this instance is not marked as read only.
            /// </summary>
            /// <typeparam name="T">The type of the value stored by the field.</typeparam>
            /// <param name="field">The field to change.</param>
            /// <param name="value">The value to set.</param>
            private void SetIfNotReadOnly<T>(ref T field, T value)
            {
                Verify.Operation(!this.isFrozen, "This instance is read only.");
                field = value;
            }

            internal class EqualWithDefaultsComparer : IEqualityComparer<CloudBuildOptions?>
            {
                internal static readonly EqualWithDefaultsComparer Singleton = new EqualWithDefaultsComparer();

                private EqualWithDefaultsComparer() { }

                /// <inheritdoc />
                public bool Equals(CloudBuildOptions? x, CloudBuildOptions? y)
                {
                    if (ReferenceEquals(x, y))
                    {
                        return true;
                    }

                    if (x is null || y is null)
                    {
                        return false;
                    }

                    return x.SetVersionVariablesOrDefault == y.SetVersionVariablesOrDefault
                        && x.SetAllVariablesOrDefault == y.SetAllVariablesOrDefault
                        && CloudBuildNumberOptions.EqualWithDefaultsComparer.Singleton.Equals(x.BuildNumberOrDefault, y.BuildNumberOrDefault);
                }

                /// <inheritdoc />
                public int GetHashCode(CloudBuildOptions? obj)
                {
                    if (obj is null)
                    {
                        return 0;
                    }

                    return (obj.SetVersionVariablesOrDefault ? 1 : 0)
                        + (obj.SetAllVariablesOrDefault ? 1 : 0)
                        + obj.BuildNumberOrDefault.GetHashCode();
                }
            }
        }

        /// <summary>
        /// Override the build number preset by the cloud build with one enriched with version information.
        /// </summary>
        public class CloudBuildNumberOptions : IEquatable<CloudBuildNumberOptions>
        {
            /// <summary>
            /// The default (uninitialized) instance.
            /// </summary>
            internal static readonly CloudBuildNumberOptions DefaultInstance = new CloudBuildNumberOptions()
            {
                isFrozen = true,
                enabled = false,
            };

            [DebuggerBrowsable(DebuggerBrowsableState.Never)]
            private bool isFrozen;

            [DebuggerBrowsable(DebuggerBrowsableState.Never)]
            private bool? enabled;
            private CloudBuildNumberCommitIdOptions? includeCommitId;

            /// <summary>
            /// Initializes a new instance of the <see cref="CloudBuildNumberOptions"/> class.
            /// </summary>
            public CloudBuildNumberOptions()
            {
            }

            /// <summary>
            /// Initializes a new instance of the <see cref="CloudBuildNumberOptions"/> class.
            /// </summary>
            public CloudBuildNumberOptions(CloudBuildNumberOptions copyFrom)
            {
                this.enabled = copyFrom.enabled;
                this.includeCommitId = copyFrom.includeCommitId is object ? new CloudBuildNumberCommitIdOptions(copyFrom.includeCommitId) : null;
            }

            /// <summary>
            /// Gets or sets a value indicating whether to override the build number preset by the cloud build.
            /// </summary>
            public bool? Enabled
            {
                get => this.enabled;
                set => this.SetIfNotReadOnly(ref this.enabled, value);
            }

            /// <summary>
            /// Gets a value indicating whether to override the build number preset by the cloud build.
            /// </summary>
            [JsonIgnore]
            public bool EnabledOrDefault => this.Enabled ?? DefaultInstance.Enabled!.Value;

            /// <summary>
            /// Gets or sets when and where to include information about the git commit being built.
            /// </summary>
            public CloudBuildNumberCommitIdOptions? IncludeCommitId
            {
                get => this.includeCommitId;
                set => this.SetIfNotReadOnly(ref this.includeCommitId, value);
            }

            /// <summary>
            /// Gets when and where to include information about the git commit being built.
            /// </summary>
            [JsonIgnore]
            public CloudBuildNumberCommitIdOptions IncludeCommitIdOrDefault => this.IncludeCommitId ?? CloudBuildNumberCommitIdOptions.DefaultInstance;

            /// <summary>
            /// Gets a value indicating whether this instance rejects all attempts to mutate it.
            /// </summary>
            [JsonIgnore]
            public bool IsFrozen => this.isFrozen;

            /// <summary>
            /// Freezes this instance so no more changes can be made to it.
            /// </summary>
            public void Freeze()
            {
                if (!this.isFrozen)
                {
                    this.isFrozen = true;
                    this.IncludeCommitId?.Freeze();
                }
            }

            /// <inheritdoc />
            public override bool Equals(object? obj) => this.Equals(obj as CloudBuildNumberOptions);

            /// <inheritdoc />
            public bool Equals(CloudBuildNumberOptions? other) => EqualWithDefaultsComparer.Singleton.Equals(this, other);

            /// <inheritdoc />
            public override int GetHashCode() => EqualWithDefaultsComparer.Singleton.GetHashCode(this);

            /// <summary>
            /// Gets a value indicating whether this instance is equivalent to the default instance.
            /// </summary>
            internal bool IsDefault => this.Equals(DefaultInstance);

            /// <summary>
            /// Sets the value of a field if this instance is not marked as read only.
            /// </summary>
            /// <typeparam name="T">The type of the value stored by the field.</typeparam>
            /// <param name="field">The field to change.</param>
            /// <param name="value">The value to set.</param>
            private void SetIfNotReadOnly<T>(ref T field, T value)
            {
                Verify.Operation(!this.isFrozen, "This instance is read only.");
                field = value;
            }

            internal class EqualWithDefaultsComparer : IEqualityComparer<CloudBuildNumberOptions?>
            {
                internal static readonly EqualWithDefaultsComparer Singleton = new EqualWithDefaultsComparer();

                private EqualWithDefaultsComparer() { }

                /// <inheritdoc />
                public bool Equals(CloudBuildNumberOptions? x, CloudBuildNumberOptions? y)
                {
                    if (ReferenceEquals(x, y))
                    {
                        return true;
                    }

                    if (x is null || y is null)
                    {
                        return false;
                    }

                    return x.EnabledOrDefault == y.EnabledOrDefault
                        && CloudBuildNumberCommitIdOptions.EqualWithDefaultsComparer.Singleton.Equals(x.IncludeCommitIdOrDefault, y.IncludeCommitIdOrDefault);
                }

                /// <inheritdoc />
                public int GetHashCode(CloudBuildNumberOptions? obj)
                {
                    if (obj is null)
                    {
                        return 0;
                    }

                    return obj.EnabledOrDefault ? 1 : 0
                        + obj.IncludeCommitIdOrDefault.GetHashCode();
                }
            }
        }

        /// <summary>
        /// Describes when and where to include information about the git commit being built.
        /// </summary>
        public class CloudBuildNumberCommitIdOptions : IEquatable<CloudBuildNumberCommitIdOptions>
        {
            /// <summary>
            /// The default (uninitialized) instance.
            /// </summary>
            internal static readonly CloudBuildNumberCommitIdOptions DefaultInstance = new CloudBuildNumberCommitIdOptions()
            {
                isFrozen = true,
                when = CloudBuildNumberCommitWhen.NonPublicReleaseOnly,
                where = CloudBuildNumberCommitWhere.BuildMetadata,
            };

            [DebuggerBrowsable(DebuggerBrowsableState.Never)]
            private bool isFrozen;

            [DebuggerBrowsable(DebuggerBrowsableState.Never)]
            private CloudBuildNumberCommitWhen? when;

            [DebuggerBrowsable(DebuggerBrowsableState.Never)]
            private CloudBuildNumberCommitWhere? where;

            /// <summary>
            /// Initializes a new instance of the <see cref="CloudBuildNumberCommitIdOptions"/> class.
            /// </summary>
            public CloudBuildNumberCommitIdOptions()
            {
            }

            /// <summary>
            /// Initializes a new instance of the <see cref="CloudBuildNumberCommitIdOptions"/> class.
            /// </summary>
            public CloudBuildNumberCommitIdOptions(CloudBuildNumberCommitIdOptions copyFrom)
            {
                this.when = copyFrom.when;
                this.where = copyFrom.where;
            }

            /// <summary>
            /// Gets or sets the conditions when the commit ID is included in the build number.
            /// </summary>
            public CloudBuildNumberCommitWhen? When
            {
                get => this.when;
                set => this.SetIfNotReadOnly(ref this.when, value);
            }

            /// <summary>
            /// Gets the conditions when the commit ID is included in the build number.
            /// </summary>
            [JsonIgnore]
            public CloudBuildNumberCommitWhen WhenOrDefault => this.When ?? DefaultInstance.When!.Value;

            /// <summary>
            /// Gets or sets the position to include the commit ID information.
            /// </summary>
            public CloudBuildNumberCommitWhere? Where
            {
                get => this.where;
                set => this.SetIfNotReadOnly(ref this.where, value);
            }

            /// <summary>
            /// Gets the position to include the commit ID information.
            /// </summary>
            [JsonIgnore]
            public CloudBuildNumberCommitWhere WhereOrDefault => this.Where ?? DefaultInstance.Where!.Value;

            /// <summary>
            /// Gets a value indicating whether this instance rejects all attempts to mutate it.
            /// </summary>
            [JsonIgnore]
            public bool IsFrozen => this.isFrozen;

            /// <summary>
            /// Freezes this instance so no more changes can be made to it.
            /// </summary>
            public void Freeze() => this.isFrozen = true;

            /// <inheritdoc />
            public override bool Equals(object? obj) => this.Equals(obj as CloudBuildNumberCommitIdOptions);

            /// <inheritdoc />
            public bool Equals(CloudBuildNumberCommitIdOptions? other) => EqualWithDefaultsComparer.Singleton.Equals(this, other);
            /// <inheritdoc />
            public override int GetHashCode() => EqualWithDefaultsComparer.Singleton.GetHashCode(this);

            /// <summary>
            /// Gets a value indicating whether this instance is equivalent to the default instance.
            /// </summary>
            internal bool IsDefault => this.Equals(DefaultInstance);

            /// <summary>
            /// Sets the value of a field if this instance is not marked as read only.
            /// </summary>
            /// <typeparam name="T">The type of the value stored by the field.</typeparam>
            /// <param name="field">The field to change.</param>
            /// <param name="value">The value to set.</param>
            private void SetIfNotReadOnly<T>(ref T field, T value)
            {
                Verify.Operation(!this.isFrozen, "This instance is read only.");
                field = value;
            }

            internal class EqualWithDefaultsComparer : IEqualityComparer<CloudBuildNumberCommitIdOptions?>
            {
                internal static readonly EqualWithDefaultsComparer Singleton = new EqualWithDefaultsComparer();

                private EqualWithDefaultsComparer() { }

                /// <inheritdoc />
                public bool Equals(CloudBuildNumberCommitIdOptions? x, CloudBuildNumberCommitIdOptions? y)
                {
                    if (ReferenceEquals(x, y))
                    {
                        return true;
                    }

                    if (x is null || y is null)
                    {
                        return false;
                    }

                    return x.WhenOrDefault == y.WhenOrDefault
                        && x.WhereOrDefault == y.WhereOrDefault;
                }

                /// <inheritdoc />
                public int GetHashCode(CloudBuildNumberCommitIdOptions? obj)
                {
                    if (obj is null)
                    {
                        return 0;
                    }

                    return (int)obj.WhereOrDefault + (int)obj.WhenOrDefault * 0x10;
                }
            }
        }

        private class EqualWithDefaultsComparer : IEqualityComparer<VersionOptions?>
        {
            internal static readonly EqualWithDefaultsComparer Singleton = new EqualWithDefaultsComparer();

            private EqualWithDefaultsComparer() { }

            /// <inheritdoc />
            public bool Equals(VersionOptions? x, VersionOptions? y)
            {
                if (ReferenceEquals(x, y))
                {
                    return true;
                }

                if (x is null || y is null)
                {
                    return false;
                }

                return EqualityComparer<SemanticVersion?>.Default.Equals(x.Version, y.Version)
                    && AssemblyVersionOptions.EqualWithDefaultsComparer.Singleton.Equals(x.AssemblyVersionOrDefault, y.AssemblyVersionOrDefault)
                    && NuGetPackageVersionOptions.EqualWithDefaultsComparer.Singleton.Equals(x.NuGetPackageVersionOrDefault, y.NuGetPackageVersionOrDefault)
                    && CloudBuildOptions.EqualWithDefaultsComparer.Singleton.Equals(x.CloudBuildOrDefault, y.CloudBuildOrDefault)
                    && ReleaseOptions.EqualWithDefaultsComparer.Singleton.Equals(x.ReleaseOrDefault, y.ReleaseOrDefault)
                    && x.VersionHeightOffset == y.VersionHeightOffset;
            }

            /// <inheritdoc />
            public int GetHashCode(VersionOptions? obj)
            {
                return obj?.Version?.GetHashCode() ?? 0;
            }
        }

        /// <summary>
        /// The last component to control in a 4 integer version.
        /// </summary>
        public enum VersionPrecision
        {
            /// <summary>
            /// The first integer is the last number set. The rest will be zeros.
            /// </summary>
            Major,

            /// <summary>
            /// The second integer is the last number set. The rest will be zeros.
            /// </summary>
            Minor,

            /// <summary>
            /// The third integer is the last number set. The fourth will be zero.
            /// </summary>
            Build,

            /// <summary>
            /// All four integers will be set.
            /// </summary>
            Revision,
        }

        /// <summary>
        /// The conditions a commit ID is included in a cloud build number.
        /// </summary>
        public enum CloudBuildNumberCommitWhen
        {
            /// <summary>
            /// Always include the commit information in the cloud Build Number.
            /// </summary>
            Always,

            /// <summary>
            /// Only include the commit information when building a non-PublicRelease.
            /// </summary>
            NonPublicReleaseOnly,

            /// <summary>
            /// Never include the commit information.
            /// </summary>
            Never,
        }

        /// <summary>
        /// The position a commit ID can appear in a cloud build number.
        /// </summary>
        public enum CloudBuildNumberCommitWhere
        {
            /// <summary>
            /// The commit ID appears in build metadata (e.g. +ga1b2c3).
            /// </summary>
            BuildMetadata,

            /// <summary>
            /// The commit ID appears as the 4th integer in the version (e.g. 1.2.3.23523).
            /// </summary>
            FourthVersionComponent,
        }

        /// <summary>
        /// Encapsulates settings for the "prepare-release" command
        /// </summary>
        public class ReleaseOptions : IEquatable<ReleaseOptions>
        {
            /// <summary>
            /// The default (uninitialized) instance.
            /// </summary>
            internal static readonly ReleaseOptions DefaultInstance = new ReleaseOptions()
            {
                isFrozen = true,
                branchName = "v{version}",
                versionIncrement = ReleaseVersionIncrement.Minor,
                firstUnstableTag = "alpha"
            };

            [DebuggerBrowsable(DebuggerBrowsableState.Never)]
            private bool isFrozen;

            [DebuggerBrowsable(DebuggerBrowsableState.Never)]
            private string? branchName;

            [DebuggerBrowsable(DebuggerBrowsableState.Never)]
            private ReleaseVersionIncrement? versionIncrement;

            [DebuggerBrowsable(DebuggerBrowsableState.Never)]
            private string? firstUnstableTag;

            /// <summary>
            /// Initializes a new instance of the <see cref="ReleaseOptions"/> class
            /// </summary>
            public ReleaseOptions()
            {
            }

            /// <summary>
            /// Initializes a new instance of the <see cref="ReleaseOptions"/> class
            /// </summary>
            public ReleaseOptions(ReleaseOptions copyFrom)
            {
                this.branchName = copyFrom.branchName;
                this.versionIncrement = copyFrom.versionIncrement;
                this.firstUnstableTag = copyFrom.firstUnstableTag;
            }

            /// <summary>
            /// Gets or sets the branch name template for release branches
            /// </summary>
            [JsonProperty(DefaultValueHandling = DefaultValueHandling.Ignore)]
            public string? BranchName
            {
                get => this.branchName;
                set => this.SetIfNotReadOnly(ref this.branchName, value);
            }

            /// <summary>
            /// Gets the set branch name template for release branches
            /// </summary>
            [JsonIgnore]
            public string BranchNameOrDefault => this.BranchName ?? DefaultInstance.BranchName!;

            /// <summary>
            /// Gets or sets the setting specifying how to increment the version when creating a release
            /// </summary>
            [JsonProperty(DefaultValueHandling = DefaultValueHandling.Ignore)]
            public ReleaseVersionIncrement? VersionIncrement
            {
                get => this.versionIncrement;
                set => this.SetIfNotReadOnly(ref this.versionIncrement, value);
            }

            /// <summary>
            /// Gets or sets the setting specifying how to increment the version when creating a release.
            /// </summary>
            [JsonIgnore]
            public ReleaseVersionIncrement VersionIncrementOrDefault => this.VersionIncrement ?? DefaultInstance.VersionIncrement!.Value;

            /// <summary>
            /// Gets or sets the first/default prerelease tag for new versions.
            /// </summary>
            [JsonProperty(DefaultValueHandling = DefaultValueHandling.Ignore)]
            public string? FirstUnstableTag
            {
                get => this.firstUnstableTag;
                set => this.SetIfNotReadOnly(ref this.firstUnstableTag, value);
            }

            /// <summary>
            /// Gets or sets the first/default prerelease tag for new versions.
            /// </summary>
            [JsonIgnore]
            public string FirstUnstableTagOrDefault => this.FirstUnstableTag ?? DefaultInstance.FirstUnstableTag!;

            /// <summary>
            /// Gets a value indicating whether this instance rejects all attempts to mutate it.
            /// </summary>
            [JsonIgnore]
            public bool IsFrozen => this.isFrozen;

            /// <summary>
            /// Freezes this instance so no more changes can be made to it.
            /// </summary>
            public void Freeze() => this.isFrozen = true;

            /// <inheritdoc />
            public override bool Equals(object? obj) => this.Equals(obj as ReleaseOptions);

            /// <inheritdoc />
            public bool Equals(ReleaseOptions? other) => EqualWithDefaultsComparer.Singleton.Equals(this, other);

            /// <inheritdoc />
            public override int GetHashCode() => EqualWithDefaultsComparer.Singleton.GetHashCode(this);

            /// <summary>
            /// Gets a value indicating whether this instance is equivalent to the default instance.
            /// </summary>
            internal bool IsDefault => this.Equals(DefaultInstance);

            /// <summary>
            /// Sets the value of a field if this instance is not marked as read only.
            /// </summary>
            /// <typeparam name="T">The type of the value stored by the field.</typeparam>
            /// <param name="field">The field to change.</param>
            /// <param name="value">The value to set.</param>
            private void SetIfNotReadOnly<T>(ref T field, T value)
            {
                Verify.Operation(!this.isFrozen, "This instance is read only.");
                field = value;
            }

            internal class EqualWithDefaultsComparer : IEqualityComparer<ReleaseOptions?>
            {
                internal static readonly EqualWithDefaultsComparer Singleton = new EqualWithDefaultsComparer();

                private EqualWithDefaultsComparer() { }

                /// <inheritdoc />
                public bool Equals(ReleaseOptions? x, ReleaseOptions? y)
                {
                    if (ReferenceEquals(x, y))
                    {
                        return true;
                    }

                    if (x is null || y is null)
                    {
                        return false;
                    }

                    return StringComparer.Ordinal.Equals(x.BranchNameOrDefault, y.BranchNameOrDefault) &&
                           x.VersionIncrementOrDefault == y.VersionIncrementOrDefault &&
                           StringComparer.Ordinal.Equals(x.FirstUnstableTagOrDefault, y.FirstUnstableTagOrDefault);
                }

                /// <inheritdoc />
                public int GetHashCode(ReleaseOptions? obj)
                {
                    if (obj == null)
                        return 0;

                    unchecked
                    {
                        var hash = StringComparer.Ordinal.GetHashCode(obj.BranchNameOrDefault) * 397;
                        hash ^= (int)obj.VersionIncrementOrDefault;
                        hash ^= StringComparer.Ordinal.GetHashCode(obj.FirstUnstableTagOrDefault);
                        return hash;
                    }
                }
            }
        }

        /// <summary>
        /// Possible increments of the version after creating release branches
        /// </summary>
        public enum ReleaseVersionIncrement
        {
            /// <summary>
            /// Increment the major version after creating a release branch
            /// </summary>
            Major,

            /// <summary>
            /// Increment the minor version after creating a release branch
            /// </summary>
            Minor,

            /// <summary>
            /// Increment the build number (the third number in a version) after creating a release branch.
            /// </summary>
            Build,
        }
    }
}
