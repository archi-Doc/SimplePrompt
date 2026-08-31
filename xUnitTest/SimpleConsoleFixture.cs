// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Arc.Unit;
using SimplePrompt;

namespace xUnitTest;

/// <summary>
/// Provides the shared <see cref="SimpleConsole"/> instance used by the console tests.<br/>
/// <see cref="SimpleConsole"/> is a process-wide singleton which takes over <see cref="Console.Out"/> and <see cref="Console.In"/>,
/// so every test that touches it must belong to <see cref="SimpleConsoleTests"/> and therefore run sequentially.
/// </summary>
public sealed class SimpleConsoleFixture
{
    /// <summary>
    /// The maximum time to wait for a ReadLine operation (the worker polls the input every 10 milliseconds).
    /// </summary>
    public static readonly TimeSpan Timeout = TimeSpan.FromSeconds(30);

    /// <summary>
    /// Delays for the specified time (the current test can cancel it).
    /// </summary>
    /// <param name="milliseconds">The delay in milliseconds.</param>
    /// <returns>A task.</returns>
    public static Task Delay(int milliseconds)
        => Task.Delay(milliseconds, TestContext.Current.CancellationToken);

    /// <summary>
    /// Waits for the specified task with the standard timeout.
    /// </summary>
    /// <typeparam name="T">The result type.</typeparam>
    /// <param name="task">The task.</param>
    /// <returns>The result.</returns>
    public static Task<T> WaitAny<T>(Task<T> task)
        => task.WaitAsync(Timeout, TestContext.Current.CancellationToken);

    public SimpleConsoleFixture()
    {
        System.Console.SetOut(this.Sink); // The instance captures Console.Out when it is created.
        this.Console = SimpleConsole.Instance;
        this.ConsoleOut = System.Console.Out; // SimpleTextWriter
        this.ConsoleIn = System.Console.In; // SimpleTextReader
    }

    /// <summary>
    /// Gets the writer which receives all console output.
    /// </summary>
    public StringWriter Sink { get; } = new();

    /// <summary>
    /// Gets the singleton instance under test.
    /// </summary>
    public SimpleConsole Console { get; }

    /// <summary>
    /// Gets the <see cref="TextWriter"/> that <see cref="SimpleConsole"/> installed as <see cref="System.Console.Out"/>.
    /// </summary>
    public TextWriter ConsoleOut { get; }

    /// <summary>
    /// Gets the <see cref="TextReader"/> that <see cref="SimpleConsole"/> installed as <see cref="System.Console.In"/>.
    /// </summary>
    public TextReader ConsoleIn { get; }

    /// <summary>
    /// Starts a ReadLine operation.
    /// </summary>
    /// <param name="options">The options. If not specified, an empty-line-tolerant default is used.</param>
    /// <returns>The task.</returns>
    public Task<InputResult> ReadLine(ReadLineOptions? options = default)
        => this.Console.ReadLine(options ?? new() { AllowEmptyLineInput = true });

    /// <summary>
    /// Waits for the ReadLine operation and returns the input text.
    /// </summary>
    /// <param name="task">The task returned by <see cref="ReadLine(ReadLineOptions?)"/>.</param>
    /// <returns>The input text.</returns>
    public async Task<string?> Wait(Task<InputResult> task)
        => (await task.WaitAsync(Timeout)).Text;

    /// <summary>
    /// Waits for the ReadLine operation and returns the result.
    /// </summary>
    /// <param name="task">The task returned by <see cref="ReadLine(ReadLineOptions?)"/>.</param>
    /// <returns>The result.</returns>
    public Task<InputResult> WaitResult(Task<InputResult> task)
        => task.WaitAsync(Timeout);

    /// <summary>
    /// Enqueues the specified text as if the user had typed it.
    /// </summary>
    /// <param name="text">The text.</param>
    public void Type(string text)
    {
        foreach (var c in text)
        {
            this.Console.EnqueueKey(new ConsoleKeyInfo(c, default, false, false, false));
        }
    }

    /// <summary>
    /// Enqueues the specified key.
    /// </summary>
    /// <param name="key">The key.</param>
    /// <param name="keyChar">The character.</param>
    /// <param name="control">Whether the Control key is pressed.</param>
    public void Key(ConsoleKey key, char keyChar = default, bool control = false)
        => this.Console.EnqueueKey(new ConsoleKeyInfo(keyChar, key, false, false, control));

    /// <summary>
    /// Clears the recorded console output.
    /// </summary>
    public void ClearOutput()
        => this.Sink.GetStringBuilder().Clear();

    /// <summary>
    /// Clears the recorded console output and returns what was recorded.
    /// </summary>
    /// <returns>The recorded output.</returns>
    public string TakeOutput()
    {
        var output = this.Sink.ToString();
        this.ClearOutput();
        return output;
    }

    /// <summary>
    /// Waits until no ReadLine operation is in progress.
    /// </summary>
    /// <returns>A task.</returns>
    public async Task WaitForIdle()
    {
        var start = Environment.TickCount64;
        while (this.Console.IsReadLineInProgress)
        {
            if ((Environment.TickCount64 - start) > (long)Timeout.TotalMilliseconds)
            {
                throw new TimeoutException("A ReadLine operation is still in progress.");
            }

            await Task.Delay(5);
        }
    }
}

/// <summary>
/// Defines the collection which serializes every test that uses <see cref="SimpleConsoleFixture"/>.
/// </summary>
[CollectionDefinition(Name)]
public class SimpleConsoleTests : ICollectionFixture<SimpleConsoleFixture>
{
    /// <summary>
    /// The collection name.
    /// </summary>
    public const string Name = "SimpleConsole";
}
