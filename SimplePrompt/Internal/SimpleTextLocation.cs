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

    public bool TryGetLineAndRow([MaybeNullWhen(false)] out SimpleTextLine line, [MaybeNullWhen(false)] out SimpleTextRow row)
    {
        if (this.LineIndex >= this.readLineInstance.LineList.Count)
        {
            line = default;
            row = default;
            return false;
        }

        line = this.readLineInstance.LineList[this.LineIndex];
        var count = this.RowIndex;
        row = line.Rows[this.RowIndex];
        while (count > 0 && row is not null)
        {
            row = row.SliceLink.Next;
            count--;
        }

        return row is not null;
    }

    public void Reset()
    {
        foreach (var x in this.readLineInstance.LineList)
        {
            if (x.IsInput && x.Rows.SliceChain.First is { } row)
            {
                this.LineIndex = x.Index;
                this.RowIndex = 0;
                this.ArrayPosition = x.PromptLength;
                this.CursorPosition = x.PromptWidth;
                this.SetCursor(row);
                return;
            }
        }

        this.ResetInternal();
    }

    public void SetCursor()
    {
        if (this.LineIndex >= this.readLineInstance.LineList.Count)
        {
            this.ResetInternal();
            return;
        }

        var line = this.readLineInstance.LineList[this.LineIndex];
        if (this.RowIndex >= line.Rows.Count)
        {
            this.ResetInternal();
            return;
        }

        var row = line.Rows.SliceChain.First;
        for (var i = 0; i < this.RowIndex; i++)
        {
            row = row?.SliceLink.Next;
        }

        if (row is null)
        {
            this.ResetInternal();
            return;
        }

        this.SetCursor(row);
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
            if (row.SliceLink.Previous is { } previousRow)
            {
                this.RowIndex--;
                this.ArrayPosition -= length;
                this.CursorPosition = row.Width;
            }
        }
        else
        {
            this.ArrayPosition -= length;
            this.CursorPosition -= width;
        }

        this.SetCursor(row);
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
            if (row.SliceLink.Next is { } nextRow)
            {
                this.RowIndex++;
                this.CursorPosition -= row.Width;
            }
            else
            {
                this.CursorPosition = row.Width;
            }
        }

        this.SetCursor(row);
    }

    public void Move(int lengthDiff, int widthDiff)
    {
        this.ArrayPosition += lengthDiff;
        this.CursorPosition += widthDiff;
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

    private void SetCursor(SimpleTextRow row)
    {
        if (this.ArrayPosition < row.Start ||
            row.End < this.ArrayPosition)
        {
            this.ResetInternal();
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
    private void ResetInternal()
    {
        this.LineIndex = 0;
        this.RowIndex = 0;
        this.ArrayPosition = 0;
        this.CursorPosition = 0;
    }
}
