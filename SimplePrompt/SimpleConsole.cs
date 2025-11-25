// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using System.Buffers;
using System.Runtime.CompilerServices;
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
    private const int DelayInMilliseconds = 10;
    private const int WindowBufferSize = 64 * 1024;

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

    public static char[] RentWindowBuffer()
        => ArrayPool<char>.Shared.Rent(WindowBufferSize);

    public static void ReturnWindowBuffer(char[] buffer)
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

    public TextWriter UnderlyingTextWriter => this.simpleTextWriter.UnderlyingTextWriter;

    public bool IsReadLineInProgress => this.instanceArray.Length > 0;

    internal RawConsole RawConsole { get; }

    internal int WindowWidth { get; private set; }

    internal int WindowHeight { get; private set; }

    internal int CursorLeft { get; set; }

    internal int CursorTop { get; set; }

    private readonly SimpleTextWriter simpleTextWriter;
    private readonly ObjectPool<ReadLineInstance> instancePool;
    private readonly ObjectPool<ReadLineBuffer> bufferPool;

    private readonly Lock syncInstanceArray = new();
    private ReadLineInstance[] instanceArray = [];

    private SimpleConsole()
    {
        this.simpleTextWriter = new(this, Console.Out);
        this.RawConsole = new(this);
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
        // Create and prepare a ReadLineInstance.
        var currentInstance = this.RentInstance(options ?? this.DefaultOptions);
        currentInstance.Prepare();
        using (this.syncInstanceArray.EnterScope())
        {
            this.instanceArray = [.. this.instanceArray, currentInstance];
        }

        var position = 0;
        ConsoleKeyInfo pendingKeyInfo = default;
        while (!this.Core.IsTerminated)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var array = this.instanceArray;
            var idx = array.IndexOf(currentInstance);
            if (idx < 0)
            {// Not found
                return new(InputResultKind.Terminated);
            }
            else if (idx != (array.Length - 1))
            {// Not active instance
                await Task.Delay(DelayInMilliseconds, cancellationToken);
                continue;
            }

            if (this.PrepareWindow())
            {
                currentInstance.PrepareWindow();
            }

            // Polling isn’t an ideal approach, but due to the fact that the normal method causes a significant performance drop and that the function must be able to exit when the application terminates, this implementation was chosen.
            if (!this.RawConsole.TryRead(out var keyInfo))
            {
                await Task.Delay(DelayInMilliseconds, cancellationToken);
                continue;
            }

            currentInstance.ProcessKeyInfo(keyInfo);

ProcessKeyInfo:
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
                currentInstance.Clear();
                return new(InputResultKind.Canceled);
            }

            /*else if (keyInfo.Key == ConsoleKey.C &&
                keyInfo.Modifiers.HasFlag(ConsoleModifiers.Control))
            { // Ctrl+C
                ThreadCore.Root.Terminate(); // Send a termination signal to the root.
                return null;
            }*/

            /*if (keyInfo.Key == ConsoleKey.F1)
            {
                this.WriteLine("Inserted text");
                continue;
            }
            else if (keyInfo.Key == ConsoleKey.F2)
            {
                this.WriteLine("Text1\nText2");
                continue;
            }*/

            bool flush = true;
            if (IsControl(keyInfo))
            {// Control
            }
            else
            {// Not control
                this.charBuffer[position++] = keyInfo.KeyChar;
                if (this.RawConsole.TryRead(out keyInfo))
                {
                    flush = false;
                    if (position >= (CharBufferSize - 2))
                    {
                        if (position >= CharBufferSize ||
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
                var result = this.Flush(keyInfo, this.charBuffer.AsSpan(0, position));
                position = 0;
                if (result is not null)
                {
                    this.UnderlyingTextWriter.WriteLine();
                    this.Clear();
                    return new(result);
                }

                if (pendingKeyInfo.Key != ConsoleKey.None)
                {// Process pending key input.
                    keyInfo = pendingKeyInfo;
                    goto ProcessKeyInfo;
                }
            }
        }

        // Terminated
        // this.SetCursorPosition(this.WindowWidth - 1, this.CursorTop, true);
        this.UnderlyingTextWriter.WriteLine();
        this.Clear();
        return new(InputResultKind.Terminated);
    }

    Task<InputResult> IConsoleService.ReadLine(CancellationToken cancellationToken)
        => this.ReadLine(default, cancellationToken);

    void IConsoleService.Write(string? message)
    {
        using (this.lockObject.EnterScope())
        {
            if (this.buffers.Count == 0)
            {
                this.UnderlyingTextWriter.Write(message);
                return;
            }
        }

        /*if (Environment.NewLine == "\r\n" && message is not null)
        {
            message = Arc.BaseHelper.ConvertLfToCrLf(message);
        }

        try
        {
            this.UnderlyingTextWriter.Write(message);
        }
        catch
        {
        }*/
    }

    public void WriteLine(string? message = null)
    {
        /*if (Environment.NewLine == "\r\n" && message is not null)
        {
            message = Arc.BaseHelper.ConvertLfToCrLf(message);
        }*/

        try
        {
            using (this.lockObject.EnterScope())
            {
                if (!this.IsReadLineInProgress)
                {
                    this.WriteInternal(message);
                    (this.CursorLeft, this.CursorTop) = Console.GetCursorPosition();
                    return;
                }

                var location = this.GetLocation();

                this.SetCursorAtFirst(CursorOperation.Hide);
                this.WriteInternal(message);
                this.RedrawInternal();

                var buffer = this.buffers[location.BufferIndex];
                var cursor = buffer.ToCursor(location.CursorIndex);
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

    internal bool IsLengthWithinLimit(int dif)
    {
        var length = 0;
        var isFirst = true;
        for (var i = this.editableBufferIndex; i < this.buffers.Count; i++)
        {
            if (isFirst)
            {
                isFirst = false;
            }
            else
            {
                length += 1; // New line
            }

            length += this.buffers[i].Length;
        }

        return length + dif <= this.CurrentOptions.MaxInputLength;
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

    private void WriteInternal(ReadOnlySpan<char> message)
    {
        var windowBuffer = SimpleConsole.RentWindowBuffer();
        var span = windowBuffer.AsSpan();

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

            if (!TryCopy(ConsoleHelper.EraseToEndOfLineAndNewLineSpan, ref span))
            {
                break;
            }
        }

        this.RawConsole.WriteInternal(windowBuffer.AsSpan(0, windowBuffer.Length - span.Length));
        SimpleConsole.ReturnWindowBuffer(windowBuffer);
    }

    private void Initialize()
    {
        Console.OutputEncoding = System.Text.Encoding.UTF8;
        Console.SetOut(this.simpleTextWriter);
    }

    private void SetCursor(ReadLineBuffer buffer)
    {
        var cursorLeft = buffer.PromtWidth;
        var cursorTop = buffer.Top;
        this.SetCursorPosition(cursorLeft, cursorTop, CursorOperation.None);
    }

    private int SetCursorAtFirst(CursorOperation cursorOperation)
    {
        if (this.buffers.Count == 0)
        {
            return 0;
        }

        var buffer = this.buffers[0];
        var top = Math.Max(0, buffer.Top);
        this.SetCursorPosition(0, top, cursorOperation);
        return top;
    }

    private void SetCursorAtEnd(CursorOperation cursorOperation)
    {
        if (this.buffers.Count == 0)
        {
            return;
        }

        var buffer = this.buffers[this.buffers.Count - 1];
        var newCursor = buffer.ToCursor(buffer.Width);
        newCursor.Top += buffer.Top;
        this.SetCursorPosition(newCursor.Left, newCursor.Top, cursorOperation);
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

    internal bool PrepareWindow()
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
        {
            return false;
        }

        this.WindowWidth = windowWidth;
        this.WindowHeight = windowHeight;
        return true;
    }

    private void Prepare()
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

    private string? Flush(ConsoleKeyInfo keyInfo, Span<char> charBuffer)
    {
        this.Prepare();
        using (this.lockObject.EnterScope())
        {
            var buffer = this.PrepareAndFindBuffer();
            if (buffer is null)
            {
                return string.Empty;
            }

            if (buffer.ProcessInternal(keyInfo, charBuffer))
            {// Exit input mode and return the concatenated string.
                if (this.buffers.Count == 0)
                {
                    return string.Empty;
                }

                if (!string.IsNullOrEmpty(this.CurrentOptions.MultilineIdentifier) &&
                    (SimpleCommandLine.SimpleParserHelper.CountOccurrences(buffer.TextSpan, this.CurrentOptions.MultilineIdentifier) % 2) > 0)
                {// Multiple line
                    if (buffer.Index == this.editableBufferIndex)
                    {// Start
                        this.MultilineMode = true;
                    }
                    else
                    {// End
                        this.MultilineMode = false;
                    }
                }

                if (this.MultilineMode)
                {
                    if (buffer.Index == (this.buffers.Count - 1))
                    {// New InputBuffer
                        if (buffer.Length == 0)
                        {// Empty
                            return null;
                        }
                        else if (!this.IsLengthWithinLimit(1))
                        {// Exceeding max length
                            return null;
                        }

                        buffer = this.RentBuffer(this, this.buffers.Count, this.CurrentOptions.MultilinePrompt);
                        this.buffers.Add(buffer);
                        var previousTop = this.CursorTop;
                        this.UnderlyingTextWriter.WriteLine();
                        this.UnderlyingTextWriter.Write(this.CurrentOptions.MultilinePrompt);
                        (this.CursorLeft, this.CursorTop) = Console.GetCursorPosition();
                        if (this.CursorTop == previousTop)
                        {
                            this.Scroll(1, false);
                        }

                        return null;
                    }
                    else
                    {// Next buffer
                        this.SetCursor(this.buffers[buffer.Index + 1]);
                        return null;
                    }
                }

                var length = this.buffers[this.editableBufferIndex].Length;
                for (var i = this.editableBufferIndex + 1; i < this.buffers.Count; i++)
                {
                    length += 1 + this.buffers[i].Length;
                }

                var result = string.Create(length, this.buffers, (span, buffers) =>
                {
                    var isFirst = true;
                    for (var i = this.editableBufferIndex; i < buffers.Count; i++)
                    {
                        if (!isFirst)
                        {
                            span[0] = '\n';
                            span = span.Slice(1);
                        }
                        else
                        {
                            isFirst = false;
                        }

                        buffers[i].TextSpan.CopyTo(span);
                        span = span.Slice(buffers[i].Length);
                    }
                });

                this.SetCursorAtEnd(CursorOperation.None);
                return result;
            }
            else
            {
                return null;
            }
        }
    }

    private (int BufferIndex, int CursorIndex) GetLocation()
    {
        if (this.buffers.Count == 0)
        {
            return default;
        }

        var y = this.buffers[0].Top;
        ReadLineBuffer? buffer = null;
        foreach (var x in this.buffers)
        {
            x.Top = y;
            x.UpdateHeight(false);
            y += x.Height;
            if (buffer is null &&
                this.CursorTop >= x.Top &&
                this.CursorTop < y)
            {
                buffer = x;
                break;
            }
        }

        if (buffer is null)
        {
            return default;
        }
        else
        {
            return (buffer.Index, buffer.GetCursorIndex());
        }
    }

    private void RedrawInternal()
    {
        if (this.buffers.Count == 0)
        {
            return;
        }

        var span = this.WindowBuffer.AsSpan();

        /*if (resetCursor)
        {
            TryCopy(ResetCursor, ref span);
            this.CursorLeft = 0;
            this.CursorTop = 0;
        }*/

        (this.CursorLeft, this.CursorTop) = Console.GetCursorPosition();
        var y = this.CursorTop;
        var isFirst = true;
        var remainingHeight = this.WindowHeight;
        for (var i = 0; i < this.buffers.Count; i++)
        {
            var buffer = this.buffers[i];
            if (buffer.Top >= 0 && buffer.Height <= remainingHeight)
            {
                if (isFirst)
                {
                    isFirst = false;
                }
                else
                {
                    TryCopy(ConsoleHelper.NewLineSpan, ref span);
                }

                remainingHeight -= buffer.Height;

                if (buffer.Prompt is not null)
                {
                    TryCopy(buffer.Prompt.AsSpan(), ref span);
                }

                TryCopy(ConsoleHelper.GetForegroundColorEscapeCode(this.CurrentOptions.InputColor).AsSpan(), ref span); // Input color
                TryCopy(buffer.GetVisualSpan(0, buffer.Length), ref span);
                TryCopy(ConsoleHelper.ResetSpan, ref span); // Reset color
                TryCopy(ConsoleHelper.EraseToEndOfLineSpan, ref span);
            }

            buffer.Top = y;
            y += buffer.Height;
        }

        remainingHeight = this.WindowHeight - remainingHeight;
        var scroll = this.CursorTop + remainingHeight - this.WindowHeight;
        if (scroll > 0)
        {
            this.Scroll(scroll, true);
        }

        this.RawConsole.WriteInternal(this.WindowBuffer.AsSpan(0, this.WindowBuffer.Length - span.Length));
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static bool TryCopy(ReadOnlySpan<char> source, ref Span<char> destination)
    {
        if (source.Length > destination.Length)
        {
            return false;
        }

        source.CopyTo(destination);
        destination = destination.Slice(source.Length);
        return true;
    }

    private int GetBuffersHeightInternal()
    {
        var height = 0;
        for (var i = 0; i < (this.buffers.Count - 1); i++)
        {
            this.buffers[i].UpdateHeight(false);
            height += this.buffers[i].Height;
        }

        return height;
    }

    private ReadLineBuffer? PrepareAndFindBuffer()
    {
        if (this.buffers.Count == 0)
        {
            return null;
        }

        // Calculate buffer heights.
        var y = this.buffers[0].Top;
        ReadLineBuffer? buffer = null;
        foreach (var x in this.buffers)
        {
            x.Top = y;
            x.UpdateHeight(false);
            y += x.Height;
            if (buffer is null &&
                this.CursorTop >= x.Top &&
                this.CursorTop < y)
            {
                buffer = x;
            }
        }

        buffer ??= this.buffers[0];
        return buffer;
    }

    private void Clear()
    {
        using (this.lockObject.EnterScope())
        {
            this.MultilineMode = false;
            foreach (var buffer in this.buffers)
            {
                this.bufferPool.Return(buffer);
            }

            this.buffers.Clear();
        }
    }
}
