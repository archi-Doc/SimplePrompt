// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Arc.Unit;
using SimplePrompt.Internal;

namespace SimplePrompt;

internal class SimpleLocation
{
    #region FieldAndProperty

    private readonly SimpleConsole simpleConsole;

    private ReadLineInstance? previousInstance;
    private int previousCursorLeft;
    private int previousCursorTop;
    // private int previousCursorExtra;

    #endregion

    public SimpleLocation(SimpleConsole simpleConsole)
    {
        this.simpleConsole = simpleConsole;
        this.Invalidate();
    }

    public void Update(ReadLineInstance readLineInstance)
    {
        if (this.simpleConsole.CursorLeft == this.previousCursorLeft &&
            this.simpleConsole.CursorTop == this.previousCursorTop/* &&
            this.simpleConsole.CursorExtra == this.previousCursorExtra*/)
        {// Identical cursor position
            return;
        }

        this.previousInstance = readLineInstance;
        this.previousCursorLeft = this.simpleConsole.CursorLeft;
        this.previousCursorTop = this.simpleConsole.CursorTop;
        // this.previousCursorExtra = this.simpleConsole.CursorExtra;
        this.previousInstance.PrepareLocation();
    }

    public void Invalidate()
    {
        this.previousCursorLeft = -1;
        this.previousCursorTop = -1;
        // this.previousCursorExtra = -1;
    }

    /*public void Redraw()
    {
        if (this.previousInstance is null)
        {
            this.Reset();
            return;
        }

        var bufferList = this.previousInstance.BufferList;
        if (this.BufferIndex >= bufferList.Count)
        {// Invalid buffer index
            this.Reset();
            return;
        }

        var buffer = bufferList[this.BufferIndex];
        if (this.BufferPosition > buffer.Width)
        {// Invalid buffer position
            this.Reset();
            return;
        }

        var totalHeight = 0;
        foreach (var x in bufferList)
        {
            x.UpdateHeight(false);
            totalHeight += x.Height;
        }

        var diff = bufferList[0].Top + totalHeight - this.simpleConsole.WindowHeight;
        var minBuffer = bufferList.Count - 1;
        if (diff > 0)
        {
            foreach (var x in bufferList)
            {
                x.Top -= diff;
                if (x.Top >= 0 && minBuffer > x.Index)
                {
                    minBuffer = x.Index;
                }
            }
        }

        if (this.BufferIndex < minBuffer)
        {
            this.BufferIndex = minBuffer;
            this.BufferPosition = 0;
        }

        buffer = bufferList[this.BufferIndex];
        var newCursor = buffer.ToCursor(this.BufferPosition);
        newCursor.Top += buffer.Top;
        this.simpleConsole.SetCursorPosition(newCursor.Left, newCursor.Top, CursorOperation.None);
    }

    public void Reset()
    {
    }*/

    public void RearrangeBuffers((int Left, int Top) newCursor)
    {
        // this.Log($"({newCursor.Left}, {newCursor.Top}) {this.simpleConsole.WindowWidth}-{this.simpleConsole.WindowHeight}\r\n");
        if (this.previousInstance is null)
        {
            return;
        }

        var bufferList = this.previousInstance.BufferList;
        if (this.previousInstance.BufferIndex >= bufferList.Count)
        {// Invalid buffer index
            return;
        }

        var buffer = bufferList[this.previousInstance.BufferIndex];
        if (this.previousInstance.BufferPosition > buffer.Width)
        {// Invalid buffer position
            return;
        }

        var position = newCursor.Left + (newCursor.Top * this.simpleConsole.WindowWidth) - this.previousInstance.BufferPosition - buffer.PromtWidth;
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
                x.UpdateHeight(false);
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
        this.simpleConsole.CursorTop = newCursor.Top;
        // this.simpleConsole.CursorExtra = 0;
    }

    public void CorrectCursorTop(ReadLineInstance readLineInstance)
    {//
        var newCursor = Console.GetCursorPosition();
        // this.simpleConsole.UnderlyingTextWriter.Write($"{this.simpleConsole.CursorTop}->{newCursor.Top}, ");
        if (newCursor.Top == this.simpleConsole.CursorTop)
        {
            return;
        }

        var topDiff = newCursor.Top - this.simpleConsole.CursorTop;
        foreach (var x in readLineInstance.BufferList)
        {
            x.Top += topDiff;
        }

        (_, this.simpleConsole.CursorTop) = newCursor;
    }

    private void Log(string message)
    {
        File.AppendAllText("log.txt", message);
    }
}
