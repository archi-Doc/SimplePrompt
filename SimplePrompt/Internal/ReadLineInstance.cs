// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using System.Runtime.CompilerServices;
using Arc;
using Arc.Unit;
using CrossChannel;

namespace SimplePrompt.Internal;

internal class ReadLineInstance
{
    public const int CharBufferSize = 1024;

    public ReadLineOptions Options => this.options;

    public RawConsole RawConsole => this.simpleConsole.RawConsole;

    public char[] CharBuffer { get; private set; } = new char[CharBufferSize];

    public List<ReadLineBuffer> BufferList { get; private set; } = new();

    public bool MultilineMode { get; private set; }

    public int EditableBufferIndex { get; private set; }

    private readonly SimpleConsole simpleConsole;
    private ReadLineOptions options = new();

    public ReadLineInstance(SimpleConsole simpleConsole)
    {
        this.simpleConsole = simpleConsole;
    }

    public void Initialize(ReadLineOptions options)
    {
        GhostCopy.Copy(ref options, ref this.options);
    }

    public void Prepare()
    {
        var prompt = this.Options.Prompt.AsSpan();
        var bufferIndex = 0;
        char[]? windowBuffer = null;
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

            this.BufferList.Add(buffer);
            buffer.Top = this.simpleConsole.CursorTop;
            buffer.UpdateHeight(false);

            windowBuffer ??= SimpleConsole.RentWindowBuffer();
            var span = windowBuffer.AsSpan();
            SimpleConsole.TryCopy(buffer.Prompt.AsSpan(), ref span);
            if (prompt.Length == 0)
            {
                SimpleConsole.TryCopy(ConsoleHelper.EraseToEndOfLineSpan, ref span);
                // this.simpleConsole.CursorTop += buffer.Height - 1;
            }
            else
            {
                SimpleConsole.TryCopy(ConsoleHelper.EraseToEndOfLineAndNewLineSpan, ref span);
                this.simpleConsole.CursorTop += buffer.Height;
            }

            this.RawConsole.WriteInternal(windowBuffer.AsSpan(0, windowBuffer.Length - span.Length));

            if (prompt.Length == 0)
            {// Last buffer
                this.EditableBufferIndex = bufferIndex - 1;
                this.simpleConsole.MoveCursor2(buffer.PromtWidth);
                this.simpleConsole.TrimCursor();
                // this.simpleConsole.SetCursorPosition(this.simpleConsole.CursorLeft, this.simpleConsole.CursorTop, CursorOperation.None);
                break;
            }
        }

        if (windowBuffer is not null)
        {
            SimpleConsole.ReturnWindowBuffer(windowBuffer);
        }
    }

    public string? Flush(ConsoleKeyInfo keyInfo, Span<char> charBuffer)
    {
        this.simpleConsole.Prepare();
        // using (this.lockObject.EnterScope())
        {
            var buffer = this.PrepareAndFindBuffer();
            if (buffer is null)
            {
                return string.Empty;
            }

            if (buffer.ProcessInternal(keyInfo, charBuffer))
            {// Exit input mode and return the concatenated string.
                if (this.BufferList.Count == 0)
                {
                    return string.Empty;
                }

                if (!string.IsNullOrEmpty(this.Options.MultilineIdentifier) &&
                    (SimpleCommandLine.SimpleParserHelper.CountOccurrences(buffer.TextSpan, this.Options.MultilineIdentifier) % 2) > 0)
                {// Multiple line
                    if (buffer.Index == this.EditableBufferIndex)
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
                    if (buffer.Index == (this.BufferList.Count - 1))
                    {// New InputBuffer
                        if (buffer.Length == 0)
                        {// Empty
                            return null;
                        }
                        else if (!this.IsLengthWithinLimit(1))
                        {// Exceeding max length
                            return null;
                        }

                        buffer = this.simpleConsole.RentBuffer(this, this.BufferList.Count, this.Options.MultilinePrompt);
                        this.BufferList.Add(buffer);
                        var previousTop = this.simpleConsole.CursorTop;
                        this.simpleConsole.UnderlyingTextWriter.WriteLine();
                        this.simpleConsole.UnderlyingTextWriter.Write(this.Options.MultilinePrompt);
                        (this.simpleConsole.CursorLeft, this.simpleConsole.CursorTop) = Console.GetCursorPosition();
                        if (this.simpleConsole.CursorTop == previousTop)
                        {
                            this.simpleConsole.Scroll(1, false);
                        }

                        return null;
                    }
                    else
                    {// Next buffer
                        this.simpleConsole.SetCursor(this.BufferList[buffer.Index + 1]);
                        return null;
                    }
                }

                var length = this.BufferList[this.EditableBufferIndex].Length;
                for (var i = this.EditableBufferIndex + 1; i < this.BufferList.Count; i++)
                {
                    length += 1 + this.BufferList[i].Length;
                }

                var result = string.Create(length, this.BufferList, (span, buffers) =>
                {
                    var isFirst = true;
                    for (var i = this.EditableBufferIndex; i < buffers.Count; i++)
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

    public void HeightChanged(int index, int dif)
    {
        var cursorTop = this.simpleConsole.CursorTop;
        var cursorLeft = this.simpleConsole.CursorLeft;

        for (var i = index + 1; i < this.BufferList.Count; i++)
        {
            var buffer = this.BufferList[i];
            buffer.Top += dif;
            buffer.Write(0, -1, 0, 0, true);
        }

        if (dif < 0)
        {
            var buffer = this.BufferList[this.BufferList.Count - 1];
            var top = buffer.Top + buffer.Height;
            this.ClearLine(top);
        }

        this.simpleConsole.SetCursorPosition(cursorLeft, cursorTop, CursorOperation.Show);
    }

    public void TryDeleteBuffer(int index)
    {
        if (index < 0 ||
            index >= (this.BufferList.Count - 1))
        {
            return;
        }

        var dif = -this.BufferList[index].Height;
        this.BufferList.RemoveAt(index);
        for (var i = index; i < this.BufferList.Count; i++)
        {
            var buffer = this.BufferList[i];
            buffer.Index = i;
            buffer.Top += dif;
            buffer.Write(0, -1, 0, 0, true);
        }

        this.ClearLastLine(dif);
        this.simpleConsole.SetCursor(this.BufferList[index]);
    }

    public bool IsLengthWithinLimit(int dif)
    {
        var length = 0;
        var isFirst = true;
        for (var i = this.EditableBufferIndex; i < this.BufferList.Count; i++)
        {
            if (isFirst)
            {
                isFirst = false;
            }
            else
            {
                length += 1; // New line
            }

            length += this.BufferList[i].Length;
        }

        return length + dif <= this.Options.MaxInputLength;
    }

    public int SetCursorAtFirst(CursorOperation cursorOperation)
    {
        if (this.BufferList.Count == 0)
        {
            return 0;
        }

        var buffer = this.BufferList[0];
        var top = Math.Max(0, buffer.Top);
        this.simpleConsole.SetCursorPosition(0, top, cursorOperation);
        return top;
    }

    public void SetCursorAtEnd(CursorOperation cursorOperation)
    {
        if (this.BufferList.Count == 0)
        {
            return;
        }

        var buffer = this.BufferList[this.BufferList.Count - 1];
        var newCursor = buffer.ToCursor(buffer.Width);
        newCursor.Top += buffer.Top;
        this.simpleConsole.SetCursorPosition(newCursor.Left, newCursor.Top, cursorOperation);
    }

    public void Clear()
    {
        this.MultilineMode = false;
        this.EditableBufferIndex = 0;
        foreach (var buffer in this.BufferList)
        {
            this.simpleConsole.ReturnBuffer(buffer);
        }

        this.BufferList.Clear();
    }

    public void RedrawInternal()
    {
        if (this.BufferList.Count == 0)
        {
            return;
        }

        var windowBuffer = SimpleConsole.RentWindowBuffer();
        var span = windowBuffer.AsSpan();

        /*if (resetCursor)
        {
            SimpleConsole.TryCopy(ResetCursor, ref span);
            this.simpleConsole.CursorLeft = 0;
            this.simpleConsole.CursorTop = 0;
        }*/

        (this.simpleConsole.CursorLeft, this.simpleConsole.CursorTop) = Console.GetCursorPosition();
        var y = this.simpleConsole.CursorTop;
        var isFirst = true;
        var remainingHeight = this.simpleConsole.WindowHeight;
        for (var i = 0; i < this.BufferList.Count; i++)
        {
            var buffer = this.BufferList[i];
            if (buffer.Top >= 0 && buffer.Height <= remainingHeight)
            {
                if (isFirst)
                {
                    isFirst = false;
                }
                else
                {
                    SimpleConsole.TryCopy(ConsoleHelper.NewLineSpan, ref span);
                }

                remainingHeight -= buffer.Height;

                if (buffer.Prompt is not null)
                {
                    SimpleConsole.TryCopy(buffer.Prompt.AsSpan(), ref span);
                }

                SimpleConsole.TryCopy(ConsoleHelper.GetForegroundColorEscapeCode(this.Options.InputColor).AsSpan(), ref span); // Input color

                var maskingCharacter = this.Options.MaskingCharacter;
                if (maskingCharacter == default)
                {
                    SimpleConsole.TryCopy(buffer.TextSpan, ref span);
                }
                else
                {
                    if (span.Length >= buffer.Width)
                    {
                        span.Slice(0, buffer.Width).Fill(maskingCharacter);
                        span = span.Slice(buffer.Width);
                    }
                }

                SimpleConsole.TryCopy(ConsoleHelper.ResetSpan, ref span); // Reset color
                SimpleConsole.TryCopy(ConsoleHelper.EraseToEndOfLineSpan, ref span);
            }

            buffer.Top = y;
            y += buffer.Height;
        }

        remainingHeight = this.simpleConsole.WindowHeight - remainingHeight;
        var scroll = this.simpleConsole.CursorTop + remainingHeight - this.simpleConsole.WindowHeight;
        if (scroll > 0)
        {
            this.simpleConsole.Scroll(scroll, true);
        }

        this.RawConsole.WriteInternal(windowBuffer.AsSpan(0, windowBuffer.Length - span.Length));
        SimpleConsole.ReturnWindowBuffer(windowBuffer);
    }

    private ReadLineBuffer? PrepareAndFindBuffer()
    {
        if (this.BufferList.Count == 0)
        {
            return null;
        }

        // Calculate buffer heights.
        var y = this.BufferList[0].Top;
        ReadLineBuffer? buffer = null;
        foreach (var x in this.BufferList)
        {
            x.Top = y;
            x.UpdateHeight(false);
            y += x.Height;
            if (buffer is null &&
                this.simpleConsole.CursorTop >= x.Top &&
                this.simpleConsole.CursorTop < y)
            {
                buffer = x;
            }
        }

        buffer ??= this.BufferList[0];
        return buffer;
    }

    private void ClearLastLine(int dif)
    {
        var buffer = this.BufferList[this.BufferList.Count - 1];
        var top = buffer.Top + buffer.Height;
        for (var i = 0; i < -dif; i++)
        {
            this.ClearLine(top + i);
        }
    }

    private void ClearLine(int top)
    {
        var windowBuffer = SimpleConsole.RentWindowBuffer();
        var buffer = windowBuffer.AsSpan();
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

        this.RawConsole.WriteInternal(windowBuffer.AsSpan(0, written));
        SimpleConsole.ReturnWindowBuffer(windowBuffer);
    }
}
