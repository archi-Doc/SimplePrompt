// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;
using Arc;
using Arc.Collections;
using Arc.Unit;

namespace SimplePrompt.Internal;

/// <summary>
/// Represents a logical input line (a prompt and its input) which is wrapped into one or more <see cref="SimpleTextRow"/>.
/// </summary>
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

    private readonly List<SimpleTextRow> rows = new();
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

    public int WindowWidth => this.SimpleConsole._windowWidth;

    public int WindowHeight => this.SimpleConsole._windowHeight;

    public int Index { get; internal set; }

    public bool IsInput { get; private set; }

    public int Top { get; set; }

    public int InitialCursorPosition { get; private set; }

    public int InitialRowIndex { get; private set; }

    public int Height => this.rows.Count;

    public int PromptLength => this._promptLength;

    public int PromptWidth => this._promptWidth;

    public int InputLength => this._inputLength;

    public int InputWidth => this._inputWidth;

    public int TotalLength => this.PromptLength + this.InputLength;

    public int TotalWidth => this.PromptWidth + this.InputWidth;

    internal List<SimpleTextRow> Rows => this.rows;

    internal char[] CharArray => this.charArray;

    internal byte[] WidthArray => this.widthArray;

    #endregion

    private SimpleTextLine()
    {
        this.SimpleConsole = default!;
        this.ReadLineInstance = default!;
    }

    public bool EndsWithEmptyRow => this.Rows.Count > 0 && this.Rows[this.Rows.Count - 1].Length == 0;

    internal ReadOnlySpan<char> PromptSpan => this.charArray.AsSpan(0, this.PromptLength);

    internal ReadOnlySpan<char> InputSpan => this.charArray.AsSpan(this.PromptLength, this.InputLength);

    public bool ProcessInternal(ConsoleKeyInfo keyInfo, Span<char> charBuffer)
    {
        if (charBuffer.Length > 0)
        {
            this.ProcessCharBuffer(charBuffer);
        }

        if (keyInfo.Key != ConsoleKey.None)
        {// Control
            var key = keyInfo.Key;
            if (key == ConsoleKey.Enter)
            {// Exit or Multiline """
                if (!this.ReadLineInstance.Options.AllowEmptyInput)
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
                if (this.ReadLineInstance.Mode.IsMultiline())
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
                if (this.ReadLineInstance.Mode.IsMultiline())
                {// Down
                    this.ReadLineInstance.CurrentLocation.MoveHorizontal(false);
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
        => this.rows.Count == 0 ? string.Empty : $"{this.rows.Count} lines: {this.rows[0].ToString()}";

    internal bool TryGetRowFromArrayPosition(int arrayPosition, [MaybeNullWhen(false)] out SimpleTextRow row)
    {
        if (this.Rows.Count == 0)
        {
            row = default;
            return false;
        }

        row = this.Rows[this.Rows.Count - 1];
        if (arrayPosition >= row.Start &&
            arrayPosition <= row.End)
        {
            return true;
        }

        for (var i = 0; i < this.Rows.Count - 1; i++)
        {
            row = this.Rows[i];
            if (arrayPosition >= row.Start &&
                arrayPosition < row.End)
            {
                return true;
            }
        }

        row = default;
        return false;
    }

    internal void Redraw()
        => this.Write(0, -1, false, 0, true);

    internal void Write(int startIndex, int endIndex, bool restoreCursor, int removedWidth, bool eraseLine = false)
    {
        if (endIndex < 0)
        {// The entire line
            startIndex = 0;
            endIndex = this.TotalLength;
        }

        var startCursor = this.GetCursor(startIndex);
        var endCursor = endIndex == this.TotalLength ? this.GetEndCursor() : this.GetCursor(endIndex);
        var scroll = endCursor.Top - this.WindowHeight + 1;

        var capacity = checked(Math.Max(this.TotalLength, this.PromptLength + this.InputWidth) + removedWidth + 128);
        var windowBuffer = SimpleConsole.RentWindowBuffer(capacity);
        var buffer = windowBuffer.AsSpan();

        // Hide cursor
        SimplePromptHelper.TryCopy(ConsoleHelper.HideCursorSpan, ref buffer);

        if (startCursor.Left != this.SimpleConsole._cursorLeft || startCursor.Top != this.SimpleConsole._cursorTop)
        {// Move cursor
            SimplePromptHelper.TryCopySetCursor(ref buffer, startCursor.Left, startCursor.Top);
        }

        if (restoreCursor)
        {// Save cursor
            SimplePromptHelper.TryCopy(ConsoleHelper.SaveCursorSpan, ref buffer);
        }

        if (startIndex < this.PromptLength)
        {// Prompt
            SimplePromptHelper.TryCopy(this.charArray.AsSpan(0, this.PromptLength), ref buffer);
            startIndex = this.PromptLength;
        }

        var length = endIndex - startIndex;

        // Input color
        var colorSpan = this.SimpleConsole.GetColorEscapeCode(this.ReadLineInstance.Options.InputColor);
        SimplePromptHelper.TryCopy(colorSpan, ref buffer);

        // Characters
        var maskingCharacter = this.ReadLineInstance.Options.MaskingCharacter;
        if (maskingCharacter == default)
        {// Plain
            SimplePromptHelper.TryCopy(this.charArray.AsSpan(startIndex, length), ref buffer);
        }
        else
        {// Masked
            var totalWidth = (int)BaseHelper.Sum(this.widthArray.AsSpan(startIndex, length));
            if (totalWidth <= buffer.Length)
            {
                buffer.Slice(0, totalWidth).Fill(maskingCharacter);
                buffer = buffer.Slice(totalWidth);
            }
        }

        if (endCursor.Left == 0)
        {// New line at the end
            SimplePromptHelper.TryCopy(SimplePromptHelper.ForceNewLineCursor, ref buffer);
        }

        // Reset color
        if (colorSpan.Length > 0)
        {
            SimplePromptHelper.TryCopy(ConsoleHelper.ResetSpan, ref buffer);
        }

        if (removedWidth > 0 && removedWidth <= buffer.Length)
        {// Erase the columns that the removed character occupied.
            buffer.Slice(0, removedWidth).Fill(' ');
            buffer = buffer.Slice(removedWidth);
        }

        if (eraseLine)
        {// Erase line
            SimplePromptHelper.TryCopy(ConsoleHelper.EraseToEndOfLineSpan, ref buffer);
        }

        if (restoreCursor)
        {// Restore cursor
            SimplePromptHelper.TryCopy(ConsoleHelper.RestoreCursorSpan, ref buffer);
        }

        // Show cursor
        SimplePromptHelper.TryCopy(ConsoleHelper.ShowCursorSpan, ref buffer);

        if (scroll > 0)
        {
            this.SimpleConsole.Scroll(scroll, true);
        }
        else
        {
            scroll = 0;
        }

        this.SimpleConsole.RawConsole.WriteInternal(windowBuffer.AsSpan(0, windowBuffer.Length - buffer.Length));
        SimpleConsole.ReturnWindowBuffer(windowBuffer);

        if (restoreCursor)
        {
            this.SimpleConsole._cursorLeft = startCursor.Left;
            this.SimpleConsole._cursorTop = startCursor.Top - scroll;
        }
        else
        {
            this.SimpleConsole._cursorLeft = endCursor.Left;
            this.SimpleConsole._cursorTop = endCursor.Top - scroll;
        }

        if (this.SimpleConsole._cursorLeft == 0)
        {
            this.SimpleConsole.SetCursorPosition(this.SimpleConsole._cursorLeft, this.SimpleConsole._cursorTop, CursorOperation.None);
        }
    }

    internal (int Left, int Top, int RowIndex) GetCursor(int arrayIndex)
    {
        if (arrayIndex < 0 || arrayIndex > this.TotalLength)
        {
            return (this.InitialCursorPosition, this.Top + this.InitialRowIndex, this.InitialRowIndex);
        }

        for (var i = 0; i < this.Rows.Count; i++)
        {
            var row = this.Rows[i];
            if (row.Start <= arrayIndex &&
                arrayIndex < row.End)
            {
                var left = (int)BaseHelper.Sum(this.WidthArray.AsSpan(row.Start, arrayIndex - row.Start));
                return (left, this.Top + i, i);
            }
        }

        return this.GetEndCursor();
    }

    internal void Clear()
    {
        this._inputLength = 0;
        this._inputWidth = 0;

        this.ReleaseRows();
        this.ResetRows();
    }

    internal void UpdateInitialLocation()
    {
        if (this.TryGetRowFromArrayPosition(this.PromptLength, out var row))
        {
            this.InitialRowIndex = row.Index;
            this.InitialCursorPosition = row.ArrayPositionToCursorPosition(this.PromptLength);
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private (int Left, int Top, int RowIndex) GetEndCursor()
    {
        var lastIndex = this.Rows.Count - 1;
        if (lastIndex < 0)
        {
            return (this.InitialCursorPosition, this.Top + this.InitialRowIndex, this.InitialRowIndex);
        }

        return (this.Rows[lastIndex].Width, this.Top + lastIndex, lastIndex);
    }

    private void ResetRows()
    {
        var row = SimpleTextRow.Rent(this);
        row.Prepare(0, this.IsInput ? this.PromptLength : -1, this.TotalLength, this.TotalWidth);
        bool rowChanged = false;
        int widthDiff = 0;
        bool emptyRow = false;
        row.Arrange(ref rowChanged, ref widthDiff, ref emptyRow);
    }

    private int RemoveBuffer(int index, int count)
    {// Removes 'count' characters at 'index' and returns the removed width.
        var width = 0;
        for (var i = 0; i < count; i++)
        {
            width += this.widthArray[index + i];
        }

        var remaining = this.TotalLength - index - count;
        if (remaining > 0)
        {
            this.charArray.AsSpan(index + count, remaining).CopyTo(this.charArray.AsSpan(index));
            this.widthArray.AsSpan(index + count, remaining).CopyTo(this.widthArray.AsSpan(index));
        }

        return width;
    }

    private void ProcessDelete(bool backspace)
    {
        if (this.InputLength == 0)
        {// Delete empty buffer
            if (backspace || this.Index < this.ReadLineInstance.LineList.Count - 1)
            {
                this.ReadLineInstance.TryDeleteLine(this.Index, backspace);
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

        // A surrogate pair (high surrogate followed by a low surrogate) must be removed as a single character.
        var removedLength = (char.IsHighSurrogate(this.charArray[location.ArrayPosition]) &&
            ((location.ArrayPosition + 1) < this.TotalLength) &&
            char.IsLowSurrogate(this.charArray[location.ArrayPosition + 1])) ? 2 : 1;
        var removedWidth = this.RemoveBuffer(location.ArrayPosition, removedLength);

        var previousHeight = this.Height;
        var result = row.AddInput(-removedLength, -removedWidth);
        this.Write(location.ArrayPosition, this.TotalLength, true, Math.Max(0, -result.WidthDiff), eraseLine: true);

        if (result.RowChanged)
        {
            this.ReadLineInstance.HeightChanged(this, this.Height - previousHeight);
        }
    }

    private void ClearLine()
    {
        // Overwrite the displayed input (InputWidth columns) with spaces, then drop the content.
        var inputWidth = this.InputWidth;
        this.EnsureBuffer(this.PromptLength + inputWidth);
        Array.Fill<char>(this.charArray, ' ', this.PromptLength, inputWidth);
        Array.Fill<byte>(this.widthArray, 1, this.PromptLength, inputWidth);
        this._inputLength = inputWidth;
        this.Write(this.PromptLength, this.TotalLength, false, 0);

        if (this.Rows.Count > 1)
        {
            this.ReadLineInstance.HeightChanged(this, 1 - this.Rows.Count);
        }

        this.Clear();
        this.ReadLineInstance.CurrentLocation.Reset(this, CursorOperation.ForceSet);
    }

    private void ProcessCharBuffer(Span<char> charBuffer)
    {
        var remaining = this.ReadLineInstance.GetRemainingLength();
        if (charBuffer.Length > remaining)
        {// Accept only the characters which fit within MaxInputLength.
            if (remaining > 0 && char.IsHighSurrogate(charBuffer[remaining - 1]))
            {// Do not split a surrogate pair.
                remaining--;
            }

            if (remaining <= 0)
            {
                return;
            }

            charBuffer = charBuffer.Slice(0, remaining);
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

        var previousHeight = this.Height;
        var result = row.AddInput(charBuffer.Length, width);
        if (result.RowChanged)
        {// Height changed
            this.ReadLineInstance.HeightChanged(this, this.Height - previousHeight);
        }

        this.Write(position, this.TotalLength, false, 0);
        this.ReadLineInstance.CurrentLocation.Advance(charBuffer.Length, width);
        this.ReadLineInstance.CurrentLocation.LocationToCursor();
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
            var c = this.charArray[i];
            if (char.IsHighSurrogate(c) && (i + 1) < prompt.Length && char.IsLowSurrogate(this.charArray[i + 1]))
            {// A surrogate pair: the width is assigned to the low surrogate (the same layout as the input buffer).
                this.widthArray[i++] = 0;
                this.widthArray[i] = SimplePromptHelper.GetCharWidth(char.ConvertToUtf32(c, this.charArray[i]));
            }
            else
            {
                this.widthArray[i] = SimplePromptHelper.GetCharWidth(c);
            }
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
    {// SimpleTextRow.Return() removes the row from this.rows, so iterate backwards.
        for (var i = this.rows.Count - 1; i >= 0; i--)
        {
            SimpleTextRow.Return(this.rows[i]);
        }
    }
}
