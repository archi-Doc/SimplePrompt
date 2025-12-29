// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using SimplePrompt.Internal;

namespace SimplePrompt;

internal sealed class SimpleArrange
{
    #region FieldAndProperty

    private readonly SimpleConsole simpleConsole;

    private ReadLineInstance? previousInstance;
    // private int previousCursorLeft;
    // private int previousCursorTop;

    #endregion

    public SimpleArrange(SimpleConsole simpleConsole)
    {
        this.simpleConsole = simpleConsole;
    }

    public void Update(ReadLineInstance readLineInstance)
    {
        this.previousInstance = readLineInstance;
    }

    public void Arrange((int Left, int Top) newCursor)
    {
        if (this.previousInstance is null)
        {
            return;
        }

        var lineList = this.previousInstance.LineList;
        var location = this.previousInstance.CurrentLocation;
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

        if (!redraw)
        {
            return;
        }

        // Log($"Redraw\n");
        this.previousInstance.ResetCursor(CursorOperation.None);
        this.previousInstance.Redraw();
        this.previousInstance.CurrentLocation.Restore(CursorOperation.None);
    }

    private static void Log(string message)
    {
        File.AppendAllText("log.txt", message);
    }
}
