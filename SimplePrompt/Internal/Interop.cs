// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace SimplePrompt.Internal;

#pragma warning disable SA1202 // Elements should be ordered by access
#pragma warning disable SA1300 // Element should begin with upper-case letter
#pragma warning disable SA1307 // Accessible fields should begin with upper-case letter
#pragma warning disable SA1310 // Field names should not contain underscore

internal static partial class Interop
{
    [Flags]
    internal enum OpenFlags
    {
        O_RDONLY = 0x0000,
        O_WRONLY = 0x0001,
        O_RDWR = 0x0002,
        O_CLOEXEC = 0x0010,
        O_CREAT = 0x0020,
        O_EXCL = 0x0040,
        O_TRUNC = 0x0080,
        O_SYNC = 0x0100,
        O_NOFOLLOW = 0x0200,
    }

    internal enum ControlCharacterNames : int
    {
        VINTR = 0,
        VQUIT = 1,
        VERASE = 2,
        VKILL = 3,
        VEOF = 4,
        VTIME = 5,
        VMIN = 6,
        VSWTC = 7,
        VSTART = 8,
        VSTOP = 9,
        VSUSP = 10,
        VEOL = 11,
        VREPRINT = 12,
        VDISCARD = 13,
        VWERASE = 14,
        VLNEXT = 15,
        VEOL2 = 16,
    }

    internal static class FileDescriptors
    {
        internal static readonly SafeFileHandle STDIN_FILENO = CreateFileHandle(0);
        internal static readonly SafeFileHandle STDOUT_FILENO = CreateFileHandle(1);
        internal static readonly SafeFileHandle STDERR_FILENO = CreateFileHandle(2);

        private static SafeFileHandle CreateFileHandle(int fileNumber)
        {
            return new SafeFileHandle((IntPtr)fileNumber, ownsHandle: false);
        }
    }

    internal static partial class Sys
    {
        private const string SystemNative = "libSystem.Native";

        [LibraryImport(SystemNative, EntryPoint = "SystemNative_Open", StringMarshalling = StringMarshalling.Utf8, SetLastError = true)]
        internal static partial SafeFileHandle Open(string filename, OpenFlags flags, int mode);

        [LibraryImport(SystemNative, EntryPoint = "SystemNative_Dup", SetLastError = true)]
        internal static partial SafeFileHandle Dup(SafeFileHandle oldfd);

        [LibraryImport(SystemNative, EntryPoint = "SystemNative_Write", SetLastError = true)]
        internal static unsafe partial int Write(SafeHandle fd, byte* buffer, int bufferSize);

        [LibraryImport(SystemNative, EntryPoint = "SystemNative_ReadStdin", SetLastError = true)]
        internal static unsafe partial int ReadStdin(byte* buffer, int bufferSize);

        [LibraryImport(SystemNative, EntryPoint = "SystemNative_StdinReady")]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static partial bool StdinReady();

        [LibraryImport(SystemNative, EntryPoint = "SystemNative_InitializeConsoleBeforeRead")]
        internal static partial void InitializeConsoleBeforeRead(byte minChars = 1, byte decisecondsTimeout = 0);

        [LibraryImport(SystemNative, EntryPoint = "SystemNative_UninitializeConsoleAfterRead")]
        internal static partial void UninitializeConsoleAfterRead();

        [LibraryImport(SystemNative, EntryPoint = "SystemNative_GetControlCharacters")]
        internal static unsafe partial void GetControlCharacters(Span<Interop.ControlCharacterNames> controlCharacterNames, Span<byte> controlCharacterValues, int controlCharacterLength, out byte posixDisableValue);
    }
}
