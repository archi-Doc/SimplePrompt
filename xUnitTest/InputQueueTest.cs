// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Arc.Unit;
using SimplePrompt;

namespace xUnitTest;

[Collection(SimpleConsoleTests.Name)]
public class InputQueueTest(SimpleConsoleFixture fixture)
{
    [Fact]
    public async Task GlobalHookCanStartReadWithFullIdleBuffer()
    {
        await fixture.WaitForIdle();
        var originalHook = fixture.Console.KeyInputHook;
        var originalBuffering = fixture.Console.BufferKeyInputWhileIdle;
        var started = new TaskCompletionSource<Task<InputResult>>(TaskCreationOptions.RunContinuationsAsynchronously);
        fixture.Console.BufferKeyInputWhileIdle = true;
        fixture.Console.KeyInputHook = (ref ConsoleKeyInfo key) =>
        {
            if (key.Key == ConsoleKey.F1)
            {
                started.SetResult(fixture.ReadLineAsync(ReadLineOptions.SingleLine with { MaxInputLength = 40000 }));
                return KeyInputHookResult.Handled;
            }

            return KeyInputHookResult.NotHandled;
        };

        try
        {
            var buffered = new string('x', 32768);
            fixture.Type(buffered);
            fixture.Key(ConsoleKey.F1);
            fixture.Type("tail");
            fixture.Key(ConsoleKey.Enter);
            var task = await started.Task.WaitAsync(SimpleConsoleFixture.Timeout, TestContext.Current.CancellationToken);
            Assert.Equal(buffered + "tail", (await task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken)).Text);
        }
        finally
        {
            fixture.Console.KeyInputHook = originalHook;
            fixture.Console.BufferKeyInputWhileIdle = originalBuffering;
            fixture.CancelPendingReadLine();
            await fixture.WaitForIdle();
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task KeyHookCanStartNestedRead(bool handlesKey)
    {
        await fixture.WaitForIdle();
        var nested = new TaskCompletionSource<Task<InputResult>>(TaskCreationOptions.RunContinuationsAsynchronously);
        try
        {
            var outer = fixture.ReadLineAsync(ReadLineOptions.SingleLine with
            {
                KeyInputHook = (ref ConsoleKeyInfo key) =>
                {
                    if (key.Key == ConsoleKey.F1)
                    {
                        nested.SetResult(fixture.ReadLineAsync(ReadLineOptions.SingleLine with { Prompt = "inner> " }));
                        return handlesKey ? KeyInputHookResult.Handled : KeyInputHookResult.NotHandled;
                    }

                    return KeyInputHookResult.NotHandled;
                },
            });
            fixture.Type("outer");
            fixture.Key(ConsoleKey.F1);
            fixture.Type("inner");
            fixture.Key(ConsoleKey.Enter);

            var inner = await nested.Task.WaitAsync(SimpleConsoleFixture.Timeout, TestContext.Current.CancellationToken);
            Assert.Equal("inner", (await inner.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken)).Text);
            Assert.False(outer.IsCompleted);
            fixture.Key(ConsoleKey.Enter);
            Assert.Equal("outer", await fixture.Wait(outer));
        }
        finally
        {
            fixture.CancelPendingReadLine();
            await fixture.WaitForIdle();
        }
    }

    [Fact]
    public async Task RejectedSubmitCanStartNestedRead()
    {
        await fixture.WaitForIdle();
        var nested = new TaskCompletionSource<Task<InputResult>>(TaskCreationOptions.RunContinuationsAsynchronously);
        try
        {
            var outer = fixture.ReadLineAsync(ReadLineOptions.SingleLine with
            {
                SubmitHook = text =>
                {
                    if (text == "retry")
                    {
                        nested.SetResult(fixture.ReadLineAsync(ReadLineOptions.SingleLine with { Prompt = "inner> " }));
                        return null;
                    }

                    return text;
                },
            });
            fixture.Type("retry");
            fixture.Key(ConsoleKey.Enter);
            fixture.Type("inner");
            fixture.Key(ConsoleKey.Enter);

            var inner = await nested.Task.WaitAsync(SimpleConsoleFixture.Timeout, TestContext.Current.CancellationToken);
            Assert.Equal("inner", (await inner.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken)).Text);
            Assert.False(outer.IsCompleted);
            fixture.Type("accepted");
            fixture.Key(ConsoleKey.Enter);
            Assert.Equal("accepted", await fixture.Wait(outer));
        }
        finally
        {
            fixture.CancelPendingReadLine();
            await fixture.WaitForIdle();
        }
    }

    [Fact]
    public async Task LargeKeyBatchPreservesTextAndSubmission()
    {
        await fixture.WaitForIdle();
        using var releaseWorker = new ManualResetEventSlim();
        var workerEntered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var originalHook = fixture.Console.KeyInputHook;
        fixture.Console.KeyInputHook = (ref ConsoleKeyInfo key) =>
        {
            if (key.Key == ConsoleKey.F23)
            {
                workerEntered.SetResult();
                if (!releaseWorker.Wait(SimpleConsoleFixture.Timeout))
                {
                    throw new TimeoutException("The input batch was not released.");
                }

                return KeyInputHookResult.Handled;
            }

            return KeyInputHookResult.NotHandled;
        };

        try
        {
            var task = fixture.ReadLineAsync(ReadLineOptions.SingleLine with { MaxInputLength = 50000 });
            fixture.Key(ConsoleKey.F23);
            await workerEntered.Task.WaitAsync(SimpleConsoleFixture.Timeout, TestContext.Current.CancellationToken);
            var expected = new string('x', 40000);
            fixture.Type(expected);
            fixture.Key(ConsoleKey.Enter);
            releaseWorker.Set();

            Assert.Equal(expected, (await task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken)).Text);
        }
        finally
        {
            releaseWorker.Set();
            fixture.Console.KeyInputHook = originalHook;
            fixture.CancelPendingReadLine();
            await fixture.WaitForIdle();
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task IdleBufferingFollowsSetting(bool buffer)
    {
        await fixture.WaitForIdle();
        var originalBuffering = fixture.Console.BufferKeyInputWhileIdle;
        var originalHook = fixture.Console.KeyInputHook;
        var consumed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        fixture.Console.BufferKeyInputWhileIdle = buffer;
        fixture.Console.KeyInputHook = (ref ConsoleKeyInfo key) =>
        {
            if (key.Key == ConsoleKey.F23)
            {
                consumed.SetResult();
                return KeyInputHookResult.Handled;
            }

            return KeyInputHookResult.NotHandled;
        };

        try
        {
            fixture.Type("idle");
            fixture.Key(ConsoleKey.F23);
            await consumed.Task.WaitAsync(SimpleConsoleFixture.Timeout, TestContext.Current.CancellationToken);
            var task = fixture.ReadLineAsync(ReadLineOptions.SingleLine with { AllowEmptyInput = true });
            fixture.Key(ConsoleKey.Enter);
            Assert.Equal(buffer ? "idle" : string.Empty, await fixture.Wait(task));
        }
        finally
        {
            fixture.Console.BufferKeyInputWhileIdle = originalBuffering;
            fixture.Console.KeyInputHook = originalHook;
            fixture.CancelPendingReadLine();
            await fixture.WaitForIdle();
        }
    }
}
