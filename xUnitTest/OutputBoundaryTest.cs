// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using System.Globalization;
using System.Text;
using SimplePrompt.Internal;

namespace xUnitTest;

[Collection(SimpleConsoleTests.Name)]
public class OutputBoundaryTest(SimpleConsoleFixture fixture)
{
    [Fact]
    public async Task EmptyWriteLineDoesNotErasePreviouslyWrittenText()
    {
        await fixture.WaitForIdle();
        fixture.Console.Write("prefix");
        fixture.ClearOutput();
        fixture.Console.WriteLine();
        var output = fixture.TakeOutput();
        Assert.DoesNotContain("\e[2K", output);
        Assert.Contains('\n', output);
    }

    [Theory]
    [InlineData("text\n", 2)]
    [InlineData("text\r\n", 2)]
    [InlineData("\n\n", 3)]
    public async Task WriteLineAppendsNewlineAfterTrailingNewline(string text, int expected)
    {
        await fixture.WaitForIdle();
        fixture.ClearOutput();
        fixture.Console.WriteLine(text);
        Assert.Equal(expected, fixture.TakeOutput().Count(c => c == '\n'));
    }

    [Theory]
    [InlineData(10, false)]
    [InlineData(256, true)]
    [InlineData(257, false)]
    [InlineData(40000, true)]
    public async Task StringBuilderOutputPreservesAllChunks(int length, bool newLine)
    {
        await fixture.WaitForIdle();
        var builder = new StringBuilder(1);
        builder.Append('x', length);
        builder.Append("😀");
        fixture.ClearOutput();
        if (newLine)
        {
            fixture.ConsoleOut.WriteLine(builder);
        }
        else
        {
            fixture.ConsoleOut.Write(builder);
        }

        var output = fixture.TakeOutput();
        Assert.Contains(builder.ToString(), output);
        Assert.Equal(newLine ? 1 : 0, output.Count(c => c == '\n'));
    }

    [Fact]
    public async Task ShortOutputCanFlushExpandedNewlineSequences()
    {
        await fixture.WaitForIdle();
        fixture.ClearOutput();
        fixture.Console.Write(new string('\n', 190) + "😀");
        var output = fixture.TakeOutput();
        Assert.Equal(190, output.Count(c => c == '\n'));
        Assert.Contains("😀", output);
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public void ObjectFormattingUsesProviderAndFallsBack(bool overflow, bool newLine)
    {
        using var sink = new StringWriter(CultureInfo.InvariantCulture);
        using var writer = new SimpleTextWriter(fixture.Console, sink);
        var value = new FormattedValue(overflow);
        fixture.ClearOutput();
        if (newLine)
        {
            writer.WriteLine((object)value);
        }
        else
        {
            writer.Write((object)value);
        }

        Assert.Contains(overflow ? "fallback" : "span", fixture.TakeOutput());
        Assert.Same(CultureInfo.InvariantCulture, value.Provider);
        Assert.Equal(overflow, value.UsedFallback);
    }

    private sealed class FormattedValue(bool overflow) : ISpanFormattable
    {
        public IFormatProvider? Provider { get; private set; }

        public bool UsedFallback { get; private set; }

        public bool TryFormat(Span<char> destination, out int charsWritten, ReadOnlySpan<char> format, IFormatProvider? provider)
        {
            this.Provider = provider;
            charsWritten = overflow ? 0 : 4;
            if (overflow)
            {
                return false;
            }

            "span".CopyTo(destination);
            return true;
        }

        public string ToString(string? format, IFormatProvider? formatProvider)
        {
            this.Provider = formatProvider;
            this.UsedFallback = true;
            return "fallback";
        }
    }
}
