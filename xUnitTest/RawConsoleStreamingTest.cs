// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using System.Text;
using SimplePrompt.Internal;

namespace xUnitTest;

[Collection(SimpleConsoleTests.Name)]
public class RawConsoleStreamingTest(SimpleConsoleFixture fixture)
{
    [Theory]
    [InlineData("\e[A", ConsoleKey.UpArrow, ConsoleModifiers.None)]
    [InlineData("\eOP", ConsoleKey.F1, ConsoleModifiers.None)]
    [InlineData("\e[[A", ConsoleKey.F1, ConsoleModifiers.None)]
    [InlineData("\e[3~", ConsoleKey.Delete, ConsoleModifiers.None)]
    [InlineData("\e[12~", ConsoleKey.F2, ConsoleModifiers.None)]
    [InlineData("\e[1;5A", ConsoleKey.UpArrow, ConsoleModifiers.Control)]
    [InlineData("\e[12;8~", ConsoleKey.F2, ConsoleModifiers.Shift | ConsoleModifiers.Alt | ConsoleModifiers.Control)]
    [InlineData("\e\e[A", ConsoleKey.UpArrow, ConsoleModifiers.Alt)]
    [InlineData("\e\e[1;5A", ConsoleKey.UpArrow, ConsoleModifiers.Alt | ConsoleModifiers.Control)]
    [InlineData("\ea", ConsoleKey.A, ConsoleModifiers.Alt)]
    public void ByteFragmentsDecodeAsOneKey(string sequence, ConsoleKey expectedKey, ConsoleModifiers expectedModifiers)
    {
        var raw = new RawConsole(fixture.Console);
        var input = Encoding.UTF8.GetBytes(sequence);
        for (var i = 0; i < input.Length; i++)
        {
            raw.AppendStdinInput(input.AsSpan(i, 1));
            if (i + 1 < input.Length)
            {
                Assert.False(raw.TryReadStdinBuffer(i * 10, out _));
            }
        }

        Assert.True(raw.TryReadStdinBuffer(input.Length * 10, out var key));
        Assert.Equal(expectedKey, key.Key);
        Assert.Equal(expectedModifiers, key.Modifiers);
        Assert.False(raw.TryReadStdinBuffer(input.Length * 10, out _));
    }

    [Theory]
    [InlineData("\e")]
    [InlineData("\e[")]
    [InlineData("\eO")]
    [InlineData("\e[1")]
    [InlineData("\e[12")]
    [InlineData("\e[1;")]
    [InlineData("\e[12;")]
    [InlineData("\e[1;5")]
    public void IncompleteInputExpiresWithoutReceivingMoreBytes(string input)
    {
        var raw = new RawConsole(fixture.Console);
        raw.AppendStdinInput(Encoding.UTF8.GetBytes(input));
        Assert.False(raw.TryReadStdinBuffer(1000, out _));
        Assert.False(raw.TryReadStdinBuffer(1000 + RawConsole.EscapeSequenceTimeoutMilliseconds - 1, out _));

        var decoded = new StringBuilder();
        while (raw.TryReadStdinBuffer(1000 + RawConsole.EscapeSequenceTimeoutMilliseconds, out var key))
        {
            decoded.Append(key.KeyChar);
        }

        Assert.Equal(input, decoded.ToString());
        raw.AppendStdinInput("x"u8);
        Assert.True(raw.TryReadStdinBuffer(1200, out var next));
        Assert.Equal('x', next.KeyChar);
    }

    [Fact]
    public void AdditionalFragmentsDoNotExtendTheEscapeDeadline()
    {
        var raw = new RawConsole(fixture.Console);
        raw.AppendStdinInput("\e"u8);
        Assert.False(raw.TryReadStdinBuffer(0, out _));
        raw.AppendStdinInput("["u8);
        Assert.False(raw.TryReadStdinBuffer(90, out _));
        Assert.True(raw.TryReadStdinBuffer(100, out var key));
        Assert.Equal(ConsoleKey.Escape, key.Key);
        Assert.True(raw.TryReadStdinBuffer(100, out key));
        Assert.Equal('[', key.KeyChar);
    }

    [Theory]
    [InlineData("abc")]
    [InlineData("\e[?")]
    [InlineData("\e[0~")]
    [InlineData("\e[1;9A")]
    [InlineData("\eO?")]
    public void OrdinaryAndUnknownInputDoesNotWait(string input)
    {
        var raw = new RawConsole(fixture.Console);
        raw.AppendStdinInput(Encoding.UTF8.GetBytes(input));
        var decoded = new StringBuilder();
        while (raw.TryReadStdinBuffer(0, out var key))
        {
            decoded.Append(key.KeyChar);
        }

        Assert.Equal(input, decoded.ToString());
    }

    [Fact]
    public void Utf8FragmentsAndEscapeFragmentsHaveIndependentBuffers()
    {
        var raw = new RawConsole(fixture.Console);
        var text = "日本語😀";
        var input = Encoding.UTF8.GetBytes(text);
        var decoded = new StringBuilder();
        for (var i = 0; i < input.Length; i++)
        {
            raw.AppendStdinInput(input.AsSpan(i, 1));
            while (raw.TryReadStdinBuffer(i, out var key))
            {
                decoded.Append(key.KeyChar);
            }
        }

        Assert.Equal(text, decoded.ToString());
        raw.AppendStdinInput("\e["u8);
        Assert.False(raw.TryReadStdinBuffer(20, out _));
        raw.AppendStdinInput("D"u8);
        Assert.True(raw.TryReadStdinBuffer(30, out var arrow));
        Assert.Equal(ConsoleKey.LeftArrow, arrow.Key);
    }

    [Fact]
    public void EscapeCanPrecedeFragmentedUtf8Text()
    {
        var raw = new RawConsole(fixture.Console);
        raw.AppendStdinInput([0x1B, 0xE6]); // Escape and the first byte of 日.
        Assert.False(raw.TryReadStdinBuffer(0, out _));
        raw.AppendStdinInput([0x97]);
        Assert.False(raw.TryReadStdinBuffer(10, out _));
        raw.AppendStdinInput([0xA5]);
        Assert.True(raw.TryReadStdinBuffer(20, out var key));
        Assert.Equal('日', key.KeyChar);
        Assert.False(raw.TryReadStdinBuffer(20, out _));
    }

    [Fact]
    public void PendingEscapeAtCapacityPreservesTheNextPacket()
    {
        var raw = new RawConsole(fixture.Console);
        raw.AppendStdinInput(Encoding.ASCII.GetBytes(new string('a', 1023) + "\e"));
        for (var i = 0; i < 1023; i++)
        {
            Assert.True(raw.TryReadStdinBuffer(0, out var key));
            Assert.Equal('a', key.KeyChar);
        }

        Assert.False(raw.TryReadStdinBuffer(0, out _));
        raw.AppendStdinInput(Encoding.ASCII.GetBytes("[A" + new string('b', 1021)));
        Assert.True(raw.TryReadStdinBuffer(10, out var arrow));
        Assert.Equal(ConsoleKey.UpArrow, arrow.Key);
        for (var i = 0; i < 1021; i++)
        {
            Assert.True(raw.TryReadStdinBuffer(10, out var key));
            Assert.Equal('b', key.KeyChar);
        }

        Assert.False(raw.TryReadStdinBuffer(10, out _));
    }

    [Fact]
    public void Utf8TailAtCapacityContinuesWithAnEscapeSequence()
    {
        var raw = new RawConsole(fixture.Console);
        var first = new byte[1024];
        first.AsSpan(0, 1023).Fill((byte)'a');
        first[^1] = 0xF0; // Start of 😀.
        raw.AppendStdinInput(first);
        for (var i = 0; i < 1023; i++)
        {
            Assert.True(raw.TryReadStdinBuffer(0, out _));
        }

        Assert.False(raw.TryReadStdinBuffer(0, out _));
        raw.AppendStdinInput([0x9F, 0x98, 0x80, 0x1B, (byte)'[']);
        Assert.True(raw.TryReadStdinBuffer(10, out var high));
        Assert.True(raw.TryReadStdinBuffer(10, out var low));
        Assert.Equal("😀", new string([high.KeyChar, low.KeyChar]));
        Assert.False(raw.TryReadStdinBuffer(10, out _));
        raw.AppendStdinInput("A"u8);
        Assert.True(raw.TryReadStdinBuffer(20, out var arrow));
        Assert.Equal(ConsoleKey.UpArrow, arrow.Key);
    }

    [Fact]
    public void StreamingDecodeDoesNotAllocatePerKey()
    {
        var raw = new RawConsole(fixture.Console);
        for (var i = 0; i < 100; i++)
        {
            ReadOne(i * 1000);
        }

        var count = 0;
        var before = GC.GetAllocatedBytesForCurrentThread();
        for (var i = 0; i < 1000; i++)
        {
            if (ReadOne(i * 1000))
            {
                count++;
            }
        }

        var allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        Assert.Equal(1000, count);
        Assert.Equal(0, allocated);

        bool ReadOne(long timestamp)
        {
            raw.AppendStdinInput("\e["u8);
            if (raw.TryReadStdinBuffer(timestamp, out _))
            {
                return false;
            }

            raw.AppendStdinInput("1;5A"u8);
            return raw.TryReadStdinBuffer(timestamp + 1, out var key) && key.Key == ConsoleKey.UpArrow && key.Modifiers == ConsoleModifiers.Control;
        }
    }
}
