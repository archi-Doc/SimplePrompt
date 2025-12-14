// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

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

    public void AddInput(int length, int width)
    {
        this.ChangeInputLengthAndWidth(length, width);
        this.AdjustRow();
    }

    public override string ToString()
    {
        return this.CharSpan.ToString();
    }

    private void AdjustRow()
    {
        if (this.Width >= this.simpleTextLine.WindowWidth)
        {
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

            var nextRow = this.SliceLink.Next;
            var nextStart = this.Start + this.Length;
            if (nextRow is null)
            {
                nextRow = SimpleTextRow.Rent(this.simpleTextLine);
                nextRow.Prepare(nextStart, nextStart, lengthDiff, widthDiff);
                nextRow.AdjustRow();
            }
            else
            {
                nextRow.SetStartPosition(nextStart, lengthDiff, widthDiff);
            }
        }
    }

    private void Initialize(SimpleTextLine simpleTextLine)
    {
        this.simpleTextLine = simpleTextLine;
        this.Goshujin = simpleTextLine.Slices;
    }

    private void Uninitialize()
    {
        this.simpleTextLine = default!;
        this.Goshujin = default;
    }
}
