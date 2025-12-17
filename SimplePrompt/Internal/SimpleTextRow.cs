// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using System.Runtime.CompilerServices;
using Arc.Collections;
using ValueLink;

namespace SimplePrompt.Internal;

[ValueLinkObject]
internal partial class SimpleTextRow
{
    #region ObjectPool

    private const int PoolSize = 32;
    private static readonly ObjectPool<SimpleTextRow> Pool = new(() => new(), PoolSize);

    public static SimpleTextRow Rent(SimpleTextLine simpleTextLine)
    {
        var obj = Pool.Rent();
        obj.Initialize(simpleTextLine);
        return obj;
    }

    public static void Return(SimpleTextRow obj)
    {
        obj.Uninitialize();
        Pool.Return(obj);
    }

    #endregion

    #region FiendAndProperty

    private int _length;
    private int _width;

    public SimpleTextLine Line { get; private set; }

    public bool IsInput => this.InputStart >= 0;

    public int Start { get; private set; }

    public int End => this.Start + this.Length;

    public int InputStart { get; private set; }

    public int Length => this._length;

    public int Width => this._width;

    public ReadOnlySpan<char> CharSpan => this.Line.CharArray.AsSpan(this.Start, this.Length);

    public ReadOnlySpan<byte> WidthSpan => this.Line.WidthArray.AsSpan(this.Start, this.Length);

    /*[MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void ChangeInputLengthAndWidth(int lengthDiff, int widthDiff)
    {
        this._length += lengthDiff;
        this._width += widthDiff;

        // this.Line.ChangeInputLengthAndWidth(lengthDiff, widthDiff);
        this.Line._inputLength += lengthDiff;
        this.Line._inputWidth += widthDiff;
    }*/

    #endregion

    [Link(Primary = true, Type = ChainType.List, Name = "List")]
    private SimpleTextRow()
    {
        this.Line = default!;
    }

    public void Prepare(int start, int inputStart, int length, int width)
    {
        this.Start = start;
        this.InputStart = inputStart;
        this._length = length;
        this._width = width;
    }

    public bool AddInput(int lengthDiff, int widthDiff)
    {
        this._length += lengthDiff;
        this._width += widthDiff;
        this.Line._inputLength += lengthDiff;
        this.Line._inputWidth += widthDiff;

        return this.Arrange();
    }

    public void TrimCursorPosition(ref int cursorPosition, out int arrayPosition)
    {
        if (cursorPosition <= this.InputStart)
        {
        }

        var i = 0;
        var cursor = 0;
        var nextCursor = 0;
        for (i = this.Start; i < this.End; i++)
        {
            nextCursor = cursor + this.WidthSpan[i];
            if (nextCursor >= cursorPosition)
            {
                break;
            }

            cursor = nextCursor;
        }

        cursorPosition = nextCursor;
        arrayPosition = i;
    }

    public override string ToString()
    {
        return this.CharSpan.ToString();
    }

    private bool Arrange()
    {
        // This is the core functionality of SimpleTextRow.
        // If a row is too short, it pulls data from the next row; if it is too long, it pushes excess data to the next row, maintaining the correct line/ row structure.
        var chain = this.Line.Rows.ListChain;
        if (chain is null)
        {
            return false;
        }

        var nextIndex = this.ListLink.Index + 1;
        var nextRow = nextIndex >= chain.Count ? null : chain[nextIndex];
        if (this.Width < this.Line.WindowWidth)
        {// The width is within WindowWidth. If necessary, the array is moved starting from the next row.
            if (nextRow is null)
            {// There is no next row, so nothing to move.
                return false;
            }
            else
            {// Move from the next row if there is extra space.
                var width = this.Line.WindowWidth - this.Width;
                var index = this.End;
                var end = this.Line.TotalLength;
                while (index < end &&
                    width >= this.Line.WidthArray[index])
                {
                    width -= this.Line.WidthArray[index];
                    index++;
                }

                this._length += index - this.End;
                this._width += this.Line.WindowWidth - this.Width - width;
                nextRow.Arrange();
                return true;
            }
        }
        else if (this.Width > this.Line.WindowWidth)
        {// The width exceeds WindowWidth.
            var index = this.Start + this.Length - 1;
            var width = this.Width;
            while (width > this.Line.WindowWidth)
            {
                width -= this.Line.WidthArray[index];
                index--;
            }

            var lengthDiff = this.Start + this.Length - 1 - index;
            var widthDiff = this.Width - width;
            this._length = index + 1 - this.Start;
            this._width = width;

            if (nextRow is null)
            {
                nextRow = SimpleTextRow.Rent(this.Line);
                nextRow.Prepare(this.End, this.End, lengthDiff, widthDiff);
                nextRow.Arrange();
            }
            else
            {
                nextRow.ChangeStartPosition(this.End, lengthDiff, widthDiff);
                nextRow.Arrange();
            }

            return true;
        }
        else
        {// The width is exactly equal to WindowWidth.
            if (nextRow is null)
            {
                nextRow = SimpleTextRow.Rent(this.Line);
                nextRow.Prepare(this.End, this.End, 0, 0);
                nextRow.Arrange();
                return true;
            }
            else
            {
                return false;
            }
        }
    }

    private void ChangeStartPosition(int newStart, int lengthDiff, int widthDiff)
    {
        this.Start = newStart;
        this._length += lengthDiff;
        this._width += widthDiff;
    }

    private void Initialize(SimpleTextLine simpleTextLine)
    {
        this.Line = simpleTextLine;
        this.Goshujin = simpleTextLine.Rows;
    }

    private void Uninitialize()
    {
        this.Line = default!;
        this.Goshujin = default;
    }
}
