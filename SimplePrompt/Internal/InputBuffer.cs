// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using System;
using System.Runtime.CompilerServices;
using Arc;
using Arc.Unit;

#pragma warning disable SA1202 // Elements should be ordered by access

namespace SimplePrompt;

internal class InputBuffer
{
    private const int BufferSize = 1_024;
    private const int BufferMargin = 32;
    private const int MaxPromptWidth = 256;

    public SimpleConsole InputConsole { get; }

    public int Index { get; set; }

    public int Top { get; set; }

    /// <summary>
    /// Gets the cursor's horizontal position relative to the buffer's left edge.
    /// </summary>
    public int CursorLeft => this.InputConsole.CursorLeft;

    /// <summary>
    /// Gets the cursor's vertical position relative to the buffer's top edge.
    /// </summary>
    public int CursorTop => this.InputConsole.CursorTop - this.Top;

    public string? Prompt { get; private set; }

    public int PromtWidth { get; private set; }

    public int Length { get; set; }

    public int Width { get; set; }

    public int Height { get; set; }

    public int TotalWidth => this.PromtWidth + this.Width;

    public int WindowWidth => this.InputConsole.WindowWidth;

    public int WindowHeight => this.InputConsole.WindowHeight;

    public Span<char> TextSpan => this.charArray.AsSpan(0, this.Length);

    private char[] charArray = new char[BufferSize];
    private byte[] widthArray = new byte[BufferSize];

    public InputBuffer(SimpleConsole inputConsole)
    {
        this.InputConsole = inputConsole;
    }

    public override string ToString()
    {
        const int MaxLength = 32;
        if (this.TextSpan.Length <= MaxLength)
        {
            return new string(this.TextSpan);
        }

        return new string(this.TextSpan.Slice(0, MaxLength));
    }

    public bool ProcessInternal(ConsoleKeyInfo keyInfo, Span<char> charBuffer)
    {
        if (charBuffer.Length > 0)
        {
            var arrayPosition = this.GetArrayPosition();
            this.ProcessCharacterInternal(arrayPosition, charBuffer);
        }

        if (keyInfo.Key != ConsoleKey.None)
        {// Control
            var key = keyInfo.Key;
            if (key == ConsoleKey.Enter)
            {// Exit or Multiline """
                return true;
            }
            else if (key == ConsoleKey.Backspace)
            {
                if (this.Length == 0)
                {// Delete empty buffer
                    this.InputConsole.TryDeleteBuffer(this.Index);
                    return false;
                }

                var arrayPosition = this.GetArrayPosition();
                if (arrayPosition > 0)
                {
                    this.MoveLeft(arrayPosition);
                    if (char.IsLowSurrogate(this.charArray[arrayPosition - 1]) &&
                        (arrayPosition > 1) &&
                        char.IsHighSurrogate(this.charArray[arrayPosition - 2]))
                    {
                        var removedWidth = this.Remove2At(arrayPosition - 2);
                        this.Write(arrayPosition - 2, this.Length, 0, removedWidth);
                    }
                    else
                    {
                        var removedWidth = this.RemoveAt(arrayPosition - 1);
                        this.Write(arrayPosition - 1, this.Length, 0, removedWidth);
                    }

                    this.UpdateHeight(true);
                }

                return false;
            }
            else if (key == ConsoleKey.Delete)
            {
                if (this.Length == 0)
                {// Delete empty buffer
                    this.InputConsole.TryDeleteBuffer(this.Index);
                    return false;
                }

                var arrayPosition = this.GetArrayPosition();
                if (arrayPosition < this.Length)
                {
                    int removedWidth;
                    if (char.IsHighSurrogate(this.charArray[arrayPosition]) &&
                        (arrayPosition + 1) < this.Length &&
                        char.IsLowSurrogate(this.charArray[arrayPosition + 1]))
                    {
                        removedWidth = this.Remove2At(arrayPosition);
                    }
                    else
                    {
                        removedWidth = this.RemoveAt(arrayPosition);
                    }

                    this.Write(arrayPosition, this.Length, 0, removedWidth);
                    this.UpdateHeight(true);
                }

                return false;
            }
            else if (key == ConsoleKey.U && keyInfo.Modifiers == ConsoleModifiers.Control)
            {// Ctrl+U: Clear line
                this.ClearLine();
            }
            else if (key == ConsoleKey.Home)
            {
                this.SetCursorPosition(this.PromtWidth, 0, CursorOperation.None);
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
                if (this.InputConsole.MultilineMode)
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
                if (this.InputConsole.MultilineMode)
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

    internal void UpdateHeight(bool refresh)
    {
        var previousHeight = this.Height;
        this.Height = (this.TotalWidth + this.WindowWidth) / this.WindowWidth;
        if (refresh && previousHeight != this.Height)
        {
            this.InputConsole.HeightChanged(this.Index, this.Height - previousHeight);
        }
    }

    private void ClearLine()
    {
        Array.Fill<char>(this.charArray, ' ', 0, this.Width);
        Array.Fill<byte>(this.widthArray, 1, 0, this.Width);
        this.Length = this.Width;
        this.Write(0, this.Width, 0, 0);

        this.Length = 0;
        this.Width = 0;
        this.SetCursorPosition(this.PromtWidth, 0, CursorOperation.None);
        // this.UpdateConsole(0, this.Length, 0, true);
    }

    /*public int GetWidth()
    {
        return (int)BaseHelper.Sum(this.widthArray.AsSpan(0, this.Length));
    }*/

    public void Initialize(int index, string? prompt)
    {
        this.Index = index;
        if (prompt?.Length > MaxPromptWidth)
        {
            prompt = prompt.Substring(0, MaxPromptWidth);
        }

        this.Prompt = prompt;
        this.PromtWidth = SimplePromptHelper.GetWidth(this.Prompt);
        this.Length = 0;
        this.Width = 0;
        this.Height = 1;
    }

    private void EnsureCapacity(int capacity)
    {
        capacity += BufferMargin;
        if (this.charArray.Length < capacity)
        {
            var newSize = this.charArray.Length;
            while (newSize < capacity)
            {
                newSize *= 2;
            }

            Array.Resize(ref this.charArray, newSize);
            Array.Resize(ref this.widthArray, newSize);
        }
    }

    private void ProcessCharacterInternal(int arrayPosition, Span<char> charBuffer)
    {
        // var bufferWidth = InputConsoleHelper.GetWidth(charBuffer);

        if (this.InputConsole.IsInsertMode)
        {// Insert
            this.EnsureCapacity(this.Length + charBuffer.Length);

            this.charArray.AsSpan(arrayPosition, this.Length - arrayPosition).CopyTo(this.charArray.AsSpan(arrayPosition + charBuffer.Length));
            charBuffer.CopyTo(this.charArray.AsSpan(arrayPosition));
            this.widthArray.AsSpan(arrayPosition, this.Length - arrayPosition).CopyTo(this.widthArray.AsSpan(arrayPosition + charBuffer.Length));
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

            this.Length += charBuffer.Length;
            this.Width += width;
            this.Write(arrayPosition, this.Length, width, 0);
        }
        else
        {// Overtype (Not implemented yet)
            /*this.EnsureCapacity(arrayPosition + charBuffer.Length);

            charBuffer.CopyTo(this.charArray.AsSpan(arrayPosition));
            for (var i = 0; i < charBuffer.Length; i++)
            {
                var c = charBuffer[i];
                int width, dif;
                if (char.IsHighSurrogate(c) && (i + 1) < charBuffer.Length && char.IsLowSurrogate(charBuffer[i + 1]))
                {
                    var codePoint = char.ConvertToUtf32(c, charBuffer[i + 1]);
                    dif = InputConsoleHelper.GetCharWidth(codePoint);
                    width = dif;
                    dif -= this.widthArray[arrayPosition + i];
                    this.widthArray[arrayPosition + i++] = 0;
                    dif -= this.widthArray[arrayPosition + i];
                    this.widthArray[arrayPosition + i] = (byte)width;
                }
                else
                {
                    dif = InputConsoleHelper.GetCharWidth(c);
                    width = dif;
                    dif -= this.widthArray[arrayPosition + i];
                    this.widthArray[arrayPosition + i] = (byte)width;
                }

                this.Width += dif;
            }

            this.UpdateConsole(arrayPosition, arrayPosition + charBuffer.Length, 0);*/
        }
    }

    private int GetArrayPosition()
    {
        var index = this.GetCursorIndex();

        int arrayPosition;
        for (arrayPosition = 0; arrayPosition < this.Length; arrayPosition++)
        {
            if (index <= 0)
            {
                break;
            }

            index -= this.widthArray[arrayPosition];
        }

        return arrayPosition;
    }

    private void TrimCursorIndex(ref int cursorIndex)
    {
        if (cursorIndex <= 0)
        {
            cursorIndex = 0;
            return;
        }

        var newIndex = 0;
        for (var arrayPosition = 0; arrayPosition < this.Length; arrayPosition++)
        {
            var width = this.widthArray[arrayPosition];
            cursorIndex -= width;
            newIndex += width;
            if (cursorIndex <= 0)
            {
                break;
            }
        }

        cursorIndex = newIndex;
    }

    internal (int Left, int Top) ToCursor(int cursorIndex)
    {
        cursorIndex += this.PromtWidth;
        var top = cursorIndex / this.InputConsole.WindowWidth;
        var left = cursorIndex - (top * this.InputConsole.WindowWidth);
        return (left, top);
    }

    internal void Write(int startIndex, int endIndex, int cursorDif, int removedWidth, bool eraseLine = false)
    {
        int x, y, w;
        var length = endIndex < 0 ? this.Length : endIndex - startIndex;
        var charSpan = this.charArray.AsSpan(startIndex, length);
        var widthSpan = this.widthArray.AsSpan(startIndex, length);
        var totalWidth = endIndex < 0 ? this.TotalWidth : (int)BaseHelper.Sum(widthSpan);
        var startPosition = endIndex < 0 ? 0 : this.PromtWidth + (int)BaseHelper.Sum(this.widthArray.AsSpan(0, startIndex));

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

        startCursor += cursorDif;
        var newCursorLeft = startCursor % this.WindowWidth;
        var newCursorTop = startCursor / this.WindowWidth;
        var appendLineFeed = startCursor == (this.WindowWidth * this.WindowHeight);

        ReadOnlySpan<char> span;
        var buffer = this.InputConsole.WindowBuffer.AsSpan();
        var written = 0;

        // Hide cursor
        span = ConsoleHelper.HideCursorSpan;
        span.CopyTo(buffer);
        written += span.Length;
        buffer = buffer.Slice(span.Length);

        if (startCursorLeft != this.CursorLeft || startCursorTop != (this.Top + this.CursorTop))
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

        if (endIndex < 0 && this.Prompt is not null)
        {// Prompt
            span = this.Prompt.AsSpan();
            span.CopyTo(buffer);
            written += span.Length;
            buffer = buffer.Slice(span.Length);
        }

        // Input color
        span = ConsoleHelper.GetForegroundColorEscapeCode(this.InputConsole.InputColor).AsSpan();
        span.CopyTo(buffer);
        written += span.Length;
        buffer = buffer.Slice(span.Length);

        // Characters
        span = charSpan;
        span.CopyTo(buffer);
        written += span.Length;
        buffer = buffer.Slice(span.Length);

        if (appendLineFeed)
        {
            buffer[0] = '\n';
            written += 1;
            buffer = buffer.Slice(1);
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
            span = ConsoleHelper.EraseLineSpan;
            span.CopyTo(buffer);
            written += span.Length;
            buffer = buffer.Slice(span.Length);
        }

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

        // Show cursor
        span = ConsoleHelper.ShowCursorSpan;
        span.CopyTo(buffer);
        buffer = buffer.Slice(span.Length);
        written += span.Length;

        if (scroll > 0)
        {
            this.InputConsole.Scroll(scroll, true);
        }

        try
        {
            this.InputConsole.RawConsole.WriteInternal(this.InputConsole.WindowBuffer.AsSpan(0, written));
            // Console.Out.Write(this.InputConsole.WindowBuffer.AsSpan(0, written));

            // this.SetCursorPosition(newCursorLeft - this.Left, newCursorTop - this.Top, true);
            this.InputConsole.CursorLeft = newCursorLeft;
            this.InputConsole.CursorTop = newCursorTop;
        }
        catch
        {
        }
    }

    private int RemoveAt(int index)
    {
        var w = this.widthArray[index];
        this.Length--;
        this.Width -= w;
        this.charArray.AsSpan(index + 1, this.Length - index).CopyTo(this.charArray.AsSpan(index));
        this.widthArray.AsSpan(index + 1, this.Length - index).CopyTo(this.widthArray.AsSpan(index));
        return w;
    }

    private int Remove2At(int index)
    {
        var w = this.widthArray[index] + this.widthArray[index + 1];
        this.Length -= 2;
        this.Width -= w;
        this.charArray.AsSpan(index + 2, this.Length - index).CopyTo(this.charArray.AsSpan(index));
        this.widthArray.AsSpan(index + 2, this.Length - index).CopyTo(this.widthArray.AsSpan(index));
        return w;
    }

    private int GetLeftWidth(int index)
    {
        if (index < 1)
        {
            return 0;
        }

        if (char.IsLowSurrogate(this.charArray[index - 1]) &&
            index > 1 &&
            char.IsHighSurrogate(this.charArray[index - 2]))
        {
            return this.widthArray[index - 1] + this.widthArray[index - 2];
        }
        else
        {
            return this.widthArray[index - 1];
        }
    }

    private int GetRightWidth(int index)
    {
        if (index >= this.Length)
        {
            return 0;
        }

        if (char.IsHighSurrogate(this.charArray[index]) &&
            (index + 1) < this.Length &&
            char.IsLowSurrogate(this.charArray[index + 1]))
        {
            return this.widthArray[index] + this.widthArray[index + 1];
        }
        else
        {
            return this.widthArray[index];
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private int GetCursorIndex(int cursorLeft, int cursorTop)
    {
        var index = cursorLeft - this.PromtWidth + (cursorTop * this.InputConsole.WindowWidth);
        return index;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal int GetCursorIndex()
        => this.GetCursorIndex(this.CursorLeft, this.CursorTop);

    private void MoveLeft(int arrayPosition)
    {
        if (arrayPosition == 0)
        {
            return;
        }

        var width = this.GetLeftWidth(arrayPosition);
        var cursorIndex = this.GetCursorIndex() - width;
        if (cursorIndex >= 0)
        {
            var newCursor = this.ToCursor(cursorIndex);
            if (this.CursorLeft != newCursor.Left ||
                this.CursorTop != newCursor.Top)
            {
                this.SetCursorPosition(newCursor.Left, newCursor.Top, CursorOperation.None);
            }
        }
    }

    private void MoveRight(int arrayPosition)
    {
        if (arrayPosition >= this.Length)
        {
            return;
        }

        var width = this.GetRightWidth(arrayPosition);
        var cursorIndex = this.GetCursorIndex() + width;
        if (cursorIndex >= 0)
        {
            var newCursor = this.ToCursor(cursorIndex);
            if (this.CursorLeft != newCursor.Left ||
                this.CursorTop != newCursor.Top)
            {
                this.SetCursorPosition(newCursor.Left, newCursor.Top, CursorOperation.None);
            }
        }
    }

    private void MoveUpOrDown(bool up)
    {
        var buffer = this;
        var cursorLeft = this.CursorLeft;
        var cursorTop = this.CursorTop;

        if (up)
        {// Up arrow
            if (cursorTop <= 0)
            {// Previous buffer
                if (this.Index <= 0)
                {
                    return;
                }

                buffer = this.InputConsole.Buffers[this.Index - 1];
                cursorTop = buffer.Height - 1;
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
                if (this.Index + 1 >= this.InputConsole.Buffers.Count)
                {
                    return;
                }

                buffer = this.InputConsole.Buffers[this.Index + 1];
                cursorTop = 0;
            }
            else
            {// Current buffer (move downward)
                cursorTop++;
            }
        }

        var cursorIndex = buffer.GetCursorIndex(cursorLeft, cursorTop);
        buffer.TrimCursorIndex(ref cursorIndex);

        var newCursor = buffer.ToCursor(cursorIndex);
        if (buffer.CursorLeft != newCursor.Left ||
            buffer.CursorTop != newCursor.Top ||
            buffer != this)
        {
            buffer.SetCursorPosition(newCursor.Left, newCursor.Top, CursorOperation.None);
        }
    }

    /// <summary>
    /// Specifies the cursor position relative to the current InputBuffer’s Left and Top.
    /// </summary>
    private void SetCursorPosition(int cursorLeft, int cursorTop, CursorOperation cursorOperation)
    {
        try
        {
            if (cursorOperation == CursorOperation.Show ||
                cursorLeft != this.CursorLeft ||
                cursorTop != this.CursorTop)
            {
                this.InputConsole.SetCursorPosition(cursorLeft, this.Top + cursorTop, cursorOperation);
            }
        }
        catch
        {
        }
    }
}
