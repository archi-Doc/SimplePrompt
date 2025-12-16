// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using System.Runtime.CompilerServices;
using Arc;
using Arc.Collections;
using Arc.Unit;
using ValueLink;

namespace SimplePrompt.Internal;

internal class SimpleTextLine
{
    private const int PoolSize = 32;
    private const int InitialBufferSize = 256;

    #region ObjectPool

    private static readonly ObjectPool<SimpleTextLine> Pool = new(() => new(), PoolSize);

    public static SimpleTextLine Rent(SimpleConsole simpleConsole, ReadLineInstance readLineInstance, int index, ReadOnlySpan<char> prompt, bool isInput)
    {
        var obj = Pool.Rent();
        obj.Initialize(simpleConsole, readLineInstance, index, prompt, isInput);
        return obj;
    }

    public static void Return(SimpleTextLine obj)
    {
        obj.Uninitialize();
        Pool.Return(obj);
    }

    #endregion

    #region FiendAndProperty

    private readonly SimpleTextRow.GoshujinClass rows = new();
    private char[] charArray = new char[InitialBufferSize];
    private byte[] widthArray = new byte[InitialBufferSize];
    private int _promptLength;
    private int _promptWidth;
    private int _inputLength;
    private int _inputWidth;

    public SimpleConsole SimpleConsole { get; private set; }

    public ReadLineInstance ReadLineInstance { get; private set; }

    public int WindowWidth => this.SimpleConsole.WindowWidth;

    public int WindowHeight => this.SimpleConsole.WindowHeight;

    public int Index { get; private set; }

    public bool IsInput { get; private set; }

    public int Top { get; set; }

    /// <summary>
    /// Gets the cursor's horizontal position relative to the line's left edge.
    /// </summary>
    public int CursorLeft => this.SimpleConsole.CursorLeft;

    /// <summary>
    /// Gets the cursor's vertical position relative to the line's top edge.
    /// </summary>
    public int CursorTop => this.SimpleConsole.CursorTop - this.Top;

    public int Height => this.rows.Count;

    public int PromptLength => this._promptLength;

    public int PromptWidth => this._promptWidth;

    public int InputLength => this._inputLength;

    public int InputWidth => this._inputWidth;

    public int TotalLength => this.PromptLength + this.InputLength;

    public int TotalWidth => this.PromptWidth + this.InputWidth;

    internal SimpleTextRow.GoshujinClass Rows => this.rows;

    internal char[] CharArray => this.charArray;

    internal byte[] WidthArray => this.widthArray;

    internal bool IsEmpty => this.rows.Count == 0;

    #endregion

    private SimpleTextLine()
    {
        this.SimpleConsole = default!;
        this.ReadLineInstance = default!;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal void ChangeInputLengthAndWidth(int lengthDiff, int widthDiff)
    {
        this._inputLength += lengthDiff;
        this._inputWidth += widthDiff;
    }

    internal ReadOnlySpan<char> PromptSpan => this.charArray.AsSpan(0, this.PromptLength);

    internal ReadOnlySpan<char> InputSpan => this.charArray.AsSpan(this.PromptLength, this.InputLength);

    public bool ProcessInternal(ConsoleKeyInfo keyInfo, Span<char> charBuffer)
    {
        if (charBuffer.Length > 0)
        {
            this.ProcessCharacterInternal(charBuffer);
            this.SimpleConsole.CheckCursor();
        }

        if (keyInfo.Key != ConsoleKey.None)
        {// Control
            var key = keyInfo.Key;
            if (key == ConsoleKey.Enter)
            {// Exit or Multiline """
                if (!this.ReadLineInstance.Options.AllowEmptyLineInput)
                {
                    if (this.ReadLineInstance.IsEmptyInput())
                    {// Empty input
                        return false;
                    }
                }

                return true;
            }
            /*else if (key == ConsoleKey.Backspace)
            {
                if (this.InputLength == 0)
                {// Delete empty buffer
                    this.readLineInstance.TryDeleteBuffer(this.Index);
                    return false;
                }

                var location = this.GetArrayPosition();
                if (location.Position > 0)
                {
                    (int RemovedWidth, int Index, int Diff) r;
                    this.MoveLeft(location.Position);
                    if (char.IsLowSurrogate(this.charArray[location.Position - 1]) &&
                        (location.Position > 1) &&
                        char.IsHighSurrogate(this.charArray[location.Position - 2]))
                    {
                        r = this.Remove2At(location.Position - 2);
                        this.Write(location.Position - 2, this.Length, 0, r.RemovedWidth);
                    }
                    else
                    {
                        r = this.RemoveAt(location.Position - 1);
                        this.Write(location.Position - 1, this.Length, 0, r.RemovedWidth);
                    }

                    if (r.Diff != 0)
                    {
                        this.readLineInstance.HeightChanged(r.Index, r.Diff);
                    }
                }

                return false;
            }
            else if (key == ConsoleKey.Delete)
            {
                if (this.Length == 0)
                {// Delete empty buffer
                    this.readLineInstance.TryDeleteBuffer(this.Index);
                    return false;
                }

                var location = this.GetArrayPosition();
                if (location.Position < this.Length)
                {
                    (int RemovedWidth, int Index, int Diff) r;
                    if (char.IsHighSurrogate(this.charArray[location.Position]) &&
                        (location.Position + 1) < this.Length &&
                        char.IsLowSurrogate(this.charArray[location.Position + 1]))
                    {
                        r = this.Remove2At(location.Position);
                    }
                    else
                    {
                        r = this.RemoveAt(location.Position);
                    }

                    this.Write(location.Position, this.Length, 0, r.RemovedWidth);
                    if (r.Diff != 0)
                    {
                        this.readLineInstance.HeightChanged(r.Index, r.Diff);
                    }
                }

                return false;
            }*/
            else if (key == ConsoleKey.U && keyInfo.Modifiers == ConsoleModifiers.Control)
            {// Ctrl+U: Clear line
                this.ClearLine();
            }
            else if (key == ConsoleKey.Home)
            {
                this.ReadLineInstance.CurrentLocation.MoveFirst();
            }
            else if (key == ConsoleKey.End)
            {
                this.ReadLineInstance.CurrentLocation.MoveLast();
            }
            else if (key == ConsoleKey.LeftArrow)
            {
                this.ReadLineInstance.CurrentLocation.MoveLeft();
            }
            else if (key == ConsoleKey.RightArrow)
            {
                this.ReadLineInstance.CurrentLocation.MoveRight();
            }
            else if (key == ConsoleKey.UpArrow)
            {// History or move line
                if (this.ReadLineInstance.MultilineMode)
                {// Up
                    this.MoveUpOrDown(true);
                }
                else
                {// History
                }

                return false;
            }
            else if (key == ConsoleKey.DownArrow)
            {// History or move line
                if (this.ReadLineInstance.MultilineMode)
                {// Down
                    this.MoveUpOrDown(false);
                }
                else
                {// History
                }

                return false;
            }
            else if (key == ConsoleKey.Insert)
            {// Toggle insert mode
                // Overtype mode is not implemented yet.
                // this.InputConsole.IsInsertMode = !this.InputConsole.IsInsertMode;
            }
        }

        return false;
    }

    public override string ToString()
    {
        if (this.rows.Count == 0)
        {
            return string.Empty;
        }
        else
        {
            if (this.rows.Count == 0)
            {
                return $"{this.rows.Count} lines";
            }
            else
            {
                return $"{this.rows.Count} lines: {this.rows.ListChain[0].ToString()}";
            }
        }
    }

    internal void Write(int startIndex, int endIndex, int cursorDif, int removedWidth, bool eraseLine = false)
    {
        int x, y, w;
        var length = endIndex < 0 ? this.TotalLength : endIndex - startIndex;
        var widthSpan = this.widthArray.AsSpan(startIndex, length);
        var totalWidth = endIndex < 0 ? this.TotalWidth : (int)BaseHelper.Sum(widthSpan);
        var startPosition = endIndex < 0 ? 0 : (int)BaseHelper.Sum(this.widthArray.AsSpan(0, startIndex));

        var startCursor = (this.Top * this.WindowWidth) + startPosition;
        var windowRemaining = (this.WindowWidth * this.WindowHeight) - startCursor;
        if (totalWidth > windowRemaining)
        {
        }

        var startCursorLeft = startCursor % this.WindowWidth;
        var startCursorTop = startCursor / this.WindowWidth;
        if (startCursorTop < 0)
        {
            return;
        }

        var scroll = startCursorTop + 1 + ((startCursorLeft + totalWidth) / this.WindowWidth) - this.WindowHeight;

        var newCursor = startCursor + cursorDif;
        var newCursorLeft = newCursor % this.WindowWidth;
        var newCursorTop = newCursor / this.WindowWidth;

        ReadOnlySpan<char> span;
        var windowBuffer = SimpleConsole.RentWindowBuffer();
        var buffer = windowBuffer.AsSpan();
        var written = 0;

        // Hide cursor
        span = ConsoleHelper.HideCursorSpan;
        span.CopyTo(buffer);
        written += span.Length;
        buffer = buffer.Slice(span.Length);

        if (startCursorLeft != this.SimpleConsole.CursorLeft || startCursorTop != this.SimpleConsole.CursorTop)
        {// Move cursor
            span = ConsoleHelper.SetCursorSpan;
            span.CopyTo(buffer);
            buffer = buffer.Slice(span.Length);
            written += span.Length;

            x = newCursorTop + 1;
            y = newCursorLeft + 1;
            x.TryFormat(buffer, out w);
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
        }

        if (endIndex < 0 && this.PromptLength > 0)
        {// Prompt
            span = this.CharArray.AsSpan(0, this.PromptLength);
            span.CopyTo(buffer);
            written += span.Length;
            buffer = buffer.Slice(span.Length);
        }

        // Input color
        span = ConsoleHelper.GetForegroundColorEscapeCode(this.ReadLineInstance.Options.InputColor).AsSpan();
        span.CopyTo(buffer);
        written += span.Length;
        buffer = buffer.Slice(span.Length);

        // Characters
        var maskingCharacter = this.ReadLineInstance.Options.MaskingCharacter;
        if (maskingCharacter == default)
        {// Plain
            span = this.charArray.AsSpan(startIndex, length);
            span.CopyTo(buffer);
            written += span.Length;
            buffer = buffer.Slice(span.Length);
        }
        else
        {// Masked
            buffer.Slice(0, totalWidth).Fill(maskingCharacter);
            written += totalWidth;
            buffer = buffer.Slice(totalWidth);
        }

        if (newCursorLeft == 0 && cursorDif > 0)
        {// New line at the end
            span = SimplePromptHelper.ForceNewLineCursor;
            span.CopyTo(buffer);
            written += span.Length;
            buffer = buffer.Slice(span.Length);
        }

        // Reset color
        span = ConsoleHelper.ResetSpan;
        span.CopyTo(buffer);
        written += span.Length;
        buffer = buffer.Slice(span.Length);

        if (removedWidth == 1)
        {
            buffer[0] = ' ';
            written += 1;
            buffer = buffer.Slice(1);
        }
        else if (removedWidth == 2)
        {
            buffer[0] = ' ';
            buffer[1] = ' ';
            written += 2;
            buffer = buffer.Slice(2);
        }

        if (eraseLine)
        {// Erase line
            if ((startCursor + totalWidth) % this.WindowWidth == 0)
            {// Add one space to clear the next line (add a space and move to the next line).
                buffer[0] = ' ';
                written += 1;
                buffer = buffer.Slice(1);
            }

            span = ConsoleHelper.EraseToEndOfLineSpan;
            span.CopyTo(buffer);
            written += span.Length;
            buffer = buffer.Slice(span.Length);
        }

        if (cursorDif != totalWidth || cursorDif == 0)
        {
            // Set cursor
            span = ConsoleHelper.SetCursorSpan;
            span.CopyTo(buffer);
            buffer = buffer.Slice(span.Length);
            written += span.Length;

            x = newCursorTop + 1;
            y = newCursorLeft + 1;
            x.TryFormat(buffer, out w);
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
        }

        // Show cursor
        span = ConsoleHelper.ShowCursorSpan;
        span.CopyTo(buffer);
        buffer = buffer.Slice(span.Length);
        written += span.Length;

        if (scroll > 0)
        {
            this.SimpleConsole.Scroll(scroll, true);
            newCursorTop -= scroll;
        }

        this.SimpleConsole.RawConsole.WriteInternal(windowBuffer.AsSpan(0, written));
        SimpleConsole.ReturnWindowBuffer(windowBuffer);
        this.SimpleConsole.CursorLeft = newCursorLeft;
        this.SimpleConsole.CursorTop = newCursorTop;

        this.ReadLineInstance.LinePosition = endIndex;
    }

    internal (int Left, int Top) ToCursor(int cursorIndex)
    {
        var top = cursorIndex / this.SimpleConsole.WindowWidth;
        var left = cursorIndex - (top * this.SimpleConsole.WindowWidth);
        return (left, top);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal int GetCursorIndex()
        => this.GetCursorIndex(this.CursorLeft, this.CursorTop);

    internal void SetCursorPosition(int cursorLeft, int cursorTop, CursorOperation cursorOperation)
    {
        try
        {
            if (cursorOperation == CursorOperation.Show ||
                cursorLeft != this.CursorLeft ||
                cursorTop != this.CursorTop)
            {
                this.SimpleConsole.SetCursorPosition(cursorLeft, this.Top + cursorTop, cursorOperation);
            }
        }
        catch
        {
        }
    }

    internal void ResetRows()
    {
        SimpleTextRow row;
        var start = 0;
        var windowWidth = this.SimpleConsole.WindowWidth;
        while (start < this.PromptLength)
        {// Prepare slices
            var width = 0;
            var end = start;
            var inputStart = start;
            while (end < this.PromptLength)
            {
                if (width + this.widthArray[end] > windowWidth)
                {// Immutable slice
                    inputStart = -1;
                    break;
                }
                else
                {// Mutable slice
                    width += this.widthArray[end];
                    end++;
                    inputStart = end;
                }
            }

            if (!this.IsInput)
            {
                inputStart = -1;
            }

            var length = end - start;
            row = SimpleTextRow.Rent(this);
            row.Prepare(start, inputStart, length, width);
            start = end;
        }
    }

    private void ClearLine()
    {
        Array.Fill<char>(this.charArray, ' ', this.PromptWidth, this.InputWidth);
        Array.Fill<byte>(this.widthArray, 1, this.PromptWidth, this.InputWidth);
        this.Write(this.PromptWidth, this.TotalWidth, 0, 0);

        this.Clear();
        this.ReadLineInstance.CurrentLocation.Reset(this);
    }

    private void Clear()
    {
        this._inputLength = 0;
        this._inputWidth = 0;

        this.ReleaseRows();
        this.ResetRows();
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private int GetCursorIndex(int cursorLeft, int cursorTop)
    {
        var index = cursorLeft + (cursorTop * this.SimpleConsole.WindowWidth);
        if (index < 0)
        {
            return 0;
        }
        else if (index >= this.TotalWidth)
        {
            return this.TotalWidth;
        }
        else
        {
            return index;
        }
    }

    private void MoveUpOrDown(bool up)
    {
        var line = this;
        var cursorLeft = this.CursorLeft;
        var cursorTop = this.CursorTop;

        if (up)
        {// Up arrow
            if (cursorTop <= 0)
            {// Previous buffer
                if (this.Index <= this.ReadLineInstance.FirstInputIndex)
                {
                    return;
                }

                line = this.ReadLineInstance.LineList[this.Index - 1];
                cursorTop = line.Height - 1;
            }
            else
            {// Current buffer (move upward)
                cursorTop--;
            }
        }
        else
        {// Down arrow
            if (cursorTop + 1 >= this.Height)
            {// Next buffer
                var idx = this.Index + 1;
                if (idx >= this.ReadLineInstance.BufferList.Count)
                {
                    return;
                }

                line = this.ReadLineInstance.LineList[this.Index + 1];
                cursorTop = 0;
            }
            else
            {// Current buffer (move downward)
                cursorTop++;
            }
        }

        var cursorIndex = line.GetCursorIndex(cursorLeft, cursorTop);
        line.TrimCursorIndex(ref cursorIndex);

        var newCursor = line.ToCursor(cursorIndex);
        if (line.CursorLeft != newCursor.Left ||
            line.CursorTop != newCursor.Top ||
            line != this)
        {
            line.SetCursorPosition(newCursor.Left, newCursor.Top, CursorOperation.None);
        }
    }

    private void TrimCursorIndex(ref int cursorIndex)
    {
        if (cursorIndex <= this.PromptWidth)
        {
            cursorIndex = this.PromptWidth;
            return;
        }

        var newIndex = 0;
        var totalLength = this.TotalLength;
        for (var i = this.PromptLength; i < totalLength; i++)
        {
            var width = this.widthArray[i];
            cursorIndex -= width;
            newIndex += width;
            if (cursorIndex <= 0)
            {
                break;
            }
        }

        cursorIndex = newIndex;
    }

    private void ProcessCharacterInternal(Span<char> charBuffer)
    {
        if (!this.ReadLineInstance.IsLengthWithinLimit(charBuffer.Length))
        {
            return;
        }

        this.EnsureBuffer(this.TotalLength + charBuffer.Length);
        if (!this.ReadLineInstance.CurrentLocation.TryGetLineAndRow(out var line, out var row))
        {
            return;
        }

        var position = Math.Min(this.ReadLineInstance.CurrentLocation.ArrayPosition, this.TotalLength);
        this.charArray.AsSpan(position, this.TotalLength - position).CopyTo(this.charArray.AsSpan(position + charBuffer.Length));
        charBuffer.CopyTo(this.charArray.AsSpan(position));
        this.widthArray.AsSpan(position, this.TotalLength - position).CopyTo(this.widthArray.AsSpan(position + charBuffer.Length));
        var width = 0;
        for (var i = 0; i < charBuffer.Length; i++)
        {
            int w;
            var c = charBuffer[i];
            if (char.IsHighSurrogate(c) && (i + 1) < charBuffer.Length && char.IsLowSurrogate(charBuffer[i + 1]))
            {
                var codePoint = char.ConvertToUtf32(c, charBuffer[i + 1]);
                w = SimplePromptHelper.GetCharWidth(codePoint);
                this.widthArray[position + i++] = 0;
                this.widthArray[position + i] = (byte)w;
            }
            else
            {
                w = SimplePromptHelper.GetCharWidth(c);
                this.widthArray[position + i] = (byte)w;
            }

            width += w;
        }

        row.AddInput(charBuffer.Length, width);

        this.Write(position, this.TotalLength, width, 0);
        this.ReadLineInstance.CurrentLocation.Move(charBuffer.Length, width);

        /*var line = this.FindLine();

        var heightChanged = this.ChangeLengthAndWidth(charBuffer.Length, width);
        if (heightChanged.Diff == 0)
        {
            this.Write(arrayPosition, this.Length, width, 0);//
            // if (this.CursorLeft == 0 && width > 0)
            // {
            //     this.readLineInstance.HeightChanged(heightChanged.Index, 1);
            // }
        }
        else
        {
            this.Write(arrayPosition, this.Length, width, 0, true);
            this.readLineInstance.HeightChanged(heightChanged.Index, heightChanged.Diff);
        }*/
    }

    private void EnsureBuffer(int capacity)
    {
        if (this.charArray.Length < capacity)
        {
            var newSize = CollectionHelper.CalculatePowerOfTwoCapacity(capacity);
            Array.Resize(ref this.charArray, newSize);
            Array.Resize(ref this.widthArray, newSize);
        }
    }

    private void SetPrompt(ReadOnlySpan<char> prompt)
    {
        // this.Uninitialize();

        this.EnsureBuffer(prompt.Length);
        prompt.CopyTo(this.charArray);
        for (var i = 0; i < prompt.Length; i++)
        {
            this.widthArray[i] = SimplePromptHelper.GetCharWidth(this.charArray[i]);
        }

        this._promptLength = prompt.Length;
        this._promptWidth = (int)BaseHelper.Sum(this.widthArray.AsSpan(0, this.PromptLength));

        this.ResetRows();
    }

    private void Initialize(SimpleConsole simpleConsole, ReadLineInstance readLineInstance, int index, ReadOnlySpan<char> prompt, bool isInput)
    {
        this._promptLength = 0;
        this._promptWidth = 0;
        this._inputLength = 0;
        this._inputWidth = 0;
        this.SimpleConsole = simpleConsole;
        this.ReadLineInstance = readLineInstance;
        this.Index = index;
        this.IsInput = isInput;
        this.SetPrompt(prompt);
    }

    private void Uninitialize()
    {
        this.SimpleConsole = default!;
        this.ReadLineInstance = default!;

        this.ReleaseRows();
    }

    private void ReleaseRows()
    {
        TemporaryList<SimpleTextRow> list = default;
        foreach (var x in this.Rows)
        {
            list.Add(x);
        }

        foreach (var x in list)
        {
            SimpleTextRow.Return(x);
        }
    }
}
