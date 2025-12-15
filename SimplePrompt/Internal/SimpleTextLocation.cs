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

    public int Position { get; set; }

    public void Reset()
    {
        foreach (var x in this.readLineInstance.LineList)
        {
            if (x.IsInput)
            {
                this.LineIndex = x.Index;
                this.RowIndex = 0;
                this.Position = x.PromptLength;
                this.SetCursor();
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

        if (this.Position < row.Start ||
            row.End < this.Position)
        {
            this.ResetInternal();
            return;
        }

        var top = line.Top + this.RowIndex;
        var left = (int)BaseHelper.Sum(line.WidthArray.AsSpan(0, this.Position));//

        if (this.simpleConsole.CursorTop != top ||
            this.simpleConsole.CursorLeft != left)
        {
            this.simpleConsole.SetCursorPosition(left, top, CursorOperation.None);
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

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void ResetInternal()
    {
        this.LineIndex = 0;
        this.RowIndex = 0;
        this.Position = 0;
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
