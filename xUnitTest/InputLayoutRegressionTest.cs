// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Arc.Unit;
using SimplePrompt;
using SimplePrompt.Internal;

namespace xUnitTest;

/// <summary>
/// Tests input buffering, caret reflow, and multiline prompt layout.
/// </summary>
/// <param name="fixture">The shared console fixture.</param>
[Collection(SimpleConsoleTests.Name)]
public class InputLayoutRegressionTest(SimpleConsoleFixture fixture)
{
    [Fact]
    public void PendingCharactersKeepTheReadNonempty()
    {
        var instance = ReadLineInstance.Rent(fixture.Console, new() { Prompt = string.Empty }, TestContext.Current.CancellationToken);
        try
        {
            instance.Prepare();
            instance.CharBuffer[0] = 'x';
            instance.CharPosition = 1;

            Assert.False(instance.IsEmptyInput());
            Assert.True(instance.IsEmptyInput(includeBufferedCharacters: false));
        }
        finally
        {
            ReadLineInstance.Return(instance);
        }
    }

    [Theory]
    [InlineData(0, "x")]
    [InlineData(1, "😀")]
    public void DiscardedBufferedCharactersDoNotPermitEmptySubmission(int limit, string text)
    {
        var instance = ReadLineInstance.Rent(fixture.Console, new() { Prompt = string.Empty, MaxInputLength = limit }, TestContext.Current.CancellationToken);
        try
        {
            instance.Prepare();
            text.AsSpan().CopyTo(instance.CharBuffer);
            instance.CharPosition = text.Length;

            Assert.Null(instance.ProcessInput(SimplePromptHelper.EnterKeyInfo, instance.CharBuffer.AsSpan(0, instance.CharPosition)));
            Assert.Equal(0, instance.LineList[0].InputLength);
        }
        finally
        {
            ReadLineInstance.Return(instance);
        }
    }

    [Fact]
    public async Task PrintableHookKeepsNestedDisplayAndQueuedTextInOrder()
    {
        await fixture.WaitForIdle();
        if (SimpleConsole.CursorLeft > 0)
        {
            fixture.Console.WriteLine();
        }

        fixture.ClearOutput();
        var terminal = fixture.CreateTerminal();
        var nested = new TaskCompletionSource<Task<InputResult>>(TaskCreationOptions.RunContinuationsAsynchronously);
        try
        {
            var outer = fixture.ReadLineAsync(ReadLineOptions.SingleLine with
            {
                KeyInputHook = (ref ConsoleKeyInfo key) =>
                {
                    if (key.KeyChar != '~')
                    {
                        return KeyInputHookResult.NotHandled;
                    }

                    nested.SetResult(fixture.ReadLineAsync(ReadLineOptions.SingleLine with
                    {
                        Prompt = "inner> ",
                        KeyInputHook = fixture.SettleHook,
                        SubmitHook = text =>
                        {
                            fixture.Console.EnqueueLine("queued");
                            return text;
                        },
                    }));
                    return KeyInputHookResult.Handled;
                },
            });

            fixture.Type("outer~inner");
            var inner = await nested.Task.WaitAsync(SimpleConsoleFixture.Timeout, TestContext.Current.CancellationToken);
            await fixture.Settle();
            fixture.AssertCursor(terminal);
            Assert.Equal("inner> inner", terminal.GetRow(terminal.Top));

            fixture.Key(ConsoleKey.Enter);
            Assert.Equal("inner", await fixture.Wait(inner));
            fixture.Key(ConsoleKey.Enter);
            Assert.Equal("outer", await fixture.Wait(outer));

            var next = fixture.ReadLineAsync(ReadLineOptions.SingleLine);
            Assert.Equal("queued", await fixture.Wait(next));
        }
        finally
        {
            fixture.CancelPendingReadLine();
            await fixture.WaitForIdle();
        }
    }

    [Theory]
    [InlineData("z")]
    [InlineData("zzz")]
    public void DeleteWideCharacterUpdatesCaretWhenHeightIsUnchanged(string suffix)
    {
        var instance = ReadLineInstance.Rent(fixture.Console, new() { Prompt = string.Empty }, TestContext.Current.CancellationToken);
        try
        {
            instance.Prepare();
            var prefix = new string('a', SimpleConsole.WindowWidth - 1);
            instance.ProcessInput(default, prefix + "日" + suffix);
            instance.CurrentLocation.MoveFirst();
            for (var i = 0; i < prefix.Length; i++)
            {
                instance.CurrentLocation.MoveRight();
            }

            var line = instance.LineList[0];
            var height = line.Height;
            Assert.Equal(1, instance.CurrentLocation.RowIndex);
            instance.ProcessInput(new(default, ConsoleKey.Delete, false, false, false), default);

            Assert.Equal(height, line.Height);
            Assert.Equal(prefix + suffix, line.InputSpan.ToString());
            Assert.Equal(0, instance.CurrentLocation.RowIndex);
            Assert.Equal(prefix.Length, instance.CurrentLocation.CursorPosition);

            instance.CurrentLocation.MoveRight();
            Assert.Equal(1, instance.CurrentLocation.RowIndex);
            Assert.Equal(0, instance.CurrentLocation.CursorPosition);
        }
        finally
        {
            ReadLineInstance.Return(instance);
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ArrangeCountsEmptyRowsBeforeInput(bool blankPromptLine)
    {
        var prompt = new string('p', SimpleConsole.WindowWidth) + (blankPromptLine ? "\n\n> " : "\n> ");
        var instance = ReadLineInstance.Rent(fixture.Console, new() { Prompt = prompt }, TestContext.Current.CancellationToken);
        try
        {
            instance.Prepare();
            var top = 0;
            foreach (var line in instance.LineList)
            {
                line.Top = top;
                top += line.Height;
            }

            var input = instance.LineList[instance.FirstInputIndex];
            new SimpleArrange(fixture.Console).Arrange(instance, (input.InitialCursorPosition, 5), false);

            Assert.Equal(5, input.Top);
            Assert.Equal(5, SimpleConsole.CursorTop);
            Assert.Equal(5 - (blankPromptLine ? 3 : 2), instance.LineList[0].Top);
        }
        finally
        {
            ReadLineInstance.Return(instance);
        }
    }

    [Fact]
    public void ClearingLongPromptDoesNotAllocateRows()
    {
        var prompt = new string('p', (SimpleConsole.WindowWidth * 40) + 3);
        var instance = ReadLineInstance.Rent(fixture.Console, new() { Prompt = prompt }, TestContext.Current.CancellationToken);
        try
        {
            instance.Prepare();
            var line = instance.LineList[0];
            for (var i = 0; i < 100; i++)
            {
                line.Clear();
            }

            var before = GC.GetAllocatedBytesForCurrentThread();
            for (var i = 0; i < 100; i++)
            {
                line.Clear();
            }

            var allocated = GC.GetAllocatedBytesForCurrentThread() - before;
            Assert.Equal(0, allocated);
            Assert.Equal(41, line.Height);
            Assert.Equal(3, line.InitialCursorPosition);
        }
        finally
        {
            ReadLineInstance.Return(instance);
        }
    }
}
