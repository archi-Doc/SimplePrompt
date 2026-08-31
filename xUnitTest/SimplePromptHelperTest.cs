// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using SimplePrompt.Internal;

namespace xUnitTest;

/// <summary>
/// Tests the internal helper functions (character width calculation and buffer writing).
/// </summary>
public class SimplePromptHelperTest
{
    [Theory]

    // Control characters have no width.
    [InlineData(0x00, 0)]
    [InlineData('\t', 0)]
    [InlineData('\n', 0)]
    [InlineData(0x1F, 0)]
    [InlineData(0x7F, 0)]
    [InlineData(0x9F, 0)]

    // Half width characters.
    [InlineData(' ', 1)]
    [InlineData('a', 1)]
    [InlineData('Z', 1)]
    [InlineData('0', 1)]
    [InlineData('~', 1)]
    [InlineData(0x00A0, 1)] // No-break space
    [InlineData(0x00E9, 1)] // é
    [InlineData(0x02B0, 1)] // Modifier letter small h
    [InlineData(0x0416, 1)] // Cyrillic Zhe
    [InlineData(0x2500, 1)] // Box drawing

    // Combining marks have no width.
    [InlineData(0x0301, 0)] // Combining acute accent
    [InlineData(0x20DD, 0)] // Combining enclosing circle
    [InlineData(0x3099, 0)] // Combining katakana-hiragana voiced sound mark

    // Full width characters.
    [InlineData(0x3042, 2)] // Hiragana A
    [InlineData(0x30AB, 2)] // Katakana Ka
    [InlineData(0x6F22, 2)] // Kanji
    [InlineData(0x3400, 2)] // CJK extension A
    [InlineData(0xFF21, 2)] // Full width A
    [InlineData(0xFFE5, 2)] // Full width yen sign
    [InlineData(0xAC00, 2)] // Hangul
    [InlineData(0x3000, 2)] // Ideographic space
    [InlineData(0xF900, 2)] // CJK compatibility ideograph
    [InlineData(0x1F600, 2)] // Emoji
    [InlineData(0x2600, 2)] // Black sun with rays
    [InlineData(0x20000, 2)] // CJK extension B
    public void GetCharWidth(int codePoint, int expected)
        => Assert.Equal(expected, SimplePromptHelper.GetCharWidth(codePoint));

    [Fact]
    public void TryCopy()
    {
        var array = new char[8];
        var span = array.AsSpan();

        Assert.True(SimplePromptHelper.TryCopy("abc", ref span));
        Assert.Equal(5, span.Length);
        Assert.Equal("abc", array.AsSpan(0, 3).ToString());

        Assert.True(SimplePromptHelper.TryCopy("de", ref span));
        Assert.Equal(3, span.Length);
        Assert.Equal("abcde", array.AsSpan(0, 5).ToString());

        // Does not fit: nothing is copied and the destination is unchanged.
        Assert.False(SimplePromptHelper.TryCopy("wxyz", ref span));
        Assert.Equal(3, span.Length);

        Assert.True(SimplePromptHelper.TryCopy(default, ref span)); // Empty
        Assert.Equal(3, span.Length);

        Assert.True(SimplePromptHelper.TryCopy("xyz", ref span)); // Exactly fits.
        Assert.Equal(0, span.Length);
        Assert.Equal("abcdexyz", array.AsSpan().ToString());
    }

    [Theory]
    [InlineData(0, 0, "\e[1;1H")]
    [InlineData(3, 5, "\e[6;4H")]
    [InlineData(119, 29, "\e[30;120H")]
    [InlineData(9999, 9999, "\e[10000;10000H")]
    public void TryCopySetCursor(int left, int top, string expected)
    {
        var array = new char[64];
        var span = array.AsSpan();
        Assert.True(SimplePromptHelper.TryCopySetCursor(ref span, left, top));
        Assert.Equal(expected, array.AsSpan(0, array.Length - span.Length).ToString());
    }

    [Fact]
    public void TryCopySetCursorWithoutEnoughSpace()
    {
        var array = new char[4];
        var span = array.AsSpan();
        Assert.False(SimplePromptHelper.TryCopySetCursor(ref span, 0, 0));
        Assert.Equal(4, span.Length); // Unchanged.
    }

    [Fact]
    public void IsMultiline()
    {
        Assert.False(ReadLineMode.Singleline.IsMultiline());
        Assert.True(ReadLineMode.Delimiter.IsMultiline());
        Assert.True(ReadLineMode.LineContinuation.IsMultiline());
    }

    [Fact]
    public void EnterKeyInfo()
    {
        Assert.Equal(ConsoleKey.Enter, SimplePromptHelper.EnterKeyInfo.Key);
        Assert.Equal(default, SimplePromptHelper.EnterKeyInfo.Modifiers);
    }
}
