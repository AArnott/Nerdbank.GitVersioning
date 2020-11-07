﻿#nullable enable

using System;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;
using static PInvoke.Kernel32;
using FileShare = PInvoke.Kernel32.FileShare;

namespace NerdBank.GitVersioning.Managed
{
    internal static class FileHelpers
    {
        private static readonly bool IsWindows = RuntimeInformation.IsOSPlatform(OSPlatform.Windows);

        /// <summary>
        /// Opens the file with a given path, if it exists.
        /// </summary>
        /// <param name="path">The path to the file.</param>
        /// <param name="stream">The stream to open to, if the file exists.</param>
        /// <returns><see langword="true" /> if the file exists; otherwise <see langword="false" />.</returns>
        internal static bool TryOpen(string path, out FileStream? stream)
        {
            if (IsWindows)
            {
                var handle = CreateFile(path, ACCESS_MASK.GenericRight.GENERIC_READ, FileShare.FILE_SHARE_READ, (SECURITY_ATTRIBUTES?)null, CreationDisposition.OPEN_EXISTING, CreateFileFlags.FILE_ATTRIBUTE_NORMAL, SafeObjectHandle.Null);

                if (!handle.IsInvalid)
                {
                    var fileHandle = new SafeFileHandle(handle.DangerousGetHandle(), ownsHandle: true);
                    handle.SetHandleAsInvalid();
                    stream = new FileStream(fileHandle, System.IO.FileAccess.Read);
                    return true;
                }
                else
                {
                    stream = null;
                    return false;
                }
            }
            else
            {
                if (!File.Exists(path))
                {
                    stream = null;
                    return false;
                }

                stream = File.OpenRead(path);
                return true;
            }
        }

        /// <summary>
        /// Opens the file with a given path, if it exists.
        /// </summary>
        /// <param name="path">The path to the file, as a null-terminated UTF-16 character array.</param>
        /// <param name="stream">The stream to open to, if the file exists.</param>
        /// <returns><see langword="true" /> if the file exists; otherwise <see langword="false" />.</returns>
        internal static unsafe bool TryOpen(ReadOnlySpan<char> path, [NotNullWhen(true)] out FileStream? stream)
        {
            if (IsWindows)
            {
                var handle = CreateFile(path, ACCESS_MASK.GenericRight.GENERIC_READ, FileShare.FILE_SHARE_READ, null, CreationDisposition.OPEN_EXISTING, CreateFileFlags.FILE_ATTRIBUTE_NORMAL, SafeObjectHandle.Null);

                if (!handle.IsInvalid)
                {
                    var fileHandle = new SafeFileHandle(handle.DangerousGetHandle(), ownsHandle: true);
                    handle.SetHandleAsInvalid();
                    stream = new FileStream(fileHandle, System.IO.FileAccess.Read);
                    return true;
                }
                else
                {
                    stream = null;
                    return false;
                }
            }
            else
            {
                // Make sure to trim the trailing \0
                string fullPath = GetUtf16String(path.Slice(0, path.Length - 1));

                if (!File.Exists(fullPath))
                {
                    stream = null;
                    return false;
                }

                stream = File.OpenRead(fullPath);
                return true;
            }
        }

        private static unsafe string GetUtf16String(ReadOnlySpan<char> chars)
        {
            fixed (char* pChars = chars)
            {
                return new string(pChars, 0, chars.Length);
            }
        }
    }
}
