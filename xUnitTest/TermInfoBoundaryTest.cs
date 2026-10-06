// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using System.Buffers.Binary;
using System.Text;
using SimplePrompt.Internal;

namespace xUnitTest;

public class TermInfoBoundaryTest
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ExtendedStringsFollowBooleanAndNumberSections(bool wideNumbers)
    {
        var database = new TermInfo.Database("test", BuildMixedDatabase(wideNumbers));
        Assert.True(database.HasExtendedStrings);
        Assert.Equal("\e[1;5A", database.GetExtendedString("kUP5"));
        Assert.Equal("\e[1;5A", database.GetExtendedString("kUP5".AsSpan()));
        Assert.Null(database.GetExtendedString("kUP3".AsSpan()));

        var formats = new TerminalFormatStrings(database);
        Assert.True(formats.KeyFormatToConsoleKey.TryGetValue("\e[1;5A", out var key));
        Assert.Equal(ConsoleKey.UpArrow, key.Key);
        Assert.Equal(ConsoleModifiers.Control, key.Modifiers);
    }

    [Theory]
    [InlineData(3, ConsoleModifiers.Alt)]
    [InlineData(4, ConsoleModifiers.Shift | ConsoleModifiers.Alt)]
    [InlineData(5, ConsoleModifiers.Control)]
    [InlineData(6, ConsoleModifiers.Shift | ConsoleModifiers.Control)]
    [InlineData(7, ConsoleModifiers.Alt | ConsoleModifiers.Control)]
    [InlineData(8, ConsoleModifiers.Shift | ConsoleModifiers.Alt | ConsoleModifiers.Control)]
    public void ExtendedPrefixMappingsPreserveModifiers(int modifier, ConsoleModifiers expected)
    {
        var data = BuildMixedDatabase(false);
        data[^2] = (byte)('0' + modifier);
        var database = new TermInfo.Database("test", data);
        var formats = new TerminalFormatStrings(database);
        Assert.True(formats.KeyFormatToConsoleKey.TryGetValue("\e[1;5A", out var key));
        Assert.Equal(ConsoleKey.UpArrow, key.Key);
        Assert.Equal(expected, key.Modifiers);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(-2)]
    public void MissingExtendedValuesDoNotAdvertiseCapabilities(short offset)
    {
        var data = BuildMixedDatabase(false);
        if (offset == 0)
        {
            BinaryPrimitives.WriteInt16LittleEndian(data.AsSpan(16), 0); // No string capabilities.
        }
        else
        {
            BinaryPrimitives.WriteInt16LittleEndian(data.AsSpan(26), offset);
        }

        var database = new TermInfo.Database("test", data);
        Assert.False(database.HasExtendedStrings);
        Assert.Null(database.GetExtendedString("kUP5".AsSpan()));
        Assert.False(new TerminalFormatStrings(database).KeyFormatToConsoleKey.TryGetValue("\e[1;5A", out _));
    }

    [Theory]
    [InlineData(26, short.MaxValue)]
    [InlineData(32, short.MaxValue)]
    [InlineData(32, -1)]
    public void InvalidExtendedOffsetsAreIgnored(int index, short offset)
    {
        var data = BuildMixedDatabase(false);
        BinaryPrimitives.WriteInt16LittleEndian(data.AsSpan(index), offset);
        var database = new TermInfo.Database("test", data);
        Assert.False(database.HasExtendedStrings);
        Assert.Null(database.GetExtendedString("kUP5"));
    }

    [Fact]
    public void ExtendedSpanLookupDoesNotAllocate()
    {
        var database = new TermInfo.Database("test", BuildMixedDatabase(false));
        for (var i = 0; i < 100; i++)
        {
            database.GetExtendedString("kUP5".AsSpan());
        }

        var before = GC.GetAllocatedBytesForCurrentThread();
        for (var i = 0; i < 1000; i++)
        {
            GC.KeepAlive(database.GetExtendedString("kUP5".AsSpan()));
            GC.KeepAlive(database.GetExtendedString("missing".AsSpan()));
        }

        Assert.Equal(0, GC.GetAllocatedBytesForCurrentThread() - before);
    }

    private static byte[] BuildMixedDatabase(bool wideNumbers)
    {
        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream);
        writer.Write((short)(wideNumbers ? 0x21E : 0x11A));
        for (var i = 0; i < 5; i++)
        {
            writer.Write((short)0); // Empty standard sections.
        }

        var table = Encoding.ASCII.GetBytes("\e[1;5A\0AX\0XM\0kUP5\0");
        writer.Write((short)1); // Extended boolean count.
        writer.Write((short)1); // Extended number count.
        writer.Write((short)1); // Extended string count.
        writer.Write((short)4); // One value offset and three capability-name offsets.
        writer.Write((short)table.Length);
        writer.Write((short)1); // Boolean value and alignment byte.
        if (wideNumbers)
        {
            writer.Write(100000);
        }
        else
        {
            writer.Write((short)42);
        }

        writer.Write((short)0); // String-value offset.
        writer.Write((short)0); // Boolean-name offset.
        writer.Write((short)3); // Number-name offset.
        writer.Write((short)6); // String-name offset.
        writer.Write(table);
        return stream.ToArray();
    }
}
