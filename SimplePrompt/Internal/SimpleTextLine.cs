// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using System.Globalization;
using System.Runtime.CompilerServices;
using Arc;
using Arc.Collections;
using Arc.Unit;
using static Arc.Unit.UnitMessage;

namespace SimplePrompt.Internal;

internal sealed class SimpleTextLine
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

#pragma warning disable SA1202 // Elements should be ordered by access
#pragma warning disable SA1401 // Fields should be private
    internal int _inputLength;
    internal int _inputWidth;
#pragma warning restore SA1401 // Fields should be private
#pragma warning restore SA1202 // Elements should be ordered by access

    public SimpleConsole SimpleConsole { get; private set; }

    public ReadLineInstance ReadLineInstance { get; private set; }

    public int WindowWidth => this.SimpleConsole.WindowWidth;

    public int WindowHeight => this.SimpleConsole.WindowHeight;

    public int Index { get; internal set; }

    public bool IsInput { get; private set; }

    public int Top { get; set; }

    public int InitialCursorPosition { get; private set; }

    public int InitialRowIndex { get; private set; }

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

    /*[MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal void ChangeInputLengthAndWidth(int lengthDiff, int widthDiff)
    {
        this._inputLength += lengthDiff;
        this._inputWidth += widthDiff;
    }*/

    internal ReadOnlySpan<char> PromptSpan => this.charArray.AsSpan(0, this.PromptLength);

    internal ReadOnlySpan<char> InputSpan => this.charArray.AsSpan(this.PromptLength, this.InputLength);

    public bool ProcessInternal(ConsoleKeyInfo keyInfo, Span<char> charBuffer)
    {
        if (charBuffer.Length > 0)
        {
            this.ProcessCharBuffer(charBuffer);
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
            else if (key == ConsoleKey.Backspace)
            {
                this.ProcessDelete(true);
            }
            else if (key == ConsoleKey.Delete)
            {
                this.ProcessDelete(false);
            }
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
                this.ReadLineInstance.CurrentLocation.MoveLeft(true);
            }
            else if (key == ConsoleKey.RightArrow)
            {
                this.ReadLineInstance.CurrentLocation.MoveRight();
            }
            else if (key == ConsoleKey.UpArrow)
            {// History or move line
                if (this.ReadLineInstance.Mode.IsMultiline)
                {// Up
                    this.ReadLineInstance.CurrentLocation.MoveHorizontal(true);
                    // this.ReadLineInstance.CurrentLocation.ChangeLine(-1, true);
                }
                else
                {// History
                }

                return false;
            }
            else if (key == ConsoleKey.DownArrow)
            {// History or move line
                if (this.ReadLineInstance.Mode.IsMultiline)
                {// Down
                    this.ReadLineInstance.CurrentLocation.MoveHorizontal(false);
                    // this.ReadLineInstance.CurrentLocation.ChangeLine(+1, true);
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

    internal void Redraw()
        => this.Write(0, -1, false, 0, true);

    internal void Write(int startIndex, int endIndex, bool restoreCursor, int removedWidth, bool eraseLine = false)
    {
        int x, y, w, length;
        if (endIndex < 0)
        {
            startIndex = 0;
            endIndex = this.TotalLength;
            length = this.InputLength;
        }
        else
        {
            length = endIndex - startIndex;
        }

        var startCursor = this.GetCursor(startIndex);
        var endCursor = endIndex == this.TotalLength ? this.GetEndCursor() : this.GetCursor(endIndex);
        var scroll = endCursor.Top - this.WindowHeight;

        ReadOnlySpan<char> span;
        var windowBuffer = SimpleConsole.RentWindowBuffer();
        var buffer = windowBuffer.AsSpan();
        var written = 0;

        // Hide cursor
        span = ConsoleHelper.HideCursorSpan;
        span.CopyTo(buffer);
        written += span.Length;
        buffer = buffer.Slice(span.Length);

        if (startCursor.Left != this.SimpleConsole.CursorLeft || startCursor.Top != this.SimpleConsole.CursorTop)
        {// Move cursor
            span = ConsoleHelper.SetCursorSpan;
            span.CopyTo(buffer);
            buffer = buffer.Slice(span.Length);
            written += span.Length;

            x = startCursor.Top + 1;
            y = startCursor.Left + 1;
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

        if (restoreCursor)
        {// Save cursor
            span = ConsoleHelper.SaveCursorSpan;
            span.CopyTo(buffer);
            written += span.Length;
            buffer = buffer.Slice(span.Length);
        }

        if (startIndex < this.PromptLength && this.PromptLength > 0)
        {// Prompt
            startIndex = this.PromptLength;
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
            var widthSpan = this.widthArray.AsSpan(startIndex, length);
            var totalWidth = endIndex < 0 ? this.TotalWidth : (int)BaseHelper.Sum(widthSpan);
            buffer.Slice(0, totalWidth).Fill(maskingCharacter);
            written += totalWidth;
            buffer = buffer.Slice(totalWidth);
        }

        /*if (endCursor.Left == 0)
        {// New line at the end
            span = SimplePromptHelper.ForceNewLineCursor;
            span.CopyTo(buffer);
            written += span.Length;
            buffer = buffer.Slice(span.Length);
        }*/

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
            /*if ((startCursor + totalWidth) % this.WindowWidth == 0)
            {// Add one space to clear the next line (add a space and move to the next line).
                buffer[0] = ' ';
                written += 1;
                buffer = buffer.Slice(1);
            }*/

            span = ConsoleHelper.EraseToEndOfLineSpan;
            span.CopyTo(buffer);
            written += span.Length;
            buffer = buffer.Slice(span.Length);
        }

        if (restoreCursor)
        {// Restore cursor
            span = ConsoleHelper.RestoreCursorSpan;
            span.CopyTo(buffer);
            written += span.Length;
            buffer = buffer.Slice(span.Length);
        }

        // Show cursor
        span = ConsoleHelper.ShowCursorSpan;
        span.CopyTo(buffer);
        buffer = buffer.Slice(span.Length);
        written += span.Length;

        if (scroll > 0)
        {
            this.SimpleConsole.Scroll(scroll, true);
        }
        else
        {
            scroll = 0;
        }

        this.SimpleConsole.RawConsole.WriteInternal(windowBuffer.AsSpan(0, written));
        SimpleConsole.ReturnWindowBuffer(windowBuffer);

        if (restoreCursor)
        {
            this.SimpleConsole.CursorLeft = startCursor.Left;
            this.SimpleConsole.CursorTop = startCursor.Top - scroll;
        }
        else
        {
            this.SimpleConsole.CursorLeft = endCursor.Left;
            this.SimpleConsole.CursorTop = endCursor.Top - scroll;
        }

        this.ReadLineInstance.LinePosition = endIndex;

        if (this.SimpleConsole.CursorLeft == 0)
        {
            this.SimpleConsole.SetCursorPosition(this.SimpleConsole.CursorLeft, this.SimpleConsole.CursorTop, CursorOperation.None);
        }
    }

    private (int Left, int Top) GetCursor(int arrayIndex)
    {
        if (arrayIndex < 0 || arrayIndex > this.TotalLength)
        {
            return (this.InitialCursorPosition, this.InitialRowIndex);
        }

        for (var i = 0; i < this.Rows.Count; i++)
        {
            var row = this.Rows.ListChain[i];
            if (row.Start <= arrayIndex &&
                arrayIndex < row.End)
            {
                var left = (int)BaseHelper.Sum(this.WidthArray.AsSpan(row.Start, arrayIndex - row.Start));
                return (left, this.Top + i);
            }
        }

        return (this.InitialCursorPosition, this.InitialRowIndex);
    }

    private (int Left, int Top) GetEndCursor()
    {
        var count = this.Rows.Count;
        if (count == 0)
        {
            return (this.InitialCursorPosition, this.InitialRowIndex);
        }

        return (this.Rows.ListChain[count - 1].Width, this.Top + count - 1);
    }

    /*internal (int Left, int Top) ToCursor(int cursorIndex)
    {
        var top = cursorIndex / this.SimpleConsole.WindowWidth;
        var left = cursorIndex - (top * this.SimpleConsole.WindowWidth);
        return (left, top);
    }

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
    }*/

    private void ResetRows()
    {
        this.InitialRowIndex = 0;
        this.InitialCursorPosition = 0;

        SimpleTextRow row;
        var start = 0;
        var windowWidth = this.SimpleConsole.WindowWidth;
        while (start < this.PromptLength)
        {// Prepare rows
            var width = 0;
            var end = start;
            var inputStart = start;
            while (end < this.PromptLength)
            {
                if (width + this.widthArray[end] > windowWidth)
                {// Immutable row
                    inputStart = -1;
                    break;
                }
                else
                {// Mutable row
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

            if (inputStart >= 0)
            {
                this.InitialRowIndex = row.ListLink.Index;
                this.InitialCursorPosition = width;
            }
        }
    }

    private int RemoveBuffer1(int index)
    {
        var width = this.widthArray[index];
        this.charArray.AsSpan(index + 1, this.TotalLength - index).CopyTo(this.charArray.AsSpan(index));
        this.widthArray.AsSpan(index + 1, this.TotalLength - index).CopyTo(this.widthArray.AsSpan(index));

        return width;
    }

    private int RemoveBuffer2(int index)
    {
        var width = this.widthArray[index] + this.widthArray[index + 1];
        this.charArray.AsSpan(index + 2, this.TotalLength - index).CopyTo(this.charArray.AsSpan(index));
        this.widthArray.AsSpan(index + 2, this.TotalLength - index).CopyTo(this.widthArray.AsSpan(index));

        return width;
    }

    private void ProcessDelete(bool backspace)
    {
        if (this.InputLength == 0)
        {// Delete empty buffer
            if (backspace || this.Index < this.ReadLineInstance.LineList.Count - 1)
            {
                this.ReadLineInstance.TryDeleteBuffer(this.Index, backspace);
                return;
            }
        }

        var location = this.ReadLineInstance.CurrentLocation;
        if (backspace)
        {
            if (!location.MoveLeft(false))
            {
                return;
            }
        }

        if (!location.TryGetLineAndRow(out var line, out var row))
        {
            return;
        }

        if (location.ArrayPosition >= line.TotalLength)
        {
            return;
        }

        int removedLength;
        int removedWidth;
        if (char.IsLowSurrogate(this.charArray[location.ArrayPosition]) &&
        ((location.ArrayPosition + 1) < this.TotalLength) &&
        char.IsHighSurrogate(this.charArray[location.ArrayPosition + 1]))
        {
            removedLength = 2;
            removedWidth = this.RemoveBuffer2(location.ArrayPosition);
        }
        else
        {
            removedLength = 1;
            removedWidth = this.RemoveBuffer1(location.ArrayPosition);
        }

        var heightChanged = row.AddInput(-removedLength, -removedWidth);
        this.Write(location.ArrayPosition, this.TotalLength, true, removedWidth);

        if (heightChanged)
        {
            this.ReadLineInstance.HeightChanged(row, -1);
        }

        /*if (r.Diff != 0)
        {
            this.readLineInstance.HeightChanged(r.Index, r.Diff);
        }*/
    }

    private void ClearLine()
    {
        Array.Fill<char>(this.charArray, ' ', this.PromptWidth, this.InputWidth);
        Array.Fill<byte>(this.widthArray, 1, this.PromptWidth, this.InputWidth);
        this.Write(this.PromptWidth, this.TotalWidth, false, 0);

        if (this.Rows.Count > 1)
        {
            var row = this.Rows.ListChain[0];
            this.ReadLineInstance.HeightChanged(row, 1 - this.Rows.Count);
        }

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

    private void ProcessCharBuffer(Span<char> charBuffer)
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

        if (row.AddInput(charBuffer.Length, width))
        {// Height changed
            this.ReadLineInstance.HeightChanged(row, +1);
            this.Write(position, this.TotalLength, false, 0);
            this.ReadLineInstance.CurrentLocation.Advance(charBuffer.Length, width);
            this.ReadLineInstance.CurrentLocation.LocationToCursor();
        }
        else
        {
            this.Write(position, this.TotalLength, false, 0);
            this.ReadLineInstance.CurrentLocation.Advance(charBuffer.Length, width);
            this.ReadLineInstance.CurrentLocation.LocationToCursor();
        }

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
