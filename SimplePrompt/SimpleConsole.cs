// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Arc.Collections;
using Arc.Threading;
using Arc.Unit;

#pragma warning disable SA1204 // Static elements should appear before instance elements

namespace SimplePrompt;

public partial class SimpleConsole : IConsoleService
{
    private const int CharBufferSize = 1024;
    private const int WindowBufferMargin = 1024;
    private static readonly ConsoleKeyInfo EnterKeyInfo = new(default, ConsoleKey.Enter, false, false, false);
    private static readonly ConsoleKeyInfo SpaceKeyInfo = new(' ', ConsoleKey.Spacebar, false, false, false);
    public static ReadOnlySpan<char> EraseLineAndReturn => "\u001b[K\n";

    public ILogger? Logger { get; set; }

    public ConsoleColor InputColor { get; set; } = ConsoleColor.Yellow;

    public string MultilineIdentifier { get; set; } = "\"\"\"";

    public bool IsInsertMode { get; set; } = true;

    internal RawConsole RawConsole { get; private set; }

    internal int WindowWidth { get; private set; }

    internal int WindowHeight { get; private set; }

    internal int CursorLeft { get; set; }

    internal int CursorTop { get; set; }

    internal int StartingCursorTop { get; set; }

    internal bool MultilineMode { get; set; }

    internal char[] WindowBuffer => this.windowBuffer;

    internal byte[] Utf8Buffer => this.utf8Buffer;

    internal List<InputBuffer> Buffers => this.buffers;

    private int WindowBufferCapacity => (this.WindowWidth * this.WindowHeight * 2) + WindowBufferMargin;

    private readonly char[] charBuffer = new char[CharBufferSize];
    private readonly ObjectPool<InputBuffer> bufferPool;

    private readonly Lock lockObject = new();
    private List<InputBuffer> buffers = new();
    private char[] windowBuffer = [];
    private byte[] utf8Buffer = [];

    public SimpleConsole(ConsoleColor inputColor = (ConsoleColor)(-1))
    {
        Console.OutputEncoding = System.Text.Encoding.UTF8;

        this.RawConsole = new(this);
        this.bufferPool = new(() => new InputBuffer(this), 32);
        if (inputColor >= 0)
        {
            this.InputColor = inputColor;
        }
    }

    InputResult IConsoleService.ReadLine(string? prompt)
        => this.ReadLine(prompt, default).Result;

    public async Task<InputResult> ReadLine(string? prompt = default, string? multilinePrompt = default)
    {
        InputBuffer? buffer;
        var position = 0;

        using (this.lockObject.EnterScope())
        {
            this.ReturnAllBuffersInternal();
            buffer = this.RentBuffer(0, prompt);
            this.buffers.Add(buffer);
            this.StartingCursorTop = Console.CursorTop;
        }

        if (!string.IsNullOrEmpty(prompt))
        {
            Console.Out.Write(prompt);
        }

        (this.CursorLeft, this.CursorTop) = Console.GetCursorPosition();

        // Console.TreatControlCAsInput = true;
        ConsoleKeyInfo pendingKeyInfo = default;
        while (!ThreadCore.Root.IsTerminated)
        {
            this.PrepareWindow();

            // Polling isn’t an ideal approach, but due to the fact that the normal method causes a significant performance drop and that the function must be able to exit when the application terminates, this implementation was chosen.
            if (!this.RawConsole.TryRead(out var keyInfo))
            {
                await Task.Delay(10);
                continue;
            }

ProcessKeyInfo:
            if (keyInfo.KeyChar == '\n' ||
                keyInfo.Key == ConsoleKey.Enter)
            {
                keyInfo = EnterKeyInfo;
            }
            else if (keyInfo.KeyChar == '\t' ||
                keyInfo.Key == ConsoleKey.Tab)
            {// Tab -> Space
                keyInfo = SpaceKeyInfo;
            }
            else if (keyInfo.KeyChar == '\r')
            {// CrLf -> Lf
                continue;
            }
            else if (keyInfo.Key == ConsoleKey.Escape)
            {
                Console.Out.WriteLine();
                return new(InputResultKind.Canceled);
            }
            else if (keyInfo.Key == ConsoleKey.C &&
                keyInfo.Modifiers.HasFlag(ConsoleModifiers.Control))
            { // Ctrl+C
                // ThreadCore.Root.Terminate(); // Send a termination signal to the root.
                // return null;
            }

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
                var result = this.Flush(keyInfo, this.charBuffer.AsSpan().Slice(0, position), multilinePrompt);
                position = 0;
                if (result is not null)
                {
                    Console.Out.WriteLine();
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
        Console.Out.WriteLine();
        return new(InputResultKind.Terminated);
    }

    void IConsoleService.Write(string? message = null)
    {
        if (Environment.NewLine == "\r\n" && message is not null)
        {
            message = Arc.BaseHelper.ConvertLfToCrLf(message);
        }

        try
        {
            Console.Out.Write(message);
        }
        catch
        {
        }
    }

    public void WriteLine(string? message = null)
    {
        if (Environment.NewLine == "\r\n" && message is not null)
        {
            message = Arc.BaseHelper.ConvertLfToCrLf(message);
        }

        try
        {
            if (this.buffers.Count == 0)
            {
                Console.Out.WriteLine(message);
                return;
            }

            using (this.lockObject.EnterScope())
            {
                var cursorLeft = this.CursorLeft;
                var cursorTop = this.CursorTop;

                this.SetCursorAtFirst();
                Console.Out.Write(message);
                Console.Out.Write(EraseLineAndReturn);
                this.RedrawInternal();

                this.SetCursorPosition(cursorLeft, cursorTop, false);
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

    internal void SetCursorPosition(int cursorLeft, int cursorTop, bool showCursor)
    {// Move and show cursor.
        /*if (this.CursorLeft == cursorLeft &&
            this.CursorTop == cursorTop)
        {
            return;
        }*/

        var buffer = this.windowBuffer.AsSpan();
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

        if (showCursor)
        {
            span = ConsoleHelper.ShowCursorSpan;
            span.CopyTo(buffer);
            buffer = buffer.Slice(span.Length);
            written += span.Length;
        }

        this.RawConsole.WriteInternal(this.windowBuffer.AsSpan(0, written));

        this.CursorLeft = cursorLeft;
        this.CursorTop = cursorTop;
    }

    internal void Scroll(int scroll)
    {
        if (scroll > 0)
        {
            this.StartingCursorTop -= scroll;
            this.CursorTop -= scroll;
            foreach (var x in this.buffers)
            {
                x.Top -= scroll;
                // x.CursorTop += scroll;
            }
        }
    }

    internal void HeightChanged(int index, int dif)
    {
        var cursorTop = this.CursorTop;
        var cursorLeft = this.CursorLeft;

        for (var i = index + 1; i < this.buffers.Count; i++)
        {
            var buffer = this.buffers[i];
            buffer.Top += dif;
            buffer.Write(0, -1, 0, 0, true);
        }

        if (dif < 0)
        {
            var buffer = this.buffers[this.buffers.Count - 1];
            var top = buffer.Top + buffer.Height;
            this.ClearLine(top);
        }

        this.SetCursorPosition(cursorLeft, cursorTop, true);
    }

    internal void TryDeleteBuffer(int index)
    {
        if (index < 0 ||
            index >= (this.buffers.Count - 1))
        {
            return;
        }

        var dif = -this.buffers[index].Height;
        this.buffers.RemoveAt(index);
        for (var i = index; i < this.buffers.Count; i++)
        {
            var buffer = this.buffers[i];
            buffer.Index = i;
            buffer.Top += dif;
            buffer.Write(0, -1, 0, 0, true);
        }

        this.ClearLastLine(dif);
        this.SetCursor(this.buffers[index]);
    }

    private void ClearLastLine(int dif)
    {
        var buffer = this.buffers[this.buffers.Count - 1];
        var top = buffer.Top + buffer.Height;
        for (var i = 0; i < -dif; i++)
        {
            this.ClearLine(top + i);
        }
    }

    private void SetCursor(InputBuffer buffer)
    {
        var cursorLeft = buffer.Left + buffer.PromtWidth;
        var cursorTop = buffer.Top;
        this.SetCursorPosition(cursorLeft, cursorTop, false);
    }

    private void SetCursorAtFirst()
    {
        if (this.buffers.Count == 0)
        {
            return;
        }

        var buffer = this.buffers[0];
        this.SetCursorPosition(buffer.Left, buffer.Top, false);
    }

    private void SetCursorAtEnd()
    {
        if (this.buffers.Count == 0)
        {
            return;
        }

        var buffer = this.buffers[this.buffers.Count - 1];
        var newCursor = buffer.ToCursor(buffer.Width);
        newCursor.Left += buffer.Left;
        newCursor.Top += buffer.Top;
        this.SetCursorPosition(newCursor.Left, newCursor.Top, false);
    }

    private void ClearLine(int top)
    {
        var buffer = this.windowBuffer.AsSpan();
        var written = 0;
        ReadOnlySpan<char> span;

        /*span = ConsoleHelper.SaveCursorSpan;
        span.CopyTo(buffer);
        buffer = buffer.Slice(span.Length);
        written += span.Length;*/

        span = ConsoleHelper.SetCursorSpan;
        span.CopyTo(buffer);
        buffer = buffer.Slice(span.Length);
        written += span.Length;

        var x = top + 1;
        var y = 0 + 1;
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

        span = ConsoleHelper.EraseLine2Span;
        span.CopyTo(buffer);
        buffer = buffer.Slice(span.Length);
        written += span.Length;

        /*span = ConsoleHelper.RestoreCursorSpan;
        span.CopyTo(buffer);
        buffer = buffer.Slice(span.Length);
        written += span.Length;*/

        this.RawConsole.WriteInternal(this.windowBuffer.AsSpan(0, written));
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

    private void PrepareWindow()
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
            return;
        }

        this.WindowWidth = windowWidth;
        this.WindowHeight = windowHeight;

        var newCursor = Console.GetCursorPosition();
        var dif = newCursor.Top - this.CursorTop;
        (this.CursorLeft, this.CursorTop) = newCursor;
        this.StartingCursorTop += dif;
        foreach (var x in this.buffers)
        {
            x.Top += dif;
        }

        /*this.Prepare();
        using (this.lockObject.EnterScope())
        {
            this.PrepareAndFindBuffer();
        }*/
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

        if (this.windowBuffer.Length != this.WindowBufferCapacity)
        {
            this.windowBuffer = new char[this.WindowBufferCapacity];
            this.utf8Buffer = new byte[this.WindowBufferCapacity * 3];
        }
    }

    private string? Flush(ConsoleKeyInfo keyInfo, Span<char> charBuffer, string? multilinePrompt)
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

                if (multilinePrompt is not null &&
                    (SimpleCommandLine.SimpleParserHelper.CountOccurrences(buffer.TextSpan, this.MultilineIdentifier) % 2) > 0)
                {// Multiple line
                    if (buffer == this.buffers[0])
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

                        buffer = this.RentBuffer(this.buffers.Count, multilinePrompt);
                        this.buffers.Add(buffer);
                        Console.Out.WriteLine();
                        Console.Out.Write(multilinePrompt);
                        (this.CursorLeft, this.CursorTop) = Console.GetCursorPosition();
                        this.StartingCursorTop = this.CursorTop - this.GetBuffersHeightInternal();
                        return null;
                    }
                    else
                    {// Next buffer
                        this.SetCursor(this.buffers[buffer.Index + 1]);
                        return null;
                    }
                }

                var length = this.buffers[0].Length;
                for (var i = 1; i < this.buffers.Count; i++)
                {
                    length += 1 + this.buffers[i].Length;
                }

                var result = string.Create(length, this.buffers, static (span, buffers) =>
                {
                    buffers[0].TextSpan.CopyTo(span);
                    span = span.Slice(buffers[0].Length);
                    for (var i = 1; i < buffers.Count; i++)
                    {
                        span[0] = '\n';
                        span = span.Slice(1);

                        buffers[i].TextSpan.CopyTo(span);
                        span = span.Slice(buffers[i].Length);
                    }
                });

                this.SetCursorAtEnd();
                this.ReturnAllBuffersInternal();
                return result;
            }
            else
            {
                return null;
            }
        }
    }

    private void RedrawInternal()
    {
        var firstBuffer = 0;//
        var span = this.windowBuffer.AsSpan();

        for (var i = firstBuffer; i < this.buffers.Count; i++)
        {
            var buffer = this.buffers[i];

            if (buffer.Prompt is not null &&
                !TryCopy(buffer.Prompt.AsSpan(), ref span))
            {
                goto Exit;
            }

            if (!TryCopy(buffer.TextSpan, ref span))
            {
                goto Exit;
            }

            if (!TryCopy(EraseLineAndReturn, ref span))
            {
                goto Exit;
            }
        }
Exit:
        this.RawConsole.WriteInternal(this.windowBuffer.AsSpan(0, this.WindowBufferCapacity - span.Length));

        (this.CursorLeft, this.CursorTop) = Console.GetCursorPosition();
        this.StartingCursorTop = this.CursorTop;

        bool TryCopy(ReadOnlySpan<char> source, ref Span<char> destination)
        {
            if (source.Length > destination.Length)
            {
                return false;
            }

            source.CopyTo(destination);
            destination = destination.Slice(source.Length);
            return true;
        }
    }

    private int GetBuffersHeightInternal()
    {
        var height = 0;
        for (var i = 1; i < this.buffers.Count; i++)
        {
            this.buffers[i].UpdateHeight(false);
            height += this.buffers[i].Height;
        }

        return height;
    }

    private InputBuffer? PrepareAndFindBuffer()
    {
        if (this.buffers.Count == 0)
        {
            return null;
        }

        // Calculate buffer heights.
        var y = this.StartingCursorTop;
        InputBuffer? buffer = null;
        foreach (var x in this.buffers)
        {
            x.Left = 0;
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

    private InputBuffer RentBuffer(int index, string? prompt)
    {
        var buffer = this.bufferPool.Rent();
        buffer.Initialize(index, prompt);
        return buffer;
    }

    private void ReturnAllBuffersInternal()
    {
        foreach (var buffer in this.buffers)
        {
            this.bufferPool.Return(buffer);
        }

        this.buffers.Clear();
    }
}
