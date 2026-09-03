// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Arc;
using Arc.Collections;

namespace SimplePrompt.Internal;

/// <summary>
/// Represents a single displayed row of a <see cref="SimpleTextLine"/> (the part which fits in the window width).
/// </summary>
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

    public int Index { get; private set; }

    public bool IsInput => this.InputStart >= 0;

    public int Top => this.Line.Top + this.Index;

    public int Start { get; private set; }

    public int End => this.Start + this.Length;

    public int InputStart { get; private set; }

    public int Length => this._length;

    public int Width => this._width;

    #endregion

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
        var line = this.Line;
        this._length += lengthDiff;
        this._width += widthDiff;
        this.Line._inputLength += lengthDiff;
        this.Line._inputWidth += widthDiff;

        var previousHeight = line.Height;
        bool rowChanged = false;
        bool emptyRow = false;
        var firstRow = this.Line.Rows[Math.Max(0, this.Index - 1)];
        firstRow.Arrange(ref rowChanged, ref widthDiff, ref emptyRow);
        return (previousHeight != line.Height, widthDiff);
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

    internal void Arrange(ref bool rowChanged, ref int widthDiff, ref bool emptyRow)
    {
        // Reflow from this row in one pass. Subsequent offsets must also change when
        // a row remains exactly full; surrogate pairs must stay on the same row.
        var rows = this.Line.Rows;
        var oldCount = rows.Count;
        var oldLastWidth = rows[^1].Width;
        var position = this.Start;
        var rowIndex = this.Index;
        while (true)
        {
            var start = position;
            var width = 0;
            while (position < this.Line.TotalLength)
            {
                var length = char.IsHighSurrogate(this.Line.CharArray[position]) &&
                    position + 1 < this.Line.TotalLength && char.IsLowSurrogate(this.Line.CharArray[position + 1]) ? 2 : 1;
                var characterWidth = this.Line.WidthArray[position];
                if (length == 2)
                {
                    characterWidth += this.Line.WidthArray[position + 1];
                }

                if (width + characterWidth > this.Line.WindowWidth)
                {
                    break;
                }

                width += characterWidth;
                position += length;
            }

            var row = rowIndex < rows.Count ? rows[rowIndex] : Rent(this.Line);
            var inputStart = this.Line.IsInput && position >= this.Line.PromptLength ? Math.Max(start, this.Line.PromptLength) : -1;
            rowChanged |= row.Start != start || row.Length != position - start || row.Width != width;
            row.Prepare(start, inputStart, position - start, width);
            rowIndex++;
            if (position == this.Line.TotalLength && width < this.Line.WindowWidth)
            {
                break;
            }
        }

        for (var i = rows.Count - 1; i >= rowIndex; i--)
        {
            Return(rows[i]);
        }

        rowChanged |= oldCount != rows.Count;
        widthDiff = rows[^1].Width - oldLastWidth;
        emptyRow = rows[^1].Length == 0;
        this.Line.UpdateInitialLocation();
    }

    internal int ArrayPositionToCursorPosition(int arrayPosition)
    {
        return (int)BaseHelper.Sum(this.Line.WidthArray.AsSpan(this.Start, arrayPosition - this.Start));

        /*var charArray = this.Line.CharArray;
        var widthArray = this.Line.WidthArray;
        var cursorPosition = 0;
        while (arrayPosition > 0)
        {
            if (char.IsLowSurrogate(charArray[arrayPosition - 1]) &&
            arrayPosition > 1 &&
            char.IsHighSurrogate(charArray[arrayPosition - 2]))
            {
                arrayPosition -= 2;
                cursorPosition += widthArray[arrayPosition - 1] + widthArray[arrayPosition - 2];
            }
            else
            {
                arrayPosition--;
                cursorPosition += widthArray[arrayPosition - 1];
            }
        }*/
    }

    private void Initialize(SimpleTextLine simpleTextLine)
    {
        this.Line = simpleTextLine;
        this.Index = simpleTextLine.Rows.Count;
        simpleTextLine.Rows.Add(this);
    }

    private void Uninitialize()
    {
        var list = this.Line.Rows;
        list.RemoveAt(this.Index);
        for (var i = this.Index; i < list.Count; i++)
        {
            list[i].Index--;
        }

        this.Index = 0;
        this.Line = default!;
    }
}
