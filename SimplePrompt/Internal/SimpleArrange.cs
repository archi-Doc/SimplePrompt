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
        this.Invalidate();
    }

    public void Update(ReadLineInstance readLineInstance)
    {
        /*if (this.simpleConsole.CursorLeft == this.previousCursorLeft &&
            this.simpleConsole.CursorTop == this.previousCursorTop)
        {// Identical cursor position
            return;
        }*/

        this.previousInstance = readLineInstance;
        // this.previousCursorLeft = this.simpleConsole.CursorLeft;
        // this.previousCursorTop = this.simpleConsole.CursorTop;
        // this.previousInstance.PrepareLocation();
    }

    public void Invalidate()
    {
        // this.previousCursorLeft = -1;
        // this.previousCursorTop = -1;
    }

    public void Arrange((int Left, int Top) newCursor)
    {
        // this.Log($"({newCursor.Left}, {newCursor.Top}) {this.simpleConsole.WindowWidth}-{this.simpleConsole.WindowHeight}\r\n");
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

        var line = lineList[location.LineIndex];
        /*if (location.RowIndex >= line.Rows.Count)
        {// Invalid row index
            location.Reset();
            return;
        }

        var previousTop = line.Top + location.RowIndex;
        var previousLeft = location.CursorPosition;
        var topDiff = newCursor.Top - previousTop;*/

        Log($"Width:{this.simpleConsole.WindowWidth} Height:{this.simpleConsole.WindowHeight}\n");
        foreach (var x in lineList)
        {
            if (x.Rows.Count > 0)
            {
                bool rowChanged = false;
                int widthDiff = 0;
                x.Rows.ListChain[0].Arrange(ref rowChanged, ref widthDiff);
                Log($"Arrange {x.Index} Row changed:{rowChanged} Width diff:{widthDiff}\n");
            }
        }

        var currentCursor = line.GetCursor(location.ArrayPosition);
        var currentTop = currentCursor.Top - currentCursor.RowIndex;
        location.CursorPosition = currentCursor.Left;
        if (location.CursorPosition != newCursor.Left)
        {
        }
        /*if (line.Rows.Count > 0)
        {
            row = line.Rows.ListChain[line.Rows.Count - 1];
            if (arrayPosition >= row.Start &&
                arrayPosition <= row.End)
            {
                currentTop = newCursor.Top - line.Rows.Count + 1;
            }
            else
            {
                for (var i = 0; i < line.Rows.Count - 1; i++)
                {
                    row = line.Rows.ListChain[i];
                    if (arrayPosition >= row.Start &&
                        arrayPosition < row.End)
                    {
                        currentTop = newCursor.Top - i;
                        break;
                    }
                }
            }
        }*/


        if (currentTop < 0)
        {
            location.Reset();
            return;
        }

        var top = currentTop;
        for (var i = location.LineIndex; i >= 0; i--)
        {
            lineList[i].Top = top;
            if (i > 0)
            {
                top -= lineList[i - 1].Height;
            }
        }

        // currentTop = newCursor.Top + lineList[location.LineIndex].Height;
        top = currentTop + lineList[location.LineIndex].Height;
        for (var i = location.LineIndex + 1; i < lineList.Count; i++)
        {
            lineList[i].Top = top;
            top += lineList[i].Height;
        }

        this.previousInstance.Scroll();
    }

    private static void Log(string message)
    {
        File.AppendAllText("log.txt", message);
    }
}
