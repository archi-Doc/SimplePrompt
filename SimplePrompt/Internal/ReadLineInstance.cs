// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using System.Globalization;
using Arc;
using Arc.Collections;
using Arc.Unit;
using CrossChannel;

namespace SimplePrompt.Internal;

internal sealed class ReadLineInstance
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

    public List<SimpleTextLine> LineList { get; private set; } = new();

    public SimpleTextLocation CurrentLocation { get; private set; } = new();

    public ReadLineMode Mode { get; private set; }

    public int FirstInputIndex { get; private set; }

    public int TotalHeight
    {
        get
        {
            var height = 0;
            foreach (var x in this.LineList)
            {
                height += x.Height;
            }

            return height;
        }
    }

    private SimpleConsole simpleConsole;
    private ReadLineOptions options = new();
    // private DateTime correctedCursorTime;

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
            if (x.IsInput)
            {
                foreach (var y in x.Rows)
                {
                    if (y.IsInput && y.InputStart < y.End)
                    {// Not empty
                        return false;
                    }
                }
            }
        }

        // Empty input
        return true;
    }

    internal void Prepare()
    {
        var top = this.simpleConsole.CursorTop;
        var prompt = this.Options.Prompt.AsSpan();
        var lineIndex = 0;
        char[]? windowBuffer = null;
        while (prompt.Length >= 0)
        {
            // For a multi-line prompt, multiple SimpleTextLine instances are created and each line is assigned accordingly.
            var index = BaseHelper.IndexOfLfOrCrLf(prompt, out var newLineLength);
            SimpleTextLine line;
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

            line = SimpleTextLine.Rent(this.simpleConsole, this, lineIndex, currentPrompt, isInput);
            lineIndex++;

            this.LineList.Add(line);
            line.Top = top;
            top += line.Height;

            windowBuffer ??= SimpleConsole.RentWindowBuffer();
            var span = windowBuffer.AsSpan();

            if (lineIndex == 1)
            {// Hide the cursor during the initial rendering.
                SimplePromptHelper.TryCopy(ConsoleHelper.HideCursorSpan, ref span);
            }

            SimplePromptHelper.TryCopy(currentPrompt, ref span);
            if (isInput)
            {
                SimplePromptHelper.TryCopy(ConsoleHelper.EraseToEndOfLineSpan, ref span);
            }
            else
            {
                SimplePromptHelper.TryCopy(ConsoleHelper.EraseToEndOfLineAndNewLineSpan, ref span);
            }

            if (isInput)
            {// Show the cursor during the final rendering.
                SimplePromptHelper.TryCopy(ConsoleHelper.ShowCursorSpan, ref span);
            }

            this.RawConsole.WriteInternal(windowBuffer.AsSpan(0, windowBuffer.Length - span.Length));

            if (isInput)
            {// Input
                this.FirstInputIndex = lineIndex - 1;
                break;
            }
        }

        if (windowBuffer is not null)
        {
            SimpleConsole.ReturnWindowBuffer(windowBuffer);
        }


        this.Scroll();

        this.CurrentLocation.Reset();
    }

    internal void Scroll()
    {
        if (this.LineList.Count == 0)
        {
            return;
        }

        var line = this.LineList[^1];
        var scroll = line.Top + line.Height - this.simpleConsole.WindowHeight;
        if (scroll > 0)
        {
            this.simpleConsole.Scroll(scroll, true);
        }
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
                    this.simpleConsole.AdvanceCursor(line.PromptSpan, false);
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

    public void HeightChanged(SimpleTextRow row, int diff)
    {
        var index = -1;
        for (var i = 0; i < this.LineList.Count; i++)
        {
            if (this.LineList[i] == row.Line)
            {
                index = i;
                break;
            }
        }

        if (index < 0)
        {
            return;
        }

        var line = this.LineList[index];
        if (diff > 0)
        {
            this.simpleConsole.ClearRow(line.Top + line.Height - 1);
        }

        var top = -1;
        for (var i = index + 1; i < this.LineList.Count; i++)
        {
            this.LineList[i].Top += diff;
            this.LineList[i].Redraw();
            top = this.LineList[i].Top + this.LineList[i].Height;
        }

        if (diff < 0)
        {
            if (top >= 0)
            {
                for (var i = 0; i < -diff; i++)
                {
                    this.simpleConsole.ClearRow(top++);
                }
            }
        }

        this.CurrentLocation.LocationToCursor();
    }

    public void TryDeleteLine(int index, bool backspace)
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
            var line = this.LineList[i];
            line.Index = i;
            line.Top += dif;
            line.Redraw();
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

        if (this.LineList.Count <= this.FirstInputIndex + 1)
        {
            this.Mode = ReadLineMode.Singleline;
        }

        this.CurrentLocation.Reset(this.LineList[index], CursorOperation.None, backspace);
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
        if (this.LineList.Count == 0)
        {
            return 0;
        }

        var buffer = this.LineList[0];
        var top = Math.Max(0, buffer.Top);
        this.simpleConsole.SetCursorPosition(0, top, cursorOperation);
        return top;
    }

    public void ResetCursor(CursorOperation cursorOperation)
    {
        if (this.LineList.Count == 0)
        {
            return;
        }

        var top = this.LineList[0].Top;
        if (this.simpleConsole.CursorTop != top ||
            this.simpleConsole.CursorLeft != 0)
        {
            this.simpleConsole.SetCursorPosition(0, top, cursorOperation);
        }
        else if (cursorOperation == CursorOperation.Show)
        {
            this.simpleConsole.ShowCursor();
        }
    }

    public void Reset()
    {
        this.Mode = default;
        var indexToRemove = this.FirstInputIndex + 1;
        var numberToRemove = this.LineList.Count - indexToRemove;
        while (numberToRemove-- > 0)
        {
            var listToRemove = this.LineList[indexToRemove];
            this.LineList.RemoveAt(indexToRemove);
            SimpleTextLine.Return(listToRemove);
        }

        this.LineList[this.FirstInputIndex].Clear();
    }

    public void Clear()
    {
        this.Mode = default;
        this.FirstInputIndex = 0;

        this.ReleaseLines();
    }

    public void Redraw()
    {
        if (this.LineList.Count == 0)
        {
            return;
        }

        var windowBuffer = SimpleConsole.RentWindowBuffer();
        var span = windowBuffer.AsSpan();

        // var scroll = y + this.TotalHeight - this.simpleConsole.WindowHeight;
        var y = this.simpleConsole.CursorTop;
        var isFirst = true;
        for (var i = 0; i < this.LineList.Count; i++)
        {
            var line = this.LineList[i];
            // if (line.Top >= 0 && line.Height <= remainingHeight)

            if (isFirst)
            {
                isFirst = false;
            }
            else
            {
                SimplePromptHelper.TryCopy(ConsoleHelper.NewLineSpan, ref span);
            }

            if (line.PromptLength > 0)
            {
                SimplePromptHelper.TryCopy(line.PromptSpan, ref span);
            }

            SimplePromptHelper.TryCopy(ConsoleHelper.GetForegroundColorEscapeCode(this.Options.InputColor).AsSpan(), ref span); // Input color

            var maskingCharacter = this.Options.MaskingCharacter;
            if (maskingCharacter == default)
            {
                SimplePromptHelper.TryCopy(line.InputSpan, ref span);
            }
            else
            {
                if (span.Length >= line.InputWidth)
                {
                    span.Slice(0, line.InputWidth).Fill(maskingCharacter);
                    span = span.Slice(line.InputWidth);
                }
            }

            SimplePromptHelper.TryCopy(ConsoleHelper.ResetSpan, ref span); // Reset color
            if (line.EndsWithEmptyRow)
            {
                // SimplePromptHelper.TryCopy(ConsoleHelper.EraseToEndOfLineAndNewLineSpan, ref span);
                SimplePromptHelper.TryCopy(SimplePromptHelper.ForceNewLineCursor, ref span);
                SimplePromptHelper.TryCopy(ConsoleHelper.EraseToEndOfLineSpan, ref span);
            }
            else
            {
                SimplePromptHelper.TryCopy(ConsoleHelper.EraseToEndOfLineSpan, ref span);
            }

            line.Top = y;
            y += line.Height;
        }

        var scroll = y - this.simpleConsole.WindowHeight;
        if (scroll > 0)
        {
            this.simpleConsole.Scroll(scroll, true);
        }

        this.RawConsole.WriteInternal(windowBuffer.AsSpan(0, windowBuffer.Length - span.Length));
        SimpleConsole.ReturnWindowBuffer(windowBuffer);
    }

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

    internal bool CorrectCursorTop()
    {
        var (_, newCursorTop) = Console.GetCursorPosition(); // I have just got a new theory of eternity in this method.
        if (newCursorTop == this.simpleConsole.CursorTop)
        {
            return false;
        }

        // this.RawConsole.WriteInternal($"<Cursor top {this.simpleConsole.CursorTop} -> {newCursorTop}>");

        /*var topDiff = newCursorTop - this.simpleConsole.CursorTop;
        foreach (var x in this.LineList)
        {
            x.Top += topDiff;
        }

        this.simpleConsole.CursorTop = newCursorTop;*/
        return true;
    }
}
