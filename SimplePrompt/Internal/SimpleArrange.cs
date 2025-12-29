// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using SimplePrompt.Internal;
using static Arc.Unit.UnitMessage;

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

    public void Arrange((int Left, int Top) newCursor)
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

        bool redraw = false;
        // Log($"Width:{this.simpleConsole.WindowWidth} Height:{this.simpleConsole.WindowHeight}\n");
        foreach (var x in lineList)
        {
            if (x.Rows.Count > 0)
            {
                bool rowChanged = false;
                int widthDiff = 0;
                bool emptyRow = false;
                x.Rows.ListChain[0].Arrange(ref rowChanged, ref widthDiff, ref emptyRow);
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

        /*if (!location.TryGetLineAndRow(out var line, out var row))
        {// Invalid location
            location.Reset();
            return;

        }

        if (row.Top != newCursor.Top)
        {
            redraw = true;
            var total = 0;
            for (var i = 0; i < line.Index; i++)
            {
                total += lineList[i].Height;
            }

            lineList[0].Top = newCursor.Top - row.ListLink.Index - total;

        }*/

        if (!redraw)
        {
            this.readLineInstance.CurrentLocation.Restore(CursorOperation.None);
            return;
        }

        this.simpleConsole.Clear(false);

        // Log($"Redraw\n");
        /*this.readLineInstance.ResetCursor(CursorOperation.None);
        this.readLineInstance.Redraw();
        this.readLineInstance.CurrentLocation.Restore(CursorOperation.None);*/
    }

    private static void Log(string message)
    {
        File.AppendAllText("log.txt", message);
    }
}
