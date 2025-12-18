// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using System.Globalization;
using System.Runtime.CompilerServices;

namespace SimplePrompt.Internal;

internal static class SimplePromptHelper
{
    public static readonly ConsoleKeyInfo EnterKeyInfo = new(default, ConsoleKey.Enter, false, false, false);
    public static readonly ConsoleKeyInfo SpaceKeyInfo = new(' ', ConsoleKey.Spacebar, false, false, false);

    public static ReadOnlySpan<char> ForceNewLineCursor => " \e[1D";

#pragma warning disable SA1101 // Prefix local calls with this
    extension(ReadLineMode mode)
    {
        public bool IsMultiline => mode != ReadLineMode.Singleline;
    }
#pragma warning restore SA1101 // Prefix local calls with this

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool TryCopy(ReadOnlySpan<char> source, ref Span<char> destination)
    {
        if (source.Length > destination.Length)
        {
            return false;
        }

        source.CopyTo(destination);
        destination = destination.Slice(source.Length);
        return true;
    }

    public static byte GetCharWidth(int codePoint)
    {
        // Control characters
        if (codePoint < 0x20 || (codePoint >= 0x7F && codePoint < 0xA0))
        {
            return 0;
        }

        // Extend characters (combining marks)
        var category = CharUnicodeInfo.GetUnicodeCategory(codePoint);
        if (category == UnicodeCategory.NonSpacingMark ||
            category == UnicodeCategory.EnclosingMark ||
            category == UnicodeCategory.SpacingCombiningMark)
        {
            return 0;
        }

        // Kanji and other CJK characters
        if ((codePoint >= 0x4E00 && codePoint <= 0x9FFF) ||
            (codePoint >= 0x3400 && codePoint <= 0x4DBF) ||
            (codePoint >= 0x20000 && codePoint <= 0x2A6DF) ||
            (codePoint >= 0x2A700 && codePoint <= 0x2B73F) ||
            (codePoint >= 0x2B740 && codePoint <= 0x2B81F) ||
            (codePoint >= 0x2B820 && codePoint <= 0x2CEAF) ||
            (codePoint >= 0x2CEB0 && codePoint <= 0x2EBEF))
        {
            return 2;
        }

        // Fullwidth characters
        if ((codePoint >= 0xFF01 && codePoint <= 0xFF60) ||
            (codePoint >= 0xFFE0 && codePoint <= 0xFFE6))
        {
            return 2;
        }

        // Hiragana and Katakana
        if ((codePoint >= 0x3040 && codePoint <= 0x309F) ||
            (codePoint >= 0x30A0 && codePoint <= 0x30FF))
        {
            return 2;
        }

        // Hangul
        if ((codePoint >= 0xAC00 && codePoint <= 0xD7AF) ||
            (codePoint >= 0x1100 && codePoint <= 0x11FF) ||
            (codePoint >= 0x3130 && codePoint <= 0x318F) ||
            (codePoint >= 0xA960 && codePoint <= 0xA97F) ||
            (codePoint >= 0xD7B0 && codePoint <= 0xD7FF))
        {
            return 2;
        }

        // Other East Asian wide characters
        if ((codePoint >= 0x2E80 && codePoint <= 0x2EFF) ||
            (codePoint >= 0x2F00 && codePoint <= 0x2FDF) ||
            (codePoint >= 0x3000 && codePoint <= 0x303F) ||
            (codePoint >= 0x3200 && codePoint <= 0x32FF) ||
            (codePoint >= 0x3300 && codePoint <= 0x33FF) ||
            (codePoint >= 0xFE30 && codePoint <= 0xFE4F) ||
            (codePoint >= 0xF900 && codePoint <= 0xFAFF) ||
            (codePoint >= 0x2FF0 && codePoint <= 0x2FFF))
        {
            return 2;
        }

        // Emoji and other symbols
        if ((codePoint >= 0x1F300 && codePoint <= 0x1F9FF) ||
            (codePoint >= 0x1F600 && codePoint <= 0x1F64F) ||
            (codePoint >= 0x1F680 && codePoint <= 0x1F6FF) ||
            (codePoint >= 0x2600 && codePoint <= 0x26FF) ||
            (codePoint >= 0x2700 && codePoint <= 0x27BF))
        {
            return 2;
        }

        // Default to single width
        return 1;
    }

    public static int GetWidth(ReadOnlySpan<char> text)
    {
        var width = 0;
        foreach (var x in text)
        {
            width += GetCharWidth(x);
        }

        return width;
    }
}
