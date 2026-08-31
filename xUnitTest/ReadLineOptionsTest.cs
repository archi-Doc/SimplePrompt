// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using SimplePrompt;

namespace xUnitTest;

/// <summary>
/// Tests <see cref="ReadLineOptions"/> and the hook delegates.
/// </summary>
/// <param name="fixture">The shared console fixture.</param>
[Collection(SimpleConsoleTests.Name)]
public class ReadLineOptionsTest(SimpleConsoleFixture fixture)
{
    [Fact]
    public void Defaults()
    {
        var options = new ReadLineOptions();
        Assert.Equal(ConsoleColor.Yellow, options.InputColor);
        Assert.Equal(1024 * 64, options.MaxInputLength);
        Assert.Equal("> ", options.Prompt);
        Assert.Equal("# ", options.MultilinePrompt);
        Assert.Equal("\"\"\"", options.MultilineDelimiter);
        Assert.Equal(default, options.LineContinuation);
        Assert.False(options.CancelOnEscape);
        Assert.False(options.AllowEmptyLineInput);
        Assert.Equal(default, options.MaskingCharacter);
        Assert.Null(options.KeyInputHook);
        Assert.Null(options.TextInputHook);
    }

    [Fact]
    public void SingleLinePreset()
    {
        Assert.Equal(1024, ReadLineOptions.SingleLine.MaxInputLength);
        Assert.Null(ReadLineOptions.SingleLine.MultilineDelimiter);
        Assert.Equal(default, ReadLineOptions.SingleLine.LineContinuation);
        Assert.False(ReadLineOptions.SingleLine.AllowEmptyLineInput);
    }

    [Fact]
    public void MultiLinePreset()
    {
        Assert.Equal("\"\"\"", ReadLineOptions.MultiLine.MultilineDelimiter);
    }

    [Fact]
    public void YesNoPreset()
    {
        var hook = ReadLineOptions.YesNo.TextInputHook;
        Assert.NotNull(hook);
        Assert.Equal(3, ReadLineOptions.YesNo.MaxInputLength);
        Assert.False(ReadLineOptions.YesNo.CancelOnEscape);

        Assert.Equal("y", hook("y"));
        Assert.Equal("Y", hook("Y"));
        Assert.Equal("yes", hook("yes"));
        Assert.Equal("n", hook("n"));
        Assert.Equal("NO", hook("NO"));
        Assert.Equal(" y ", hook(" y ")); // Trimmed before the comparison.
        Assert.Null(hook("maybe"));
        Assert.Null(hook(string.Empty));
    }

    [Fact]
    public void RecordSemantics()
    {
        var options = ReadLineOptions.SingleLine with { Prompt = "copy> " };
        Assert.Equal("copy> ", options.Prompt);
        Assert.Equal(ReadLineOptions.SingleLine.MaxInputLength, options.MaxInputLength);
        Assert.NotSame(ReadLineOptions.SingleLine, options);
        Assert.NotEqual(ReadLineOptions.SingleLine, options);
        Assert.Equal(options, options with { });
    }

    [Fact]
    public async Task MaxInputLengthLimitsTheInput()
    {
        var task = fixture.ReadLine(new() { AllowEmptyLineInput = true, MaxInputLength = 5 });
        fixture.Type("abcdefghij"); // Only the first five characters are accepted.
        fixture.Key(ConsoleKey.Enter);
        Assert.Equal("abcde", await fixture.Wait(task));
    }

    [Fact]
    public async Task MaxInputLengthLimitsMultipleLines()
    {
        // The newline between the input lines is counted as well: "|ab" + '\n' + "|cdefg" is exactly ten characters.
        var task = fixture.ReadLine(new() { AllowEmptyLineInput = true, MaxInputLength = 10, MultilineDelimiter = "|" });
        fixture.Type("|ab");
        fixture.Key(ConsoleKey.Enter);
        fixture.Type("|cdefghij");
        fixture.Key(ConsoleKey.Enter);
        Assert.Equal("|ab\n|cdefg", await fixture.Wait(task));
    }

    [Fact]
    public async Task MultilinePromptIsDisplayed()
    {
        fixture.ClearOutput();
        var task = fixture.ReadLine(new() { AllowEmptyLineInput = true, MultilineDelimiter = "|", MultilinePrompt = "... " });
        fixture.Type("|");
        fixture.Key(ConsoleKey.Enter);
        fixture.Type("second|");
        fixture.Key(ConsoleKey.Enter);
        Assert.Equal("|\nsecond|", await fixture.Wait(task));
        Assert.Contains("... ", fixture.TakeOutput());
    }

    [Fact]
    public async Task TextInputHookTransformsTheResult()
    {
        var task = fixture.ReadLine(new()
        {
            AllowEmptyLineInput = true,
            TextInputHook = text => text.ToUpperInvariant(),
        });

        fixture.Type("transform me");
        fixture.Key(ConsoleKey.Enter);
        Assert.Equal("TRANSFORM ME", await fixture.Wait(task));
    }

    [Fact]
    public async Task TextInputHookRejectsTheInput()
    {
        var count = 0;
        var task = fixture.ReadLine(new()
        {
            AllowEmptyLineInput = true,
            TextInputHook = text =>
            {
                count++;
                return text == "good" ? text : null; // Reject anything else.
            },
        });

        fixture.Type("bad");
        fixture.Key(ConsoleKey.Enter);
        await SimpleConsoleFixture.Delay(50);
        Assert.False(task.IsCompleted);

        fixture.Type("good");
        fixture.Key(ConsoleKey.Enter);
        Assert.Equal("good", await fixture.Wait(task));
        Assert.Equal(2, count);
    }

    [Fact]
    public async Task KeyInputHookHandlesTheKey()
    {
        var task = fixture.ReadLine(new()
        {
            AllowEmptyLineInput = true,
            KeyInputHook = (ref ConsoleKeyInfo keyInfo) =>
                keyInfo.KeyChar == 'x' ? KeyInputHookResult.Handled : KeyInputHookResult.NotHandled,
        });

        fixture.Type("axbxc"); // 'x' is swallowed by the hook.
        fixture.Key(ConsoleKey.Enter);
        Assert.Equal("abc", await fixture.Wait(task));
    }

    [Fact]
    public async Task KeyInputHookRewritesTheKey()
    {
        var task = fixture.ReadLine(new()
        {
            AllowEmptyLineInput = true,
            KeyInputHook = (ref ConsoleKeyInfo keyInfo) =>
            {
                if (keyInfo.KeyChar == 'a')
                {
                    keyInfo = new ConsoleKeyInfo('A', keyInfo.Key, false, false, false);
                }

                return KeyInputHookResult.NotHandled;
            },
        });

        fixture.Type("banana");
        fixture.Key(ConsoleKey.Enter);
        Assert.Equal("bAnAnA", await fixture.Wait(task));
    }

    [Fact]
    public async Task KeyInputHookCancelsTheInput()
    {
        var task = fixture.ReadLine(new()
        {
            AllowEmptyLineInput = true,
            KeyInputHook = (ref ConsoleKeyInfo keyInfo) =>
                keyInfo.Key == ConsoleKey.F1 ? KeyInputHookResult.Cancel : KeyInputHookResult.NotHandled,
        });

        fixture.Type("abc");
        fixture.Key(ConsoleKey.F1);
        Assert.Equal(Arc.Unit.InputResultKind.Canceled, (await fixture.WaitResult(task)).Kind);
    }

    [Fact]
    public async Task GlobalKeyInputHookHandlesTheKey()
    {
        // The hook of SimpleConsole is applied to the keys injected by EnqueueKey as well.
        fixture.Console.KeyInputHook = (ref ConsoleKeyInfo keyInfo) =>
            keyInfo.KeyChar == 'x' ? KeyInputHookResult.Handled : KeyInputHookResult.NotHandled;
        try
        {
            var task = fixture.ReadLine();
            fixture.Type("axbxc"); // 'x' is swallowed by the hook.
            fixture.Key(ConsoleKey.Enter);
            Assert.Equal("abc", await fixture.Wait(task));
        }
        finally
        {
            fixture.Console.KeyInputHook = null;
        }
    }

    [Fact]
    public async Task GlobalKeyInputHookRewritesTheKey()
    {
        fixture.Console.KeyInputHook = (ref ConsoleKeyInfo keyInfo) =>
        {
            if (keyInfo.KeyChar == 'a')
            {
                keyInfo = new ConsoleKeyInfo('A', keyInfo.Key, false, false, false);
            }

            return KeyInputHookResult.NotHandled;
        };

        try
        {
            var task = fixture.ReadLine();
            fixture.Type("banana");
            fixture.Key(ConsoleKey.Enter);
            Assert.Equal("bAnAnA", await fixture.Wait(task));
        }
        finally
        {
            fixture.Console.KeyInputHook = null;
        }
    }

    [Fact]
    public async Task GlobalKeyInputHookIsAppliedBeforeTheOptionsHook()
    {
        fixture.Console.KeyInputHook = (ref ConsoleKeyInfo keyInfo) =>
        {
            if (keyInfo.KeyChar == 'a')
            {
                keyInfo = new ConsoleKeyInfo('b', keyInfo.Key, false, false, false);
            }

            return KeyInputHookResult.NotHandled;
        };

        try
        {
            var task = fixture.ReadLine(new()
            {
                AllowEmptyLineInput = true,
                KeyInputHook = (ref ConsoleKeyInfo keyInfo) =>
                    keyInfo.KeyChar == 'b' ? KeyInputHookResult.Handled : KeyInputHookResult.NotHandled,
            });

            fixture.Type("abc"); // 'a' is rewritten to 'b' and then swallowed by the options hook.
            fixture.Key(ConsoleKey.Enter);
            Assert.Equal("c", await fixture.Wait(task));
        }
        finally
        {
            fixture.Console.KeyInputHook = null;
        }
    }

    [Fact]
    public async Task InputColorIsApplied()
    {
        fixture.ClearOutput();
        var task = fixture.ReadLine(new() { AllowEmptyLineInput = true, InputColor = ConsoleColor.Blue });
        fixture.Type("blue");
        fixture.Key(ConsoleKey.Enter);
        Assert.Equal("blue", await fixture.Wait(task));
        Assert.Contains("\e[34m", fixture.TakeOutput()); // Blue
    }

    [Fact]
    public async Task SingleLinePresetIgnoresTheDelimiter()
    {
        var options = ReadLineOptions.SingleLine with { AllowEmptyLineInput = true };
        var task = fixture.ReadLine(options);
        fixture.Type("\"\"\"");
        fixture.Key(ConsoleKey.Enter); // Multiline is disabled, so the input completes.
        Assert.Equal("\"\"\"", await fixture.Wait(task));
    }
}
