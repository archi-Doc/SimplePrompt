// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using System.Text;
using SimplePrompt;

namespace xUnitTest;

/// <summary>
/// Tests the ReadLine/Write behavior of <see cref="SimpleConsole"/>.<br/>
/// <see cref="SimpleConsole"/> is a process-wide singleton, so the tests share a single instance and must not run in parallel.
/// </summary>
[Collection(nameof(ReadLineTest))]
public class ReadLineTest
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(30);
    private static readonly StringWriter Sink = new();
    private static readonly SimpleConsole Prompt = CreateConsole();

    [Fact]
    public async Task Plain()
    {
        var task = ReadLine();
        Type("hello world");
        Key(ConsoleKey.Enter);
        Assert.Equal("hello world", await Wait(task));
    }

    [Fact]
    public async Task Backspace()
    {
        var task = ReadLine();
        Type("abcdef");
        Key(ConsoleKey.Backspace);
        Key(ConsoleKey.Backspace);
        Type("Z");
        Key(ConsoleKey.Enter);
        Assert.Equal("abcdZ", await Wait(task));
    }

    [Fact]
    public async Task BackspaceSurrogatePair()
    {
        var task = ReadLine();
        Type("a\U0001F600b");
        Key(ConsoleKey.Backspace); // 'b'
        Key(ConsoleKey.Backspace); // The surrogate pair must be deleted as a single character.
        Type("c");
        Key(ConsoleKey.Enter);
        Assert.Equal("ac", await Wait(task));
    }

    [Fact]
    public async Task BackspaceAtBufferBoundary()
    {
        // The prompt "> " plus 254 characters exactly fills the initial 256-character buffer.
        var task = ReadLine();
        Type(new string('y', 254));
        Key(ConsoleKey.Backspace);
        Key(ConsoleKey.Enter);
        Assert.Equal(new string('y', 253), await Wait(task));
    }

    [Fact]
    public async Task Delete()
    {
        var task = ReadLine();
        Type("abcdef");
        Key(ConsoleKey.Home);
        Key(ConsoleKey.Delete);
        Key(ConsoleKey.Delete);
        Key(ConsoleKey.End);
        Type("!");
        Key(ConsoleKey.Enter);
        Assert.Equal("cdef!", await Wait(task));
    }

    [Fact]
    public async Task ClearLine()
    {
        var task = ReadLine();
        Type("to be cleared");
        Key(ConsoleKey.U, 'u', control: true); // Ctrl+U
        Type("new");
        Key(ConsoleKey.Enter);
        Assert.Equal("new", await Wait(task));
    }

    [Fact]
    public async Task ClearLineWithWideCharacterPrompt()
    {
        var task = ReadLine(new() { Prompt = "あ> ", AllowEmptyLineInput = true });
        Type("かなカナ");
        Key(ConsoleKey.U, 'u', control: true); // Ctrl+U
        Type("ok");
        Key(ConsoleKey.Enter);
        Assert.Equal("ok", await Wait(task));
    }

    [Fact]
    public async Task WideCharacterPrompt()
    {
        var task = ReadLine(new() { Prompt = "あ> ", AllowEmptyLineInput = true });
        Type("かなカナ漢字");
        Key(ConsoleKey.Backspace);
        Key(ConsoleKey.Enter);
        Assert.Equal("かなカナ漢", await Wait(task));
    }

    [Fact]
    public async Task LongInput()
    {
        var task = ReadLine();
        Type(new string('x', 300)); // Longer than the window width and the initial buffer.
        Key(ConsoleKey.Backspace);
        Key(ConsoleKey.Enter);
        Assert.Equal(new string('x', 299), await Wait(task));
    }

    [Fact]
    public async Task CursorMove()
    {
        var task = ReadLine();
        Type("world");
        Key(ConsoleKey.Home);
        Type("hello ");
        Key(ConsoleKey.End);
        Type("!");
        Key(ConsoleKey.LeftArrow);
        Key(ConsoleKey.LeftArrow);
        Type("_");
        Key(ConsoleKey.Enter);
        Assert.Equal("hello worl_d!", await Wait(task));
    }

    [Fact]
    public async Task MultilineDelimiter()
    {
        var task = ReadLine(new() { AllowEmptyLineInput = true, MultilineDelimiter = "\"\"\"" });
        Type("\"\"\"");
        Key(ConsoleKey.Enter);
        Type("line1");
        Key(ConsoleKey.Enter);
        Type("line2\"\"\"");
        Key(ConsoleKey.Enter);
        Assert.Equal("\"\"\"\nline1\nline2\"\"\"", await Wait(task));
    }

    [Fact]
    public async Task MultilineDeleteLine()
    {
        var task = ReadLine(new() { AllowEmptyLineInput = true, MultilineDelimiter = "\"\"\"" });
        Type("\"\"\"");
        Key(ConsoleKey.Enter);
        Type("second");
        Key(ConsoleKey.Enter);
        Key(ConsoleKey.Backspace); // Delete the empty third line.
        Type("\"\"\"");
        Key(ConsoleKey.Enter);
        Assert.Equal("\"\"\"\nsecond\"\"\"", await Wait(task));
    }

    [Fact]
    public async Task LineContinuation()
    {
        var task = ReadLine(new() { AllowEmptyLineInput = true, MultilineDelimiter = null, LineContinuation = '\\' });
        Type("abc\\");
        Key(ConsoleKey.Enter);
        Type("def");
        Key(ConsoleKey.Enter);
        Assert.Equal("abcdef", await Wait(task));
    }

    [Fact]
    public async Task MaskedInput()
    {
        var task = ReadLine(new() { AllowEmptyLineInput = true, MaskingCharacter = '*' });
        Type("secret");
        Key(ConsoleKey.Backspace);
        Key(ConsoleKey.Enter);
        Assert.Equal("secre", await Wait(task));
    }

    [Fact]
    public async Task EnqueueInput()
    {
        var task = ReadLine();
        Prompt.EnqueueInput("injected text");
        Assert.Equal("injected text", await Wait(task));
    }

    [Fact]
    public async Task CancelOnEscape()
    {
        var task = ReadLine(new() { CancelOnEscape = true });
        Type("abc");
        Key(ConsoleKey.Escape, '\e');
        var result = await task.WaitAsync(Timeout, TestContext.Current.CancellationToken);
        Assert.Equal(Arc.Unit.InputResultKind.Canceled, result.Kind);

        // The canceled input must not leak into the next ReadLine().
        var next = ReadLine();
        Type("xyz");
        Key(ConsoleKey.Enter);
        Assert.Equal("xyz", await Wait(next));
    }

    [Fact]
    public async Task SameOptionsShareTask()
    {
        var options = new ReadLineOptions() { AllowEmptyLineInput = true };
        var task = Prompt.ReadLine(options, TestContext.Current.CancellationToken);
        Assert.Same(task, Prompt.ReadLine(options, TestContext.Current.CancellationToken));
        Type("once");
        Key(ConsoleKey.Enter);
        Assert.Equal("once", await Wait(task));
    }

    [Fact]
    public void WriteEmpty()
    {
        lock (Sink)
        {
            Sink.GetStringBuilder().Clear();
            Prompt.Write(string.Empty);
            Assert.DoesNotContain('\n', Sink.ToString());

            Sink.GetStringBuilder().Clear();
            Prompt.WriteLine(string.Empty);
            Assert.Contains('\n', Sink.ToString());
        }
    }

    [Fact]
    public void WriteLongerThanInternalBuffer()
    {
        var text = string.Concat(Enumerable.Repeat("0123456789", 5_000)); // 50,000 characters
        lock (Sink)
        {
            Sink.GetStringBuilder().Clear();
            Prompt.WriteLine(text);
            Assert.Equal(text.Length, Sink.ToString().Count(char.IsAsciiDigit));
        }
    }

    [Fact]
    public void WriteSurrogatePairsLongerThanInternalBuffer()
    {
        var text = string.Concat(Enumerable.Repeat("\U0001F600", 20_000)); // 40,000 characters
        lock (Sink)
        {
            Sink.GetStringBuilder().Clear();
            Prompt.WriteLine(text);
            var output = Sink.ToString();
            var count = 0;
            for (var i = 0; i < output.Length - 1; i++)
            {
                if (char.IsHighSurrogate(output[i]) && char.IsLowSurrogate(output[i + 1]))
                {// A surrogate pair must not be split across the internal buffer boundary.
                    count++;
                    i++;
                }
            }

            Assert.Equal(20_000, count);
        }
    }

    [Fact]
    public async Task DisableColor()
    {
        Prompt.EnableColor = false;
        try
        {
            Sink.GetStringBuilder().Clear();
            var task = ReadLine(new() { AllowEmptyLineInput = true, InputColor = ConsoleColor.Green });
            Type("plain");
            Key(ConsoleKey.Enter);
            Assert.Equal("plain", await Wait(task));

            var output = Sink.ToString();
            Assert.DoesNotContain("\e[32m", output); // Green
            Assert.DoesNotContain("\e[0m", output); // Reset
        }
        finally
        {
            Prompt.EnableColor = true;
        }
    }

    private static SimpleConsole CreateConsole()
    {
        Console.SetOut(Sink); // The instance captures Console.Out on creation.
        return SimpleConsole.Instance;
    }

    private static Task<Arc.Unit.InputResult> ReadLine(ReadLineOptions? options = default)
        => Prompt.ReadLine(options ?? new() { AllowEmptyLineInput = true });

    private static async Task<string?> Wait(Task<Arc.Unit.InputResult> task)
        => (await task.WaitAsync(Timeout)).Text;

    private static void Type(string text)
    {
        foreach (var c in text)
        {
            Prompt.EnqueueKey(new ConsoleKeyInfo(c, default, false, false, false));
        }
    }

    private static void Key(ConsoleKey key, char keyChar = default, bool control = false)
        => Prompt.EnqueueKey(new ConsoleKeyInfo(keyChar, key, false, false, control));
}
