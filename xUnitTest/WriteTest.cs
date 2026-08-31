// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using System.Text;
using Arc.Unit;
using SimplePrompt;

namespace xUnitTest;

/// <summary>
/// Tests the output side of <see cref="SimpleConsole"/> and the <see cref="TextWriter"/> installed as <see cref="Console.Out"/>.
/// </summary>
/// <param name="fixture">The shared console fixture.</param>
[Collection(SimpleConsoleTests.Name)]
public class WriteTest(SimpleConsoleFixture fixture)
{
    private const string Green = "\e[32m";
    private const string Reset = "\e[0m";

    [Fact]
    public async Task WriteAndWriteLine()
    {
        await fixture.WaitForIdle();
        fixture.ClearOutput();

        fixture.Console.Write("abc");
        Assert.Contains("abc", fixture.TakeOutput());

        fixture.Console.WriteLine("def");
        var output = fixture.TakeOutput();
        Assert.Contains("def", output);
        Assert.Contains('\n', output);
    }

    [Fact]
    public async Task WriteEmpty()
    {
        await fixture.WaitForIdle();

        fixture.ClearOutput();
        fixture.Console.Write(string.Empty);
        Assert.DoesNotContain('\n', fixture.TakeOutput()); // Write() of an empty string outputs nothing.

        fixture.ClearOutput();
        fixture.Console.WriteLine(string.Empty);
        Assert.Contains('\n', fixture.TakeOutput());

        fixture.ClearOutput();
        fixture.Console.WriteLine();
        Assert.Contains('\n', fixture.TakeOutput());
    }

    [Fact]
    public async Task WriteNull()
    {
        await fixture.WaitForIdle();

        fixture.ClearOutput();
        fixture.Console.Write((string?)null);
        Assert.DoesNotContain('\n', fixture.TakeOutput());

        fixture.ClearOutput();
        fixture.Console.WriteLine((string?)null);
        Assert.Contains('\n', fixture.TakeOutput());
    }

    [Fact]
    public async Task WriteMultipleLines()
    {
        await fixture.WaitForIdle();
        fixture.Console.WriteLine(); // Move to the beginning of a line.
        var top = SimpleConsole.CursorTop;
        fixture.ClearOutput();

        fixture.Console.WriteLine("first\nsecond\r\nthird");
        var output = fixture.TakeOutput();
        Assert.Contains("first", output);
        Assert.Contains("second", output);
        Assert.Contains("third", output);
        Assert.Equal(0, SimpleConsole.CursorLeft);
        Assert.Equal(ExpectedTop(top, 3), SimpleConsole.CursorTop);
    }

    [Fact]
    public async Task WriteLongerThanInternalBuffer()
    {
        await fixture.WaitForIdle();
        var text = string.Concat(Enumerable.Repeat("0123456789", 5_000)); // 50,000 characters
        fixture.ClearOutput();
        fixture.Console.WriteLine(text);
        Assert.Equal(text.Length, fixture.TakeOutput().Count(char.IsAsciiDigit));
    }

    [Fact]
    public async Task WriteSurrogatePairsLongerThanInternalBuffer()
    {
        await fixture.WaitForIdle();
        var text = string.Concat(Enumerable.Repeat("\U0001F600", 20_000)); // 40,000 characters
        fixture.ClearOutput();
        fixture.Console.WriteLine(text);

        var output = fixture.TakeOutput();
        var count = 0;
        for (var i = 0; i < (output.Length - 1); i++)
        {
            if (char.IsHighSurrogate(output[i]) && char.IsLowSurrogate(output[i + 1]))
            {// A surrogate pair must not be split at the internal buffer boundary.
                count++;
                i++;
            }
        }

        Assert.Equal(20_000, count);
    }

    [Fact]
    public async Task CursorIsAdvancedByTheDisplayWidth()
    {
        await fixture.WaitForIdle();
        fixture.Console.WriteLine();
        Assert.Equal(0, SimpleConsole.CursorLeft);

        fixture.Console.Write("abc");
        Assert.Equal(3, SimpleConsole.CursorLeft);

        fixture.Console.Write("あ"); // A full width character occupies two columns.
        Assert.Equal(5, SimpleConsole.CursorLeft);

        fixture.Console.Write("\e[31mX\e[0m"); // Escape sequences occupy no column.
        Assert.Equal(6, SimpleConsole.CursorLeft);

        fixture.Console.Write("\U0001F600"); // An emoji occupies two columns.
        Assert.Equal(8, SimpleConsole.CursorLeft);

        var top = SimpleConsole.CursorTop;
        fixture.Console.WriteLine();
        Assert.Equal(0, SimpleConsole.CursorLeft);
        Assert.Equal(ExpectedTop(top, 1), SimpleConsole.CursorTop);
    }

    [Fact]
    public async Task CursorWrapsAtTheWindowWidth()
    {
        await fixture.WaitForIdle();
        fixture.Console.WriteLine();
        var top = SimpleConsole.CursorTop;

        fixture.Console.Write(new string('a', SimpleConsole.WindowWidth + 3));
        Assert.Equal(3, SimpleConsole.CursorLeft);
        Assert.Equal(ExpectedTop(top, 1), SimpleConsole.CursorTop);
        fixture.Console.WriteLine();
    }

    [Fact]
    public async Task Color()
    {
        await fixture.WaitForIdle();

        fixture.ClearOutput();
        fixture.Console.WriteLine("colored", ConsoleColor.Green);
        var output = fixture.TakeOutput();
        Assert.Contains(Green, output);
        Assert.Contains(Reset, output);

        fixture.ClearOutput();
        fixture.Console.WriteLine("default color");
        Assert.DoesNotContain(Green, fixture.TakeOutput());
    }

    [Fact]
    public async Task ColorIsSuppressedWhenDisabled()
    {
        await fixture.WaitForIdle();
        fixture.Console.EnableColor = false;
        try
        {
            fixture.ClearOutput();
            fixture.Console.WriteLine("not colored", ConsoleColor.Green);
            var output = fixture.TakeOutput();
            Assert.Contains("not colored", output);
            Assert.DoesNotContain(Green, output);
            Assert.DoesNotContain(Reset, output);
        }
        finally
        {
            fixture.Console.EnableColor = true;
        }
    }

    [Fact]
    public async Task InputColorIsSuppressedWhenDisabled()
    {
        await fixture.WaitForIdle();
        fixture.Console.EnableColor = false;
        try
        {
            fixture.ClearOutput();
            var task = fixture.ReadLine(new() { AllowEmptyLineInput = true, InputColor = ConsoleColor.Green });
            fixture.Type("plain");
            fixture.Key(ConsoleKey.Enter);
            Assert.Equal("plain", await fixture.Wait(task));

            var output = fixture.TakeOutput();
            Assert.DoesNotContain(Green, output);
            Assert.DoesNotContain(Reset, output);
        }
        finally
        {
            fixture.Console.EnableColor = true;
        }
    }

    [Fact]
    public async Task WriteWhileReadLineIsInProgress()
    {
        await fixture.WaitForIdle();
        fixture.ClearOutput();

        var task = fixture.ReadLine(new() { Prompt = "busy> ", AllowEmptyLineInput = true });
        fixture.Type("typed");
        await SimpleConsoleFixture.Delay(50);

        // The message is written above the prompt and the input line is redrawn.
        fixture.Console.WriteLine("background message");
        await SimpleConsoleFixture.Delay(50);
        var output = fixture.TakeOutput();
        Assert.Contains("background message", output);
        Assert.Contains("busy> ", output); // Redrawn.

        fixture.Key(ConsoleKey.Enter);
        Assert.Equal("typed", await fixture.Wait(task)); // The input is preserved.
    }

    [Fact]
    public async Task ClearScreen()
    {
        await fixture.WaitForIdle();
        fixture.Console.WriteLine("something");
        fixture.ClearOutput();

        fixture.Console.Clear(false);
        Assert.Contains("\e[2J", fixture.TakeOutput()); // Erase the entire screen.
        Assert.Equal(0, SimpleConsole.CursorLeft);
        Assert.Equal(0, SimpleConsole.CursorTop);
    }

    [Fact]
    public async Task ClearScreenWhileReadLineIsInProgress()
    {
        await fixture.WaitForIdle();
        var task = fixture.ReadLine(new() { Prompt = "kept> ", AllowEmptyLineInput = true });
        fixture.Type("input");
        await SimpleConsoleFixture.Delay(50);

        fixture.ClearOutput();
        fixture.Console.Clear(false);
        Assert.Contains("kept> ", fixture.TakeOutput()); // The prompt is redrawn after clearing.

        fixture.Key(ConsoleKey.Enter);
        Assert.Equal("input", await fixture.Wait(task));
    }

    [Fact]
    public async Task WriteValueOverloads()
    {
        await fixture.WaitForIdle();
        var provider = fixture.Console.UnderlyingTextWriter.FormatProvider;

        this.AssertWrite("True", () => fixture.Console.Write(true));
        this.AssertWrite("x", () => fixture.Console.Write('x'));
        this.AssertWrite(123.ToString(provider), () => fixture.Console.Write(123));
        this.AssertWrite(123u.ToString(provider), () => fixture.Console.Write(123u));
        this.AssertWrite(1234567890123L.ToString(provider), () => fixture.Console.Write(1234567890123L));
        this.AssertWrite(1234567890123ul.ToString(provider), () => fixture.Console.Write(1234567890123ul));
        this.AssertWrite(1.5f.ToString(provider), () => fixture.Console.Write(1.5f));
        this.AssertWrite(1.5d.ToString(provider), () => fixture.Console.Write(1.5d));
        this.AssertWrite(1.5m.ToString(provider), () => fixture.Console.Write(1.5m));

        this.AssertWrite("False", () => fixture.Console.WriteLine(false));
        this.AssertWrite("y", () => fixture.Console.WriteLine('y'));
        this.AssertWrite(456.ToString(provider), () => fixture.Console.WriteLine(456));
        this.AssertWrite(456u.ToString(provider), () => fixture.Console.WriteLine(456u));
        this.AssertWrite(456L.ToString(provider), () => fixture.Console.WriteLine(456L));
        this.AssertWrite(456ul.ToString(provider), () => fixture.Console.WriteLine(456ul));
        this.AssertWrite(2.5f.ToString(provider), () => fixture.Console.WriteLine(2.5f));
        this.AssertWrite(2.5d.ToString(provider), () => fixture.Console.WriteLine(2.5d));
        this.AssertWrite(2.5m.ToString(provider), () => fixture.Console.WriteLine(2.5m));
    }

    [Fact]
    public async Task ConsoleOutIsRoutedToSimpleConsole()
    {
        await fixture.WaitForIdle();
        var writer = fixture.ConsoleOut;
        var provider = writer.FormatProvider;

        Assert.Equal(Encoding.UTF8, writer.Encoding);
        Assert.NotNull(fixture.Console.UnderlyingTextWriter);

        this.AssertWrite("plain string", () => writer.Write("plain string"));
        this.AssertWrite("with newline", () => writer.WriteLine("with newline"));
        this.AssertWrite("True", () => writer.Write(true));
        this.AssertWrite("False", () => writer.WriteLine(false));
        this.AssertWrite("c", () => writer.Write('c'));
        this.AssertWrite("d", () => writer.WriteLine('d'));
        this.AssertWrite("chars", () => writer.Write("chars".ToCharArray()));
        this.AssertWrite("chars2", () => writer.WriteLine("chars2".ToCharArray()));
        this.AssertWrite("ell", () => writer.Write("hello".ToCharArray(), 1, 3));
        this.AssertWrite("ell", () => writer.WriteLine("hello".ToCharArray(), 1, 3));
        this.AssertWrite("span", () => writer.Write("span".AsSpan()));
        this.AssertWrite("span2", () => writer.WriteLine("span2".AsSpan()));
        this.AssertWrite(789.ToString(provider), () => writer.Write(789));
        this.AssertWrite(789u.ToString(provider), () => writer.WriteLine(789u));
        this.AssertWrite(3.5d.ToString(provider), () => writer.Write(3.5d));
        this.AssertWrite(3.5f.ToString(provider), () => writer.WriteLine(3.5f));
        this.AssertWrite(3.5m.ToString(provider), () => writer.Write(3.5m));
        this.AssertWrite(99L.ToString(provider), () => writer.WriteLine(99L));
        this.AssertWrite(99ul.ToString(provider), () => writer.Write(99ul));
        this.AssertWrite("builder", () => writer.Write(new StringBuilder("builder")));
        this.AssertWrite("builder2", () => writer.WriteLine(new StringBuilder("builder2")));
        this.AssertWrite("object", () => writer.Write((object)"object"));
        this.AssertWrite("object2", () => writer.WriteLine((object)"object2"));
        this.AssertWrite(42.ToString(provider), () => writer.Write((object)42)); // IFormattable
        this.AssertWrite(43.ToString(provider), () => writer.WriteLine((object)43));
    }

    [Fact]
    public async Task ConsoleOutFormatOverloads()
    {
        await fixture.WaitForIdle();
        var writer = fixture.ConsoleOut;

        this.AssertWrite("a-1", () => writer.Write("{0}-{1}", "a", 1));
        this.AssertWrite("b-2", () => writer.WriteLine("{0}-{1}", "b", 2));
        this.AssertWrite("[c]", () => writer.Write("[{0}]", "c"));
        this.AssertWrite("[d]", () => writer.WriteLine("[{0}]", "d"));
        this.AssertWrite("e/f/g", () => writer.Write("{0}/{1}/{2}", "e", "f", "g"));
        this.AssertWrite("h/i/j", () => writer.WriteLine("{0}/{1}/{2}", "h", "i", "j"));
        this.AssertWrite("k+l+m+n", () => writer.Write("{0}+{1}+{2}+{3}", "k", "l", "m", "n"));
        this.AssertWrite("o+p+q+r", () => writer.WriteLine("{0}+{1}+{2}+{3}", "o", "p", "q", "r"));
    }

    [Fact]
    public async Task ConsoleOutWriteLineWhileReadLineIsInProgress()
    {
        await fixture.WaitForIdle();
        var task = fixture.ReadLine(new() { AllowEmptyLineInput = true });
        fixture.Type("kept");
        await SimpleConsoleFixture.Delay(50);

        fixture.ClearOutput();
        fixture.ConsoleOut.WriteLine("from Console.Out");
        await SimpleConsoleFixture.Delay(50);
        Assert.Contains("from Console.Out", fixture.TakeOutput());

        fixture.Key(ConsoleKey.Enter);
        Assert.Equal("kept", await fixture.Wait(task));
    }

    [Fact]
    public void StaticProperties()
    {
        Assert.True(SimpleConsole.WindowWidth >= 30);
        Assert.True(SimpleConsole.WindowHeight >= 10);
        Assert.True(SimpleConsole.CursorLeft >= 0);
        Assert.True(SimpleConsole.CursorTop >= 0);

        var (left, top) = SimpleConsole.GetCursorPosition();
        Assert.Equal(SimpleConsole.CursorLeft, left);
        Assert.Equal(SimpleConsole.CursorTop, top);
    }

    [Fact]
    public void ConsoleServiceMembers()
    {
        IConsoleService service = fixture.Console;
        service.Write("service write");
        service.WriteLine("service writeline");
        Assert.Contains("service write", fixture.Sink.ToString());

        // Without a real console these simply return default values instead of throwing.
        _ = service.KeyAvailable;
        _ = service.ReadKey(true);
    }

    [Fact]
    public async Task ConsoleServiceReadLine()
    {
        await fixture.WaitForIdle();
        IConsoleService service = fixture.Console;
        var task = service.ReadLine(TestContext.Current.CancellationToken);
        fixture.Type("service");
        fixture.Key(ConsoleKey.Enter);
        Assert.Equal("service", (await fixture.WaitResult(task)).Text);
    }

    private static int ExpectedTop(int top, int lines)
        => Math.Min(top + lines, SimpleConsole.WindowHeight - 1); // The window scrolls at the bottom.

    private void AssertWrite(string expected, Action action)
    {
        fixture.ClearOutput();
        action();
        Assert.Contains(expected, fixture.TakeOutput());
    }
}
