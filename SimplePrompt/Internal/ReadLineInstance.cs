// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Arc;
using Arc.Collections;
using Arc.Unit;
using CrossChannel;

namespace SimplePrompt.Internal;

internal class ReadLineInstance
{
    public const int CharBufferSize = 1024;
    private const int PoolSize = 4;

    #region ObjectPool

    private static readonly ObjectPool<ReadLineInstance> Pool = new(() => new(), PoolSize);

    public static ReadLineInstance Rent(SimpleConsole simpleConsole, ReadLineOptions options)
    {
        var obj = Pool.Rent();
        obj.Initialize(simpleConsole, options);
        return obj;
    }

    public static void Return(ReadLineInstance obj)
    {
        obj.Uninitialize();
        Pool.Return(obj);
    }

    #endregion

    #region FieldAndProperty

    public ReadLineOptions Options => this.options;

    public RawConsole RawConsole => this.simpleConsole.RawConsole;

    public char[] CharBuffer { get; private set; } = new char[CharBufferSize];

    public List<ReadLineBuffer> BufferList { get; private set; } = new();

    public List<SimpleTextLine> LineList { get; private set; } = new();

    public SimpleTextLocation CurrentLocation { get; private set; } = new();

    public int LineIndex { get; set; }

    public int LinePosition { get; set; }

    public ReadLineMode Mode { get; private set; }

    public int FirstInputIndex { get; private set; }

    private SimpleConsole simpleConsole;
    private ReadLineOptions options = new();

    #endregion

    public ReadLineInstance()
    {
        this.simpleConsole = default!;
    }

    public void Initialize(SimpleConsole simpleConsole, ReadLineOptions options)
    {
        this.simpleConsole = simpleConsole;
        this.CurrentLocation.Initialize(simpleConsole, this);
        GhostCopy.Copy(ref options, ref this.options);
    }

    public void Uninitialize()
    {
        this.simpleConsole = default!;
        this.CurrentLocation.Uninitialize();
    }

    public bool IsEmptyInput()
    {
        foreach (var x in this.LineList)
        {
            if (!x.IsInput)
            {
                continue;
            }

            foreach (var y in x.Rows)
            {
                if (y.IsInput && y.InputStart < y.End)
                {// Not empty
                    return false;
                }
            }
        }

        // Empty input
        return true;
    }

    public void Prepare()
    {
        var prompt = this.Options.Prompt.AsSpan();
        var bufferIndex = 0;
        char[]? windowBuffer = null;
        while (prompt.Length >= 0)
        {
            // For a multi-line prompt, multiple SimpleTextLine instances are created and each line is assigned accordingly.
            var index = BaseHelper.IndexOfLfOrCrLf(prompt, out var newLineLength);
            ReadLineBuffer buffer;
            SimpleTextLine simpleTextLine;
            ReadOnlySpan<char> currentPrompt;
            var isInput = false;
            if (index < 0)
            {
                currentPrompt = prompt;
                isInput = true;
            }
            else
            {
                currentPrompt = prompt.Slice(0, index);
                prompt = prompt.Slice(index + newLineLength);
            }

            buffer = this.simpleConsole.RentBuffer(this, bufferIndex, currentPrompt.ToString());
            simpleTextLine = SimpleTextLine.Rent(this.simpleConsole, this, bufferIndex, currentPrompt, isInput);
            bufferIndex++;

            this.BufferList.Add(buffer);
            buffer.Top = this.simpleConsole.CursorTop;
            buffer.UpdateHeight();

            this.LineList.Add(simpleTextLine);
            simpleTextLine.Top = this.simpleConsole.CursorTop;

            windowBuffer ??= SimpleConsole.RentWindowBuffer();
            var span = windowBuffer.AsSpan();
            SimplePromptHelper.TryCopy(currentPrompt, ref span);
            if (isInput)
            {
                SimplePromptHelper.TryCopy(ConsoleHelper.EraseToEndOfLineSpan, ref span);
                this.simpleConsole.AdvanceCursor(buffer.PromptWidth, false);
            }
            else
            {
                SimplePromptHelper.TryCopy(ConsoleHelper.EraseToEndOfLineAndNewLineSpan, ref span);
                this.simpleConsole.AdvanceCursor(buffer.PromptWidth, true);
            }

            this.RawConsole.WriteInternal(windowBuffer.AsSpan(0, windowBuffer.Length - span.Length));

            if (isInput)
            {// Input
                this.FirstInputIndex = bufferIndex - 1;
                break;
            }
        }

        if (windowBuffer is not null)
        {
            SimpleConsole.ReturnWindowBuffer(windowBuffer);
        }

        this.CurrentLocation.Reset();
    }

    public string? ProcessInput(ConsoleKeyInfo keyInfo, Span<char> charBuffer)
    {
        if (this.CurrentLocation.LineIndex >= this.LineList.Count)
        {
            return string.Empty;
        }

        var line = this.LineList[this.CurrentLocation.LineIndex];
        if (line.ProcessInternal(keyInfo, charBuffer))
        {// Exit input mode and return the concatenated string.
            if (!string.IsNullOrEmpty(this.Options.MultilineDelimiter) &&
                (SimpleCommandLine.SimpleParserHelper.CountOccurrences(line.InputSpan, this.Options.MultilineDelimiter) % 2) > 0)
            {// Multiple line (Delimiter)
                if (line.Index == this.FirstInputIndex)
                {// Start
                    this.Mode = ReadLineMode.Delimiter;
                }
                else
                {// End
                    this.Mode = default;
                }
            }

            var lineContinuation = false;
            if (this.Mode == ReadLineMode.Singleline)
            {// Single line mode -> Multiple line mode
                if (this.Options.LineContinuation != default)
                {
                    if (line.InputLength > 0 && line.InputSpan[^1] == this.Options.LineContinuation)
                    {// Multiple line (LineContinuation)
                        this.Mode = ReadLineMode.LineContinuation;
                    }
                }
            }
            else if (this.Mode == ReadLineMode.LineContinuation)
            {
                if (line.InputLength > 0 && line.InputSpan[^1] == this.Options.LineContinuation)
                {// Multiple line (LineContinuation)
                }
                else
                {
                    lineContinuation = true;
                    this.Mode = default;
                }
            }

            if (this.Mode.IsMultiline)
            {
                if (line.Index == (this.LineList.Count - 1))
                {// New InputBuffer
                    if (line.InputLength == 0)
                    {// Empty input
                        return null;
                    }
                    else if (!this.IsLengthWithinLimit(1))
                    {// Exceeding max length
                        return null;
                    }

                    var previousLine = this.LineList[this.LineList.Count - 1];
                    line = SimpleTextLine.Rent(this.simpleConsole, this, this.LineList.Count, this.Options.MultilinePrompt.AsSpan(), true);
                    this.LineList.Add(line);
                    line.Top = previousLine.Top + previousLine.Height;

                    this.simpleConsole.UnderlyingTextWriter.WriteLine();
                    this.simpleConsole.NewLineCursor();
                    this.simpleConsole.UnderlyingTextWriter.Write(line.PromptSpan);
                    this.simpleConsole.AdvanceCursor(line.PromptWidth, false);
                    this.CurrentLocation.Reset(line);

                    return null;
                }
                else
                {// Next line
                    this.CurrentLocation.ChangeLine(1);
                    return null;
                }
            }

            string result;
            if (lineContinuation)
            {// A\ B\ -> AB
                var length = 1;
                for (var i = this.FirstInputIndex; i < this.LineList.Count; i++)
                {
                    length += this.LineList[i].InputLength - 1;
                }

                result = string.Create(length, this.LineList, (span, lines) =>
                {
                    for (var i = this.FirstInputIndex; i < lines.Count; i++)
                    {
                        var inputSpan = lines[i].InputSpan;
                        if (i != (lines.Count - 1) && inputSpan.Length > 0)
                        {
                            inputSpan = inputSpan.Slice(0, inputSpan.Length - 1);
                        }

                        inputSpan.CopyTo(span);
                        span = span.Slice(inputSpan.Length);
                    }
                });
            }
            else
            {// """ABC""" -> ABC
                var length = this.LineList[this.FirstInputIndex].InputLength;
                for (var i = this.FirstInputIndex + 1; i < this.LineList.Count; i++)
                {
                    length += 1 + this.LineList[i].InputLength;
                }

                result = string.Create(length, this.LineList, (span, lines) =>
                {
                    var isFirst = true;
                    for (var i = this.FirstInputIndex; i < lines.Count; i++)
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

                        var inputSpan = lines[i].InputSpan;
                        inputSpan.CopyTo(span);
                        span = span.Slice(inputSpan.Length);
                    }
                });
            }

            return result;
        }
        else
        {
            return null;
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

    public void TryDeleteBuffer(int index, bool backspace)
    {
        if (index <= this.FirstInputIndex ||
            index >= this.LineList.Count)
        {
            return;
        }

        var lineToDelete = this.LineList[index];
        var dif = -lineToDelete.Height;
        this.LineList.RemoveAt(index);
        for (var i = index; i < this.LineList.Count; i++)
        {
            var buffer = this.LineList[i];
            buffer.Index = i;
            buffer.Top += dif;
            buffer.Write(0, -1, 0, 0, true);
        }

        SimpleTextLine.Return(lineToDelete);

        this.ClearLastLine(dif);

        if (backspace)
        {
            if (index > 0)
            {
                index--;
            }
        }
        else
        {
            if (this.LineList.Count > 0 &&
                index > (this.LineList.Count - 1))
            {
                index = this.LineList.Count - 1;
                backspace = true;
            }
        }

        this.CurrentLocation.Reset(this.LineList[index], backspace);
    }

    public bool IsLengthWithinLimit(int dif)
    {
        var length = 0;
        var isFirst = true;
        for (var i = 0; i < this.LineList.Count; i++)
        {
            if (!this.LineList[i].IsInput)
            {
                continue;
            }

            if (isFirst)
            {
                isFirst = false;
            }
            else
            {
                length += 1; // New line
            }

            length += this.LineList[i].InputLength;
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

    public void SetCursorAtLocation()
    {
        if (this.LineIndex >= this.LineList.Count)
        {
            return;
        }

        var line = this.LineList[this.LineIndex];
    }

    public void Reset()
    {
        this.Mode = default;
        for (var i = this.FirstInputIndex + 1; i < this.BufferList.Count; i++)
        {
            this.BufferList.Remove(this.BufferList[i]);
            this.simpleConsole.ReturnBuffer(this.BufferList[i]);

            var listToRemove = this.LineList[i];
            this.LineList.RemoveAt(i);
            SimpleTextLine.Return(listToRemove);
        }

        this.BufferList[this.FirstInputIndex].Reset();
    }

    public void Clear()
    {
        this.Mode = default;
        this.FirstInputIndex = 0;

        this.ReleaseLines();

        foreach (var buffer in this.BufferList)
        {
            this.simpleConsole.ReturnBuffer(buffer);
        }

        this.BufferList.Clear();
    }

    public void Restore()
    {
        this.simpleConsole.SyncCursor();
        var y = this.simpleConsole.CursorTop;
        foreach (var x in this.BufferList)
        {
            x.Top = y;
            x.UpdateHeight();
            y += x.Height;
        }
    }

    public void Redraw()
    {
        if (this.BufferList.Count == 0)
        {
            return;
        }

        var windowBuffer = SimpleConsole.RentWindowBuffer();
        var span = windowBuffer.AsSpan();

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
                    SimplePromptHelper.TryCopy(ConsoleHelper.NewLineSpan, ref span);
                }

                remainingHeight -= buffer.Height;

                if (buffer.Prompt is not null)
                {
                    SimplePromptHelper.TryCopy(buffer.Prompt.AsSpan(), ref span);
                }

                SimplePromptHelper.TryCopy(ConsoleHelper.GetForegroundColorEscapeCode(this.Options.InputColor).AsSpan(), ref span); // Input color

                var maskingCharacter = this.Options.MaskingCharacter;
                if (maskingCharacter == default)
                {
                    SimplePromptHelper.TryCopy(buffer.TextSpan, ref span);
                }
                else
                {
                    if (span.Length >= buffer.Width)
                    {
                        span.Slice(0, buffer.Width).Fill(maskingCharacter);
                        span = span.Slice(buffer.Width);
                    }
                }

                SimplePromptHelper.TryCopy(ConsoleHelper.ResetSpan, ref span); // Reset color
                SimplePromptHelper.TryCopy(ConsoleHelper.EraseToEndOfLineSpan, ref span);
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

    public void PrepareLocation()
    {
        if (this.BufferList.Count == 0)
        {
            this.LineIndex = 0;
            this.LinePosition = 0;
            return;
        }

        var y = this.BufferList[0].Top;
        ReadLineBuffer? buffer = null;
        foreach (var x in this.BufferList)
        {
            x.Top = y;
            y += x.Height;
            if (buffer is null &&
                this.simpleConsole.CursorTop >= x.Top &&
                this.simpleConsole.CursorTop < y)
            {
                buffer = x;
                break;
            }
        }

        if (buffer is null)
        {
            if (this.simpleConsole.CursorTop < this.BufferList[0].Top)
            {
                buffer = this.BufferList[0];
            }
            else
            {
                buffer = this.BufferList[this.BufferList.Count - 1];
            }
        }

        this.LineIndex = buffer.Index;
        this.LinePosition = buffer.GetCursorIndex();
        return;
    }

    /*private ReadLineBuffer? PrepareAndFindBuffer()
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
            x.UpdateHeight();
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
    }*/

    private void ReleaseLines()
    {
        TemporaryList<SimpleTextLine> list = default;
        foreach (var x in this.LineList)
        {
            list.Add(x);
        }

        foreach (var x in list)
        {
            SimpleTextLine.Return(x);
        }

        this.LineList.Clear();
    }

    private void ClearLastLine(int dif)
    {
        if (this.LineList.Count == 0)
        {
            return;
        }

        var line = this.LineList[this.LineList.Count - 1];
        var top = line.Top + line.Height;
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
