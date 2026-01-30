// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using SimplePrompt.Internal;

namespace SimplePrompt;

internal sealed class SimpleArrange
{
    #region FieldAndProperty

    private readonly SimpleConsole simpleConsole;
    private ReadLineInstance? readLineInstance;

    #endregion

    public SimpleArrange(SimpleConsole simpleConsole)
    {
        this.simpleConsole = simpleConsole;
    }

    public void Set(ReadLineInstance readLineInstance)
    {
        this.readLineInstance = readLineInstance;
    }

    public void Arrange((int Left, int Top) newCursor, bool redraw)
    {
        if (this.readLineInstance is null)
        {
            return;
        }

        var lineList = this.readLineInstance.LineList;
        var location = this.readLineInstance.CurrentLocation;
        if (location.LineIndex >= lineList.Count)
        {// Invalid line index
            location.Reset();
            return;
        }

        var line = lineList[location.LineIndex];
        // Log($"Width:{this.simpleConsole.WindowWidth} Height:{this.simpleConsole.WindowHeight}\n");
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

                // Log($"Arrange {x.Index} Row changed:{rowChanged} Width diff:{widthDiff}\n");
            }
        }

        if (this.simpleConsole.CursorTop != newCursor.Top/* ||
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
            this.readLineInstance.CurrentLocation.Restore(CursorOperation.None);
            return;
        }

        this.readLineInstance.ResetCursor(CursorOperation.None);
        this.readLineInstance.Redraw();
        this.readLineInstance.CurrentLocation.Restore(CursorOperation.None);

        // this.simpleConsole.Clear(false);
    }

    private static void Log(string message)
    {
        File.AppendAllText("log.txt", message);
    }
}
