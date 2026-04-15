// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using System;
using System.Buffers;
using System.Collections.Concurrent;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Runtime.InteropServices;
using Arc.Threading;
using Arc.Unit;
using SimplePrompt.Internal;

#pragma warning disable CA1001 // Types that own disposable fields should be disposable
#pragma warning disable SA1204 // Static elements should appear before instance elements
#pragma warning disable SA1401 // Fields should be private

namespace SimplePrompt;

/// <summary>
/// Provides a simple console interface with advanced input handling capabilities including multiline support and custom prompts.
/// This class implements <see cref="IConsoleService"/> and manages console input/output operations.
/// </summary>
public partial class SimpleConsole : IConsoleService
{
    private const int WindowBufferSize = 32 * 1024;
    private const int InitialWindowWidth = 120;
    private const int InitialWindowHeight = 30;
    private const int MinimumWindowWidth = 30;
    private const int MinimumWindowHeight = 10;
    private static readonly TimeSpan adjustWindowInterval = TimeSpan.FromMilliseconds(1000);

    private static SimpleConsole? _instance;

    /// <summary>
    /// Gets or creates the singleton instance of <see cref="SimpleConsole"/> using thread-safe lazy initialization.
    /// If an instance already exists, it returns the existing instance; otherwise, it creates and initializes a new one.
    /// </summary>
    /// <returns>The singleton <see cref="SimpleConsole"/> instance.</returns>
    public static SimpleConsole GetOrCreate()
    {
        var instance = Volatile.Read(ref _instance);
        if (instance is not null)
        {
            return instance;
        }

        instance = new SimpleConsole();
        var original = Interlocked.CompareExchange(ref _instance, instance, null);
        if (original is not null)
        {
            return original;
        }

        instance.Initialize();
        return instance;
    }

    internal static char[] RentWindowBuffer()
        => ArrayPool<char>.Shared.Rent(WindowBufferSize);

    internal static void ReturnWindowBuffer(char[] buffer)
        => ArrayPool<char>.Shared.Return(buffer);

    #region FieldAndProperty

    /// <summary>
    /// Gets or sets the <see cref="ThreadCoreBase"/> used for thread coordination and cancellation.<br/>
    /// Default is <see cref="ThreadCore.Root"/>.
    /// </summary>
    public ThreadCoreBase Core { get; set; } = ThreadCore.Root;

    public KeyInputHook? KeyInputHook { get; set; }

    public bool EnableColor { get; set; } = true;

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
    private readonly ConcurrentQueue<string?> inputTextQueue = new();
    private readonly Queue<ConsoleKeyInfo> inputKeyQueue = new(); // Process()
    private readonly PosixSignalRegistration? posixSignalRegistration;

    private readonly Lock syncObject = new();
    private List<ReadLineInstance> instanceList = [];
    private DateTime adjustWindowTime;

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
        this.worker = new(this, ThreadCore.Root);

        try
        {
#pragma warning disable CA1416 // Validate platform compatibility
            this.posixSignalRegistration = PosixSignalRegistration.Create(PosixSignal.SIGWINCH, _ =>
            {
                var cursor = Console.GetCursorPosition();
                using (this.syncObject.EnterScope())
                {// Adjusts the cursor position when attached to a console.
                    if (this.TryGetActiveInstance(out var activeInstance))
                    {
                        try
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
                        catch
                        {
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

    public void Terminate()
    {
        this.worker.Dispose();
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
        // this.RunJob(JobKind.PrepareWindow);
        // this.CheckCursor();

        using (this.syncObject.EnterScope())
        {
            if (this.worker.IsTerminated)
            {
                return Task<InputResult>.FromResult(new InputResult(InputResultKind.Terminated));
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
            var currentInstance = ReadLineInstance.Rent(this, options ?? this.DefaultOptions, cancellationToken);
            this.instanceList.Add(currentInstance);
            currentInstance.Prepare();
            // this.CheckCursor();

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
            this.RawConsole.WriteInternal($"\e[2J");
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
        this.inputTextQueue.Enqueue(message); // ConcurrentQueue
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

    /*public void WriteLineAndForget(string? message = null, ConsoleColor color = ConsoleHelper.DefaultColor)
    {
        var job = this.worker.Rent();
        job.Kind = JobKind.WriteLine;
        job.Message = message;
        job.Color = color;
        this.worker.Add(job);
    }*/

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

    /*[Conditional("DEBUG")]
    internal void CheckCursor()
    {
        try
        {
            if (this.RawConsole.UseStdin)
            {// With Interop.Sys.Write(), changes are not applied immediately, so the cursor position cannot be retrieved.
                return;
            }

            var cursor = SimpleConsole.GetCursorPosition();
            if (cursor.Left != this.CursorLeft ||
                cursor.Top != this.CursorTop)
            {// Inconsistent cursor position
                var st = $"({this.CursorLeft}, {this.CursorTop})->({cursor.Left},{cursor.Top})";
                this.UnderlyingTextWriter.WriteLine(st);
                this.SyncCursor();
            }
        }
        catch
        {
        }
    }*/

    internal void Abort()
    {
        using (this.syncObject.EnterScope())
        {
            foreach (var x in this.instanceList)
            {
                x.TaskCompletionSource.SetResult(new(InputResultKind.Terminated));
            }

            this.instanceList.Clear();
        }
    }

    internal void Process()
    {
        ConsoleKeyInfo keyInfo = default;
        InputResult inputResult;

        // Detect window resize.
        var current = DateTime.UtcNow;
        if ((current - this.adjustWindowTime) > adjustWindowInterval)
        {
            this.adjustWindowTime = current;
            this.AdjustWindow();
        }

        // Read key -> InputKeyQueue
        while (this.RawConsole.TryRead(out keyInfo))
        {
            // Hook
            if (this.KeyInputHook is { } keyInputHook &&
                keyInputHook(keyInfo) != KeyInputHookResult.NotHandled)
            {// Handled
                continue;
            }

            if (this.inputKeyQueue.Count < WindowBufferSize)
            {
                this.inputKeyQueue.Enqueue(keyInfo);
            }
        }

        // Get the current instance
        ReadLineInstance? currentInstance;
        using (this.syncObject.EnterScope())
        {
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
            else if (this.Core.IsTerminated)
            {// Terminated
                inputResult = new(InputResultKind.Terminated);
                goto CompleteInstance;
            }

            if (!this.inputTextQueue.IsEmpty &&
                currentInstance.IsEmptyInput() &&
                this.inputTextQueue.TryDequeue(out var queuedMessage))
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
        {
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
                var hookResult = currentInstance.Options.KeyInputHook(keyInfo);
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
            ConsoleKeyInfo pendingKeyInfo = default;
            if (IsControl(keyInfo))
            {// Control
            }
            else
            {// Not control
                currentInstance.CharBuffer[currentInstance.CharPosition++] = keyInfo.KeyChar;
                using (this.syncObject.EnterScope())
                {
                    if (this.inputKeyQueue.TryDequeue(out keyInfo))
                    {
                        processInput = false;
                        if (currentInstance.CharPosition >= (ReadLineInstance.CharBufferSize - 2))
                        {
                            if (currentInstance.CharPosition >= ReadLineInstance.CharBufferSize ||
                                char.IsLowSurrogate(keyInfo.KeyChar))
                            {
                                processInput = true;
                            }
                        }

                        if (processInput)
                        {
                            pendingKeyInfo = keyInfo;
                        }
                        else
                        {
                            goto ProcessKeyInfo;
                        }
                    }
                }
            }

            if (processInput)
            {// Process input
                string? result;
                using (this.syncObject.EnterScope())
                {
                    result = currentInstance.ProcessInput(keyInfo, currentInstance.CharBuffer.AsSpan(0, currentInstance.CharPosition));
                    if (result is not null)
                    {
                        result = ProcessTextInputHook(result);
                        if (result is null)
                        {// Rejected
                            continue;
                        }
                    }

                    currentInstance.CharPosition = 0;
                    if (result is not null)
                    {
                        inputResult = new(result);
                        goto CompleteInstance;
                    }
                }

                if (pendingKeyInfo.Key != ConsoleKey.None)
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
            {
                var codePoint = char.ConvertToUtf32(c, text[i + 1]);
                width = SimplePromptHelper.GetCharWidth(codePoint);
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
        var written = 0;
        ReadOnlySpan<char> span;

        span = ConsoleHelper.SetCursorSpan;
        span.CopyTo(buffer);
        buffer = buffer.Slice(span.Length);
        written += span.Length;

        var x = cursorTop + 1;
        var y = cursorLeft + 1;
        x.TryFormat(buffer, out var w, default, CultureInfo.InvariantCulture);
        buffer = buffer.Slice(w);
        written += w;
        buffer[0] = ';';
        buffer = buffer.Slice(1);
        written += 1;
        y.TryFormat(buffer, out w, default, CultureInfo.InvariantCulture);
        buffer = buffer.Slice(w);
        written += w;
        buffer[0] = 'H';
        buffer = buffer.Slice(1);
        written += 1;

        if (cursorOperation == CursorOperation.Show)
        {
            span = ConsoleHelper.ShowCursorSpan;
            span.CopyTo(buffer);
            buffer = buffer.Slice(span.Length);
            written += span.Length;
        }
        else if (cursorOperation == CursorOperation.Hide)
        {
            span = ConsoleHelper.HideCursorSpan;
            span.CopyTo(buffer);
            buffer = buffer.Slice(span.Length);
            written += span.Length;
        }

        // this.UnderlyingTextWriter.Write(windowBuffer.AsSpan(0, written));
        this.RawConsole.WriteInternal(windowBuffer.AsSpan(0, written));
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
            // this.CheckCursor();
            if (!this.TryGetActiveInstance(out var activeInstance))
            {
                this.WriteInternal(message, newLine, color);

                // this.CheckCursor();
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

            // this.CheckCursor();
        }
    }

    internal void ClearRow(int top)
    {
        if (top < 0 || top >= this._windowHeight)
        {
            return;
        }

        ReadOnlySpan<char> span;
        var windowBuffer = SimpleConsole.RentWindowBuffer();
        var buffer = windowBuffer.AsSpan();
        var written = 0;

        var moveCursor = this._cursorTop != top || this._cursorLeft != 0;
        if (moveCursor)
        {
            // Save cursor
            span = ConsoleHelper.SaveCursorSpan;
            span.CopyTo(buffer);
            written += span.Length;
            buffer = buffer.Slice(span.Length);

            // Move cursor
            span = ConsoleHelper.SetCursorSpan;
            span.CopyTo(buffer);
            buffer = buffer.Slice(span.Length);
            written += span.Length;

            var x = top + 1;
            var y = 0 + 1;
            int w;
            x.TryFormat(buffer, out w, default, CultureInfo.InvariantCulture);
            buffer = buffer.Slice(w);
            written += w;
            buffer[0] = ';';
            buffer = buffer.Slice(1);
            written += 1;
            y.TryFormat(buffer, out w, default, CultureInfo.InvariantCulture);
            buffer = buffer.Slice(w);
            written += w;
            buffer[0] = 'H';
            buffer = buffer.Slice(1);
            written += 1;
        }

        // Erase entire line
        span = ConsoleHelper.EraseEntireLineSpan;
        span.CopyTo(buffer);
        written += span.Length;
        buffer = buffer.Slice(span.Length);

        if (moveCursor)
        {// Restore cursor
            span = ConsoleHelper.RestoreCursorSpan;
            span.CopyTo(buffer);
            written += span.Length;
            buffer = buffer.Slice(span.Length);
        }

        this.RawConsole.WriteInternal(windowBuffer.AsSpan(0, written));
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
                this.simpleArrange.Arrange(newCursor, false);
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
            this.AdvanceCursor([], true);
            this.RawConsole.WriteInternal(ConsoleHelper.EraseEntireLineAndNewLineSpan);
        }

        var windowBuffer = SimpleConsole.RentWindowBuffer();
        var span = windowBuffer.AsSpan();

        if (color >= 0)
        {
            var temp = ConsoleHelper.GetForegroundColorEscapeCode(color);
            temp.CopyTo(span);
            span = span.Slice(temp.Length);
        }

        // SimplePromptHelper.TryCopy(ConsoleHelper.HideCursorSpan, ref span);

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
            if (!SimplePromptHelper.TryCopy(text, ref span))
            {
                break;
            }

            if (appendNewLine)
            {
                if (!SimplePromptHelper.TryCopy(ConsoleHelper.EraseToEndOfLineAndNewLineSpan, ref span))
                {
                    break;
                }
            }
            else
            {
                if (!SimplePromptHelper.TryCopy(ConsoleHelper.EraseToEndOfLineSpan, ref span))
                {
                    break;
                }
            }

            this.AdvanceCursor(text, appendNewLine);
        }

        if (color >= 0)
        {
            SimplePromptHelper.TryCopy(ConsoleHelper.ResetSpan, ref span);
        }

        // SimplePromptHelper.TryCopy(ConsoleHelper.ShowCursorSpan, ref span);

        // this.UnderlyingTextWriter.Write(windowBuffer.AsSpan(0, windowBuffer.Length - span.Length)); // Alternative
        this.RawConsole.WriteInternal(windowBuffer.AsSpan(0, windowBuffer.Length - span.Length));
        SimpleConsole.ReturnWindowBuffer(windowBuffer);
    }

    private void Initialize()
    {
        try
        {
            Console.SetOut(this.simpleTextWriter);
        }
        catch
        {
        }

        try
        {
            Console.SetIn(this.simpleTextReader);
        }
        catch
        {
        }

        (this._cursorLeft, this._cursorTop) = Console.GetCursorPosition();
        this.PrepareWindow();
    }

    private static bool IsControl(ConsoleKeyInfo keyInfo)
    {
        if (keyInfo.KeyChar == 0)
        {
            return true;
        }
        else if (keyInfo.Modifiers.HasFlag(ConsoleModifiers.Control) ||
            keyInfo.Modifiers.HasFlag(ConsoleModifiers.Alt))
        {
            return true;
        }
        else if (keyInfo.Key == ConsoleKey.Enter ||
            keyInfo.Key == ConsoleKey.Backspace ||
            keyInfo.Key == ConsoleKey.Escape)
        {
            return true;
        }
        else if (keyInfo.Key == ConsoleKey.Tab)
        {
            return true;
        }

        return false;
    }
}
