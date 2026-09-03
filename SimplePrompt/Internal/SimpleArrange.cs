// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using SimplePrompt.Internal;

namespace SimplePrompt;

/// <summary>
/// Rearranges the input lines of the active ReadLine operation when the console window is resized.
/// </summary>
internal sealed class SimpleArrange
{
    #region FieldAndProperty

    private readonly SimpleConsole simpleConsole;

    #endregion

    public SimpleArrange(SimpleConsole simpleConsole)
    {
        this.simpleConsole = simpleConsole;
    }

    public void Arrange(ReadLineInstance readLineInstance, (int Left, int Top) newCursor, bool redraw)
    {
        var lineList = readLineInstance.LineList;
        var location = readLineInstance.CurrentLocation;
        if (location.LineIndex >= lineList.Count)
        {// Invalid line index
            location.Reset();
            return;
        }

        var line = lineList[location.LineIndex];
        foreach (var x in lineList)
        {
            if (x.Rows.Count > 0)
            {
                bool rowChanged = false;
                int widthDiff = 0;
                bool emptyRow = false;
                x.Rows[0].Arrange(ref rowChanged, ref widthDiff, ref emptyRow);
                if (rowChanged || emptyRow)
                {
                    redraw = true;
                }
            }
        }

        if (this.simpleConsole._cursorTop != newCursor.Top/* ||
                this.simpleConsole.CursorLeft != newCursor.Left*/)
        {
            redraw = true;
        }

        if (line.TryGetRowFromArrayPosition(location.ArrayPosition, out var row) &&
            row.Top != newCursor.Top)
        {
            redraw = true;
            var total = 0;
            for (var i = 0; i < line.Index; i++)
            {
                foreach (var x in lineList[i].Rows)
                {
                    if (x.Length > 0)
                    {
                        total++;
                    }
                }
            }

            lineList[0].Top = newCursor.Top - row.Index - total;
        }

        if (!redraw)
        {
            readLineInstance.CurrentLocation.Restore(CursorOperation.None);
            return;
        }

        readLineInstance.ResetCursor(CursorOperation.None);
        readLineInstance.Redraw();
        readLineInstance.CurrentLocation.Restore(CursorOperation.None);
    }
}
