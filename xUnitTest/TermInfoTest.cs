// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using System.Text;
using SimplePrompt.Internal;

namespace xUnitTest;

/// <summary>
/// Tests the terminfo database parser and the key mapping built from it.<br/>
/// A synthetic database is generated so that the tests run on every platform.
/// </summary>
public class TermInfoTest
{
    private const short LegacyMagic = 0x11A;
    private const int StringCount = 88; // Must be larger than WellKnownStrings.KeyUp (87).

    [Fact]
    public void ReadStrings()
    {
        var db = new TermInfo.Database("test", BuildTerminfo(
            (TermInfo.WellKnownStrings.KeyUp, "\e[A"),
            (TermInfo.WellKnownStrings.KeyDown, "\e[B"),
            (TermInfo.WellKnownStrings.KeyBackspace, "")));

        Assert.Equal("test", db.Term);
        Assert.Equal("\e[A", db.GetString(TermInfo.WellKnownStrings.KeyUp));
        Assert.Equal("\e[B", db.GetString(TermInfo.WellKnownStrings.KeyDown));
        Assert.Equal("", db.GetString(TermInfo.WellKnownStrings.KeyBackspace));
        Assert.Null(db.GetString(TermInfo.WellKnownStrings.KeyF10)); // Not defined.
        Assert.Null(db.GetString(TermInfo.WellKnownStrings.KeyF24)); // Beyond the string section.
        Assert.False(db.HasExtendedStrings);
        Assert.Null(db.GetExtendedString("kUP5"));
    }

    [Fact]
    public void InvalidMagicNumber()
    {
        var data = BuildTerminfo([(TermInfo.WellKnownStrings.KeyUp, "\e[A")]);
        data[0] = 0xFF;
        data[1] = 0xFF;
        Assert.Throws<InvalidOperationException>(() => new TermInfo.Database("test", data));
    }

    [Fact]
    public void KeyFormatMapping()
    {
        var db = new TermInfo.Database("xterm-256color", BuildTerminfo(
            (TermInfo.WellKnownStrings.KeyUp, "\e[A"),
            (TermInfo.WellKnownStrings.KeyDown, "\e[B"),
            (TermInfo.WellKnownStrings.KeyRight, "\e[C"),
            (TermInfo.WellKnownStrings.KeyLeft, "\e[D"),
            (TermInfo.WellKnownStrings.KeyHome, "\e[H"),
            (TermInfo.WellKnownStrings.KeyDelete, "\e[3~"),
            (TermInfo.WellKnownStrings.KeyF1, "\eOP")));

        var formatStrings = new TerminalFormatStrings(db);
        Assert.False(formatStrings.IsRxvtTerm);

        AssertKey(formatStrings, "\e[A", ConsoleKey.UpArrow);
        AssertKey(formatStrings, "\e[B", ConsoleKey.DownArrow);
        AssertKey(formatStrings, "\e[C", ConsoleKey.RightArrow);
        AssertKey(formatStrings, "\e[D", ConsoleKey.LeftArrow);
        AssertKey(formatStrings, "\e[H", ConsoleKey.Home);
        AssertKey(formatStrings, "\e[3~", ConsoleKey.Delete);
        AssertKey(formatStrings, "\eOP", ConsoleKey.F1);
        Assert.False(formatStrings.KeyFormatToConsoleKey.TryGetValue("\e[Z", out _)); // Not defined.
    }

    [Fact]
    public void RxvtTerm()
    {
        var data = BuildTerminfo([(TermInfo.WellKnownStrings.KeyUp, "\e[A")]);
        Assert.True(new TerminalFormatStrings(new TermInfo.Database("rxvt-unicode-256color", data)).IsRxvtTerm);
        Assert.False(new TerminalFormatStrings(new TermInfo.Database("screen", data)).IsRxvtTerm);
    }

    [Fact]
    public void NullDatabase()
    {
        var formatStrings = new TerminalFormatStrings(null);
        Assert.False(formatStrings.IsRxvtTerm);
        Assert.False(formatStrings.KeyFormatToConsoleKey.TryGetValue("\e[A", out _));
    }

    [Fact]
    public void ExtendedStrings()
    {
        var db = new TermInfo.Database("xterm", BuildTerminfo(
            [(TermInfo.WellKnownStrings.KeyUp, "\e[A")],
            [("kUP5", "\e[1;5A"), ("kUP3", "\e[1;3A")]));

        Assert.True(db.HasExtendedStrings);
        Assert.Equal("\e[1;5A", db.GetExtendedString("kUP5"));
        Assert.Equal("\e[1;3A", db.GetExtendedString("kUP3"));
        Assert.Null(db.GetExtendedString("kDN5"));

        var formatStrings = new TerminalFormatStrings(db);
        AssertKey(formatStrings, "\e[A", ConsoleKey.UpArrow);
        AssertKey(formatStrings, "\e[1;5A", ConsoleKey.UpArrow, control: true);
        AssertKey(formatStrings, "\e[1;3A", ConsoleKey.UpArrow, alt: true);
    }

    private static void AssertKey(TerminalFormatStrings formatStrings, string format, ConsoleKey key, bool shift = false, bool alt = false, bool control = false)
    {
        Assert.True(formatStrings.KeyFormatToConsoleKey.TryGetValue(format, out var keyInfo));
        Assert.Equal(key, keyInfo.Key);
        Assert.Equal(shift, (keyInfo.Modifiers & ConsoleModifiers.Shift) != 0);
        Assert.Equal(alt, (keyInfo.Modifiers & ConsoleModifiers.Alt) != 0);
        Assert.Equal(control, (keyInfo.Modifiers & ConsoleModifiers.Control) != 0);
    }

    private static byte[] BuildTerminfo(params (TermInfo.WellKnownStrings Key, string Value)[] strings)
        => BuildTerminfo(strings, null);

    /// <summary>
    /// Builds a legacy (16 bit) terminfo binary.
    /// </summary>
    /// <param name="strings">The well-known string capabilities.</param>
    /// <param name="extended">The extended string capabilities.</param>
    /// <returns>The terminfo binary.</returns>
    private static byte[] BuildTerminfo((TermInfo.WellKnownStrings Key, string Value)[] strings, (string Name, string Value)[]? extended)
    {
        var names = Encoding.ASCII.GetBytes("test|synthetic terminfo\0");

        // The string table and the offsets into it.
        var offsets = new short[StringCount];
        Array.Fill(offsets, (short)-1);
        var table = new List<byte>();
        foreach (var (key, value) in strings)
        {
            offsets[(int)key] = (short)table.Count;
            table.AddRange(Encoding.ASCII.GetBytes(value));
            table.Add(0);
        }

        var data = new List<byte>();
        WriteInt16(data, LegacyMagic);
        WriteInt16(data, (short)names.Length);
        WriteInt16(data, 0); // Boolean count
        WriteInt16(data, 0); // Number count
        WriteInt16(data, StringCount);
        WriteInt16(data, (short)table.Count);
        data.AddRange(names);
        if ((data.Count % 2) == 1)
        {// The number section starts at an even offset.
            data.Add(0);
        }

        foreach (var offset in offsets)
        {
            WriteInt16(data, offset);
        }

        data.AddRange(table);

        if (extended is not null)
        {
            if ((data.Count % 2) == 1)
            {// The extended section starts at an even offset.
                data.Add(0);
            }

            // Values first, then names; both are NUL terminated.
            var extendedTable = new List<byte>();
            var valueOffsets = new short[extended.Length];
            var nameOffsets = new short[extended.Length];
            for (var i = 0; i < extended.Length; i++)
            {
                valueOffsets[i] = (short)extendedTable.Count;
                extendedTable.AddRange(Encoding.ASCII.GetBytes(extended[i].Value));
                extendedTable.Add(0);
            }

            for (var i = 0; i < extended.Length; i++)
            {
                nameOffsets[i] = (short)extendedTable.Count;
                extendedTable.AddRange(Encoding.ASCII.GetBytes(extended[i].Name));
                extendedTable.Add(0);
            }

            WriteInt16(data, 0); // Extended boolean count
            WriteInt16(data, 0); // Extended number count
            WriteInt16(data, (short)extended.Length);
            WriteInt16(data, (short)extended.Length);
            WriteInt16(data, (short)extendedTable.Count);
            foreach (var offset in valueOffsets)
            {
                WriteInt16(data, offset);
            }

            foreach (var offset in nameOffsets)
            {
                WriteInt16(data, offset);
            }

            data.AddRange(extendedTable);
        }

        return data.ToArray();
    }

    private static void WriteInt16(List<byte> data, short value)
    {
        data.Add((byte)(value & 0xFF));
        data.Add((byte)((value >> 8) & 0xFF));
    }
}
