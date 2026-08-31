// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using System.Buffers;
using System.Collections.Concurrent;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.InteropServices;
using Arc.Threading;
using Arc.Unit;
using SimplePrompt.Internal;

#pragma warning disable CA1001 // Types that own disposable fields should be disposable
#pragma warning disable SA1202 // Elements should be ordered by access
#pragma warning disable SA1204 // Static elements should appear before instance elements
#pragma warning disable SA1401 // Fields should be private

namespace SimplePrompt;

/// <summary>
/// Provides a simple console interface with advanced input handling capabilities including multiline support and custom prompts.
/// This class implements <see cref="IConsoleService"/> and manages console input/output operations.
/// </summary>
public partial class SimpleConsole : IConsoleService // , IDisposable
{
    private const int WindowBufferSize = 32 * 1024;
    private const int InitialWindowWidth = 120;
    private const int InitialWindowHeight = 30;
    private const int MinimumWindowWidth = 30;
    private const int MinimumWindowHeight = 10;
    private const long AdjustWindowIntervalInMilliseconds = 100;

    private static readonly Lazy<SimpleConsole> LazyInstance = new(
        static () =>
        {
            var instance = new SimpleConsole();
            instance.Initialize();
            return instance;
        },
        LazyThreadSafetyMode.ExecutionAndPublication);

    /// <summary>
    /// Gets the lazily initialized singleton instance of <see cref="SimpleConsole"/>.<br/>
    /// The instance is created on first access and initialized exactly once in a thread-safe manner.<br/>
    /// Note that all Console calls (such as Console.Out) will go through SimpleConsole.
    /// </summary>
    public static SimpleConsole Instance => LazyInstance.Value;

    /// <summary>
    /// Gets the foreground color escape code for the specified color,<br/>
    /// or an empty span if colors are disabled or the default color is specified.
    /// </summary>
    /// <param name="color">The console color.</param>
    /// <returns>The escape code.</returns>
    internal ReadOnlySpan<char> GetColorEscapeCode(ConsoleColor color)
        => (this.EnableColor && color != ConsoleHelper.DefaultColor) ? ConsoleHelper.GetForegroundColorEscapeCode(color) : default;

    internal static char[] RentWindowBuffer()
        => ArrayPool<char>.Shared.Rent(WindowBufferSize);

    internal static void ReturnWindowBuffer(char[] buffer)
        => ArrayPool<char>.Shared.Return(buffer);

    #region FieldAndProperty

    /// <summary>
    /// Gets or sets the execution group used to coordinate asynchronous console-related operations.
    /// </summary>
    public ExecutionGroup? ExecutionGroup { get; set; }

    /// <summary>
    /// Gets or sets an optional callback that can intercept and process key input events before the default input handling logic is applied.<br/>
    /// It is applied to every key, whether it comes from the terminal or from <see cref="EnqueueKey(ConsoleKeyInfo)"/>.<br/>
    /// If the callback returns anything other than <see cref="KeyInputHookResult.NotHandled"/>, the key is discarded.
    /// </summary>
    public KeyInputHook? KeyInputHook { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether ANSI/color output is enabled for console rendering.
    /// </summary>
    public bool EnableColor { get; set; } = true;

    /// <summary>
    /// Gets a value indicating whether key input should be buffered while the console window is unfocused.
    /// When <see langword="true"/>, key events are queued and processed after focus is restored.
    /// </summary>
    public bool BufferKeyInputWhenUnfocused { get; init; } = true;

    /// <summary>
    /// Gets or sets the default options for <see cref="ReadLine(ReadLineOptions?, CancellationToken)"/>.
    /// </summary>
    public ReadLineOptions DefaultOptions { get; set; }

    /// <summary>
    /// Gets the underlying <see cref="TextWriter"/> used for console output operations.
    /// This writer is used internally for all text output to the console.
    /// </summary>
    public TextWriter UnderlyingTextWriter => this.simpleTextWriter.UnderlyingTextWriter;

    /// <summary>
    /// Gets a value indicating whether a <see cref="ReadLine(ReadLineOptions?, CancellationToken)"/> operation is currently in progress.<br/>
    /// Returns <see langword="true"/> if at least one active instance exists in the instance list; otherwise, <see langword="false"/>.
    /// </summary>
    public bool IsReadLineInProgress => this.instanceList.Count > 0;

    internal RawConsole RawConsole { get; }

    internal int _windowWidth;
    internal int _windowHeight;
    internal int _cursorLeft;
    internal int _cursorTop;

    private readonly SimpleConsoleWorker worker;
    private readonly SimpleTextWriter simpleTextWriter;
    private readonly SimpleTextReader simpleTextReader;
    private readonly SimpleArrange simpleArrange;
    private readonly ConcurrentQueue<string?> concurrentTextQueue = new();
    private readonly ConcurrentQueue<ConsoleKeyInfo> concurrentKeyQueue = new();
    private readonly Queue<ConsoleKeyInfo> inputKeyQueue = new(); // Not thread-safe, but it is only used by Process().
    private readonly PosixSignalRegistration? posixSignalRegistration;

    private readonly Lock syncObject = new();
    private readonly List<ReadLineInstance> instanceList = [];

    private long adjustWindowTime;

    #endregion

    private SimpleConsole()
    {
        try
        {
            Console.OutputEncoding = System.Text.Encoding.UTF8;
        }
        catch
        {
        }

        this.simpleTextWriter = new(this, Console.Out);
        this.simpleTextReader = new(this, Console.In);
        this.RawConsole = new(this);
        this.simpleArrange = new(this);
        this.DefaultOptions = new();
        this.worker = new(this);

        try
        {
#pragma warning disable CA1416 // Validate platform compatibility
            this.posixSignalRegistration = PosixSignalRegistration.Create(PosixSignal.SIGWINCH, _ =>
            {
                (int Left, int Top) cursor;
                try
                {
                    cursor = Console.GetCursorPosition();
                }
                catch
                {
                    return;
                }

                using (this.syncObject.EnterScope())
                {// Adjusts the cursor position when attached to a console.
                    if (this.TryGetActiveInstance(out var activeInstance))
                    {
                        if (cursor.Top != this._cursorTop ||
                            cursor.Left != this._cursorLeft)
                        {// Cursor changed
                            if (activeInstance.LineList.Count > 0)
                            {
                                activeInstance.LineList[0].Top = cursor.Top;
                                activeInstance.ResetCursor(CursorOperation.None);
                                activeInstance.Redraw();
                                activeInstance.CurrentLocation.Restore(CursorOperation.None);
                            }

                            // this.simpleArrange.Arrange(cursor, true);
                        }
                    }
                }
            });
#pragma warning restore CA1416 // Validate platform compatibility
        }
        catch
        {
        }
    }

    /// <summary>
    /// Asynchronously reads a line of input from the console with support for multiline editing.
    /// </summary>
    /// <param name="options">The options for the console input, including prompts and behavior settings.<br/>
    /// If not specified, <see cref="DefaultOptions" /> will be used.
    /// </param>
    /// <param name="cancellationToken">A cancellation token to cancel the read operation.</param>
    /// <returns>
    /// A task that represents the asynchronous operation. The task result contains an <see cref="InputResult"/>.
    /// </returns>
    public Task<InputResult> ReadLine(ReadLineOptions? options = default, CancellationToken cancellationToken = default)
    {
        // Prepare the window, and if the cursor is in the middle of a line, insert a newline.
        this.PrepareWindow();

        options ??= this.DefaultOptions;
        using (this.syncObject.EnterScope())
        {
            if (this.ExecutionGroup?.IsTerminated == true)
            {
                return Task.FromResult(new InputResult(InputResultKind.Terminated));
            }

            if (cancellationToken.IsCancellationRequested)
            {
                return Task.FromResult(new InputResult(InputResultKind.Canceled));
            }

            foreach (var x in this.instanceList)
            {// If a ReadLine with the same options is already in progress, return its task.
                if (object.ReferenceEquals(x.OptionsSource, options))
                {
                    return x.TaskCompletionSource.Task;
                }
            }

            if (this.instanceList.Count > 0)
            {
                this.instanceList[^1].CurrentLocation.CursorLast();
            }

            if (this._cursorLeft > 0)
            {
                this.UnderlyingTextWriter.WriteLine();
                this.NewLineCursor();
            }

            // Create and prepare a ReadLineInstance.
            var currentInstance = ReadLineInstance.Rent(this, options, cancellationToken);
            this.instanceList.Add(currentInstance);
            currentInstance.Prepare();

            return currentInstance.TaskCompletionSource.Task;
        }
    }

    /// <summary>
    /// Clears the console display or buffer, depending on the specified parameter.
    /// </summary>
    /// <param name="clearBuffer">
    /// If <see langword="true"/>, clears the entire console buffer and resets the cursor position to the top-left corner.
    /// If <see langword="false"/>, clears only the visible console area and resets the cursor position to the top-left corner.
    /// </param>
    public void Clear(bool clearBuffer)
    {
        if (clearBuffer)
        {
            this._cursorTop = 0;
            this._cursorLeft = 0;

            try
            {
                Console.Clear();
            }
            catch
            {
            }
        }
        else
        {
            this.RawConsole.WriteInternal("\e[2J"); // Erase the entire screen.
            this.SetCursorPosition(0, 0, CursorOperation.None);
        }

        using (this.syncObject.EnterScope())
        {
            if (this.TryGetActiveInstance(out var currentInstance))
            {
                currentInstance.Redraw();
                currentInstance.CurrentLocation.Restore(CursorOperation.None);
            }
        }
    }

    /// <summary>
    /// Enqueues a string input message to be processed by the console input queue.<br/>
    /// This allows programmatic injection of input as if it were typed by the user.
    /// </summary>
    /// <param name="message">
    /// The input message to enqueue. If <c>null</c>, a null message is enqueued.
    /// </param>
    public void EnqueueInput(string? message)
    {
        this.concurrentTextQueue.Enqueue(message);
    }

    /// <summary>
    /// Enqueues a key event to be processed by the console key input queue.<br/>
    /// This enables programmatic key injection equivalent to user key presses,<br/>
    /// including the processing by <see cref="KeyInputHook"/>.
    /// </summary>
    /// <param name="keyInfo">
    /// The key information to enqueue, including key code, character, and modifier state.
    /// </param>
    public void EnqueueKey(ConsoleKeyInfo keyInfo)
    {
        this.concurrentKeyQueue.Enqueue(keyInfo);
    }

    Task<InputResult> IConsoleService.ReadLine(CancellationToken cancellationToken)
        => this.ReadLine(default, cancellationToken);

    #region Write

    public void Write(bool value, ConsoleColor color = ConsoleHelper.DefaultColor)
        => this.WriteSpan(value.ToString(), false, color);

    public void WriteLine(bool value, ConsoleColor color = ConsoleHelper.DefaultColor)
        => this.WriteSpan(value.ToString(), true, color);

    public void Write(char value, ConsoleColor color = ConsoleHelper.DefaultColor)
        => this.WriteSpan([value], false, color);

    public void WriteLine(char value, ConsoleColor color = ConsoleHelper.DefaultColor)
        => this.WriteSpan([value], true, color);

    public void Write(decimal value, ConsoleColor color = ConsoleHelper.DefaultColor)
    {
        Span<char> buffer = stackalloc char[64];
        value.TryFormat(buffer, out var written, default, this.UnderlyingTextWriter.FormatProvider);
        this.WriteSpan(buffer.Slice(0, written), false, color);
    }

    public void WriteLine(decimal value, ConsoleColor color = ConsoleHelper.DefaultColor)
    {
        Span<char> buffer = stackalloc char[64];
        value.TryFormat(buffer, out var written, default, this.UnderlyingTextWriter.FormatProvider);
        this.WriteSpan(buffer.Slice(0, written), true, color);
    }

    public void Write(double value, ConsoleColor color = ConsoleHelper.DefaultColor)
    {
        Span<char> buffer = stackalloc char[32];
        value.TryFormat(buffer, out var written, default, this.UnderlyingTextWriter.FormatProvider);
        this.WriteSpan(buffer.Slice(0, written), false, color);
    }

    public void WriteLine(double value, ConsoleColor color = ConsoleHelper.DefaultColor)
    {
        Span<char> buffer = stackalloc char[32];
        value.TryFormat(buffer, out var written, default, this.UnderlyingTextWriter.FormatProvider);
        this.WriteSpan(buffer.Slice(0, written), true, color);
    }

    public void Write(float value, ConsoleColor color = ConsoleHelper.DefaultColor)
    {
        Span<char> buffer = stackalloc char[32];
        value.TryFormat(buffer, out var written, default, this.UnderlyingTextWriter.FormatProvider);
        this.WriteSpan(buffer.Slice(0, written), false, color);
    }

    public void WriteLine(float value, ConsoleColor color = ConsoleHelper.DefaultColor)
    {
        Span<char> buffer = stackalloc char[32];
        value.TryFormat(buffer, out var written, default, this.UnderlyingTextWriter.FormatProvider);
        this.WriteSpan(buffer.Slice(0, written), true, color);
    }

    public void Write(int value, ConsoleColor color = ConsoleHelper.DefaultColor)
    {
        Span<char> buffer = stackalloc char[32];
        value.TryFormat(buffer, out var written, default, this.UnderlyingTextWriter.FormatProvider);
        this.WriteSpan(buffer.Slice(0, written), false, color);
    }

    public void WriteLine(int value, ConsoleColor color = ConsoleHelper.DefaultColor)
    {
        Span<char> buffer = stackalloc char[32];
        value.TryFormat(buffer, out var written, default, this.UnderlyingTextWriter.FormatProvider);
        this.WriteSpan(buffer.Slice(0, written), true, color);
    }

    public void Write(uint value, ConsoleColor color = ConsoleHelper.DefaultColor)
    {
        Span<char> buffer = stackalloc char[32];
        value.TryFormat(buffer, out var written, default, this.UnderlyingTextWriter.FormatProvider);
        this.WriteSpan(buffer.Slice(0, written), false, color);
    }

    public void WriteLine(uint value, ConsoleColor color = ConsoleHelper.DefaultColor)
    {
        Span<char> buffer = stackalloc char[32];
        value.TryFormat(buffer, out var written, default, this.UnderlyingTextWriter.FormatProvider);
        this.WriteSpan(buffer.Slice(0, written), true, color);
    }

    public void Write(long value, ConsoleColor color = ConsoleHelper.DefaultColor)
    {
        Span<char> buffer = stackalloc char[32];
        value.TryFormat(buffer, out var written, default, this.UnderlyingTextWriter.FormatProvider);
        this.WriteSpan(buffer.Slice(0, written), false, color);
    }

    public void WriteLine(long value, ConsoleColor color = ConsoleHelper.DefaultColor)
    {
        Span<char> buffer = stackalloc char[32];
        value.TryFormat(buffer, out var written, default, this.UnderlyingTextWriter.FormatProvider);
        this.WriteSpan(buffer.Slice(0, written), true, color);
    }

    public void Write(ulong value, ConsoleColor color = ConsoleHelper.DefaultColor)
    {
        Span<char> buffer = stackalloc char[32];
        value.TryFormat(buffer, out var written, default, this.UnderlyingTextWriter.FormatProvider);
        this.WriteSpan(buffer.Slice(0, written), false, color);
    }

    public void WriteLine(ulong value, ConsoleColor color = ConsoleHelper.DefaultColor)
    {
        Span<char> buffer = stackalloc char[32];
        value.TryFormat(buffer, out var written, default, this.UnderlyingTextWriter.FormatProvider);
        this.WriteSpan(buffer.Slice(0, written), true, color);
    }

    /// <summary>
    /// Writes the specified message to the console without a newline.<br/>
    /// Note that when ReadLine() is waiting for input, a newline is inserted after the message is displayed.
    /// </summary>
    /// <param name="message">The message to write. If empty, nothing is written.</param>
    /// <param name="color">Specify the message text color.<br/>
    /// The color may not be applied depending on the implementation.</param>
    public void Write(ReadOnlySpan<char> message = default, ConsoleColor color = ConsoleHelper.DefaultColor)
        => this.WriteSpan(message, false, color);

    /// <summary>
    /// Writes the specified message to the console followed by a newline.<br/>
    /// If a <see cref="ReadLine(ReadLineOptions?, CancellationToken)"/> operation is in progress,<br/>
    /// the message is written with proper cursor management and the active input instance is redrawn.
    /// </summary>
    /// <param name="message">
    /// The message to write. If <c>empty</c>, only a newline is written.
    /// </param>
    /// /// <param name="color">Specify the message text color.<br/>
    /// The color may not be applied depending on the implementation.</param>
    public void WriteLine(ReadOnlySpan<char> message, ConsoleColor color = ConsoleHelper.DefaultColor)
        => this.WriteSpan(message, true, color);

    /// <summary>
    /// Writes the specified message to the console without a newline.<br/>
    /// Note that when ReadLine() is waiting for input, a newline is inserted after the message is displayed.
    /// </summary>
    /// <param name="message">The message to write. If null, nothing is written.</param>
    /// <param name="color">Specify the message text color.<br/>
    /// The color may not be applied depending on the implementation.</param>
    public void Write(string? message, ConsoleColor color = ConsoleHelper.DefaultColor)
        => this.WriteSpan(message, false, color);

    /// <summary>
    /// Writes the specified message to the console followed by a newline.<br/>
    /// If a <see cref="ReadLine(ReadLineOptions?, CancellationToken)"/> operation is in progress,<br/>
    /// the message is written with proper cursor management and the active input instance is redrawn.
    /// </summary>
    /// <param name="message">
    /// The message to write. If <c>null</c>, only a newline is written.
    /// </param>
    /// /// <param name="color">Specify the message text color.<br/>
    /// The color may not be applied depending on the implementation.</param>
    public void WriteLine(string? message = null, ConsoleColor color = ConsoleHelper.DefaultColor)
        => this.WriteSpan(message, true, color);

    public bool TryGetCurrentReadLineOptions([MaybeNullWhen(false)] out ReadLineOptions readLineOptions)
    {
        using (this.syncObject.EnterScope())
        {
            if (this.TryGetActiveInstance(out var instance))
            {
                readLineOptions = instance.Options;
                return true;
            }
            else
            {
                readLineOptions = default;
                return false;
            }
        }
    }

    #endregion

    ConsoleKeyInfo IConsoleService.ReadKey(bool intercept)
    {
        try
        {
            return Console.ReadKey();
        }
        catch
        {
            return default;
        }
    }

    bool IConsoleService.KeyAvailable
    {
        get
        {
            try
            {
                return Console.KeyAvailable;
            }
            catch
            {
                return false;
            }
        }
    }

    internal void Abort()
    {
        using (this.syncObject.EnterScope())
        {
            if (this.instanceList.Count > 0)
            {// New line
                this.instanceList[^1].CurrentLocation.MoveToEnd();
                this.UnderlyingTextWriter.WriteLine();
                this.NewLineCursor();
            }

            foreach (var x in this.instanceList)
            {
                x.TaskCompletionSource.SetResult(new(InputResultKind.Terminated));
            }

            this.instanceList.Clear();
        }
    }

    internal void Process()
    {// Called by the worker thread at each IntervalTimeSpan.
        ConsoleKeyInfo keyInfo = default;
        InputResult inputResult;

        // Detect window resize.
        var current = Environment.TickCount64;
        if ((current - this.adjustWindowTime) >= AdjustWindowIntervalInMilliseconds)
        {
            this.adjustWindowTime = current;
            this.AdjustWindow();
        }

        // Read key -> InputKeyQueue
        while (this.RawConsole.TryRead(out keyInfo))
        {
            this.EnqueueKeyInput(ref keyInfo);
        }

        // KeyInfo queue (EnqueueKey) -> InputKeyQueue
        while (this.concurrentKeyQueue.TryDequeue(out keyInfo))
        {
            this.EnqueueKeyInput(ref keyInfo);
        }

        // Get the current instance
        ReadLineInstance? currentInstance;
        using (this.syncObject.EnterScope())
        {
            for (var i = 0; i < this.instanceList.Count - 1; i++)
            {// If there are any canceled instances among the pending ReadLineInstances, notify and remove them.
                var instance = this.instanceList[i];
                if (instance.CancellationToken.IsCancellationRequested)
                {
                    this.instanceList.RemoveAt(i--);
                    instance.TaskCompletionSource.SetResult(new(InputResultKind.Canceled));
                    ReadLineInstance.Return(instance);
                }
            }

            if (!this.TryGetActiveInstance(out currentInstance))
            {// No active instance
                return;
            }

            this.simpleArrange.Set(currentInstance);

            if (currentInstance.CancellationToken.IsCancellationRequested)
            {// Canceled
                inputResult = new(InputResultKind.Canceled);
                goto CompleteInstance;
            }
            else if (this.ExecutionGroup?.IsTerminated == true/* ||
                this.worker.IsTerminated*/)
            {// Terminated
                inputResult = new(InputResultKind.Terminated);
                goto CompleteInstance;
            }

            if (!this.concurrentTextQueue.IsEmpty &&
                currentInstance.IsEmptyInput() &&
                this.concurrentTextQueue.TryDequeue(out var queuedMessage))
            {
                var queuedSpan = queuedMessage.AsSpan();
                do
                {
                    var length = Math.Min(queuedSpan.Length, currentInstance.CharBuffer.Length);
                    var charSpan = currentInstance.CharBuffer.AsSpan(0, length);
                    queuedSpan.Slice(0, length).CopyTo(charSpan);
                    queuedSpan = queuedSpan.Slice(length);

                    if (queuedSpan.Length == 0)
                    {
                        var result = currentInstance.ProcessInput(SimplePromptHelper.EnterKeyInfo, charSpan);
                        if (result is not null)
                        {
                            result = ProcessTextInputHook(result);
                            if (result is null)
                            {// Rejected
                                break;
                            }
                        }

                        if (result is not null)
                        {
                            inputResult = new(result);
                            goto CompleteInstance;
                        }
                    }
                    else
                    {
                        currentInstance.ProcessInput(keyInfo, charSpan);
                    }
                }
                while (queuedSpan.Length > 0);
            }
        }

        while (this.inputKeyQueue.TryDequeue(out keyInfo))
        {// Dequeue key input and process it.
ProcessKeyInfo:
            if (keyInfo.KeyChar == '\n' ||
            keyInfo.Key == ConsoleKey.Enter)
            {
                keyInfo = SimplePromptHelper.EnterKeyInfo;
            }
            else if (keyInfo.KeyChar == '\t' ||
                keyInfo.Key == ConsoleKey.Tab)
            {// Tab; in the future, input completion.
            }
            else if (keyInfo.KeyChar == '\r')
            {// CrLf -> Lf
                continue;
            }
            else if (currentInstance.Options.CancelOnEscape &&
                keyInfo.Key == ConsoleKey.Escape)
            {
                inputResult = new(InputResultKind.Canceled);
                goto CompleteInstance;
            }

            if (currentInstance.Options.KeyInputHook is not null)
            {
                var hookResult = currentInstance.Options.KeyInputHook(ref keyInfo);
                if (hookResult == KeyInputHookResult.Handled)
                {
                    continue;
                }
                else if (hookResult == KeyInputHookResult.Cancel)
                {
                    inputResult = new(InputResultKind.Canceled);
                    goto CompleteInstance;
                }
            }

            bool processInput = true;
            bool hasPendingKey = false;
            ConsoleKeyInfo pendingKeyInfo = default;
            if (IsControl(keyInfo))
            {// Control
            }
            else
            {// Not control: accumulate the character and consume the following keys as well.
                currentInstance.CharBuffer[currentInstance.CharPosition++] = keyInfo.KeyChar;
                if (this.inputKeyQueue.TryDequeue(out var nextKeyInfo))
                {
                    processInput = false;
                    if (currentInstance.CharPosition >= (ReadLineInstance.CharBufferSize - 2))
                    {
                        if (currentInstance.CharPosition >= ReadLineInstance.CharBufferSize ||
                            char.IsLowSurrogate(nextKeyInfo.KeyChar))
                        {// The buffer is full.
                            processInput = true;
                        }
                    }

                    if (processInput)
                    {// Flush the accumulated characters first, then process the next key.
                        hasPendingKey = true;
                        pendingKeyInfo = nextKeyInfo;
                        keyInfo = default;
                    }
                    else
                    {
                        keyInfo = nextKeyInfo;
                        goto ProcessKeyInfo;
                    }
                }
            }

            if (processInput)
            {// Process input
                string? result;
                using (this.syncObject.EnterScope())
                {
                    result = currentInstance.ProcessInput(keyInfo, currentInstance.CharBuffer.AsSpan(0, currentInstance.CharPosition));
                    currentInstance.CharPosition = 0; // The characters have been consumed.
                    if (result is not null)
                    {
                        result = ProcessTextInputHook(result);
                        if (result is null)
                        {// Rejected
                            continue;
                        }

                        inputResult = new(result);
                        goto CompleteInstance;
                    }
                }

                if (hasPendingKey)
                {// Process pending key input.
                    keyInfo = pendingKeyInfo;
                    goto ProcessKeyInfo;
                }
            }
        }

        return;

CompleteInstance:
        using (this.syncObject.EnterScope())
        {
            currentInstance.CurrentLocation.MoveToEnd();
            this.UnderlyingTextWriter.WriteLine();
            this.NewLineCursor();

            this.RemoveInstance(currentInstance);
        }

        currentInstance.TaskCompletionSource.SetResult(inputResult);
        ReadLineInstance.Return(currentInstance);

        string? ProcessTextInputHook(string result)
        {
            if (currentInstance.Options.TextInputHook is { } textInputHook)
            {
                var newResult = currentInstance.Options.TextInputHook(result);
                if (newResult is null)
                {// Rejected by the hook delegate.
                    this.UnderlyingTextWriter.WriteLine();
                    this.NewLineCursor();
                    currentInstance.Reset();
                    currentInstance.Redraw();
                    currentInstance.CurrentLocation.Reset();
                }

                return newResult;
            }
            else
            {
                return result;
            }
        }
    }

    internal void AdvanceCursor(ReadOnlySpan<char> text, bool newLine)
    {
        var left = this._cursorLeft;
        var top = this._cursorTop;
        var windowWidth = this._windowWidth;
        var windowHeight = this._windowHeight;

        for (var i = 0; i < text.Length; i++)
        {
            while (text[i] == '\e')
            {// Skip ANSI escape code
                i++;
                while (i < text.Length)
                {
                    if (char.IsAsciiLetter(text[i]))
                    {
                        i++;
                        break;
                    }

                    i++;
                }

                if (i >= text.Length)
                {
                    goto Exit;
                }
            }

            int width;
            var c = text[i];
            if (char.IsHighSurrogate(c) && (i + 1) < text.Length && char.IsLowSurrogate(text[i + 1]))
            {// A surrogate pair occupies the width of a single character.
                width = SimplePromptHelper.GetCharWidth(char.ConvertToUtf32(c, text[i + 1]));
                i++;
            }
            else
            {
                width = SimplePromptHelper.GetCharWidth(c);
            }

            left += width;
            if (left == windowWidth)
            {
                left = 0;
                top++;
            }
            else if (left > windowWidth)
            {
                left = width;
                top++;
            }
        }

Exit:
        if (newLine)
        {
            if (top > this._cursorTop &&
                left == 0)
            {// Already on a new line.
            }
            else
            {
                left = 0;
                top++;
            }
        }

        this._cursorLeft = left;
        this._cursorTop = top;

        // Scroll if needed.
        var scroll = top - windowHeight + 1;
        if (scroll > 0)
        {
            this.Scroll(scroll, true);
        }
    }

    internal void NewLineCursor()
    {
        this._cursorLeft = 0;
        this._cursorTop++;

        // Scroll if needed.
        var scroll = this._cursorTop - this._windowHeight + 1;
        if (scroll > 0)
        {
            this.Scroll(scroll, true);
        }
    }

    internal void Scroll(int scroll, bool moveCursor)
    {
        if (moveCursor)
        {
            this._cursorTop -= scroll;
        }

        if (this.TryGetActiveInstance(out var activeInstance))
        {
            foreach (var y in activeInstance.LineList)
            {
                y.Top -= scroll;
            }
        }
    }

    internal void ShowCursor()
    {
        this.RawConsole.WriteInternal(ConsoleHelper.ShowCursorSpan);
    }

    internal void SetCursorPosition(int cursorLeft, int cursorTop, CursorOperation cursorOperation)
    {// Move and show cursor.
        if (cursorLeft > (this._windowWidth - 1))
        {
            cursorLeft = this._windowWidth - 1;
        }

        var windowBuffer = SimpleConsole.RentWindowBuffer();
        var buffer = windowBuffer.AsSpan();

        SimplePromptHelper.TryCopySetCursor(ref buffer, cursorLeft, cursorTop);

        if (cursorOperation == CursorOperation.Show)
        {
            SimplePromptHelper.TryCopy(ConsoleHelper.ShowCursorSpan, ref buffer);
        }
        else if (cursorOperation == CursorOperation.Hide)
        {
            SimplePromptHelper.TryCopy(ConsoleHelper.HideCursorSpan, ref buffer);
        }

        this.RawConsole.WriteInternal(windowBuffer.AsSpan(0, windowBuffer.Length - buffer.Length));
        SimpleConsole.ReturnWindowBuffer(windowBuffer);

        this._cursorLeft = cursorLeft;
        this._cursorTop = cursorTop;
    }

    internal bool TryGetActiveInstance([MaybeNullWhen(false)] out ReadLineInstance instance)
    {
        if (this.instanceList.Count == 0)
        {
            instance = null;
            return false;
        }

        instance = this.instanceList[^1];
        return true;
    }

    internal void WriteSpan(ReadOnlySpan<char> message, bool newLine, ConsoleColor color = ConsoleHelper.DefaultColor)
    {
        using (this.syncObject.EnterScope())
        {
            if (!this.TryGetActiveInstance(out var activeInstance))
            {
                this.WriteInternal(message, newLine, color);

                return;
            }

            if (message.Length == 0 &&
                !newLine)
            {
                return;
            }

            activeInstance.ResetCursor(CursorOperation.Hide);

            this.WriteInternal(message, true, color);

            activeInstance.Redraw();
            activeInstance.CurrentLocation.Restore(CursorOperation.Show);
        }
    }

    internal void ClearRow(int top)
    {
        if (top < 0 || top >= this._windowHeight)
        {
            return;
        }

        var windowBuffer = SimpleConsole.RentWindowBuffer();
        var buffer = windowBuffer.AsSpan();

        var moveCursor = this._cursorTop != top || this._cursorLeft != 0;
        if (moveCursor)
        {
            SimplePromptHelper.TryCopy(ConsoleHelper.SaveCursorSpan, ref buffer);
            SimplePromptHelper.TryCopySetCursor(ref buffer, 0, top);
        }

        // Erase entire line
        SimplePromptHelper.TryCopy(ConsoleHelper.EraseEntireLineSpan, ref buffer);

        if (moveCursor)
        {// Restore cursor
            SimplePromptHelper.TryCopy(ConsoleHelper.RestoreCursorSpan, ref buffer);
        }

        this.RawConsole.WriteInternal(windowBuffer.AsSpan(0, windowBuffer.Length - buffer.Length));
        SimpleConsole.ReturnWindowBuffer(windowBuffer);
    }

    private void AdjustWindow()
    {
        (var prevWindowWidth, var prevWindowHeight) = (this._windowWidth, this._windowHeight);
        this.PrepareWindow();
        if (prevWindowWidth != this._windowWidth ||
            prevWindowHeight != this._windowHeight)
        {// Window size changed
            try
            {
                var newCursor = Console.GetCursorPosition();
                using (this.syncObject.EnterScope())
                {
                    this.simpleArrange.Arrange(newCursor, false);
                }
            }
            catch
            {
            }
        }
    }

    private void PrepareWindow()
    {
        var windowWidth = InitialWindowWidth;
        var windowHeight = InitialWindowHeight;

        try
        {
            windowWidth = Console.WindowWidth;
            windowHeight = Console.WindowHeight;
        }
        catch
        {
        }

        if (windowWidth < MinimumWindowWidth)
        {
            windowWidth = MinimumWindowWidth;
        }

        if (windowHeight < MinimumWindowHeight)
        {
            windowHeight = MinimumWindowHeight;
        }

        this._windowWidth = windowWidth;
        this._windowHeight = windowHeight;
    }

    private void RemoveInstance(ReadLineInstance target)
    {
        target.Clear();
        this.instanceList.Remove(target);

        if (this.TryGetActiveInstance(out var activeInstance))
        {
            activeInstance.Redraw();
            activeInstance.CurrentLocation.Restore(CursorOperation.None);
        }
    }

    private void WriteInternal(ReadOnlySpan<char> message, bool newLine, ConsoleColor color)
    {
        if (message.Length == 0)
        {
            if (newLine)
            {
                this.AdvanceCursor(default, true);
                this.RawConsole.WriteInternal(ConsoleHelper.EraseEntireLineAndNewLineSpan);
            }

            return;
        }

        var windowBuffer = SimpleConsole.RentWindowBuffer();
        var span = windowBuffer.AsSpan();

        var colorSpan = this.GetColorEscapeCode(color);
        Append(colorSpan, ref span);

        while (message.Length > 0)
        {
            var appendNewLine = false;
            ReadOnlySpan<char> text;
            var i = message.IndexOf('\n');
            if (i > 0 && message[i - 1] == '\r')
            {// text\r\n
                text = message.Slice(0, i - 1);
                message = message.Slice(i + 1);
                appendNewLine = true;
            }
            else if (i >= 0)
            {// text\n
                text = message.Slice(0, i);
                message = message.Slice(i + 1);
                appendNewLine = true;
            }
            else
            {// text
                text = message;
                message = default;
                appendNewLine = newLine;
            }

            // Text
            Append(text, ref span);
            Append(appendNewLine ? ConsoleHelper.EraseToEndOfLineAndNewLineSpan : ConsoleHelper.EraseToEndOfLineSpan, ref span);

            this.AdvanceCursor(text, appendNewLine);
        }

        if (colorSpan.Length > 0)
        {
            Append(ConsoleHelper.ResetSpan, ref span);
        }

        this.RawConsole.WriteInternal(windowBuffer.AsSpan(0, windowBuffer.Length - span.Length));
        SimpleConsole.ReturnWindowBuffer(windowBuffer);

        void Append(ReadOnlySpan<char> source, ref Span<char> destination)
        {
            if (SimplePromptHelper.TryCopy(source, ref destination))
            {
                return;
            }

            // The source does not fit in the remaining buffer, so write it in chunks.
            while (source.Length > 0)
            {
                var length = Math.Min(source.Length, destination.Length);
                if (length > 0 && length < source.Length && char.IsHighSurrogate(source[length - 1]))
                {// Do not split a surrogate pair.
                    length--;
                }

                if (length == 0)
                {// Flush the buffer.
                    this.RawConsole.WriteInternal(windowBuffer.AsSpan(0, windowBuffer.Length - destination.Length));
                    destination = windowBuffer.AsSpan();
                    continue;
                }

                source.Slice(0, length).CopyTo(destination);
                destination = destination.Slice(length);
                source = source.Slice(length);
            }
        }
    }

    private void Initialize()
    {
        try
        {
            Console.SetOut(this.simpleTextWriter);
            Console.SetIn(this.simpleTextReader);
            (this._cursorLeft, this._cursorTop) = Console.GetCursorPosition();
        }
        catch
        {
        }

        this.PrepareWindow();
    }

    private void EnqueueKeyInput(ref ConsoleKeyInfo keyInfo)
    {// Applies KeyInputHook and queues the key. Called only by Process(), since inputKeyQueue is not thread-safe.
        if (this.KeyInputHook is { } keyInputHook &&
            keyInputHook(ref keyInfo) != KeyInputHookResult.NotHandled)
        {// Handled
            return;
        }

        if (this.BufferKeyInputWhenUnfocused ||
            this.instanceList.Count > 0)
        {
            if (this.inputKeyQueue.Count < WindowBufferSize)
            {
                this.inputKeyQueue.Enqueue(keyInfo);
            }
        }
    }

    private static bool IsControl(ConsoleKeyInfo keyInfo)
    {
        if (keyInfo.KeyChar == 0)
        {
            return true;
        }
        else if ((keyInfo.Modifiers & (ConsoleModifiers.Control | ConsoleModifiers.Alt)) != 0)
        {
            return true;
        }

        return keyInfo.Key is ConsoleKey.Enter or ConsoleKey.Backspace or ConsoleKey.Escape or ConsoleKey.Tab;
    }
}
