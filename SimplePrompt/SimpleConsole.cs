// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using System;
using System.Buffers;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Arc.Threading;
using Arc.Unit;
using SimplePrompt.Internal;

#pragma warning disable SA1204 // Static elements should appear before instance elements

namespace SimplePrompt;

/// <summary>
/// Provides a simple console interface with advanced input handling capabilities including multiline support and custom prompts.
/// This class implements <see cref="IConsoleService"/> and manages console input/output operations.
/// </summary>
#pragma warning disable CA1001 // Types that own disposable fields should be disposable
public partial class SimpleConsole : IConsoleService
#pragma warning restore CA1001 // Types that own disposable fields should be disposable
{
    private const int DelayInMilliseconds = 10;
    private const int WindowBufferSize = 32 * 1024;
    private const int InitialWindowWidth = 120;
    private const int InitialWindowHeight = 30;
    private const int MinimumWindowWidth = 30;
    private const int MinimumWindowHeight = 10;

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

    internal int WindowWidth { get; private set; }

    internal int WindowHeight { get; private set; }

    internal int CursorLeft { get; set; }

    internal int CursorTop { get; set; }

    private readonly SimpleTextWriter simpleTextWriter;
    private readonly SimpleArrange simpleArrange;
    private readonly ConcurrentQueue<string?> queue = new();

    private readonly Lock syncObject = new();
    private List<ReadLineInstance> instanceList = [];

    #endregion

    private SimpleConsole()
    {
        Console.OutputEncoding = System.Text.Encoding.UTF8;
        this.simpleTextWriter = new(this, Console.Out);
        this.RawConsole = new(this);
        this.simpleArrange = new(this);
        this.DefaultOptions = new();

        this.PrepareWindow();
        this.SyncCursor();



        try
        {
#pragma warning disable CA1416 // Validate platform compatibility
            _ = PosixSignalRegistration.Create(PosixSignal.SIGWINCH, _ =>
            {
                using (this.syncObject.EnterScope())
                {// Adjusts the cursor position when attached to a console.
                    if (this.instanceList.Count > 0)
                    {
                        this.AdjustWindow(this.instanceList[^1], true);
                    }
                }
            });

            /*_ = PosixSignalRegistration.Create(PosixSignal.SIGWINCH, _ =>
            {
                // Console.WriteLine($"SIGWINCH Height:{Console.WindowHeight} Width:{Console.WindowWidth} Top:{Console.CursorTop}");

                using (this.syncObject.EnterScope())
                {// Adjusts the cursor position when attached to a console.
                    var newCursor = Console.GetCursorPosition();
                    this.simpleArrange.Arrange(newCursor);
                    (this.CursorLeft, this.CursorTop) = newCursor;

                    if (this.instanceList.Count > 0)
                    {
                        var currentInstance = this.instanceList[^1];
                        if (currentInstance.CorrectCursorTop())
                        {// Since the cursor position has been corrected, redraw the prompt.
                            this.Clear(false);
                            // currentInstance.Redraw();
                            // currentInstance.CurrentLocation.Restore(CursorOperation.None);
                        }
                    }

                }
            });*/
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
    public async Task<InputResult> ReadLine(ReadLineOptions? options = default, CancellationToken cancellationToken = default)
    {
        InputResultKind inputResultKind;
        ReadLineInstance currentInstance;
        using (this.syncObject.EnterScope())
        {
            // Prepare the window, and if the cursor is in the middle of a line, insert a newline.
            this.PrepareWindow();
            this.CheckCursor();
            if (this.instanceList.Count > 0)
            {
                this.instanceList[^1].CurrentLocation.CursorLast();
            }

            if (this.CursorLeft > 0)
            {
                this.UnderlyingTextWriter.WriteLine();
                this.NewLineCursor();
            }

            // Create and prepare a ReadLineInstance.
            currentInstance = ReadLineInstance.Rent(this, options ?? this.DefaultOptions);
            this.instanceList.Add(currentInstance);
            currentInstance.Prepare();
            this.CheckCursor();
        }

        try
        {
            var delayFlag = false;
            var position = 0;
            ConsoleKeyInfo keyInfo = default;
            ConsoleKeyInfo pendingKeyInfo = default;
            while (true)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (this.Core.IsTerminated)
                {// Terminated
                    inputResultKind = InputResultKind.Terminated;
                    goto CancelOrTerminate;
                }

                if (delayFlag)
                {
                    delayFlag = false;
                    await Task.Delay(DelayInMilliseconds, cancellationToken).ConfigureAwait(false);
                }

                using (this.syncObject.EnterScope())
                {
                    var idx = this.instanceList.IndexOf(currentInstance);
                    if (idx < 0)
                    {// Not found
                        return new(InputResultKind.Terminated);
                    }
                    else if (idx != (this.instanceList.Count - 1))
                    {// Not active instance
                        delayFlag = true;
                        continue;
                    }

                    // Active instance: Prepare window and read key input.
                    this.AdjustWindow(currentInstance, false);

                    if (!this.queue.IsEmpty &&
                        currentInstance.IsEmptyInput() &&
                        this.queue.TryDequeue(out var queuedMessage))
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
                                    return new(result);
                                }
                            }
                            else
                            {
                                currentInstance.ProcessInput(keyInfo, charSpan);
                            }
                        }
                        while (queuedSpan.Length > 0);
                    }

                    if (!this.RawConsole.TryRead(out keyInfo))
                    {
                        delayFlag = true;
                        continue;
                    }
                }

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
                    inputResultKind = InputResultKind.Canceled;
                    goto CancelOrTerminate;
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
                        inputResultKind = InputResultKind.Canceled;
                        goto CancelOrTerminate;
                    }
                }

                bool processInput = true;
                if (IsControl(keyInfo))
                {// Control
                }
                else
                {// Not control
                    currentInstance.CharBuffer[position++] = keyInfo.KeyChar;
                    using (this.syncObject.EnterScope())
                    {
                        if (this.RawConsole.TryRead(out keyInfo))
                        {
                            processInput = false;
                            if (position >= (ReadLineInstance.CharBufferSize - 2))
                            {
                                if (position >= ReadLineInstance.CharBufferSize ||
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
                        result = currentInstance.ProcessInput(keyInfo, currentInstance.CharBuffer.AsSpan(0, position));
                        if (result is not null)
                        {
                            result = ProcessTextInputHook(result);
                            if (result is null)
                            {// Rejected
                                continue;
                            }
                        }

                        position = 0;
                        if (result is not null)
                        {
                            return new(result);
                        }
                    }

                    if (pendingKeyInfo.Key != ConsoleKey.None)
                    {// Process pending key input.
                        keyInfo = pendingKeyInfo;
                        goto ProcessKeyInfo;
                    }
                }
            }
        }
        finally
        {
            using (this.syncObject.EnterScope())
            {
                currentInstance.CurrentLocation.MoveToEnd();
                this.UnderlyingTextWriter.WriteLine();
                this.NewLineCursor();

                this.RemoveInstance(currentInstance);
            }

            ReadLineInstance.Return(currentInstance);
        }

CancelOrTerminate:
        return new(inputResultKind);

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

    /// <summary>
    /// Clears the console display or buffer, depending on the specified parameter.
    /// </summary>
    /// <param name="clearBuffer">
    /// If <see langword="true"/>, clears the entire console buffer and resets the cursor position to the top-left corner.
    /// If <see langword="false"/>, clears only the visible console area and resets the cursor position to the top-left corner.
    /// </param>
    public void Clear(bool clearBuffer)
    {
        ReadLineInstance? activeInstance;
        using (this.syncObject.EnterScope())
        {
            if (clearBuffer)
            {
                Console.Clear();
                this.CursorTop = 0;
                this.CursorLeft = 0;
            }
            else
            {

                /*if (this.TryGetActiveInstance(out activeInstance))
                {
                    activeInstance.CurrentLocation.CursorFirst();
                    this.RawConsole.WriteInternal("\e[0J");
                }*/

                this.RawConsole.WriteInternal($"\e[2J");
                this.SetCursorPosition(0, 0, CursorOperation.None);
            }

            if (this.TryGetActiveInstance(out activeInstance))
            {
                activeInstance.Redraw();
                activeInstance.CurrentLocation.Restore(CursorOperation.None);
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
        this.queue.Enqueue(message);
    }

    Task<InputResult> IConsoleService.ReadLine(CancellationToken cancellationToken)
        => this.ReadLine(default, cancellationToken);

    /// <summary>
    /// Writes the specified message to the console without a newline.<br/>
    /// Note that while <b>ReadLine()</b> is waiting for input, messages will not be displayed.
    /// </summary>
    /// <param name="message">The message to write. If null, nothing is written.</param>
    public void Write(string? message)
    {
        using (this.syncObject.EnterScope())
        {
            if (!this.IsReadLineInProgress)
            {
                this.CheckCursor();

                this.WriteInternal(message, false);

                this.CheckCursor();
            }
        }
    }

    /// <summary>
    /// Writes the specified message to the console followed by a newline.<br/>
    /// If a <see cref="ReadLine(ReadLineOptions?, CancellationToken)"/> operation is in progress,<br/>
    /// the message is written with proper cursor management and the active input instance is redrawn.
    /// </summary>
    /// <param name="message">
    /// The message to write. If <c>null</c>, only a newline is written.
    /// </param>
    public void WriteLine(string? message = null)
    {
        using (this.syncObject.EnterScope())
        {
            this.CheckCursor();
            if (!this.TryGetActiveInstance(out var activeInstance))
            {
                this.WriteInternal(message, true);

                this.CheckCursor();
                return;
            }

            activeInstance.ResetCursor(CursorOperation.Hide);

            this.WriteInternal(message, true);

            activeInstance.Redraw();
            activeInstance.CurrentLocation.Restore(CursorOperation.Show);

            this.CheckCursor();
        }
    }

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

    [Conditional("DEBUG")]
    internal void CheckCursor()
    {
        /*try
        {
            if (this.RawConsole.UseStdin)
            {// With Interop.Sys.Write(), changes are not applied immediately, so the cursor position cannot be retrieved.
                return;
            }

            var cursor = Console.GetCursorPosition();
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
        }*/
    }

    internal void AdvanceCursor(ReadOnlySpan<char> text, bool newLine)
    {
        var left = this.CursorLeft;
        var top = this.CursorTop;
        var windowWidth = this.WindowWidth;
        var windowHeight = this.WindowHeight;

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

            if (left + width >= windowWidth)
            {
                left += width - windowWidth;
                top++;
            }
            else
            {
                left += width;
            }
        }

Exit:
        if (newLine)
        {
            left = 0;
            top++;
        }

        this.CursorLeft = left;
        this.CursorTop = top;

        // Scroll if needed.
        var scroll = top - windowHeight + 1;
        if (scroll > 0)
        {
            this.Scroll(scroll, true);
        }
    }

    internal void NewLineCursor()
    {
        this.CursorLeft = 0;
        this.CursorTop++;

        // Scroll if needed.
        var scroll = this.CursorTop - this.WindowHeight + 1;
        if (scroll > 0)
        {
            this.Scroll(scroll, true);
        }
    }

    internal void Scroll(int scroll, bool moveCursor)
    {
        if (moveCursor)
        {
            this.CursorTop -= scroll;
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
        if (cursorLeft > (this.WindowWidth - 1))
        {
            cursorLeft = this.WindowWidth - 1;
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

        // this.UnderlyingTextWriter.Write(windowBuffer.AsSpan(0, written)); // coi
        this.RawConsole.WriteInternal(windowBuffer.AsSpan(0, written));
        SimpleConsole.ReturnWindowBuffer(windowBuffer);

        this.CursorLeft = cursorLeft;
        this.CursorTop = cursorTop;
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

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal void SyncCursor()
    {
        (this.CursorLeft, this.CursorTop) = Console.GetCursorPosition();
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

    private void WriteInternal(ReadOnlySpan<char> message, bool newLine)
    {
        var windowBuffer = SimpleConsole.RentWindowBuffer();
        var span = windowBuffer.AsSpan();

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

        // SimplePromptHelper.TryCopy(ConsoleHelper.ShowCursorSpan, ref span);

        // this.UnderlyingTextWriter.Write(windowBuffer.AsSpan(0, windowBuffer.Length - span.Length)); // Alternative
        this.RawConsole.WriteInternal(windowBuffer.AsSpan(0, windowBuffer.Length - span.Length));
        SimpleConsole.ReturnWindowBuffer(windowBuffer);
    }

    private void Initialize()
    {
        Console.SetOut(this.simpleTextWriter);
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

    private bool PrepareWindow()
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

        if (windowWidth == this.WindowWidth &&
            windowHeight == this.WindowHeight)
        {
            return false;
        }

        this.WindowWidth = windowWidth;
        this.WindowHeight = windowHeight;
        return true;
    }

    private void AdjustWindow(ReadLineInstance activeInstance, bool forceArrange)
    {
        this.simpleArrange.Set(activeInstance);

        if (!this.PrepareWindow() &&
            !forceArrange)
        {// Window size not changed
            return;
        }

        // Window size changed
        var newCursor = Console.GetCursorPosition();
        this.simpleArrange.Arrange(newCursor);
        // (this.CursorLeft, this.CursorTop) = newCursor;
    }

    internal void ClearRow(int top)
    {
        if (top < 0 || top >= this.WindowHeight)
        {
            return;
        }

        ReadOnlySpan<char> span;
        var windowBuffer = SimpleConsole.RentWindowBuffer();
        var buffer = windowBuffer.AsSpan();
        var written = 0;

        var moveCursor = this.CursorTop != top || this.CursorLeft != 0;
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
}
