﻿using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace NerdBank.GitVersioning.Managed
{
    /// <summary>
    /// Supports retrieving objects from a Git pack file.
    /// </summary>
    public class GitPack : IDisposable
    {
        /// <summary>
        /// A delegate for methods which fetch objects from the Git object store.
        /// </summary>
        /// <param name="sha">
        /// The Git object ID of the object to fetch.
        /// </param>
        /// <param name="objectType">
        /// The object type of the object to fetch.
        /// </param>
        /// <param name="seekable">
        /// A value indicating whether forward-only or seekable access is required.
        /// </param>
        /// <returns>
        /// A <see cref="Stream"/> which represents the requested object.
        /// </returns>
        public delegate Stream GetObjectFromRepositoryDelegate(GitObjectId sha, string objectType, bool seekable = false);

        private readonly string packPath;
        private readonly string indexPath;
        private readonly GetObjectFromRepositoryDelegate getObjectFromRepositoryDelegate;
        private readonly GitPackCache cache;

        // Maps GitObjectIds to offets in the git pack.
        private readonly Dictionary<GitObjectId, int> offsets = new Dictionary<GitObjectId, int>();

        // A histogram which tracks the objects which have been retrieved from this GitPack. The key is the offset
        // of the object. Used to get some insights in usage patterns.
        private readonly Dictionary<int, int> histogram = new Dictionary<int, int>();

        private Lazy<GitPackIndexReader> indexReader;

        // Operating on git packfiles can potentially open a lot of streams which point to the pack file. For example,
        // deltafied objects can have base objects which are in turn delafied. Opening and closing these streams has
        // become a performance bottleneck. This is mitigated by pooling streams (i.e. reusing the streams after they
        // are closed by the caller).
        private readonly Queue<GitPackPooledStream> pooledStreams = new Queue<GitPackPooledStream>();

        /// <summary>
        /// Initializes a new instance of the <see cref="GitPack"/> class.
        /// </summary>
        /// <param name="repository">
        /// The repository to which this pack file belongs.
        /// </param>
        /// <param name="name">
        /// The name of the pack file.
        /// </param>
        internal GitPack(GitRepository repository, string name)
            : this(
                  repository.GetObjectBySha,
                  Path.Combine(repository.ObjectDirectory, "pack", $"{name}.pack"),
                  Path.Combine(repository.ObjectDirectory, "pack", $"{name}.idx"))
        {
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="GitPack"/> class.
        /// </summary>
        /// <param name="getObjectFromRepositoryDelegate">
        /// A delegate which fetches objects from the Git object store.
        /// </param>
        /// <param name="indexPath">
        /// The full path to the index file.
        /// </param>
        /// <param name="packPath">
        /// The full path to the pack file.
        /// </param>
        /// <param name="cache">
        /// A <see cref="GitPackCache"/> which is used to cache <see cref="Stream"/> objects which operate
        /// on the pack file.
        /// </param>
        public GitPack(GetObjectFromRepositoryDelegate getObjectFromRepositoryDelegate, string indexPath, string packPath, GitPackCache cache = null)
        {
            this.GetObjectFromRepository = getObjectFromRepositoryDelegate ?? throw new ArgumentNullException(nameof(getObjectFromRepositoryDelegate));
            this.indexReader = new Lazy<GitPackIndexReader>(this.OpenIndex);
            this.packPath = packPath ?? throw new ArgumentException(nameof(packPath));
            this.indexPath = indexPath ?? throw new ArgumentNullException(nameof(indexPath));
            this.cache = cache ?? new GitPackMemoryCache();
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="GitPack"/> class.
        /// </summary>
        /// <remarks>
        /// Intended for mocking / unit testing purposes only.
        /// </remarks>
        protected GitPack()
        {
        }

        /// <summary>
        /// Gets a delegate which fetches objects from the Git object store.
        /// </summary>
        public GetObjectFromRepositoryDelegate GetObjectFromRepository { get; private set; }

        /// <summary>
        /// Attempts to retrieve a Git object from this Git pack.
        /// </summary>
        /// <param name="objectId">
        /// The Git object Id of the object to retrieve.
        /// </param>
        /// <param name="objectType">
        /// The object type of the object to retrieve.
        /// </param>
        /// <param name="value">
        /// If found, receives a <see cref="Stream"/> which represents the object.
        /// </param>
        /// <returns>
        /// <see langword="true"/> if the object was found; otherwise, <see langword="false"/>.
        /// </returns>
        public bool TryGetObject(GitObjectId objectId, string objectType, out Stream value)
        {
            var offset = this.GetOffset(objectId);

            if (offset == null)
            {
                value = null;
                return false;
            }
            else
            {
                value = this.GetObject(offset.Value, objectType);
                return true;
            }
        }

        /// <summary>
        /// Gets a Git object at a specific offset.
        /// </summary>
        /// <param name="offset">
        /// The offset of the Git object, relative to the pack file.
        /// </param>
        /// <param name="objectType">
        /// The object type of the object to retrieve.
        /// </param>
        /// <returns>
        /// A <see cref="Stream"/> which represents the object.
        /// </returns>
        public Stream GetObject(int offset, string objectType)
        {
            if (!this.histogram.TryAdd(offset, 1))
            {
                this.histogram[offset] += 1;
            }

            if (this.cache.TryOpen(offset, out Stream stream))
            {
                return stream;
            }

            GitPackObjectType packObjectType;

            switch (objectType)
            {
                case "commit":
                    packObjectType = GitPackObjectType.OBJ_COMMIT;
                    break;

                case "tree":
                    packObjectType = GitPackObjectType.OBJ_TREE;
                    break;

                case "blob":
                    packObjectType = GitPackObjectType.OBJ_BLOB;
                    break;

                default:
                    throw new GitException($"The object type '{objectType}' is not supported by the {nameof(GitPack)} class.");
            }

            var packStream = this.GetPackStream();
            Stream objectStream = GitPackReader.GetObject(this, packStream, offset, objectType, packObjectType);

            return this.cache.Add(offset, objectStream);
        }

        /// <summary>
        /// Writes cache statistics to a <see cref="StringBuilder"/>.
        /// </summary>
        /// <param name="builder">
        /// A <see cref="StringBuilder"/> to which the cache statistics are written.
        /// </param>
        public void GetCacheStatistics(StringBuilder builder)
        {
            int histogramCount = 25;

            builder.AppendLine($"Git Pack {this.packPath}:");
            builder.AppendLine($"Top {histogramCount} / {this.histogram.Count} items:");

            foreach (var item in this.histogram.OrderByDescending(v => v.Value).Take(25))
            {
                builder.AppendLine($"  {item.Key}: {item.Value}");
            }

            builder.AppendLine();

            this.cache.GetCacheStatistics(builder);
        }

        /// <inheritdoc/>
        public void Dispose()
        {
            if (this.indexReader.IsValueCreated)
            {
                this.indexReader.Value.Dispose();
            }
        }

        private int? GetOffset(GitObjectId objectId)
        {
            if (this.offsets.TryGetValue(objectId, out int cachedOffset))
            {
                return cachedOffset;
            }

            var indexReader = this.indexReader.Value;
            var offset = indexReader.GetOffset(objectId);

            if (offset != null)
            {
                this.offsets.TryAdd(objectId, offset.Value);
            }

            return offset;
        }

        private GitPackPooledStream GetPackStream()
        {
            if (this.pooledStreams.TryDequeue(out var result))
            {
                result.Seek(0, SeekOrigin.Begin);
                return result;
            }

            if (FileHelpers.TryOpen(this.packPath, CreateFileFlags.FILE_ATTRIBUTE_NORMAL | CreateFileFlags.FILE_FLAG_RANDOM_ACCESS, out var fileStream))
            {
                return new GitPackPooledStream(fileStream, this.pooledStreams);
            }
            else
            {
                throw new GitException();
            }
        }

        private GitPackIndexReader OpenIndex()
        {
            return new GitPackIndexReader(File.OpenRead(this.indexPath));
        }
    }
}
