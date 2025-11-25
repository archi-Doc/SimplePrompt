// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using System.Runtime.CompilerServices;
using Arc;
using Arc.Unit;
using CrossChannel;
using SimplePrompt.Internal;

namespace SimplePrompt.Internal;

internal record class ReadLineInstance
{
    private const int CharBufferSize = 1024;
    private const int WindowBufferSize = 64 * 1024;

    public ReadLineOptions Options => this.options;

    public RawConsole RawConsole => this.simpleConsole.RawConsole;

    public int CursorLeft { get; set; }

    public int CursorTop { get; set; }

    public bool MultilineMode { get; private set; }

    private readonly SimpleConsole simpleConsole;
    private ReadLineOptions options = new();

    private readonly Lock syncObject = new();
    private readonly char[] charBuffer;
    private readonly char[] windowBuffer;
    private List<ReadLineBuffer> bufferList = new();
    private int editableBufferIndex;

    public ReadLineInstance(SimpleConsole simpleConsole)
    {
        this.simpleConsole = simpleConsole;

        this.charBuffer = new char[CharBufferSize]; ;
        this.windowBuffer = new char[WindowBufferSize];
    }

    public void Initialize(ReadLineOptions options)
    {
        GhostCopy.Copy(ref options, ref this.options);
    }

    public void Prepare()
    {
        // Prepare the window, and if the cursor is in the middle of a line, insert a newline.
        this.simpleConsole.PrepareWindow();
        (this.CursorLeft, this.CursorTop) = Console.GetCursorPosition();
        if (this.CursorLeft > 0)
        {
            this.simpleConsole.UnderlyingTextWriter.WriteLine();
            this.CursorLeft = 0;
            if (this.CursorTop < this.simpleConsole.WindowHeight - 1)
            {
                this.CursorTop++;
            }
        }

        var prompt = this.Options.Prompt.AsSpan();
        var bufferIndex = 0;
        while (prompt.Length >= 0)
        {
            var index = BaseHelper.IndexOfLfOrCrLf(prompt, out var newLineLength);
            ReadLineBuffer buffer;
            if (index < 0)
            {
                buffer = this.simpleConsole.RentBuffer(this, bufferIndex++, prompt.ToString());
                prompt = default;
            }
            else
            {
                buffer = this.simpleConsole.RentBuffer(this, bufferIndex++, prompt.Slice(0, index).ToString());
                prompt = prompt.Slice(index + newLineLength);
            }

            this.bufferList.Add(buffer);
            buffer.Top = this.simpleConsole.CursorTop;
            buffer.UpdateHeight(false);

            var span = this.windowBuffer.AsSpan();
            TryCopy(buffer.Prompt.AsSpan(), ref span);
            if (prompt.Length == 0)
            {
                TryCopy(ConsoleHelper.EraseToEndOfLineSpan, ref span);
                this.simpleConsole.CursorTop += buffer.Height - 1;
            }
            else
            {
                TryCopy(ConsoleHelper.EraseToEndOfLineAndNewLineSpan, ref span);
                this.simpleConsole.CursorTop += buffer.Height;
            }

            this.RawConsole.WriteInternal(this.windowBuffer.AsSpan(0, this.windowBuffer.Length - span.Length));

            if (prompt.Length == 0)
            {// Last buffer
                this.editableBufferIndex = bufferIndex - 1;
                this.MoveCursor2(buffer.PromtWidth);
                this.TrimCursor();
                this.SetCursorPosition(this.CursorLeft, this.CursorTop, CursorOperation.None);
                break;
            }
        }
    }

    public void HeightChanged(int index, int dif)
    {
        var cursorTop = this.simpleConsole.CursorTop;
        var cursorLeft = this.simpleConsole.CursorLeft;

        for (var i = index + 1; i < this.bufferList.Count; i++)
        {
            var buffer = this.bufferList[i];
            buffer.Top += dif;
            buffer.Write(0, -1, 0, 0, true);
        }

        if (dif < 0)
        {
            var buffer = this.bufferList[this.bufferList.Count - 1];
            var top = buffer.Top + buffer.Height;
            this.ClearLine(top);
        }

        this.SetCursorPosition(cursorLeft, cursorTop, CursorOperation.Show);
    }

    public void TryDeleteBuffer(int index)
    {
        if (index < 0 ||
            index >= (this.bufferList.Count - 1))
        {
            return;
        }

        var dif = -this.bufferList[index].Height;
        this.bufferList.RemoveAt(index);
        for (var i = index; i < this.bufferList.Count; i++)
        {
            var buffer = this.bufferList[i];
            buffer.Index = i;
            buffer.Top += dif;
            buffer.Write(0, -1, 0, 0, true);
        }

        this.ClearLastLine(dif);
        this.SetCursor(this.bufferList[index]);
    }

    public void ProcessKeyInfo(ConsoleKeyInfo keyInfo)
    {
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
        else if (this.CurrentOptions.CancelOnEscape &&
            keyInfo.Key == ConsoleKey.Escape)
        {
            this.UnderlyingTextWriter.WriteLine();
            this.Clear();
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

    public void Clear()
    {
        this.MultilineMode = false;
        foreach (var buffer in this.bufferList)
        {
            this.simpleConsole.ReturnBuffer(buffer);
        }

        this.bufferList.Clear();
    }

    internal void MoveCursor2(int index)
    {
        this.CursorLeft += index;
        var h = this.CursorLeft >= 0 ?
            (this.CursorLeft / this.simpleConsole.WindowWidth) :
            (((this.CursorLeft - 1) / this.simpleConsole.WindowWidth) - 1);
        this.CursorLeft -= h * this.simpleConsole.WindowWidth;
        this.CursorTop += h;
    }

    internal void SetCursorPosition(int cursorLeft, int cursorTop, CursorOperation cursorOperation)
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

        this.RawConsole.WriteInternal(this.windowBuffer.AsSpan(0, written));

        this.CursorLeft = cursorLeft;
        this.CursorTop = cursorTop;
    }

    internal void Scroll(int scroll, bool moveCursor)
    {
        if (moveCursor)
        {
            this.CursorTop -= scroll;
        }

        foreach (var x in this.bufferList)
        {
            x.Top -= scroll;
        }
    }

    internal void TrimCursor()
    {
        var scroll = this.CursorTop - this.simpleConsole.WindowHeight + 1;
        if (scroll > 0)
        {
            this.Scroll(scroll, true);
        }
    }

    private void ClearLastLine(int dif)
    {
        var buffer = this.bufferList[this.bufferList.Count - 1];
        var top = buffer.Top + buffer.Height;
        for (var i = 0; i < -dif; i++)
        {
            this.ClearLine(top + i);
        }
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

        span = ConsoleHelper.EraseEntireLineSpan;
        span.CopyTo(buffer);
        buffer = buffer.Slice(span.Length);
        written += span.Length;

        /*span = ConsoleHelper.RestoreCursorSpan;
        span.CopyTo(buffer);
        buffer = buffer.Slice(span.Length);
        written += span.Length;*/

        this.RawConsole.WriteInternal(this.windowBuffer.AsSpan(0, written));
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

    internal void PrepareWindow()
    {
        var newCursor = Console.GetCursorPosition();
        var dif = newCursor.Top - this.simpleConsole.CursorTop;
        (this.simpleConsole.CursorLeft, this.simpleConsole.CursorTop) = newCursor;
        foreach (var x in this.bufferList)
        {
            x.Top += dif;
        }
    }
}
