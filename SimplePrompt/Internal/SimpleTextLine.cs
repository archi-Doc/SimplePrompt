// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using System.Runtime.CompilerServices;
using Arc;
using Arc.Collections;

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

    private readonly SimpleTextSlice.GoshujinClass slices = new();
    private SimpleConsole simpleConsole;
    private ReadLineInstance readLineInstance;
    private char[] charArray = new char[InitialBufferSize];
    private byte[] widthArray = new byte[InitialBufferSize];

    public int WindowWidth => this.simpleConsole.WindowWidth;

    public int WindowHeight => this.simpleConsole.WindowHeight;

    public int Index { get; private set; }

    public int Top { get; set; }

    public int Height { get; private set; }

    public int PromptLength => this._promptLength;

    public int PromptWidth => this._promptWidth;

    public int InputLength => this._inputLength;

    public int InputWidth => this._inputWidth;

    public int TotalLength => this.PromptLength + this.InputLength;

    public int TotalWidth => this.PromptWidth + this.InputWidth;

    internal char[] CharArray => this.charArray;

    internal byte[] WidthArray => this.widthArray;

    internal bool IsEmpty => this.slices.Count == 0;

    private int _promptLength;
    private int _promptWidth;
    private int _inputLength;
    private int _inputWidth;

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

    internal ReadOnlySpan<char> InputSpan
    {
        get
        {
            var start = -1;
            var length = 0;
            foreach (var x in this.slices)
            {

                if (start < 0)
                {
                    if (x.IsInput)
                    {
                        start = x.Start;
                    }
                }
                else
                {
                    length += x.Length;
                }
            }

            if (start < 0)
            {
                return default;
            }
            else
            {
                return this.charArray.AsSpan(start, length);
            }
        }
    }

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

    private void ProcessCharacterInternal(Span<char> charBuffer)
    {
        if (!this.readLineInstance.IsLengthWithinLimit(charBuffer.Length))
        {
            return;
        }

        this.EnsureBuffer(this.TotalLength + charBuffer.Length);
        var arrayPosition = this.readLineInstance.BufferPosition;

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

    private void SetPrompt(ReadOnlySpan<char> prompt, bool isInput)
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
        this.ChangePromptLengthAndWidth(promptLength, promptWidth);

        SimpleTextSlice slice;
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

            if (!isInput)
            {
                inputStart = -1;
            }

            slice = SimpleTextSlice.Rent(this);
            slice.Prepare(this.slices, start, inputStart, end - start, width);
            start = end;
        }
    }

    private void Initialize(SimpleConsole simpleConsole, ReadLineInstance readLineInstance, int index, ReadOnlySpan<char> prompt, bool isInput)
    {
        this.simpleConsole = simpleConsole;
        this.readLineInstance = readLineInstance;
        this.Index = index;
        this.SetPrompt(prompt, isInput);
    }

    private void Uninitialize()
    {
        this.simpleConsole = default!;
        this.readLineInstance = default!;
        foreach (var x in this.slices)
        {
            SimpleTextSlice.Return(x);
        }
    }
}
