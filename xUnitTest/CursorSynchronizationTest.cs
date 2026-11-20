// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using SimplePrompt;

namespace xUnitTest;

/// <summary>
/// Checks that the tracked cursor follows output which bypassed <see cref="SimpleConsole"/>,
/// such as a child process writing to the console. The output is fed to <see cref="VirtualTerminal"/> without being recorded,
/// and the terminal cursor is reported as the console's actual cursor position.
/// </summary>
/// <param name="fixture">The shared console fixture.</param>
[Collection(SimpleConsoleTests.Name)]
public class CursorSynchronizationTest(SimpleConsoleFixture fixture)
{
    [Fact]
    public async Task ReadAfterPartialExternalOutputStartsOnANewLine()
    {
        var terminal = await this.StartAtLineStart();
        terminal.Feed("abc"); // A child process wrote a line without a newline.
        fixture.Console.CursorPositionReader = () => terminal.Cursor;
        try
        {
            var task = fixture.ReadLineAsync(new() { Prompt = "repro> ", KeyInputHook = fixture.SettleHook });
            fixture.AssertCursor(terminal);
            Assert.Equal("repro>", terminal.GetRow(terminal.Top));
            Assert.Equal("abc", terminal.GetRow(terminal.Top - 1));

            fixture.Type("xyz");
            fixture.Key(ConsoleKey.Backspace);
            fixture.Key(ConsoleKey.Home);
            fixture.Type("w");
            await fixture.Settle();
            fixture.AssertCursor(terminal);
            Assert.Equal("repro> wxy", terminal.GetRow(terminal.Top));
            Assert.Equal("abc", terminal.GetRow(terminal.Top - 1));
            Assert.Equal(8, terminal.Left);

            fixture.Key(ConsoleKey.Enter);
            Assert.Equal("wxy", await fixture.Wait(task));
        }
        finally
        {
            fixture.Console.CursorPositionReader = SimpleConsoleFixture.NoCursorPosition;
        }
    }

    [Fact]
    public async Task ReadAfterExternalLinesEditsOnThePromptRow()
    {
        await fixture.WaitForIdle();
        fixture.Console.Clear(false);
        fixture.ClearOutput();
        var terminal = fixture.CreateTerminal();
        Assert.Equal((0, 0), terminal.Cursor);

        terminal.Feed("one\r\ntwo\r\nthree\r\n"); // The tracked cursor stays on the first row, above the bottom of the window.
        fixture.Console.CursorPositionReader = () => terminal.Cursor;
        try
        {
            var task = fixture.ReadLineAsync(new() { Prompt = "repro> ", KeyInputHook = fixture.SettleHook });
            fixture.Type("abc");
            fixture.Key(ConsoleKey.LeftArrow);
            fixture.Key(ConsoleKey.Backspace);
            fixture.Key(ConsoleKey.Home);
            fixture.Type("x");
            fixture.Key(ConsoleKey.End);
            fixture.Type("y");
            await fixture.Settle();
            fixture.AssertCursor(terminal);
            Assert.Equal("one", terminal.GetRow(0));
            Assert.Equal("two", terminal.GetRow(1));
            Assert.Equal("three", terminal.GetRow(2));
            Assert.Equal("repro> xacy", terminal.GetRow(3));
            Assert.Equal((11, 3), terminal.Cursor);

            fixture.Key(ConsoleKey.Enter);
            Assert.Equal("xacy", await fixture.Wait(task));
        }
        finally
        {
            fixture.Console.CursorPositionReader = SimpleConsoleFixture.NoCursorPosition;
        }
    }

    [Fact]
    public async Task SynchronizingLetsTheHostEndAPartialLine()
    {
        var terminal = await this.StartAtLineStart();
        terminal.Feed("[Y,N]?");
        fixture.Console.CursorPositionReader = () => terminal.Cursor;
        try
        {
            Assert.True(fixture.Console.SynchronizeCursorPosition());
            Assert.Equal(terminal.Cursor, SimpleConsole.GetCursorPosition());
            Assert.Equal(string.Empty, fixture.TakeOutput()); // Synchronizing writes nothing while no read is pending.

            if (SimpleConsole.CursorLeft > 0)
            {
                fixture.Console.WriteLine();
            }

            fixture.ConsoleOut.WriteLine("Command cancelled.");
            fixture.AssertCursor(terminal);
            Assert.Equal("Command cancelled.", terminal.GetRow(terminal.Top - 1));
            Assert.Equal("[Y,N]?", terminal.GetRow(terminal.Top - 2));
            Assert.Equal(0, terminal.Left);
        }
        finally
        {
            fixture.Console.CursorPositionReader = SimpleConsoleFixture.NoCursorPosition;
        }
    }

    [Fact]
    public async Task SynchronizingRedrawsAPendingReadBelowExternalOutput()
    {
        var terminal = await this.StartAtLineStart();
        fixture.Console.CursorPositionReader = () => terminal.Cursor;
        try
        {
            var task = fixture.ReadLineAsync(new() { Prompt = "> ", KeyInputHook = fixture.SettleHook });
            fixture.Type("ab");
            await fixture.Settle();
            fixture.AssertCursor(terminal);

            // Nothing has moved the cursor, so the input stays where it is.
            Assert.True(fixture.Console.SynchronizeCursorPosition());
            Assert.Equal(string.Empty, fixture.TakeOutput());

            terminal.Feed("out\r\npartial"); // Written at the caret, over the input.
            Assert.True(fixture.Console.SynchronizeCursorPosition());
            fixture.AssertCursor(terminal);
            Assert.Equal("> ab", terminal.GetRow(terminal.Top));
            Assert.Equal("partial", terminal.GetRow(terminal.Top - 1));
            Assert.Equal(4, terminal.Left);

            fixture.Key(ConsoleKey.Home);
            fixture.Key(ConsoleKey.Delete);
            fixture.Type("x");
            await fixture.Settle();
            fixture.AssertCursor(terminal);
            Assert.Equal("> xb", terminal.GetRow(terminal.Top));
            Assert.Equal("partial", terminal.GetRow(terminal.Top - 1));

            fixture.Key(ConsoleKey.Enter);
            Assert.Equal("xb", await fixture.Wait(task));
        }
        finally
        {
            fixture.Console.CursorPositionReader = SimpleConsoleFixture.NoCursorPosition;
        }
    }

    [Fact]
    public async Task UnreadablePositionKeepsTheTrackedCursor()
    {
        await fixture.WaitForIdle();
        fixture.Console.Write("abc");
        var cursor = SimpleConsole.GetCursorPosition();
        fixture.ClearOutput();
        try
        {
            Assert.False(fixture.Console.SynchronizeCursorPosition());
            fixture.Console.CursorPositionReader = static () => throw new IOException();
            Assert.False(fixture.Console.SynchronizeCursorPosition());
            Assert.Equal(cursor, SimpleConsole.GetCursorPosition());

            // The read still ends the tracked partial line before its prompt.
            var terminal = fixture.CreateTerminal();
            var task = fixture.ReadLineAsync(new() { Prompt = "> " });
            fixture.AssertCursor(terminal);
            Assert.Equal((2, Math.Min(cursor.Top + 1, SimpleConsole.WindowHeight - 1)), terminal.Cursor);
            fixture.Type("z");
            fixture.Key(ConsoleKey.Enter);
            Assert.Equal("z", await fixture.Wait(task));
        }
        finally
        {
            fixture.Console.CursorPositionReader = SimpleConsoleFixture.NoCursorPosition;
        }
    }

    [Fact]
    public async Task ReportedPositionIsLimitedToTheWindow()
    {
        await fixture.WaitForIdle();
        fixture.Console.CursorPositionReader = static () => (int.MaxValue, -5);
        try
        {
            Assert.True(fixture.Console.SynchronizeCursorPosition());
            Assert.Equal((SimpleConsole.WindowWidth - 1, 0), SimpleConsole.GetCursorPosition());

            fixture.Console.CursorPositionReader = static () => (-1, int.MaxValue);
            Assert.True(fixture.Console.SynchronizeCursorPosition());
            Assert.Equal((0, SimpleConsole.WindowHeight - 1), SimpleConsole.GetCursorPosition());
        }
        finally
        {
            fixture.Console.CursorPositionReader = SimpleConsoleFixture.NoCursorPosition;
            fixture.ClearOutput();
        }
    }

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
