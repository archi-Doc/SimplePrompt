// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using System.Runtime.InteropServices;

namespace SimplePrompt.Internal;

#pragma warning disable SA1202 // Elements should be ordered by access
#pragma warning disable SA1300 // Element should begin with upper-case letter
#pragma warning disable SA1307 // Accessible fields should begin with upper-case letter
#pragma warning disable SA1310 // Field names should not contain underscore

internal static partial class Interop
{
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

    internal static partial class Sys
    {
        private const string SystemNative = "libSystem.Native";

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
