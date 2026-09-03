// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using SimplePrompt.Internal;

namespace xUnitTest;

/// <summary>
/// Tests the terminal input decoding (the escape sequences a terminal sends over stdin).
/// </summary>
/// <param name="fixture">The shared console fixture.</param>
[Collection(SimpleConsoleTests.Name)]
public class RawConsoleTest(SimpleConsoleFixture fixture)
{
    [Fact]
    public void PlainCharacters()
    {
        var keys = this.Decode("abc");
        Assert.Equal(3, keys.Count);
        Assert.Equal('a', keys[0].KeyChar);
        Assert.Equal(ConsoleKey.A, keys[0].Key);
        Assert.Equal('b', keys[1].KeyChar);
        Assert.Equal('c', keys[2].KeyChar);
        Assert.Equal(default, keys[0].Modifiers);
    }

    [Fact]
    public void UpperCaseAndDigits()
    {
        var keys = this.Decode("A5");
        Assert.Equal(ConsoleKey.A, keys[0].Key);
        Assert.Equal(ConsoleModifiers.Shift, keys[0].Modifiers);
        Assert.Equal(ConsoleKey.D5, keys[1].Key);
        Assert.Equal('5', keys[1].KeyChar);
    }

    [Theory]
    [InlineData("\r", ConsoleKey.Enter)]
    [InlineData("\t", ConsoleKey.Tab)]
    [InlineData(" ", ConsoleKey.Spacebar)]
    [InlineData("\u007F", ConsoleKey.Backspace)]
    [InlineData("\e", ConsoleKey.Escape)]
    [InlineData(",", ConsoleKey.OemComma)]
    [InlineData(".", ConsoleKey.OemPeriod)]
    [InlineData("+", ConsoleKey.Add)]
    [InlineData("-", ConsoleKey.Subtract)]
    [InlineData("*", ConsoleKey.Multiply)]
    [InlineData("/", ConsoleKey.Divide)]
    public void SingleCharacterKeys(string input, ConsoleKey expected)
    {
        var keys = this.Decode(input);
        Assert.Single(keys);
        Assert.Equal(expected, keys[0].Key);
    }

    [Fact]
    public void ControlCharacters()
    {
        var keys = this.Decode("\u0001"); // Ctrl+A
        Assert.Single(keys);
        Assert.Equal(ConsoleKey.A, keys[0].Key);
        Assert.Equal(ConsoleModifiers.Control, keys[0].Modifiers);

        keys = this.Decode("\u0015"); // Ctrl+U
        Assert.Equal(ConsoleKey.U, keys[0].Key);
        Assert.Equal(ConsoleModifiers.Control, keys[0].Modifiers);

        keys = this.Decode("\b"); // Ctrl+H
        Assert.Equal(ConsoleKey.Backspace, keys[0].Key);
        Assert.Equal(ConsoleModifiers.Control, keys[0].Modifiers);
    }

    [Fact]
    public void AltAndCharacter()
    {
        // Escape followed by a single character is Alt + that character.
        var keys = this.Decode("\ea");
        Assert.Single(keys);
        Assert.Equal(ConsoleKey.A, keys[0].Key);
        Assert.Equal('a', keys[0].KeyChar);
        Assert.Equal(ConsoleModifiers.Alt, keys[0].Modifiers);
    }

    [Fact]
    public void AltKeyFollowedByOtherKeys()
    {
        var keys = this.Decode("\eabc");
        Assert.Equal(3, keys.Count);
        Assert.Equal(ConsoleModifiers.Alt, keys[0].Modifiers);
        Assert.Equal('a', keys[0].KeyChar);
        Assert.Equal('b', keys[1].KeyChar);
        Assert.Equal('c', keys[2].KeyChar);
    }

    [Theory]
    [InlineData("\e[A", ConsoleKey.UpArrow)]
    [InlineData("\e[B", ConsoleKey.DownArrow)]
    [InlineData("\e[C", ConsoleKey.RightArrow)]
    [InlineData("\e[D", ConsoleKey.LeftArrow)]
    [InlineData("\e[H", ConsoleKey.Home)]
    [InlineData("\eOA", ConsoleKey.UpArrow)]
    [InlineData("\eOB", ConsoleKey.DownArrow)]
    [InlineData("\eOC", ConsoleKey.RightArrow)]
    [InlineData("\eOD", ConsoleKey.LeftArrow)]
    [InlineData("\eOH", ConsoleKey.Home)]
    [InlineData("\eOF", ConsoleKey.End)]
    [InlineData("\eOP", ConsoleKey.F1)]
    [InlineData("\eOQ", ConsoleKey.F2)]
    public void ThreeCharacterSequences(string input, ConsoleKey expected)
    {
        var keys = this.Decode(input);
        Assert.Single(keys);
        Assert.Equal(expected, keys[0].Key);
    }

    [Theory]
    [InlineData("\e[1~", ConsoleKey.Home)]
    [InlineData("\e[2~", ConsoleKey.Insert)]
    [InlineData("\e[3~", ConsoleKey.Delete)]
    [InlineData("\e[4~", ConsoleKey.End)]
    [InlineData("\e[5~", ConsoleKey.PageUp)]
    [InlineData("\e[6~", ConsoleKey.PageDown)]
    [InlineData("\e[11~", ConsoleKey.F1)]
    [InlineData("\e[15~", ConsoleKey.F5)]
    [InlineData("\e[24~", ConsoleKey.F12)]
    public void VtSequences(string input, ConsoleKey expected)
    {
        var keys = this.Decode(input);
        Assert.Single(keys);
        Assert.Equal(expected, keys[0].Key);
    }

    [Theory]
    [InlineData("\e[1;2A", ConsoleModifiers.Shift)]
    [InlineData("\e[1;3A", ConsoleModifiers.Alt)]
    [InlineData("\e[1;5A", ConsoleModifiers.Control)]
    [InlineData("\e[1;6A", ConsoleModifiers.Shift | ConsoleModifiers.Control)]
    [InlineData("\e[1;8A", ConsoleModifiers.Shift | ConsoleModifiers.Alt | ConsoleModifiers.Control)]
    public void ModifiedSequences(string input, ConsoleModifiers expected)
    {
        var keys = this.Decode(input);
        Assert.Single(keys);
        Assert.Equal(ConsoleKey.UpArrow, keys[0].Key);
        Assert.Equal(expected, keys[0].Modifiers);
    }

    [Fact]
    public void ModifiedVtSequence()
    {
        var keys = this.Decode("\e[3;5~"); // Ctrl+Delete
        Assert.Single(keys);
        Assert.Equal(ConsoleKey.Delete, keys[0].Key);
        Assert.Equal(ConsoleModifiers.Control, keys[0].Modifiers);
    }

    [Fact]
    public void SequenceFollowedByText()
    {
        var keys = this.Decode("\e[Aab");
        Assert.Equal(3, keys.Count);
        Assert.Equal(ConsoleKey.UpArrow, keys[0].Key);
        Assert.Equal('a', keys[1].KeyChar);
        Assert.Equal('b', keys[2].KeyChar);
    }

    [Fact]
    public void MultipleSequences()
    {
        var keys = this.Decode("\e[A\e[B\e[3~");
        Assert.Equal(3, keys.Count);
        Assert.Equal(ConsoleKey.UpArrow, keys[0].Key);
        Assert.Equal(ConsoleKey.DownArrow, keys[1].Key);
        Assert.Equal(ConsoleKey.Delete, keys[2].Key);
    }

    [Fact]
    public void NonAsciiCharacters()
    {
        var keys = this.Decode("あ漢");
        Assert.Equal(2, keys.Count);
        Assert.Equal('あ', keys[0].KeyChar);
        Assert.Equal('漢', keys[1].KeyChar);
    }

    [Fact]
    public void SurrogatePair()
    {
        var keys = this.Decode("\U0001F600");
        Assert.Equal(2, keys.Count); // The pair is delivered as two key inputs.
        Assert.True(char.IsHighSurrogate(keys[0].KeyChar));
        Assert.True(char.IsLowSurrogate(keys[1].KeyChar));
    }

    [Fact]
    public void UnknownSequenceIsNotDiscarded()
    {
        // An unknown sequence falls back to single character parsing instead of being dropped.
        var keys = this.Decode("\e[\0");
        Assert.NotEmpty(keys);
        Assert.Equal(ConsoleKey.Escape, keys[0].Key);
    }

    [Fact]
    public void EmptyInput()
        => Assert.Empty(this.Decode(string.Empty));

    [Theory]
    [InlineData("\e[1\u0661~")]
    [InlineData("\e[1\uFF11~")]
    [InlineData("\e[1\u0661;5~")]
    public void NonAsciiSequenceNumberIsPreserved(string input)
    {
        var keys = this.Decode(input);
        Assert.Equal(input, new string(keys.Select(key => key.KeyChar).ToArray()));
    }

    private List<ConsoleKeyInfo> Decode(string input)
    {
        // A dedicated instance is used so that the worker thread never competes for the buffer.
        var rawConsole = new RawConsole(fixture.Console);
        return rawConsole.DecodeKeys(input);
    }
}
