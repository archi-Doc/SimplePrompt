// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using System.Text;
using SimplePrompt;
using SimplePrompt.Internal;

namespace xUnitTest;

/// <summary>
/// Checks the rendered screen and the tracked cursor with <see cref="VirtualTerminal"/>.
/// </summary>
/// <param name="fixture">The shared console fixture.</param>
[Collection(SimpleConsoleTests.Name)]
public class DisplayTest(SimpleConsoleFixture fixture)
{
    [Fact]
    public async Task OutputRestoresTheCaretAtTheStartOfInputWithoutPrompt()
    {
        await fixture.WaitForIdle();
        var task = fixture.ReadLineAsync(new() { Prompt = string.Empty, KeyInputHook = fixture.SettleHook });
        fixture.Type("abc");
        fixture.Key(ConsoleKey.Home);
        await fixture.Settle();
        fixture.ClearOutput();
        var terminal = fixture.CreateTerminal();

        fixture.Console.WriteLine("log");
        fixture.AssertCursor(terminal);
        Assert.Equal(0, terminal.Left);
        Assert.Equal("abc", terminal.GetRow(terminal.Top));
        Assert.Equal("log", terminal.GetRow(terminal.Top - 1));

        fixture.Type("x");
        await fixture.Settle();
        fixture.AssertCursor(terminal);
        Assert.Equal("xabc", terminal.GetRow(terminal.Top));

        fixture.Key(ConsoleKey.Enter);
        Assert.Equal("xabc", await fixture.Wait(task));
    }

    [Fact]
    public async Task DeletingAnEmptyMiddleLineKeepsTheCaret()
    {
        var terminal = await this.StartAtLineStart();
        var task = fixture.ReadLineAsync(new() { MultilineDelimiter = "|", KeyInputHook = fixture.SettleHook });
        fixture.Type("|");
        fixture.Key(ConsoleKey.Enter);
        fixture.Key(ConsoleKey.Enter);
        fixture.Key(ConsoleKey.UpArrow);
        fixture.Key(ConsoleKey.Delete); // Removes the empty middle line.
        fixture.Type("x");
        await fixture.Settle();
        fixture.AssertCursor(terminal);
        Assert.Equal("# x", terminal.GetRow(terminal.Top));
        Assert.Equal("> |", terminal.GetRow(terminal.Top - 1));
        Assert.Equal(string.Empty, terminal.GetRow(terminal.Top + 1));

        fixture.Type("|");
        fixture.Key(ConsoleKey.Enter);
        Assert.Equal("|\nx|", await fixture.Wait(task));
    }

    [Fact]
    public async Task ShrinkingTheLastLineClearsItsFreedRow()
    {
        var terminal = await this.StartAtLineStart();
        var text = new string('a', SimpleConsole.WindowWidth - 3) + "日";
        var task = fixture.ReadLineAsync(new() { Prompt = string.Empty, KeyInputHook = fixture.SettleHook });
        fixture.Type(text + "日"); // The second wide character wraps to the next row.
        fixture.Key(ConsoleKey.LeftArrow);
        fixture.Key(ConsoleKey.Backspace); // The remaining text fits in one row.
        await fixture.Settle();
        fixture.AssertCursor(terminal);
        Assert.Equal(text, terminal.GetRow(terminal.Top));
        Assert.Equal(string.Empty, terminal.GetRow(terminal.Top + 1));

        fixture.Key(ConsoleKey.Enter);
        Assert.Equal(text, await fixture.Wait(task));
    }

    [Fact]
    public async Task ClearingMaskedInputErasesTheMask()
    {
        var terminal = await this.StartAtLineStart();
        var task = fixture.ReadLineAsync(new() { Prompt = "pw> ", MaskingCharacter = '*', KeyInputHook = fixture.SettleHook });
        fixture.Type("secret");
        fixture.Key(ConsoleKey.U, control: true);
        await fixture.Settle();
        fixture.AssertCursor(terminal);
        Assert.Equal("pw>", terminal.GetRow(terminal.Top));
        Assert.Equal(4, terminal.Left);

        fixture.Type("ok");
        fixture.Key(ConsoleKey.Enter);
        Assert.Equal("ok", await fixture.Wait(task));
    }

    [Fact]
    public async Task PromptLineOfWindowWidthKeepsTheInputBelowIt()
    {
        var terminal = await this.StartAtLineStart();
        var header = new string('p', SimpleConsole.WindowWidth);
        var task = fixture.ReadLineAsync(new() { Prompt = header + "\n> ", KeyInputHook = fixture.SettleHook });
        fixture.Type("x");
        await fixture.Settle();
        fixture.AssertCursor(terminal);
        Assert.Equal("> x", terminal.GetRow(terminal.Top));
        Assert.Equal(string.Empty, terminal.GetRow(terminal.Top - 1)); // A full row is followed by an empty row.
        Assert.Equal(header, terminal.GetRow(terminal.Top - 2));

        fixture.Key(ConsoleKey.Enter);
        Assert.Equal("x", await fixture.Wait(task));
    }

    [Fact]
    public async Task OutputOfWindowWidthKeepsItsLastCharacterAndTheCursor()
    {
        var terminal = await this.StartAtLineStart();
        var width = SimpleConsole.WindowWidth;
        fixture.Console.WriteLine(new string('-', width));
        fixture.AssertCursor(terminal);
        Assert.Equal(new string('-', width), terminal.GetRow(terminal.Top - 1));

        fixture.Console.Write(new string('=', width));
        fixture.AssertCursor(terminal);
        Assert.Equal(new string('=', width), terminal.GetRow(terminal.Top - 1));

        fixture.Console.WriteLine();
        fixture.AssertCursor(terminal);
        Assert.Equal(0, terminal.Left);
    }

    [Theory]
    [InlineData("\e]8;;https://example.com/\e\\link\e]8;;\e\\", "link")]
    [InlineData("\e]0;title\alink", "link")]
    [InlineData("\e7\e8link", "link")]
    [InlineData("\e(Blink", "link")]
    [InlineData("\e[1;31mlink\e[0m", "link")]
    [InlineData("xy\rlink", "link")]
    [InlineData("12\tlink", "12      link")]
    public async Task CursorSkipsEscapeSequencesAndFollowsControlCharacters(string text, string expected)
    {
        var terminal = await this.StartAtLineStart();
        fixture.Console.Write(text);
        fixture.AssertCursor(terminal);
        Assert.Equal(expected.Length, terminal.Left);
        Assert.Equal(expected, terminal.GetRow(terminal.Top));
        fixture.Console.WriteLine();
        fixture.ClearOutput();
    }

    [Fact]
    public async Task WriteEndingWithNewlineDuringReadDoesNotAddBlankLine()
    {
        await fixture.WaitForIdle();
        var task = fixture.ReadLineAsync(new() { Prompt = "> ", KeyInputHook = fixture.SettleHook });
        fixture.Type("in");
        await fixture.Settle();
        fixture.ClearOutput();
        var terminal = fixture.CreateTerminal();

        fixture.Console.Write("log\n");
        fixture.AssertCursor(terminal);
        Assert.Equal("> in", terminal.GetRow(terminal.Top));
        Assert.Equal("log", terminal.GetRow(terminal.Top - 1));

        fixture.Key(ConsoleKey.Enter);
        Assert.Equal("in", await fixture.Wait(task));
    }

    [Fact]
    public async Task NewLineStartsBelowAWrappedLastLine()
    {
        var terminal = await this.StartAtLineStart();
        var width = SimpleConsole.WindowWidth;
        var text = new string('x', width + 5);
        var task = fixture.ReadLineAsync(new() { MultilineDelimiter = "|", KeyInputHook = fixture.SettleHook });
        fixture.Type("|");
        fixture.Key(ConsoleKey.Enter);
        fixture.Type(text);
        fixture.Key(ConsoleKey.Home);
        fixture.Key(ConsoleKey.Enter); // Adds a line while the caret is on the first row of the wrapped line.
        await fixture.Settle();
        fixture.AssertCursor(terminal);
        Assert.Equal("#", terminal.GetRow(terminal.Top));
        Assert.Equal(new string('x', 7), terminal.GetRow(terminal.Top - 1));
        Assert.Equal("# " + new string('x', width - 2), terminal.GetRow(terminal.Top - 2));

        fixture.Type("|");
        fixture.Key(ConsoleKey.Enter);
        Assert.Equal("|\n" + text + "\n|", await fixture.Wait(task));
    }

    [Fact]
    public async Task RejectedWrappedInputStaysAboveTheNewPrompt()
    {
        var terminal = await this.StartAtLineStart();
        var width = SimpleConsole.WindowWidth;
        var task = fixture.ReadLineAsync(new()
        {
            SubmitHook = text => text == "ok" ? text : null,
            KeyInputHook = fixture.SettleHook,
        });

        fixture.Type(new string('x', width + 5));
        fixture.Key(ConsoleKey.Home);
        fixture.Key(ConsoleKey.Enter); // Rejected while the caret is on the first row.
        await fixture.Settle();
        fixture.AssertCursor(terminal);
        Assert.Equal(">", terminal.GetRow(terminal.Top));
        Assert.Equal(new string('x', 7), terminal.GetRow(terminal.Top - 1));
        Assert.Equal("> " + new string('x', width - 2), terminal.GetRow(terminal.Top - 2));

        fixture.Type("ok");
        fixture.Key(ConsoleKey.Enter);
        Assert.Equal("ok", await fixture.Wait(task));
    }

    [Theory]
    [InlineData(1729, "> ", false)]
    [InlineData(4104, "日本\n> ", false)]
    [InlineData(13832, "> ", true)]
    public async Task RandomEditingKeepsTheScreenInSync(int seed, string prompt, bool masked)
    {
        var random = new Random(seed);
        var width = SimpleConsole.WindowWidth;
        var maskingCharacter = masked ? '*' : default;
        string[] characters = ["a", "b", "日", "😀"];
        var terminal = await this.StartAtLineStart();
        var task = fixture.ReadLineAsync(new()
        {
            Prompt = prompt,
            MaskingCharacter = maskingCharacter,
            MultilineDelimiter = "|",
            CancelOnEscape = true,
            SubmitHook = _ => null, // Every submission is rejected and prompts again.
            KeyInputHook = fixture.SettleHook,
        });

        fixture.Type("|");
        fixture.Key(ConsoleKey.Enter);
        var history = new List<int>();
        for (var step = 0; step < 120; step++)
        {
            var action = random.Next(14);
            switch (action)
            {
                case 0:
                    fixture.Type(characters[random.Next(characters.Length)]);
                    break;
                case 1:
                    fixture.Type(string.Concat(Enumerable.Range(0, width + 3).Select(_ => characters[random.Next(characters.Length)])));
                    break;
                case 2:
                    fixture.Key(ConsoleKey.LeftArrow);
                    break;
                case 3:
                    fixture.Key(ConsoleKey.RightArrow);
                    break;
                case 4:
                    fixture.Key(ConsoleKey.Home);
                    break;
                case 5:
                    fixture.Key(ConsoleKey.End);
                    break;
                case 6:
                    fixture.Key(ConsoleKey.Backspace);
                    break;
                case 7:
                    fixture.Key(ConsoleKey.Delete);
                    break;
                case 8:
                    fixture.Key(ConsoleKey.UpArrow);
                    break;
                case 9:
                    fixture.Key(ConsoleKey.DownArrow);
                    break;
                case 10:
                    fixture.Key(ConsoleKey.Enter);
                    break;
                case 11:
                    fixture.Key(ConsoleKey.U, control: true);
                    break;
                case 12:
                    fixture.Console.WriteLine("log");
                    break;
                default:
                    fixture.Type("|"); // Toggles the delimiter count of the line.
                    break;
            }

            await fixture.Settle();
            history.Add(action);
            this.AssertScreen(terminal, maskingCharacter, $"Seed {seed}, step {step}, actions {string.Join(",", history)}");
        }

        fixture.Key(ConsoleKey.Escape);
        Assert.True((await fixture.WaitResult(task)).IsCanceled);
    }

    [Fact]
    public async Task WrappedWideCharacterClearsTheLastColumn()
    {
        var terminal = await this.StartAtLineStart();
        var text = new string('a', SimpleConsole.WindowWidth - 1) + "日"; // The first row ends one column short.
        var task = fixture.ReadLineAsync(new() { Prompt = string.Empty, KeyInputHook = fixture.SettleHook });
        fixture.Type(text);
        fixture.Key(ConsoleKey.Home);
        fixture.Type("b"); // Fills the first row, so that the last column displays 'a'.
        fixture.Key(ConsoleKey.Backspace); // The first row becomes short again.
        await fixture.Settle();
        this.AssertScreen(terminal, default, nameof(this.WrappedWideCharacterClearsTheLastColumn));

        fixture.Key(ConsoleKey.Enter);
        Assert.Equal(text, await fixture.Wait(task));
    }

    [Fact]
    public async Task RowShortenedBeforeTheCaretClearsItsLastColumn()
    {
        var terminal = await this.StartAtLineStart();
        var text = new string('a', SimpleConsole.WindowWidth - 2) + "x";
        var task = fixture.ReadLineAsync(new() { Prompt = string.Empty, KeyInputHook = fixture.SettleHook });
        fixture.Type(text + "y日");
        fixture.Key(ConsoleKey.LeftArrow);
        fixture.Key(ConsoleKey.Backspace); // Removes 'y' in the last column; the wide character does not fit there.
        await fixture.Settle();
        this.AssertScreen(terminal, default, nameof(this.RowShortenedBeforeTheCaretClearsItsLastColumn));

        fixture.Key(ConsoleKey.Enter);
        Assert.Equal(text + "日", await fixture.Wait(task));
    }

    [Fact]
    public async Task LinePushedBelowTheWindowScrollsIt()
    {
        var terminal = await this.StartAtLineStart();
        for (var i = 0; i < SimpleConsole.WindowHeight; i++)
        {// Start on the last row.
            fixture.Console.WriteLine();
        }

        var width = SimpleConsole.WindowWidth;
        var task = fixture.ReadLineAsync(new() { MultilineDelimiter = "|", KeyInputHook = fixture.SettleHook });
        fixture.Type("|");
        fixture.Key(ConsoleKey.Enter);
        fixture.Type("next");
        fixture.Key(ConsoleKey.UpArrow);
        fixture.Key(ConsoleKey.End);
        fixture.Type(new string('x', width)); // The first line grows and pushes the last line below the window.
        await fixture.Settle();
        this.AssertScreen(terminal, default, nameof(this.LinePushedBelowTheWindowScrollsIt));
        Assert.Equal("# next", terminal.GetRow(terminal.Height - 1));

        fixture.Key(ConsoleKey.DownArrow);
        fixture.Key(ConsoleKey.End);
        fixture.Type("|");
        fixture.Key(ConsoleKey.Enter);
        Assert.Equal("|" + new string('x', width) + "\nnext|", await fixture.Wait(task));
    }

    private static string GetDisplayedText(SimpleTextLine line, SimpleTextRow row, char maskingCharacter)
    {
        var builder = new StringBuilder();
        for (var i = row.Start; i < row.End; i++)
        {
            if (maskingCharacter != default && i >= line.PromptLength)
            {
                builder.Append(maskingCharacter, line.WidthArray[i]);
            }
            else
            {
                builder.Append(line.CharArray[i]);
            }
        }

        return builder.ToString().TrimEnd();
    }

    /// <summary>
    /// Checks the cursor and that every row of the active read, and the row below it, display what the lines contain.
    /// </summary>
    /// <param name="terminal">The terminal.</param>
    /// <param name="maskingCharacter">The masking character of the read.</param>
    /// <param name="message">The message describing the state.</param>
    private void AssertScreen(VirtualTerminal terminal, char maskingCharacter, string message)
    {
        fixture.AssertCursor(terminal);
        Assert.True(fixture.Console.TryGetActiveInstance(out var instance), message);
        var bottom = 0;
        foreach (var line in instance.LineList)
        {
            foreach (var row in line.Rows)
            {
                if (row.Top >= 0)
                {
                    var expected = GetDisplayedText(line, row, maskingCharacter);
                    Assert.True(expected == terminal.GetRow(row.Top), $"{message}: row {row.Top} displays \"{terminal.GetRow(row.Top)}\" instead of \"{expected}\".");
                }

                bottom = row.Top + 1;
            }
        }

        if (bottom < terminal.Height)
        {
            Assert.True(terminal.GetRow(bottom).Length == 0, $"{message}: row {bottom} below the input displays \"{terminal.GetRow(bottom)}\".");
        }
    }

    /// <summary>
    /// Waits for idle, moves the cursor to the start of a line, and creates a terminal from there.
    /// </summary>
    /// <returns>The terminal.</returns>
    private async Task<VirtualTerminal> StartAtLineStart()
    {
        await fixture.WaitForIdle();
        if (SimpleConsole.CursorLeft > 0)
        {
            fixture.Console.WriteLine();
        }

        fixture.ClearOutput();
        return fixture.CreateTerminal();
    }
}
