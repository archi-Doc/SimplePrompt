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

    public void RearrangeBuffers((int Left, int Top) newCursor)
    {
        // this.Log($"({newCursor.Left}, {newCursor.Top}) {this.simpleConsole.WindowWidth}-{this.simpleConsole.WindowHeight}\r\n");
        if (this.previousInstance is null)
        {
            return;
        }

        // coi
        /*var bufferList = this.previousInstance.LineList;
        if (this.previousInstance.LineIndex >= bufferList.Count)
        {// Invalid buffer index
            return;
        }

        var buffer = bufferList[this.previousInstance.LineIndex];
        if (this.previousInstance.LinePosition > buffer.Width)
        {// Invalid buffer position
            return;
        }

        var position = newCursor.Left + (newCursor.Top * this.simpleConsole.WindowWidth) - this.previousInstance.LinePosition - buffer.PromptWidth;
        if (position < 0)
        {// Invalid position
            return;
        }

        var newTop = position / this.simpleConsole.WindowWidth;
        var newLeft = position % this.simpleConsole.WindowWidth;
        if (newLeft != 0)
        {
            return;
        }

        if (buffer.Top != newTop)
        {
            // this.Log($"Top {buffer.Top} -> {newTop}\r\n");

            buffer.Top = newTop;
            foreach (var x in bufferList)
            {
                x.UpdateHeight();
            }

            for (var i = buffer.Index - 1; i >= 0; i--)
            {
                bufferList[i].Top = bufferList[i + 1].Top - bufferList[i].Height;
            }

            for (var i = buffer.Index + 1; i < bufferList.Count; i++)
            {
                bufferList[i].Top = bufferList[i - 1].Top + bufferList[i - 1].Height;
            }
        }

        this.simpleConsole.CursorLeft = newCursor.Left;
        this.simpleConsole.CursorTop = newCursor.Top;*/
    }

    private static void Log(string message)
    {
        File.AppendAllText("log.txt", message);
    }
}
