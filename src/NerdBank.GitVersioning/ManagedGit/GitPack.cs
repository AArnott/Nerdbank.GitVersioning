#nullable enable

using System;
using System.Collections.Generic;
using System.IO;
using System.IO.MemoryMappedFiles;
using System.Linq;
using System.Text;

namespace Nerdbank.GitVersioning.ManagedGit
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
        /// <returns>
        /// A <see cref="Stream"/> which represents the requested object.
        /// </returns>
        public delegate Stream? GetObjectFromRepositoryDelegate(GitObjectId sha, string objectType);

        private readonly Func<FileStream> packStream;
        private readonly Lazy<FileStream> indexStream;
        private readonly GitPackCache cache;

        // Maps GitObjectIds to offets in the git pack.
        private readonly Dictionary<GitObjectId, long> offsets = new Dictionary<GitObjectId, long>();

        // A histogram which tracks the objects which have been retrieved from this GitPack. The key is the offset
        // of the object. Used to get some insights in usage patterns.
#if DEBUG && !NETSTANDARD
        private readonly Dictionary<long, int> histogram = new Dictionary<long, int>();
#endif

        private Lazy<GitPackIndexReader> indexReader;

        // Operating on git packfiles can potentially open a lot of streams which point to the pack file. For example,
        // deltafied objects can have base objects which are in turn delafied. Opening and closing these streams has
        // become a performance bottleneck. This is mitigated by pooling streams (i.e. reusing the streams after they
        // are closed by the caller).
        private readonly Queue<GitPackPooledStream> pooledStreams = new Queue<GitPackPooledStream>();

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
        public GitPack(GetObjectFromRepositoryDelegate getObjectFromRepositoryDelegate, string indexPath, string packPath, GitPackCache? cache = null)
            : this(getObjectFromRepositoryDelegate, new Lazy<FileStream>(() => File.OpenRead(indexPath)), () => File.OpenRead(packPath), cache)
        {
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="GitPack"/> class.
        /// </summary>
        /// <param name="getObjectFromRepositoryDelegate">
        /// A delegate which fetches objects from the Git object store.
        /// </param>
        /// <param name="indexStream">
        /// A function which creates a new <see cref="Stream"/> which provides read-only
        /// access to the index file.
        /// </param>
        /// <param name="packStream">
        /// A function which creates a new <see cref="Stream"/> which provides read-only
        /// access to the pack file.
        /// </param>
        /// <param name="cache">
        /// A <see cref="GitPackCache"/> which is used to cache <see cref="Stream"/> objects which operate
        /// on the pack file.
        /// </param>
        public GitPack(GetObjectFromRepositoryDelegate getObjectFromRepositoryDelegate, Lazy<FileStream> indexStream, Func<FileStream> packStream, GitPackCache? cache = null)
        {
            this.GetObjectFromRepository = getObjectFromRepositoryDelegate ?? throw new ArgumentNullException(nameof(getObjectFromRepositoryDelegate));
            this.indexReader = new Lazy<GitPackIndexReader>(this.OpenIndex);
            this.packStream = packStream ?? throw new ArgumentException(nameof(packStream));
            this.indexStream = indexStream ?? throw new ArgumentNullException(nameof(indexStream));
            this.cache = cache ?? new GitPackMemoryCache();
        }

        /// <summary>
        /// Gets a delegate which fetches objects from the Git object store.
        /// </summary>
        public GetObjectFromRepositoryDelegate GetObjectFromRepository { get; private set; }

        /// <summary>
        /// Finds a git object using a partial object ID.
        /// </summary>
        /// <param name="objectId">
        /// A partial object ID.
        /// </param>
        /// <param name="endsWithHalfByte"><inheritdoc cref="GitPackIndexReader.GetOffset(Span{byte}, bool)" path="/param[@name='endsWithHalfByte']"/></param>
        /// <returns>
        /// If found, a full object ID which matches the partial object ID.
        /// Otherwise, <see langword="false"/>.
        /// </returns>
        public GitObjectId? Lookup(Span<byte> objectId, bool endsWithHalfByte = false)
        {
            (var _, var actualObjectId) = this.indexReader.Value.GetOffset(objectId, endsWithHalfByte);
            return actualObjectId;
        }

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
        public bool TryGetObject(GitObjectId objectId, string objectType, out Stream? value)
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
        public Stream GetObject(long offset, string objectType)
        {
#if DEBUG && !NETSTANDARD
            if (!this.histogram.TryAdd(offset, 1))
            {
                this.histogram[offset] += 1;
            }
#endif

            if (this.cache.TryOpen(offset, out Stream? stream))
            {
                return stream!;
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
            builder.AppendLine($"Git Pack:");

#if DEBUG && !NETSTANDARD
            int histogramCount = 25;
            builder.AppendLine($"Top {histogramCount} / {this.histogram.Count} items:");

            foreach (var item in this.histogram.OrderByDescending(v => v.Value).Take(25))
            {
                builder.AppendLine($"  {item.Key}: {item.Value}");
            }

            builder.AppendLine();
#endif

            this.cache.GetCacheStatistics(builder);
        }

        /// <inheritdoc/>
        public void Dispose()
        {
            if (this.indexReader.IsValueCreated)
            {
                this.indexReader.Value.Dispose();
            }

            this.cache.Dispose();
        }

        private long? GetOffset(GitObjectId objectId)
        {
            if (this.offsets.TryGetValue(objectId, out long cachedOffset))
            {
                return cachedOffset;
            }

            var indexReader = this.indexReader.Value;
            var offset = indexReader.GetOffset(objectId);

            if (offset != null)
            {
                this.offsets.Add(objectId, offset.Value);
            }

            return offset;
        }

        private Stream GetPackStream()
        {
            return this.packStream();
        }

        private GitPackIndexReader OpenIndex()
        {
            return new GitPackIndexMappedReader(this.indexStream.Value);
        }
    }
}
