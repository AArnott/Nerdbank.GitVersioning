﻿using System;
using System.Buffers.Binary;
using System.Diagnostics;
using System.IO;
using System.IO.MemoryMappedFiles;

namespace NerdBank.GitVersioning.Managed
{
    /// <summary>
    /// A <see cref="GitPackIndexReader"/> which uses a memory-mapped file to read from the index.
    /// </summary>
    public unsafe class GitPackIndexMappedReader : GitPackIndexReader
    {
        private readonly MemoryMappedFile file;
        private readonly MemoryMappedViewAccessor accessor;

        // The fanout table consists of 
        // 256 4-byte network byte order integers.
        // The N-th entry of this table records the number of objects in the corresponding pack,
        // the first byte of whose object name is less than or equal to N.
        private readonly int[] fanoutTable = new int[257];

        private byte* ptr;
        private bool initialized;

        /// <summary>
        /// Initializes a new instance of the <see cref="GitPackIndexMappedReader"/> class.
        /// </summary>
        /// <param name="stream">
        /// A <see cref="FileStream"/> which points to the index file.
        /// </param>
        public GitPackIndexMappedReader(FileStream stream)
        {
            if (stream == null)
            {
                throw new ArgumentNullException(nameof(stream));
            }

            this.file = MemoryMappedFile.CreateFromFile(stream, mapName: null, capacity: 0, MemoryMappedFileAccess.Read, HandleInheritability.None, leaveOpen: false);
            this.accessor = this.file.CreateViewAccessor(0, 0, MemoryMappedFileAccess.Read);
            this.accessor.SafeMemoryMappedViewHandle.AcquirePointer(ref this.ptr);
        }

        private Span<byte> Value
        {
            get
            {
                return new Span<byte>(this.ptr, (int)this.accessor.Capacity);
            }
        }

        /// <inheritdoc/>
        public override int? GetOffset(GitObjectId objectId)
        {
            this.Initialize();

            Span<byte> buffer = stackalloc byte[4];
            Span<byte> objectName = stackalloc byte[20];
            objectId.CopyTo(objectName);

            var packStart = this.fanoutTable[objectName[0]];
            var packEnd = this.fanoutTable[objectName[0] + 1];
            var objectCount = this.fanoutTable[256];

            // The fanout table is followed by a table of sorted 20-byte SHA-1 object names.
            // These are packed together without offset values to reduce the cache footprint of the binary search for a specific object name.

            // The object names start at: 4 (header) + 4 (version) + 256 * 4 (fanout table) + 20 * (packStart)
            // and end at                 4 (header) + 4 (version) + 256 * 4 (fanout table) + 20 * (packEnd)

            var i = 0;
            var order = 0;

            var tableSize = 20 * (packEnd - packStart + 1);
            var table = this.Value.Slice(4 + 4 + 256 * 4 + 20 * packStart, tableSize);

            int originalPackStart = packStart;

            packEnd -= originalPackStart;
            packStart = 0;

            while (packStart <= packEnd)
            {
                i = (packStart + packEnd) / 2;

                order = table.Slice(20 * i, 20).SequenceCompareTo(objectName);

                if (order < 0)
                {
                    packStart = i + 1;
                }
                else if (order > 0)
                {
                    packEnd = i - 1;
                }
                else
                {
                    break;
                }
            }

            if (order != 0)
            {
                return null;
            }

            // Get the offset value. It's located at:
            // 4 (header) + 4 (version) + 256 * 4 (fanout table) + 20 * objectCount (SHA1 object name table) + 4 * objectCount (CRC32) + 4 * i (offset values)
            var offsetBuffer = this.Value.Slice(4 + 4 + 256 * 4 + 20 * objectCount + 4 * objectCount + 4 * (i + originalPackStart), 4);
            Debug.Assert(offsetBuffer[0] < 128); // The most significant bit should not be set; otherwise we have a 8-byte offset
            var offset = BinaryPrimitives.ReadInt32BigEndian(offsetBuffer);
            return offset;
        }

        /// <inheritdoc/>
        public override void Dispose()
        {
            this.accessor.Dispose();
            this.file.Dispose();
        }

        private void Initialize()
        {
            if (!this.initialized)
            {
                var value = this.Value;

                var header = value.Slice(0, 4);
                var version = BinaryPrimitives.ReadInt32BigEndian(value.Slice(4, 4));
                Debug.Assert(header.SequenceEqual(Header));
                Debug.Assert(version == 2);

                for (int i = 1; i <= 256; i++)
                {
                    this.fanoutTable[i] = BinaryPrimitives.ReadInt32BigEndian(value.Slice(4 + 4 * i, 4));
                }

                this.initialized = true;
            }
        }
    }
}
