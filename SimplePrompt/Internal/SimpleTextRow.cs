// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using System.Diagnostics;
using System.Runtime.CompilerServices;
using Arc;
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

    private SimpleTextLine simpleTextLine;
    private int _length;
    private int _width;

    public bool IsInput => this.InputStart >= 0;

    public int Start { get; private set; }

    public int End => this.Start + this.Length;

    public int InputStart { get; private set; }

    public int Length => this._length;

    public int Width => this._width;

    public ReadOnlySpan<char> CharSpan => this.simpleTextLine.CharArray.AsSpan(this.Start, this.Length);

    public ReadOnlySpan<byte> WidthSpan => this.simpleTextLine.WidthArray.AsSpan(this.Start, this.Length);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void ChangeInputLengthAndWidth(int lengthDiff, int widthDiff)
    {
        this._length += lengthDiff;
        this._width += widthDiff;
        this.simpleTextLine.ChangeInputLengthAndWidth(lengthDiff, widthDiff);
    }

    #endregion

    [Link(Primary = true, Type = ChainType.LinkedList, Name = "Slice")]
    private SimpleTextRow()
    {
        this.simpleTextLine = default!;
    }

    public void Prepare(int start, int inputStart, int length, int width)
    {
        this.Start = start;
        this.InputStart = inputStart;
        this._length = length;
        this._width = width;
    }

    public bool AddInput(int length, int width)
    {
        this.ChangeInputLengthAndWidth(length, width);
        return this.Arrange();
    }

    public override string ToString()
    {
        return this.CharSpan.ToString();
    }

    private bool Arrange()
    {
        // This is the core functionality of SimpleTextRow.
        // If a row is too short, it pulls data from the next row; if it is too long, it pushes excess data to the next row, maintaining the correct line/ row structure.
        var nextRow = this.SliceLink.Next;
        if (this.Width < this.simpleTextLine.WindowWidth)
        {// The width is within WindowWidth. If necessary, the array is moved starting from the next row.
            if (nextRow is null)
            {// There is no next row, so nothing to move.
                return false;
            }
            else
            {// Move from the next row if there is extra space.
                var width = this.simpleTextLine.WindowWidth - this.Width;
                var index = this.End;
                var end = this.simpleTextLine.TotalLength;
                while (index < end &&
                    width >= this.simpleTextLine.WidthArray[index])
                {
                    width -= this.simpleTextLine.WidthArray[index];
                    index++;
                }

                this._length += index - this.End;
                this._width += this.simpleTextLine.WindowWidth - this.Width - width;
                nextRow.Arrange();
                return true;
            }
        }
        else if (this.Width > this.simpleTextLine.WindowWidth)
        {// The width exceeds WindowWidth.
            var index = this.Start + this.Length - 1;
            var width = this.Width;
            while (width > this.simpleTextLine.WindowWidth)
            {
                width -= this.simpleTextLine.WidthArray[index];
                index--;
            }

            var lengthDiff = this.Start + this.Length - 1 - index;
            var widthDiff = this.Width - width;
            this._length = index;
            this._width = width;

            var nextStart = this.Start + this.Length;
            if (nextRow is null)
            {
                nextRow = SimpleTextRow.Rent(this.simpleTextLine);
                nextRow.Prepare(nextStart, nextStart, lengthDiff, widthDiff);
                nextRow.Arrange();
            }
            else
            {
                nextRow.ChangeStartPosition(nextStart, lengthDiff, widthDiff);
                nextRow.Arrange();
            }

            return true;
        }
        else
        {// The width is exactly equal to WindowWidth.
            return false;
        }
    }

    private void ChangeStartPosition(int newStart, int lengthDiff, int widthDiff)
    {
        Debug.Assert(lengthDiff == (this.Start - newStart));

        this.Start = newStart;
        this._length += lengthDiff;
        this._width += widthDiff;
    }

    private void Initialize(SimpleTextLine simpleTextLine)
    {
        this.simpleTextLine = simpleTextLine;
        this.Goshujin = simpleTextLine.Rows;
    }

    private void Uninitialize()
    {
        this.simpleTextLine = default!;
        this.Goshujin = default;
    }
}
