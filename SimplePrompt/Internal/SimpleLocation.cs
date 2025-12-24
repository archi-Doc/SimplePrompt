// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using SimplePrompt.Internal;

namespace SimplePrompt;

internal sealed class SimpleLocation
{
    #region FieldAndProperty

    private readonly SimpleConsole simpleConsole;

    private ReadLineInstance? previousInstance;
    private int previousCursorLeft;
    private int previousCursorTop;

    #endregion

    public SimpleLocation(SimpleConsole simpleConsole)
    {
        this.simpleConsole = simpleConsole;
        this.Invalidate();
    }

    public void Update(ReadLineInstance readLineInstance)
    {
        if (this.simpleConsole.CursorLeft == this.previousCursorLeft &&
            this.simpleConsole.CursorTop == this.previousCursorTop)
        {// Identical cursor position
            return;
        }

        this.previousInstance = readLineInstance;
        this.previousCursorLeft = this.simpleConsole.CursorLeft;
        this.previousCursorTop = this.simpleConsole.CursorTop;
        // this.previousInstance.PrepareLocation();
    }

    public void Invalidate()
    {
        this.previousCursorLeft = -1;
        this.previousCursorTop = -1;
    }

    public void RearrangeLines((int Left, int Top) newCursor)
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
        if (location.RowIndex >= line.Rows.Count)
        {// Invalid row index
            location.Reset();
            return;
        }

        var previousTop = line.Top + location.RowIndex;
        var previousLeft = location.CursorPosition;
        var topDiff = newCursor.Top - previousTop;

        foreach (var x in lineList)
        {
            if (x.Rows.Count > 0)
            {
                bool rowChanged = false;
                int widthDiff = 0;
                x.Rows.ListChain[0].Arrange(ref rowChanged, ref widthDiff);
            }
        }
        var currentTop = newCursor.Top;
        for (var i = location.LineIndex; i >= 0; i--)
        {
            lineList[i].Top = currentTop;
            if (i > 0)
            {
                currentTop -= lineList[i - 1].Height;
            }
        }

        currentTop = newCursor.Top + lineList[location.LineIndex].Height;
        for (var i = location.LineIndex + 1; i < lineList.Count; i++)
        {
            lineList[i].Top = currentTop;
            currentTop += lineList[i].Height;
        }

        this.previousInstance.Scroll();
    }

    private static void Log(string message)
    {
        File.AppendAllText("log.txt", message);
    }
}
