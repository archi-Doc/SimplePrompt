// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using System.Runtime.CompilerServices;
using Arc;
using Arc.Collections;
using Arc.Unit;

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

    private readonly SimpleTextRow.GoshujinClass slices = new();
    private SimpleConsole simpleConsole;
    private ReadLineInstance readLineInstance;
    private char[] charArray = new char[InitialBufferSize];
    private byte[] widthArray = new byte[InitialBufferSize];
    private int _promptLength;
    private int _promptWidth;
    private int _inputLength;
    private int _inputWidth;

    public int WindowWidth => this.simpleConsole.WindowWidth;

    public int WindowHeight => this.simpleConsole.WindowHeight;

    public int Index { get; private set; }

    public bool IsInput { get; private set; }

    public int Top { get; set; }

    public int Height { get; private set; }

    public int PromptLength => this._promptLength;

    public int PromptWidth => this._promptWidth;

    public int InputLength => this._inputLength;

    public int InputWidth => this._inputWidth;

    public int TotalLength => this.PromptLength + this.InputLength;

    public int TotalWidth => this.PromptWidth + this.InputWidth;

    internal SimpleTextRow.GoshujinClass Slices => this.slices;

    internal char[] CharArray => this.charArray;

    internal byte[] WidthArray => this.widthArray;

    internal bool IsEmpty => this.slices.Count == 0;

    #endregion

    private SimpleTextLine()
    {
        this.simpleConsole = default!;
        this.readLineInstance = default!;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal void ChangePromptLengthAndWidth(int lengthDiff, int widthDiff)
    {
        this._promptLength += lengthDiff;
        this._promptWidth += widthDiff;
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
            this.simpleConsole.CheckCursor();
        }

        /*if (keyInfo.Key != ConsoleKey.None)
        {// Control
            var key = keyInfo.Key;
            if (key == ConsoleKey.Enter)
            {// Exit or Multiline """
                if (!this.readLineInstance.Options.AllowEmptyLineInput)
                {
                    if (this.readLineInstance.BufferList.Count == 0 ||
                    (this.readLineInstance.BufferList.Count == 1 &&
                    this.readLineInstance.BufferList[0].Length == 0))
                    {// Empty input
                        return false;
                    }
                }

                return true;
            }
            else if (key == ConsoleKey.Backspace)
            {
                if (this.InputLength == 0)
                {// Delete empty buffer
                    this.readLineInstance.TryDeleteBuffer(this.Index);
                    return false;
                }

                var arrayPosition = this.GetArrayPosition();
                if (arrayPosition > 0)
                {
                    (int RemovedWidth, int Index, int Diff) r;
                    this.MoveLeft(arrayPosition);
                    if (char.IsLowSurrogate(this.charArray[arrayPosition - 1]) &&
                        (arrayPosition > 1) &&
                        char.IsHighSurrogate(this.charArray[arrayPosition - 2]))
                    {
                        r = this.Remove2At(arrayPosition - 2);
                        this.Write(arrayPosition - 2, this.Length, 0, r.RemovedWidth);
                    }
                    else
                    {
                        r = this.RemoveAt(arrayPosition - 1);
                        this.Write(arrayPosition - 1, this.Length, 0, r.RemovedWidth);
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

                var arrayPosition = this.GetArrayPosition();
                if (arrayPosition < this.Length)
                {
                    (int RemovedWidth, int Index, int Diff) r;
                    if (char.IsHighSurrogate(this.charArray[arrayPosition]) &&
                        (arrayPosition + 1) < this.Length &&
                        char.IsLowSurrogate(this.charArray[arrayPosition + 1]))
                    {
                        r = this.Remove2At(arrayPosition);
                    }
                    else
                    {
                        r = this.RemoveAt(arrayPosition);
                    }

                    this.Write(arrayPosition, this.Length, 0, r.RemovedWidth);
                    if (r.Diff != 0)
                    {
                        this.readLineInstance.HeightChanged(r.Index, r.Diff);
                    }
                }

                return false;
            }
            else if (key == ConsoleKey.U && keyInfo.Modifiers == ConsoleModifiers.Control)
            {// Ctrl+U: Clear line
                this.ClearLine();
            }
            else if (key == ConsoleKey.Home)
            {
                this.SetCursorPosition(this.PromptWidth, 0, CursorOperation.None);
            }
            else if (key == ConsoleKey.End)
            {
                var newCursor = this.ToCursor(this.Width);
                this.SetCursorPosition(newCursor.Left, newCursor.Top, CursorOperation.None);
            }
            else if (key == ConsoleKey.LeftArrow)
            {
                var arrayPosition = this.GetArrayPosition();
                this.MoveLeft(arrayPosition);
                return false;
            }
            else if (key == ConsoleKey.RightArrow)
            {
                var arrayPosition = this.GetArrayPosition();
                this.MoveRight(arrayPosition);
                return false;
            }
            else if (key == ConsoleKey.UpArrow)
            {// History or move line
                if (this.readLineInstance.MultilineMode)
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
                if (this.readLineInstance.MultilineMode)
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
        */

        return false;
    }

    public override string ToString()
    {
        if (this.slices.Count == 0)
        {
            return string.Empty;
        }
        else
        {
            return $"{this.slices.Count} lines: {this.slices.SliceChain.First?.ToString()}";
        }
    }

    internal (int Index, int Diff) UpdateHeight()
    {
        var previousHeight = this.Height;
        var totalWidth = this.TotalWidth;
        if (totalWidth == 0)
        {
            this.Height = 1;
        }
        else
        {
            this.Height = (totalWidth - 0 + this.WindowWidth) / this.WindowWidth;
        }

        return (this.Index, this.Height - previousHeight);
    }

    internal void Write(int startIndex, int endIndex, int cursorDif, int removedWidth, bool eraseLine = false)
    {
        int x, y, w;
        var length = endIndex < 0 ? this.TotalLength : endIndex - startIndex;
        var widthSpan = this.widthArray.AsSpan(startIndex, length);
        var totalWidth = endIndex < 0 ? this.TotalWidth : (int)BaseHelper.Sum(widthSpan);
        var startPosition = endIndex < 0 ? 0 : this.PromptWidth + (int)BaseHelper.Sum(this.widthArray.AsSpan(0, startIndex));

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

        if (startCursorLeft != this.simpleConsole.CursorLeft || startCursorTop != (this.Top + this.simpleConsole.CursorTop))
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
        span = ConsoleHelper.GetForegroundColorEscapeCode(this.readLineInstance.Options.InputColor).AsSpan();
        span.CopyTo(buffer);
        written += span.Length;
        buffer = buffer.Slice(span.Length);

        // Characters
        var maskingCharacter = this.readLineInstance.Options.MaskingCharacter;
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
            this.simpleConsole.Scroll(scroll, true);
            newCursorTop -= scroll;
        }

        this.simpleConsole.RawConsole.WriteInternal(windowBuffer.AsSpan(0, written));
        SimpleConsole.ReturnWindowBuffer(windowBuffer);
        this.simpleConsole.CursorLeft = newCursorLeft;
        this.simpleConsole.CursorTop = newCursorTop;
    }

    private (int ArrayPosition, SimpleTextRow Row) GetArrayPosition()
    {
        var position = Math.Max(this.PromptLength, this.readLineInstance.BufferPosition);
        if (position > this.TotalLength)
        {
            position = this.TotalLength;
        }

        foreach (var x in this.slices)
        {
            if (x.Start <= position &&
                position <= x.End)
            {
                return (position, x);
            }
        }

        throw new Exception();
    }

    private void ProcessCharacterInternal(Span<char> charBuffer)
    {
        if (!this.readLineInstance.IsLengthWithinLimit(charBuffer.Length))
        {
            return;
        }

        this.EnsureBuffer(this.TotalLength + charBuffer.Length);
        (var arrayPosition, var row) = this.GetArrayPosition();

        this.charArray.AsSpan(arrayPosition, this.TotalLength - arrayPosition).CopyTo(this.charArray.AsSpan(arrayPosition + charBuffer.Length));
        charBuffer.CopyTo(this.charArray.AsSpan(arrayPosition));
        this.widthArray.AsSpan(arrayPosition, this.TotalLength - arrayPosition).CopyTo(this.widthArray.AsSpan(arrayPosition + charBuffer.Length));
        var width = 0;
        for (var i = 0; i < charBuffer.Length; i++)
        {
            int w;
            var c = charBuffer[i];
            if (char.IsHighSurrogate(c) && (i + 1) < charBuffer.Length && char.IsLowSurrogate(charBuffer[i + 1]))
            {
                var codePoint = char.ConvertToUtf32(c, charBuffer[i + 1]);
                w = SimplePromptHelper.GetCharWidth(codePoint);
                this.widthArray[arrayPosition + i++] = 0;
                this.widthArray[arrayPosition + i] = (byte)w;
            }
            else
            {
                w = SimplePromptHelper.GetCharWidth(c);
                this.widthArray[arrayPosition + i] = (byte)w;
            }

            width += w;
        }

        row.AddInput(charBuffer.Length, width);

        this.Write(arrayPosition, this.TotalLength, width, 0);

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
        var promptLength = prompt.Length;
        for (var i = 0; i < prompt.Length; i++)
        {
            this.widthArray[i] = SimplePromptHelper.GetCharWidth(this.charArray[i]);
        }

        var promptWidth = (int)BaseHelper.Sum(this.widthArray.AsSpan(0, this.PromptLength));

        SimpleTextRow slice;
        var start = 0;
        var windowWidth = this.simpleConsole.WindowWidth;
        while (start < prompt.Length)
        {// Prepare slices
            var width = 0;
            var end = start;
            var inputStart = start;
            while (end < prompt.Length)
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
            slice = SimpleTextRow.Rent(this);
            slice.Prepare(start, inputStart, length, width);
            this.ChangePromptLengthAndWidth(length, width);
            start = end;
        }
    }

    private void Initialize(SimpleConsole simpleConsole, ReadLineInstance readLineInstance, int index, ReadOnlySpan<char> prompt, bool isInput)
    {
        this.simpleConsole = simpleConsole;
        this.readLineInstance = readLineInstance;
        this.Index = index;
        this.IsInput = isInput;
        this.SetPrompt(prompt);
    }

    private void Uninitialize()
    {
        this.simpleConsole = default!;
        this.readLineInstance = default!;
        foreach (var x in this.slices)
        {
            SimpleTextRow.Return(x);
        }
    }
}
