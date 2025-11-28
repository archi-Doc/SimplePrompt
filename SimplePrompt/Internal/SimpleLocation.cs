// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Arc.Unit;
using SimplePrompt.Internal;

namespace SimplePrompt;

internal class SimpleLocation
{
    private readonly SimpleConsole simpleConsole;

    public int CursorLeft { get; set; }

    public int CursorTop { get; set; }

    public int BufferIndex { get; private set; }

    public int BufferPosition { get; private set; }

    private ReadLineInstance? previousInstance;
    private int previousCursorLeft;
    private int previousCursorTop;

    public SimpleLocation(SimpleConsole simpleConsole)
    {
        this.simpleConsole = simpleConsole;
    }

    public void Update(ReadLineInstance readLineInstance)
    {
        this.CursorLeft = this.simpleConsole.CursorLeft;
        this.CursorTop = this.simpleConsole.CursorTop;

        if (this.CursorLeft == this.previousCursorLeft &&
            this.CursorTop == this.previousCursorTop)
        {// Identical cursor position
            return;
        }

        this.previousInstance = readLineInstance;
        this.previousCursorLeft = this.CursorLeft;
        this.previousCursorTop = this.CursorTop;

        /*if (!this.simpleConsole.TryGetActiveInstance(out var activeInstance))
        {
            this.BufferIndex = 0;
            this.BufferPosition = 0;
            return;
        }*/

        (this.BufferIndex, this.BufferPosition) = this.previousInstance.GetLocation();
    }

    public void Correct((int Left, int Top) newCursor)
    {
        if (this.previousInstance is null)
        {
            return;
        }

        var bufferList = this.previousInstance.BufferList;
        if (this.BufferIndex >= bufferList.Count)
        {// Invalid buffer index
            return;
        }

        var buffer = bufferList[this.BufferIndex];
        if (this.BufferPosition > buffer.Width)
        {// Invalid buffer position
            return;
        }

        var position = newCursor.Left + (newCursor.Top * this.simpleConsole.WindowWidth) - this.BufferPosition - buffer.PromtWidth;
        if (position < 0)
        {// Invalid position
            return;
        }

        var newTop = position / this.simpleConsole.WindowWidth;
        var newLeft = position % this.simpleConsole.WindowWidth;
        if (newLeft != 0)
        {
            // return;
        }

        if (buffer.Top != newTop)
        {
            var st = $"Correct Cursor({newCursor.Left}, {newCursor.Top}) Position:{position}, New({newLeft}, {newTop}), Top {buffer.Top} -> {newTop}\r\n";
            File.AppendAllText("log.txt", st);//

            buffer.Top = newTop;
            buffer.UpdateHeight(false);
            for (var i = buffer.Index - 1; i >= 0; i--)
            {
                bufferList[i + 1].UpdateHeight(false);
                bufferList[i].Top = bufferList[i + 1].Top - bufferList[i + 1].Height;
            }

            for (var i = buffer.Index + 1; i < bufferList.Count; i++)
            {
                bufferList[i - 1].UpdateHeight(false);
                bufferList[i].Top = bufferList[i - 1].Top + bufferList[i - 1].Height;
            }
        }

        this.CursorLeft = newCursor.Left;
        this.CursorTop = newCursor.Top;
    }
}
