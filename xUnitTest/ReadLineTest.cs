// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Arc.Unit;
using SimplePrompt;

namespace xUnitTest;

/// <summary>
/// Tests the line editing behavior of <see cref="SimpleConsole.ReadLine(ReadLineOptions?, CancellationToken)"/>.
/// </summary>
/// <param name="fixture">The shared console fixture.</param>
[Collection(SimpleConsoleTests.Name)]
public class ReadLineTest(SimpleConsoleFixture fixture)
{
    [Fact]
    public async Task Plain()
    {
        var task = fixture.ReadLine();
        fixture.Type("hello world");
        fixture.Key(ConsoleKey.Enter);
        Assert.Equal("hello world", await fixture.Wait(task));
    }

    [Fact]
    public async Task EmptyInput()
    {
        var task = fixture.ReadLine(new() { AllowEmptyLineInput = true });
        fixture.Key(ConsoleKey.Enter);
        Assert.Equal(string.Empty, await fixture.Wait(task));
    }

    [Fact]
    public async Task EmptyInputNotAllowed()
    {
        var task = fixture.ReadLine(new() { AllowEmptyLineInput = false });
        fixture.Key(ConsoleKey.Enter); // Ignored.
        fixture.Key(ConsoleKey.Enter); // Ignored.
        fixture.Type("text");
        fixture.Key(ConsoleKey.Enter);
        Assert.Equal("text", await fixture.Wait(task));
    }

    [Fact]
    public async Task Backspace()
    {
        var task = fixture.ReadLine();
        fixture.Type("abcdef");
        fixture.Key(ConsoleKey.Backspace);
        fixture.Key(ConsoleKey.Backspace);
        fixture.Type("Z");
        fixture.Key(ConsoleKey.Enter);
        Assert.Equal("abcdZ", await fixture.Wait(task));
    }

    [Fact]
    public async Task BackspaceOnEmptyInput()
    {
        var task = fixture.ReadLine();
        fixture.Key(ConsoleKey.Backspace); // Nothing to delete.
        fixture.Key(ConsoleKey.Backspace);
        fixture.Type("a");
        fixture.Key(ConsoleKey.Enter);
        Assert.Equal("a", await fixture.Wait(task));
    }

    [Fact]
    public async Task BackspaceSurrogatePair()
    {
        var task = fixture.ReadLine();
        fixture.Type("a\U0001F600b");
        fixture.Key(ConsoleKey.Backspace); // 'b'
        fixture.Key(ConsoleKey.Backspace); // The surrogate pair must be deleted as a single character.
        fixture.Type("c");
        fixture.Key(ConsoleKey.Enter);
        Assert.Equal("ac", await fixture.Wait(task));
    }

    [Fact]
    public async Task BackspaceAtBufferBoundary()
    {
        // The prompt "> " plus 254 characters exactly fills the initial 256-character buffer.
        var task = fixture.ReadLine();
        fixture.Type(new string('y', 254));
        fixture.Key(ConsoleKey.Backspace);
        fixture.Key(ConsoleKey.Enter);
        Assert.Equal(new string('y', 253), await fixture.Wait(task));
    }

    [Fact]
    public async Task Delete()
    {
        var task = fixture.ReadLine();
        fixture.Type("abcdef");
        fixture.Key(ConsoleKey.Home);
        fixture.Key(ConsoleKey.Delete);
        fixture.Key(ConsoleKey.Delete);
        fixture.Key(ConsoleKey.End);
        fixture.Type("!");
        fixture.Key(ConsoleKey.Enter);
        Assert.Equal("cdef!", await fixture.Wait(task));
    }

    [Fact]
    public async Task DeleteSurrogatePair()
    {
        var task = fixture.ReadLine();
        fixture.Type("\U0001F600ab");
        fixture.Key(ConsoleKey.Home);
        fixture.Key(ConsoleKey.Delete); // The surrogate pair must be deleted as a single character.
        fixture.Key(ConsoleKey.Enter);
        Assert.Equal("ab", await fixture.Wait(task));
    }

    [Fact]
    public async Task ClearLine()
    {
        var task = fixture.ReadLine();
        fixture.Type("to be cleared");
        fixture.Key(ConsoleKey.U, 'u', control: true); // Ctrl+U
        fixture.Type("new");
        fixture.Key(ConsoleKey.Enter);
        Assert.Equal("new", await fixture.Wait(task));
    }

    [Fact]
    public async Task ClearLineWithWideCharacterPrompt()
    {
        var task = fixture.ReadLine(new() { Prompt = "あ> ", AllowEmptyLineInput = true });
        fixture.Type("かなカナ");
        fixture.Key(ConsoleKey.U, 'u', control: true); // Ctrl+U
        fixture.Type("ok");
        fixture.Key(ConsoleKey.Enter);
        Assert.Equal("ok", await fixture.Wait(task));
    }

    [Fact]
    public async Task ClearLongLine()
    {
        var task = fixture.ReadLine();
        fixture.Type(new string('w', SimpleConsole.WindowWidth * 2)); // Wraps to several rows.
        fixture.Key(ConsoleKey.U, 'u', control: true); // Ctrl+U
        fixture.Type("short");
        fixture.Key(ConsoleKey.Enter);
        Assert.Equal("short", await fixture.Wait(task));
    }

    [Fact]
    public async Task WideCharacterPrompt()
    {
        var task = fixture.ReadLine(new() { Prompt = "あ> ", AllowEmptyLineInput = true });
        fixture.Type("かなカナ漢字");
        fixture.Key(ConsoleKey.Backspace);
        fixture.Key(ConsoleKey.Enter);
        Assert.Equal("かなカナ漢", await fixture.Wait(task));
    }

    [Fact]
    public async Task MultiLinePrompt()
    {
        var task = fixture.ReadLine(new() { Prompt = "line1\nline2\n> ", AllowEmptyLineInput = true });
        fixture.Type("input");
        fixture.Key(ConsoleKey.Enter);
        Assert.Equal("input", await fixture.Wait(task));
    }

    [Fact]
    public async Task EmptyPrompt()
    {
        var task = fixture.ReadLine(new() { Prompt = string.Empty, AllowEmptyLineInput = true });
        fixture.Type("no prompt");
        fixture.Key(ConsoleKey.Enter);
        Assert.Equal("no prompt", await fixture.Wait(task));
    }

    [Fact]
    public async Task LongInput()
    {
        var task = fixture.ReadLine();
        fixture.Type(new string('x', 300)); // Longer than the window width and the initial buffer.
        fixture.Key(ConsoleKey.Backspace);
        fixture.Key(ConsoleKey.Enter);
        Assert.Equal(new string('x', 299), await fixture.Wait(task));
    }

    [Fact]
    public async Task VeryLongInput()
    {
        var text = string.Concat(Enumerable.Repeat("0123456789", 500)); // 5,000 characters
        var task = fixture.ReadLine(new() { AllowEmptyLineInput = true, MaxInputLength = 8192 });
        fixture.Type(text);
        fixture.Key(ConsoleKey.Enter);
        Assert.Equal(text, await fixture.Wait(task));
    }

    [Fact]
    public async Task CursorMove()
    {
        var task = fixture.ReadLine();
        fixture.Type("world");
        fixture.Key(ConsoleKey.Home);
        fixture.Type("hello ");
        fixture.Key(ConsoleKey.End);
        fixture.Type("!");
        fixture.Key(ConsoleKey.LeftArrow);
        fixture.Key(ConsoleKey.LeftArrow);
        fixture.Type("_");
        fixture.Key(ConsoleKey.Enter);
        Assert.Equal("hello worl_d!", await fixture.Wait(task));
    }

    [Fact]
    public async Task CursorMoveBeyondBoundary()
    {
        var task = fixture.ReadLine();
        fixture.Type("ab");
        fixture.Key(ConsoleKey.LeftArrow);
        fixture.Key(ConsoleKey.LeftArrow);
        fixture.Key(ConsoleKey.LeftArrow); // Already at the beginning.
        fixture.Key(ConsoleKey.LeftArrow);
        fixture.Type("_");
        fixture.Key(ConsoleKey.RightArrow);
        fixture.Key(ConsoleKey.RightArrow);
        fixture.Key(ConsoleKey.RightArrow); // Already at the end.
        fixture.Type("!");
        fixture.Key(ConsoleKey.Enter);
        Assert.Equal("_ab!", await fixture.Wait(task));
    }

    [Fact]
    public async Task CursorMoveOverSurrogatePair()
    {
        var task = fixture.ReadLine();
        fixture.Type("\U0001F600");
        fixture.Key(ConsoleKey.LeftArrow); // Moves over the whole pair.
        fixture.Type("<");
        fixture.Key(ConsoleKey.RightArrow); // Moves over the whole pair.
        fixture.Type(">");
        fixture.Key(ConsoleKey.Enter);
        Assert.Equal("<\U0001F600>", await fixture.Wait(task));
    }

    [Fact]
    public async Task CursorMoveOverWrappedRows()
    {
        var width = SimpleConsole.WindowWidth;
        var task = fixture.ReadLine();
        fixture.Type(new string('a', width + 10)); // Wraps to the next row.
        fixture.Key(ConsoleKey.Home);
        fixture.Type("[");
        fixture.Key(ConsoleKey.End);
        fixture.Type("]");
        fixture.Key(ConsoleKey.Enter);
        Assert.Equal("[" + new string('a', width + 10) + "]", await fixture.Wait(task));
    }

    [Fact]
    public async Task IgnoredKeys()
    {
        var task = fixture.ReadLine();
        fixture.Type("ab");
        fixture.Key(ConsoleKey.Insert);
        fixture.Key(ConsoleKey.F5);
        fixture.Key(ConsoleKey.PageUp);
        fixture.Key(ConsoleKey.UpArrow); // No history yet.
        fixture.Key(ConsoleKey.DownArrow);
        fixture.Key(ConsoleKey.Tab);
        fixture.Type("c");
        fixture.Key(ConsoleKey.Enter);
        Assert.Equal("abc", await fixture.Wait(task));
    }

    [Fact]
    public async Task CarriageReturnIsIgnored()
    {
        var task = fixture.ReadLine();
        fixture.Type("ab");
        fixture.Console.EnqueueKey(new ConsoleKeyInfo('\r', default, false, false, false)); // CrLf -> Lf
        fixture.Type("c");
        fixture.Key(ConsoleKey.Enter);
        Assert.Equal("abc", await fixture.Wait(task));
    }

    [Fact]
    public async Task LineFeedActsAsEnter()
    {
        var task = fixture.ReadLine();
        fixture.Type("ab");
        fixture.Console.EnqueueKey(new ConsoleKeyInfo('\n', default, false, false, false));
        Assert.Equal("ab", await fixture.Wait(task));
    }

    [Fact]
    public async Task MultilineDelimiter()
    {
        var task = fixture.ReadLine(new() { AllowEmptyLineInput = true, MultilineDelimiter = "\"\"\"" });
        fixture.Type("\"\"\"");
        fixture.Key(ConsoleKey.Enter);
        fixture.Type("line1");
        fixture.Key(ConsoleKey.Enter);
        fixture.Type("line2\"\"\"");
        fixture.Key(ConsoleKey.Enter);
        Assert.Equal("\"\"\"\nline1\nline2\"\"\"", await fixture.Wait(task));
    }

    [Fact]
    public async Task MultilineDisabled()
    {
        var task = fixture.ReadLine(new() { AllowEmptyLineInput = true, MultilineDelimiter = null });
        fixture.Type("\"\"\"");
        fixture.Key(ConsoleKey.Enter); // Completes the input because multiline is disabled.
        Assert.Equal("\"\"\"", await fixture.Wait(task));
    }

    [Fact]
    public async Task MultilineDeleteLine()
    {
        var task = fixture.ReadLine(new() { AllowEmptyLineInput = true, MultilineDelimiter = "\"\"\"" });
        fixture.Type("\"\"\"");
        fixture.Key(ConsoleKey.Enter);
        fixture.Type("second");
        fixture.Key(ConsoleKey.Enter);
        fixture.Key(ConsoleKey.Backspace); // Delete the empty third line.
        fixture.Type("\"\"\"");
        fixture.Key(ConsoleKey.Enter);
        Assert.Equal("\"\"\"\nsecond\"\"\"", await fixture.Wait(task));
    }

    [Fact]
    public async Task MultilineMoveBetweenLines()
    {
        var task = fixture.ReadLine(new() { AllowEmptyLineInput = true, MultilineDelimiter = "\"\"\"" });
        fixture.Type("\"\"\"");
        fixture.Key(ConsoleKey.Enter);
        fixture.Type("first");
        fixture.Key(ConsoleKey.Enter);
        fixture.Type("second");
        fixture.Key(ConsoleKey.UpArrow); // Move to the previous line.
        fixture.Key(ConsoleKey.End);
        fixture.Type("!");
        fixture.Key(ConsoleKey.DownArrow); // Move back.
        fixture.Key(ConsoleKey.End);
        fixture.Type("\"\"\"");
        fixture.Key(ConsoleKey.Enter);
        Assert.Equal("\"\"\"\nfirst!\nsecond\"\"\"", await fixture.Wait(task));
    }

    [Fact]
    public async Task LineContinuation()
    {
        var task = fixture.ReadLine(new() { AllowEmptyLineInput = true, MultilineDelimiter = null, LineContinuation = '\\' });
        fixture.Type("abc\\");
        fixture.Key(ConsoleKey.Enter);
        fixture.Type("def");
        fixture.Key(ConsoleKey.Enter);
        Assert.Equal("abcdef", await fixture.Wait(task));
    }

    [Fact]
    public async Task LineContinuationThreeLines()
    {
        var task = fixture.ReadLine(new() { AllowEmptyLineInput = true, MultilineDelimiter = null, LineContinuation = '\\' });
        fixture.Type("a\\");
        fixture.Key(ConsoleKey.Enter);
        fixture.Type("b\\");
        fixture.Key(ConsoleKey.Enter);
        fixture.Type("c");
        fixture.Key(ConsoleKey.Enter);
        Assert.Equal("abc", await fixture.Wait(task));
    }

    [Fact]
    public async Task MaskedInput()
    {
        fixture.ClearOutput();
        var task = fixture.ReadLine(new() { AllowEmptyLineInput = true, MaskingCharacter = '*' });
        fixture.Type("secret");
        fixture.Key(ConsoleKey.Backspace);
        fixture.Key(ConsoleKey.Enter);
        Assert.Equal("secre", await fixture.Wait(task));

        var output = fixture.TakeOutput();
        Assert.DoesNotContain("secre", output); // The input must never be echoed.
        Assert.Contains('*', output);
    }

    [Fact]
    public async Task EnqueueInput()
    {
        var task = fixture.ReadLine();
        fixture.Console.EnqueueInput("injected text");
        Assert.Equal("injected text", await fixture.Wait(task));
    }

    [Fact]
    public async Task EnqueueInputNull()
    {
        var task = fixture.ReadLine();
        fixture.Console.EnqueueInput(null); // Equivalent to pressing Enter.
        Assert.Equal(string.Empty, await fixture.Wait(task));
    }

    [Fact]
    public async Task EnqueueInputLongerThanCharBuffer()
    {
        var text = string.Concat(Enumerable.Repeat("abcdefghij", 300)); // 3,000 characters
        var task = fixture.ReadLine();
        fixture.Console.EnqueueInput(text);
        Assert.Equal(text, await fixture.Wait(task));
    }

    [Fact]
    public async Task EnqueueInputIsIgnoredWhenInputIsNotEmpty()
    {
        var task = fixture.ReadLine();
        fixture.Type("typed");
        await SimpleConsoleFixture.Delay(50); // Let the worker consume the typed characters.
        fixture.Console.EnqueueInput("injected");
        fixture.Key(ConsoleKey.Enter);
        Assert.Equal("typed", await fixture.Wait(task));

        // The queued text is consumed by the next ReadLine.
        var next = fixture.ReadLine();
        Assert.Equal("injected", await fixture.Wait(next));
    }

    [Fact]
    public async Task CancelOnEscape()
    {
        var task = fixture.ReadLine(new() { CancelOnEscape = true });
        fixture.Type("abc");
        fixture.Key(ConsoleKey.Escape, '\e');
        var result = await fixture.WaitResult(task);
        Assert.Equal(InputResultKind.Canceled, result.Kind);
        Assert.True(result.Kind.IsCanceled);

        // The canceled input must not leak into the next ReadLine().
        var next = fixture.ReadLine();
        fixture.Type("xyz");
        fixture.Key(ConsoleKey.Enter);
        Assert.Equal("xyz", await fixture.Wait(next));
    }

    [Fact]
    public async Task EscapeIsIgnoredWhenCancelOnEscapeIsFalse()
    {
        var task = fixture.ReadLine(new() { AllowEmptyLineInput = true, CancelOnEscape = false });
        fixture.Type("abc");
        fixture.Key(ConsoleKey.Escape, '\e');
        fixture.Key(ConsoleKey.Enter);
        Assert.Equal("abc", await fixture.Wait(task));
    }

    [Fact]
    public async Task SameOptionsShareTask()
    {
        var options = new ReadLineOptions() { AllowEmptyLineInput = true };
        var task = fixture.Console.ReadLine(options, TestContext.Current.CancellationToken);
        Assert.Same(task, fixture.Console.ReadLine(options, TestContext.Current.CancellationToken));
        fixture.Type("once");
        fixture.Key(ConsoleKey.Enter);
        Assert.Equal("once", await fixture.Wait(task));
    }

    [Fact]
    public async Task NestedReadLine()
    {
        var outer = fixture.ReadLine(new() { Prompt = "outer> ", AllowEmptyLineInput = true });
        await SimpleConsoleFixture.Delay(50);
        var inner = fixture.ReadLine(new() { Prompt = "inner> ", AllowEmptyLineInput = true });

        // The nested (latest) instance receives the input.
        fixture.Type("inner text");
        fixture.Key(ConsoleKey.Enter);
        Assert.Equal("inner text", await fixture.Wait(inner));
        Assert.False(outer.IsCompleted);

        // Then the original instance is restored.
        fixture.Type("outer text");
        fixture.Key(ConsoleKey.Enter);
        Assert.Equal("outer text", await fixture.Wait(outer));
    }

    [Fact]
    public async Task CanceledByToken()
    {
        using var cts = new CancellationTokenSource();
        var task = fixture.Console.ReadLine(new() { AllowEmptyLineInput = true }, cts.Token);
        await SimpleConsoleFixture.Delay(50);
        await cts.CancelAsync();
        Assert.Equal(InputResultKind.Canceled, (await fixture.WaitResult(task)).Kind);
    }

    [Fact]
    public async Task CanceledTokenBeforeReadLine()
    {
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();
        var task = fixture.Console.ReadLine(new() { AllowEmptyLineInput = true }, cts.Token);
        Assert.Equal(InputResultKind.Canceled, (await fixture.WaitResult(task)).Kind);
    }

    [Fact]
    public async Task PendingInstanceCanceledByToken()
    {
        using var cts = new CancellationTokenSource();
        var pending = fixture.Console.ReadLine(new() { Prompt = "pending> ", AllowEmptyLineInput = true }, cts.Token);
        await SimpleConsoleFixture.Delay(50);
        var active = fixture.ReadLine(new() { Prompt = "active> ", AllowEmptyLineInput = true });
        await SimpleConsoleFixture.Delay(50);

        // Cancel the instance which is not currently active.
        await cts.CancelAsync();
        Assert.Equal(InputResultKind.Canceled, (await fixture.WaitResult(pending)).Kind);

        fixture.Type("active text");
        fixture.Key(ConsoleKey.Enter);
        Assert.Equal("active text", await fixture.Wait(active));
    }

    [Fact]
    public async Task IsReadLineInProgress()
    {
        await fixture.WaitForIdle();
        Assert.False(fixture.Console.IsReadLineInProgress);

        var task = fixture.ReadLine();
        Assert.True(fixture.Console.IsReadLineInProgress);

        fixture.Type("done");
        fixture.Key(ConsoleKey.Enter);
        await fixture.Wait(task);
        await fixture.WaitForIdle();
        Assert.False(fixture.Console.IsReadLineInProgress);
    }

    [Fact]
    public async Task TryGetCurrentReadLineOptions()
    {
        await fixture.WaitForIdle();
        Assert.False(fixture.Console.TryGetCurrentReadLineOptions(out _));

        var task = fixture.ReadLine(new() { Prompt = "current> ", AllowEmptyLineInput = true });
        await SimpleConsoleFixture.Delay(50);
        Assert.True(fixture.Console.TryGetCurrentReadLineOptions(out var options));
        Assert.Equal("current> ", options.Prompt);

        fixture.Key(ConsoleKey.Enter);
        await fixture.Wait(task);
    }

    [Fact]
    public async Task DefaultOptionsAreUsed()
    {
        var previous = fixture.Console.DefaultOptions;
        fixture.Console.DefaultOptions = new() { Prompt = "default> ", AllowEmptyLineInput = true };
        try
        {
            var task = fixture.Console.ReadLine(default, TestContext.Current.CancellationToken);
            await SimpleConsoleFixture.Delay(50);
            Assert.True(fixture.Console.TryGetCurrentReadLineOptions(out var options));
            Assert.Equal("default> ", options.Prompt);

            fixture.Type("value");
            fixture.Key(ConsoleKey.Enter);
            Assert.Equal("value", await fixture.Wait(task));
        }
        finally
        {
            fixture.Console.DefaultOptions = previous;
        }
    }

    [Fact]
    public async Task ConsoleInReadLine()
    {
        // Console.In is replaced with SimpleTextReader, which is synchronous.
        var task = Task.Run(() => fixture.ConsoleIn.ReadLine());
        await SimpleConsoleFixture.Delay(50);
        fixture.Type("via Console.In");
        fixture.Key(ConsoleKey.Enter);
        Assert.Equal("via Console.In", await SimpleConsoleFixture.WaitAny(task));
    }
}
