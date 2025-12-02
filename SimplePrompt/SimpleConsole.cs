// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using System;
using System.Buffers;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;
using Arc;
using Arc.Collections;
using Arc.Threading;
using Arc.Unit;
using SimplePrompt.Internal;

#pragma warning disable SA1204 // Static elements should appear before instance elements

namespace SimplePrompt;

/// <summary>
/// Provides a simple console interface with advanced input handling capabilities including multiline support and custom prompts.
/// This class implements <see cref="IConsoleService"/> and manages console input/output operations.
/// </summary>
public partial class SimpleConsole : IConsoleService
{
    /// <summary>
    /// Represents a method that handles key input events during console read operations.
    /// </summary>
    /// <param name="keyInfo">The <see cref="ConsoleKeyInfo"/> containing information about the pressed key.</param>
    /// <returns>
    /// <see langword="true"/> to indicate the key input was handled and should not be processed further;
    /// <see langword="false"/> to allow normal processing of the key input.
    /// </returns>
    public delegate bool KeyInputHook(ConsoleKeyInfo keyInfo);

    /// <summary>
    /// Represents a method that handles text input validation or transformation after the user submits input.
    /// </summary>
    /// <param name="text">The input text submitted by the user.</param>
    /// <returns>
    /// The validated or transformed text to be returned as the final result.
    /// If <see langword="null"/> is returned, the input is rejected and the user can continue editing.
    /// </returns>
    public delegate string? TextInputHook(string text);

    private const int DelayInMilliseconds = 10;
    private const int WindowBufferSize = 32 * 1024;

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

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static bool TryCopy(ReadOnlySpan<char> source, ref Span<char> destination)
    {
        if (source.Length > destination.Length)
        {
            return false;
        }

        source.CopyTo(destination);
        destination = destination.Slice(source.Length);
        return true;
    }

    internal static char[] RentWindowBuffer()
        => ArrayPool<char>.Shared.Rent(WindowBufferSize);

    internal static void ReturnWindowBuffer(char[] buffer)
        => ArrayPool<char>.Shared.Return(buffer);

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

    internal SimpleLocation Location { get; }

    private readonly SimpleTextWriter simpleTextWriter;
    private readonly ObjectPool<ReadLineInstance> instancePool;
    private readonly ObjectPool<ReadLineBuffer> bufferPool;

    private readonly Lock syncObject = new();
    private List<ReadLineInstance> instanceList = [];

    private SimpleConsole()
    {
        this.simpleTextWriter = new(this, Console.Out);
        this.RawConsole = new(this);
        this.Location = new(this);
        this.instancePool = new(() => new ReadLineInstance(this), 4);
        this.bufferPool = new(() => new ReadLineBuffer(this), 32);
        this.DefaultOptions = new();
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
        ReadLineInstance currentInstance;
        using (this.syncObject.EnterScope())
        {
            // Prepare the window, and if the cursor is in the middle of a line, insert a newline.
            this.PrepareWindow(default);
            (this.CursorLeft, this.CursorTop) = Console.GetCursorPosition();
            if (this.CursorLeft > 0)
            {
                this.UnderlyingTextWriter.WriteLine();
                this.CursorLeft = 0;
                if (this.CursorTop < this.WindowHeight - 1)
                {
                    this.CursorTop++;
                }
            }

            // Create and prepare a ReadLineInstance.
            currentInstance = this.RentInstance(options ?? this.DefaultOptions);
            this.instanceList.Add(currentInstance);
            currentInstance.Prepare();
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
                    this.UnderlyingTextWriter.WriteLine();
                    this.CursorTop++;
                    return new(InputResultKind.Terminated);
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
                    this.PrepareWindow(currentInstance);
                    if (!this.RawConsole.TryRead(out keyInfo))
                    {
                        delayFlag = true;
                        continue;
                    }

                    this.Location.CorrectCursorTop(currentInstance);//
                }

ProcessKeyInfo:
//(this.CursorLeft, this.CursorTop) = Console.GetCursorPosition();//
                this.Location.Invalidate();
                if (keyInfo.KeyChar == '\n' ||
                    keyInfo.Key == ConsoleKey.Enter)
                {
                    keyInfo = SimplePromptHelper.EnterKeyInfo;
                }
                else if (keyInfo.KeyChar == '\t' ||
                    keyInfo.Key == ConsoleKey.Tab)
                {// Tab -> Space; in the future, input completion.
                 // keyInfo = SimplePromptHelper.SpaceKeyInfo;
                }
                else if (keyInfo.KeyChar == '\r')
                {// CrLf -> Lf
                    continue;
                }
                else if (currentInstance.Options.CancelOnEscape &&
                    keyInfo.Key == ConsoleKey.Escape)
                {
                    this.UnderlyingTextWriter.WriteLine();
                    this.CursorTop++;
                    return new(InputResultKind.Canceled);
                }

                if (currentInstance.Options.KeyInputHook is not null &&
                    currentInstance.Options.KeyInputHook(keyInfo))
                {// Handled by the hook delegate.
                    continue;
                }

                /*else if (keyInfo.Key == ConsoleKey.C &&
                    keyInfo.Modifiers.HasFlag(ConsoleModifiers.Control))
                { // Ctrl+C
                    ThreadCore.Root.Terminate(); // Send a termination signal to the root.
                    return null;
                }*/

                bool flush = true;
                if (IsControl(keyInfo))
                {// Control
                }
                else
                {// Not control
                    currentInstance.CharBuffer[position++] = keyInfo.KeyChar;
                    if (this.RawConsole.TryRead(out keyInfo))
                    {
                        flush = false;
                        if (position >= (ReadLineInstance.CharBufferSize - 2))
                        {
                            if (position >= ReadLineInstance.CharBufferSize ||
                                char.IsLowSurrogate(keyInfo.KeyChar))
                            {
                                flush = true;
                            }
                        }

                        if (flush)
                        {
                            pendingKeyInfo = keyInfo;
                        }
                        else
                        {
                            goto ProcessKeyInfo;
                        }
                    }
                }

                if (flush)
                {// Flush
                    string? result;
                    using (this.syncObject.EnterScope())
                    {
                        result = currentInstance.Flush(keyInfo, currentInstance.CharBuffer.AsSpan(0, position));
                        if (result is not null &&
                            currentInstance.Options.TextInputHook is not null)
                        {
                            result = currentInstance.Options.TextInputHook(result);
                            if (result is null)
                            {// Rejected by the hook delegate.
                                this.UnderlyingTextWriter.WriteLine();
                                this.CursorTop++;
                                currentInstance.Reset();
                                currentInstance.Redraw(true);
                                var buffer = currentInstance.BufferList[currentInstance.BufferList.Count - 1];
                                var cursor = buffer.ToCursor(0);
                                this.SetCursorPosition(cursor.Left, buffer.Top + cursor.Top, CursorOperation.None);
                                continue;
                            }
                        }
                    }

                    position = 0;
                    if (result is not null)
                    {
                        this.UnderlyingTextWriter.WriteLine();
                        this.CursorTop++;
                        return new(result);
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
            this.RemoveInstance(currentInstance);
            this.ReturnInstance(currentInstance);
        }
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
                this.WriteInternal(message, false);
                return;
            }
        }
    }

    public void WriteLine(string? message = null)
    {
        try
        {
            using (this.syncObject.EnterScope())
            {
                if (!this.TryGetActiveInstance(out var activeInstance))
                {
                    this.WriteInternal(message, true);
                    // (this.CursorLeft, this.CursorTop) = Console.GetCursorPosition(); // Alternative
                    return;
                }

                this.Location.CorrectCursorTop(activeInstance);//
                activeInstance.PrepareLocation();
                activeInstance.SetCursorAtFirst(CursorOperation.Hide);
                this.WriteInternal(message, true);
                activeInstance.Redraw(false);

                var buffer = activeInstance.BufferList[activeInstance.BufferIndex];
                var cursor = buffer.ToCursor(activeInstance.BufferPosition);
                this.SetCursorPosition(cursor.Left, buffer.Top + cursor.Top, CursorOperation.Show);
            }
        }
        catch
        {
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

    internal void PrepareCursor()
    {
        if (this.CursorLeft < 0)
        {
            this.CursorLeft = 0;
        }
        else if (this.CursorLeft >= this.WindowWidth)
        {
            this.CursorLeft = this.WindowWidth - 1;
        }

        if (this.CursorTop < 0)
        {
            this.CursorTop = 0;
        }
        else if (this.CursorTop >= this.WindowHeight)
        {
            this.CursorTop = this.WindowHeight - 1;
        }
    }

    internal ReadLineInstance RentInstance(ReadLineOptions options)
    {
        var obj = this.instancePool.Rent();
        obj.Initialize(options);
        return obj;
    }

    internal void ReturnInstance(ReadLineInstance obj)
        => this.instancePool.Return(obj);

    internal ReadLineBuffer RentBuffer(ReadLineInstance @instance, int index, string? prompt)
    {
        var obj = this.bufferPool.Rent();
        obj.Initialize(@instance, index, prompt);
        return obj;
    }

    internal void ReturnBuffer(ReadLineBuffer obj)
        => this.bufferPool.Return(obj);

    internal void MoveCursor(int width, bool newLine)
    {
        this.CursorLeft += width;
        var h = this.CursorLeft >= 0 ?
            (this.CursorLeft / this.WindowWidth) :
            (((this.CursorLeft - 1) / this.WindowWidth) - 1);
        this.CursorLeft -= h * this.WindowWidth;
        this.CursorTop += h;

        if (newLine && this.CursorLeft > 0)
        {
            this.CursorLeft = 0;
            this.CursorTop++;
        }

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
            foreach (var y in activeInstance.BufferList)
            {
                y.Top -= scroll;
            }
        }
    }

    internal void SetCursor(ReadLineBuffer buffer)
    {
        var cursorLeft = buffer.PromtWidth;
        var cursorTop = buffer.Top;
        this.SetCursorPosition(cursorLeft, cursorTop, CursorOperation.None);
    }

    internal void SetCursorPosition(int cursorLeft, int cursorTop, CursorOperation cursorOperation)
    {// Move and show cursor.
        /*if (this.CursorLeft == cursorLeft &&
            this.CursorTop == cursorTop)
        {
            return;
        }*/

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
        x.TryFormat(buffer, out var w);
        buffer = buffer.Slice(w);
        written += w;
        buffer[0] = ';';
        buffer = buffer.Slice(1);
        written += 1;
        y.TryFormat(buffer, out w);
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

    private void RemoveInstance(ReadLineInstance target)
    {
        using (this.syncObject.EnterScope())
        {
            target.Clear();
            this.instanceList.Remove(target);

            if (this.TryGetActiveInstance(out var activeInstance))
            {
                activeInstance.Restore();
                activeInstance.SetCursorAtFirst(CursorOperation.Hide);
                activeInstance.Redraw(false);

                if (activeInstance.BufferIndex < activeInstance.EditableBufferIndex)
                {
                    activeInstance.BufferIndex = activeInstance.EditableBufferIndex;
                    activeInstance.BufferPosition = 0;
                }

                if (activeInstance.BufferPosition > activeInstance.BufferList[activeInstance.BufferIndex].Width)
                {
                    activeInstance.BufferPosition = activeInstance.BufferList[activeInstance.BufferIndex].Width;
                }

                var buffer = activeInstance.BufferList[activeInstance.BufferIndex];
                var cursor = buffer.ToCursor(activeInstance.BufferPosition);
                this.SetCursorPosition(cursor.Left, buffer.Top + cursor.Top, CursorOperation.Show);
            }
        }
    }

    private void WriteInternal(ReadOnlySpan<char> message, bool newLine)
    {
        var windowBuffer = SimpleConsole.RentWindowBuffer();
        var span = windowBuffer.AsSpan();
        // var height = 0;

        while (message.Length > 0)
        {
            ReadOnlySpan<char> text;
            var i = message.IndexOf('\n');
            if (i > 0 && message[i - 1] == '\r')
            {// text\r\n
                text = message.Slice(0, i - 1);
                message = message.Slice(i + 1);
            }
            else if (i >= 0)
            {// text\n
                text = message.Slice(0, i);
                message = message.Slice(i + 1);
            }
            else
            {// text
                text = message;
                message = default;
            }

            // Text
            if (!TryCopy(text, ref span))
            {
                break;
            }

            if (newLine)
            {
                if (!TryCopy(ConsoleHelper.EraseToEndOfLineAndNewLineSpan, ref span))
                {
                    break;
                }
            }
            else
            {
                if (!TryCopy(ConsoleHelper.EraseToEndOfLineSpan, ref span))
                {
                    break;
                }
            }

            this.MoveCursor(SimplePromptHelper.GetWidth(text), newLine);
        }

        // this.UnderlyingTextWriter.Write(windowBuffer.AsSpan(0, windowBuffer.Length - span.Length)); // Alternative
        this.RawConsole.WriteInternal(windowBuffer.AsSpan(0, windowBuffer.Length - span.Length));
        SimpleConsole.ReturnWindowBuffer(windowBuffer);
    }

    private void Initialize()
    {
        Console.OutputEncoding = System.Text.Encoding.UTF8;
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

    private void PrepareWindow(ReadLineInstance? activeInstance)
    {
        var windowWidth = 120;
        var windowHeight = 30;

        try
        {
            windowWidth = Console.WindowWidth;
            windowHeight = Console.WindowHeight;
        }
        catch
        {
        }

        if (windowWidth <= 0)
        {
            windowWidth = 1;
        }

        if (windowHeight <= 0)
        {
            windowHeight = 1;
        }

        if (windowWidth == this.WindowWidth &&
            windowHeight == this.WindowHeight)
        {// Window size not changed
            if (activeInstance is not null)
            {
                this.Location.Update(activeInstance);
            }

            return;
        }

        // Window size changed
        this.WindowWidth = windowWidth;
        this.WindowHeight = windowHeight;

        if (activeInstance is not null)
        {
            // this.Location.Redraw();

            var newCursor = Console.GetCursorPosition();
            this.Location.RearrangeBuffers(newCursor);
            (this.CursorLeft, this.CursorTop) = newCursor;
        }
    }
}
