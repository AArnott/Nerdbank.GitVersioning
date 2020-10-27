﻿#nullable enable

using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Text;

namespace NerdBank.GitVersioning.Managed
{
    /// <summary>
    /// Represents a cache in which objects retrieved from a <see cref="GitPack"/>
    /// are cached. Caching these objects can be of interest, because retrieving
    /// data from a <see cref="GitPack"/> can be potentially expensive: the data is
    /// compressed and can be deltified.
    /// </summary>
    public abstract class GitPackCache
    {
        /// <summary>
        /// Attempts to retrieve a Git object from cache.
        /// </summary>
        /// <param name="offset">
        /// The offset of the Git object in the Git pack.
        /// </param>
        /// <param name="stream">
        /// A <see cref="Stream"/> which will be set to the cached Git object.
        /// </param>
        /// <returns>
        /// <see langword="true"/> if the object was found in cache; otherwise,
        /// <see langword="false"/>.
        /// </returns>
        public abstract bool TryOpen(int offset, [NotNullWhen(true)] out Stream? stream);

        /// <summary>
        /// Gets statistics about the cache usage.
        /// </summary>
        /// <param name="builder">
        /// A <see cref="StringBuilder"/> to which to write the statistics.
        /// </param>
        public abstract void GetCacheStatistics(StringBuilder builder);

        /// <summary>
        /// Adds a Git object to this cache.
        /// </summary>
        /// <param name="offset">
        /// The offset of the Git object in the Git pack.
        /// </param>
        /// <param name="stream">
        /// A <see cref="Stream"/> which represents the object to add. This stream
        /// will be copied to the cache.
        /// </param>
        /// <returns>
        /// A <see cref="Stream"/> which represents the cached entry.
        /// </returns>
        public abstract Stream Add(int offset, Stream stream);
    }
}
