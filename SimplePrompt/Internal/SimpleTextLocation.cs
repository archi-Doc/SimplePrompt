// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using System.Runtime.CompilerServices;
using Arc;

namespace SimplePrompt.Internal;

internal record class SimpleTextLocation
{
    private SimpleConsole simpleConsole = default!;
    private ReadLineInstance readLineInstance = default!;

    public int LineIndex { get; set; }

    public int RowIndex { get; set; }

    public int ArrayPosition { get; set; }

    public int CursorPosition { get; set; }

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
        left = left < 0 ? 0 : left;
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

/*internal readonly record struct SimpleTextLocation
{
    public readonly int LineIndex;

    public readonly int LinePosition;

    public SimpleTextLocation(int lineIndex, int linePosition)
    {
        this.LineIndex = lineIndex;
        this.LinePosition = linePosition;
    }
}*/
