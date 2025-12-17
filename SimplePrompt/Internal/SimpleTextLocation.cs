// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;

namespace SimplePrompt.Internal;

internal record class SimpleTextLocation
{
    private SimpleConsole simpleConsole = default!;
    private ReadLineInstance readLineInstance = default!;

    public int LineIndex { get; set; }

    public int RowIndex { get; set; }

    public int ArrayPosition { get; set; }

    public int CursorPosition { get; set; }

    public bool TryGetLine([MaybeNullWhen(false)] out SimpleTextLine line)
    {
        if (this.LineIndex >= this.readLineInstance.LineList.Count)
        {
            line = default;
            return false;
        }

        line = this.readLineInstance.LineList[this.LineIndex];
        return true;
    }

    public bool TryGetLineAndRow([MaybeNullWhen(false)] out SimpleTextLine line, [MaybeNullWhen(false)] out SimpleTextRow row)
    {
        if (this.LineIndex >= this.readLineInstance.LineList.Count)
        {
            line = default;
            row = default;
            return false;
        }

        line = this.readLineInstance.LineList[this.LineIndex];
        if (this.RowIndex >= line.Rows.ListChain.Count)
        {
            line = default;
            row = default;
            return false;
        }

        row = line.Rows.ListChain[this.RowIndex];
        return true;
    }

    public void Reset()
    {
        foreach (var x in this.readLineInstance.LineList)
        {
            if (this.Reset(x))
            {
                return;
            }
        }

        this.ResetZero();
    }

    public bool Reset(SimpleTextLine line)
    {
        if (line.IsInput && line.Rows.Count > 0)
        {
            this.LineIndex = line.Index;
            this.RowIndex = line.InitialRowIndex; ;
            this.ArrayPosition = line.PromptLength;
            this.CursorPosition = line.InitialCursorPosition;
            this.LocationToCursor(line.Rows.ListChain[0]);
            return true;
        }
        else
        {
            return false;
        }
    }

    public void LocationToCursor()
    {
        if (this.LineIndex >= this.readLineInstance.LineList.Count)
        {
            this.Reset();
            return;
        }

        var line = this.readLineInstance.LineList[this.LineIndex];
        if (this.RowIndex >= line.Rows.Count)
        {
            this.Reset();
            return;
        }

        var row = line.Rows.ListChain[this.RowIndex];
        this.LocationToCursor(row);
    }

    public void MoveToLine(int index)
    {
        var lineList = this.readLineInstance.LineList;
        if (lineList.Count == 0)
        {
            return;
        }

        if (index >= lineList.Count)
        {
            index = lineList.Count - 1;
        }

        var line = lineList[index];
        this.Reset(line);

        if (line.Rows.Count == 0)
        {
            return;
        }

        var row = line.Rows.ListChain[0];
        this.LocationToCursor(row);
    }

    public void MoveFirst()
    {
        this.RowIndex = 0;
        if (!this.TryGetLineAndRow(out var line, out var row))
        {
            return;
        }

        this.Reset(line);
    }

    public void MoveLast()
    {
        if (!this.TryGetLine(out var line))
        {
            return;
        }

        if (line.Rows.Count == 0)
        {
            return;
        }

        this.RowIndex = line.Rows.Count - 1;
        var row = line.Rows.ListChain[this.RowIndex];
        this.ArrayPosition = line.TotalLength;
        this.CursorPosition = row.Width;

        this.LocationToCursor(row);
    }

    public void MoveLeft()
    {
        if (!this.TryGetLineAndRow(out var line, out var row))
        {
            return;
        }

        if (this.ArrayPosition <= line.PromptLength)
        {
            return;
        }

        int length, width;
        if (char.IsLowSurrogate(line.CharArray[this.ArrayPosition - 1]) &&
            this.ArrayPosition > 1 &&
            char.IsHighSurrogate(line.CharArray[this.ArrayPosition - 2]))
        {
            length = 2;
            width = line.WidthArray[this.ArrayPosition - 1] + line.WidthArray[this.ArrayPosition - 2];
        }
        else
        {
            length = 1;
            width = line.WidthArray[this.ArrayPosition - 1];
        }

        if (this.CursorPosition == 0)
        {
            if (this.RowIndex > 0)
            {
                this.RowIndex--;
                row = line.Rows.ListChain[this.RowIndex];
                this.ArrayPosition -= length;
                this.CursorPosition = line.Rows.ListChain[this.RowIndex].Width - width;
            }
        }
        else
        {
            this.ArrayPosition -= length;
            this.CursorPosition -= width;
        }

        this.LocationToCursor(row);
    }

    public void MoveRight()
    {
        if (!this.TryGetLineAndRow(out var line, out var row))
        {
            return;
        }

        if (this.ArrayPosition >= line.TotalLength)
        {
            return;
        }

        int length, width;
        if (char.IsHighSurrogate(line.CharArray[this.ArrayPosition]) &&
            (this.ArrayPosition + 1) < line.TotalLength &&
            char.IsLowSurrogate(line.CharArray[this.ArrayPosition + 1]))
        {
            length = 2;
            width = line.WidthArray[this.ArrayPosition] + line.WidthArray[this.ArrayPosition + 1];
        }
        else
        {
            length = 1;
            width = line.WidthArray[this.ArrayPosition];
        }

        this.ArrayPosition += length;
        if (this.ArrayPosition > line.TotalLength)
        {
            this.ArrayPosition = line.TotalLength;
        }

        this.CursorPosition += width;
        if (this.CursorPosition >= row.Width)
        {
            var nextRowIndex = this.RowIndex + 1;
            if (nextRowIndex < line.Rows.Count)
            {
                this.RowIndex = nextRowIndex;
                this.CursorPosition -= row.Width;
            }
            else
            {
                this.CursorPosition = row.Width;
            }
        }

        this.LocationToCursor(row);
    }

    public void Move(int lengthDiff, int widthDiff)
    {
        this.ArrayPosition += lengthDiff;
        this.CursorPosition += widthDiff;

        if (this.CursorPosition >= this.simpleConsole.WindowWidth)
        {
            var line = this.readLineInstance.LineList[this.LineIndex];
            var chain = line.Rows.ListChain;

            do
            {
                var row = chain[this.RowIndex];
                if (this.RowIndex >= (chain.Count - 1))
                {
                    this.ArrayPosition = row.End;
                    this.CursorPosition = row.Width;
                    break;
                }

                this.CursorPosition -= row.Width;
                this.RowIndex++;
            }
            while (this.CursorPosition > this.simpleConsole.WindowWidth);
        }
    }

    public void Initialize(SimpleConsole simpleConsole, ReadLineInstance readLineInstance)
    {
        this.simpleConsole = simpleConsole;
        this.readLineInstance = readLineInstance;
    }

    public void Uninitialize()
    {
        this.simpleConsole = default!;
        this.readLineInstance = default!;
    }

    private void LocationToCursor(SimpleTextRow row)
    {
        if (this.ArrayPosition < row.Start ||
            row.End < this.ArrayPosition)
        {
            this.Reset();
            return;
        }

        var top = row.Line.Top + this.RowIndex;
        top = top < 0 ? 0 : top;
        top = top >= this.simpleConsole.WindowHeight ? this.simpleConsole.WindowHeight - 1 : top;

        var left = this.CursorPosition;
        // left = left < 0 ? 0 : left;
        left = left >= this.simpleConsole.WindowWidth ? this.simpleConsole.WindowWidth - 1 : left;

        if (this.simpleConsole.CursorTop != top ||
            this.simpleConsole.CursorLeft != left)
        {
            this.simpleConsole.SetCursorPosition(left, top, CursorOperation.None);
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void ResetZero()
    {
        this.LineIndex = 0;
        this.RowIndex = 0;
        this.ArrayPosition = 0;
        this.CursorPosition = 0;
    }
}
