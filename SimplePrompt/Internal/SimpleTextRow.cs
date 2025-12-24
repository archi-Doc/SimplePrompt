// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Arc.Collections;
using ValueLink;

namespace SimplePrompt.Internal;

[ValueLinkObject]
internal sealed partial class SimpleTextRow
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

    public int Top => this.Line.Top + this.ListLink.Index;

    public int Start { get; private set; }

    public int End => this.Start + this.Length;

    public int InputStart { get; private set; }

    public int Length => this._length;

    public int Width => this._width;

    // public ReadOnlySpan<char> CharSpan => this.Line.CharArray.AsSpan(this.Start, this.Length);

    // public ReadOnlySpan<byte> WidthSpan => this.Line.WidthArray.AsSpan(this.Start, this.Length);

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

    public (bool RowChanged, int WidthDiff) AddInput(int lengthDiff, int widthDiff)
    {
        this._length += lengthDiff;
        this._width += widthDiff;
        this.Line._inputLength += lengthDiff;
        this.Line._inputWidth += widthDiff;

        bool rowChanged = false;
        this.Arrange(ref rowChanged, ref widthDiff);
        return (rowChanged, widthDiff);
    }

    public void TrimCursorPosition(ref int cursorPosition, out int arrayPosition)
    {
        var i = 0;
        var cursor = 0;
        for (i = this.Start; i < this.End; i++)
        {
            if (i >= this.InputStart &&
                cursor >= cursorPosition)
            {
                break;
            }

            cursor = cursor + this.Line.WidthArray[i];
        }

        cursorPosition = cursor;
        arrayPosition = i;
    }

    public override string ToString()
    {
        return this.Line.CharArray.AsSpan(this.Start, this.Length).ToString();
    }

    internal void Arrange(ref bool rowChanged, ref int widthDiff)
    {
        // This is the core functionality of SimpleTextRow.
        // If a row is too short, it pulls data from the next row; if it is too long, it pushes excess data to the next row, maintaining the correct line/ row structure.
        var chain = this.Line.Rows.ListChain;
        if (chain is null)
        {
            return;
        }

        var nextIndex = this.ListLink.Index + 1;
        var nextRow = nextIndex >= chain.Count ? null : chain[nextIndex];
        if (this.Width < this.Line.WindowWidth)
        {// The width is within WindowWidth. If necessary, the array is moved starting from the next row.
            if (nextRow is null)
            {// There is no next row, so nothing to move.
            }
            else if (nextRow.Length == 0)
            {// Empty row
                SimpleTextRow.Return(nextRow);
                rowChanged = true;
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

                var lengthDiff = this.End - index;
                widthDiff = this.Width + width - this.Line.WindowWidth;
                this._length -= lengthDiff;
                this._width -= widthDiff;

                nextRow.ChangeStartPosition(this.End, lengthDiff, widthDiff);
                nextRow.Arrange(ref rowChanged, ref widthDiff);
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
            widthDiff = this.Width - width;
            this._length = index + 1 - this.Start;
            this._width = width;

            if (nextRow is null)
            {
                nextRow = SimpleTextRow.Rent(this.Line);
                nextRow.Prepare(this.End, this.End, lengthDiff, widthDiff);
                nextRow.Arrange(ref rowChanged, ref widthDiff);
                rowChanged = true;
            }
            else
            {
                nextRow.ChangeStartPosition(this.End, lengthDiff, widthDiff);
                nextRow.Arrange(ref rowChanged, ref widthDiff);
            }
        }
        else
        {// The width is exactly equal to WindowWidth.
            if (nextRow is null)
            {
                nextRow = SimpleTextRow.Rent(this.Line);
                nextRow.Prepare(this.End, this.End, 0, 0);
                nextRow.Arrange(ref rowChanged, ref widthDiff);
                rowChanged = true;
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
