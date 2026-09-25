// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using System.Text;
using SimplePrompt.Internal;

namespace xUnitTest;

/// <summary>
/// A minimal xterm-compatible screen used to check what the console output actually displays.<br/>
/// It interprets the sequences emitted by SimplePrompt and models the deferred wrap at the right margin
/// (a character written in the last column leaves the cursor there until the next character, and erasing in that state
/// also erases the last column), so that the tracked cursor can be compared with the terminal cursor.
/// </summary>
internal sealed class VirtualTerminal
{
    private const string WideContinuation = "";

    private readonly string?[][] cells;
    private (int Left, int Top, bool PendingWrap) saved;

    public VirtualTerminal(int width, int height, (int Left, int Top) cursor)
    {
        this.Width = width;
        this.Height = height;
        this.cells = new string?[height][];
        for (var i = 0; i < height; i++)
        {
            this.cells[i] = new string?[width];
        }

        (this.Left, this.Top) = cursor;
    }

    public int Width { get; }

    public int Height { get; }

    public int Left { get; private set; }

    public int Top { get; private set; }

    /// <summary>
    /// Gets a value indicating whether the cursor is in the last column waiting for the next character to wrap.
    /// </summary>
    public bool PendingWrap { get; private set; }

    public bool CursorVisible { get; private set; } = true;

    public (int Left, int Top) Cursor => (this.Left, this.Top);

    /// <summary>
    /// Gets the displayed text of the specified row without trailing blanks.
    /// </summary>
    /// <param name="top">The zero-based row.</param>
    /// <returns>The text.</returns>
    public string GetRow(int top)
    {
        var builder = new StringBuilder();
        foreach (var cell in this.cells[top])
        {
            builder.Append(cell ?? " ");
        }

        return builder.ToString().TrimEnd();
    }

    public void Feed(ReadOnlySpan<char> output)
    {
        for (var i = 0; i < output.Length; i++)
        {
            var c = output[i];
            if (c == '\e')
            {
                i = this.ProcessEscape(output, i);
            }
            else if (c == '\r')
            {
                this.Left = 0;
                this.PendingWrap = false;
            }
            else if (c == '\n')
            {// The terminal translates LF into CR LF (ONLCR); Windows output already contains CR LF.
                this.Left = 0;
                this.PendingWrap = false;
                this.LineFeed();
            }
            else if (c == '\t')
            {
                if (!this.PendingWrap)
                {
                    this.Left = Math.Min(((this.Left / 8) + 1) * 8, this.Width - 1);
                }
            }
            else if (char.IsHighSurrogate(c) && (i + 1) < output.Length && char.IsLowSurrogate(output[i + 1]))
            {
                this.Print(output.Slice(i, 2).ToString(), SimplePromptHelper.GetCharWidth(char.ConvertToUtf32(c, output[i + 1])));
                i++;
            }
            else
            {
                this.Print(c.ToString(), SimplePromptHelper.GetCharWidth(c));
            }
        }
    }

    private static int GetParameter(ReadOnlySpan<char> parameters, int index, int defaultValue)
    {
        foreach (var range in parameters.Split(';'))
        {
            if (index-- == 0)
            {
                return int.TryParse(parameters[range], out var value) && value > 0 ? value : defaultValue;
            }
        }

        return defaultValue;
    }

    private void Print(string glyph, int width)
    {
        if (width == 0)
        {// Control characters and combining marks do not move the cursor.
            return;
        }

        if (this.PendingWrap || (this.Left + width) > this.Width)
        {
            this.Left = 0;
            this.PendingWrap = false;
            this.LineFeed();
        }

        this.cells[this.Top][this.Left] = glyph;
        if (width == 2)
        {
            this.cells[this.Top][this.Left + 1] = WideContinuation;
        }

        this.Left += width;
        if (this.Left >= this.Width)
        {
            this.Left = this.Width - 1;
            this.PendingWrap = true;
        }
    }

    private void LineFeed()
    {
        if (this.Top < this.Height - 1)
        {
            this.Top++;
            return;
        }

        // Scroll up.
        var first = this.cells[0];
        Array.Copy(this.cells, 1, this.cells, 0, this.Height - 1);
        Array.Clear(first);
        this.cells[this.Height - 1] = first;
    }

    private void Erase(int top, int start, int end)
    {
        for (var i = start; i < end; i++)
        {
            this.cells[top][i] = null;
        }
    }

    private int ProcessEscape(ReadOnlySpan<char> output, int index)
    {
        if (index + 1 >= output.Length)
        {
            return index;
        }

        var next = output[index + 1];
        if (next == '[')
        {// CSI: parameter bytes, intermediate bytes and a final byte.
            var end = index + 2;
            while (end < output.Length && output[end] is >= '0' and <= '?')
            {
                end++;
            }

            var parameters = output.Slice(index + 2, end - index - 2);
            while (end < output.Length && output[end] is >= ' ' and <= '/')
            {
                end++;
            }

            if (end >= output.Length)
            {
                throw new InvalidOperationException("Incomplete control sequence.");
            }

            this.ExecuteControlSequence(parameters, output[end]);
            return end;
        }
        else if (next == ']')
        {// OSC, terminated by BEL or ST.
            for (var i = index + 2; i < output.Length; i++)
            {
                if (output[i] == '\a')
                {
                    return i;
                }
                else if (output[i] == '\e' && (i + 1) < output.Length && output[i + 1] == '\\')
                {
                    return i + 1;
                }
            }

            throw new InvalidOperationException("Incomplete operating system command.");
        }
        else if (next == '7')
        {
            this.saved = (this.Left, this.Top, this.PendingWrap);
        }
        else if (next == '8')
        {
            (this.Left, this.Top, this.PendingWrap) = this.saved;
        }
        else
        {// Intermediate bytes followed by a final byte, such as a character set designation.
            var end = index + 1;
            while (end < output.Length && output[end] is >= ' ' and <= '/')
            {
                end++;
            }

            return end < output.Length ? end : throw new InvalidOperationException("Incomplete escape sequence.");
        }

        return index + 1;
    }

    private void ExecuteControlSequence(ReadOnlySpan<char> parameters, char final)
    {
        if (parameters.Length > 0 && parameters[0] == '?')
        {
            if (parameters.SequenceEqual("?25"))
            {
                this.CursorVisible = final == 'h';
            }

            return;
        }

        var first = GetParameter(parameters, 0, 0);
        switch (final)
        {
            case 'H':
                this.Top = Math.Clamp(Math.Max(first, 1) - 1, 0, this.Height - 1);
                this.Left = Math.Clamp(Math.Max(GetParameter(parameters, 1, 1), 1) - 1, 0, this.Width - 1);
                this.PendingWrap = false;
                break;

            case 'K': // Erasing also resets the deferred wrap, and in that state erases the last column.
                if (first == 0)
                {
                    this.Erase(this.Top, this.Left, this.Width);
                }
                else if (first == 1)
                {
                    this.Erase(this.Top, 0, this.Left + 1);
                }
                else
                {
                    this.Erase(this.Top, 0, this.Width);
                }

                this.PendingWrap = false;
                break;

            case 'J':
                if (first == 2)
                {
                    for (var i = 0; i < this.Height; i++)
                    {
                        this.Erase(i, 0, this.Width);
                    }
                }
                else
                {
                    throw new NotSupportedException($"Unsupported erase mode {first}.");
                }

                break;

            case 'D':
                this.Left = Math.Max(0, this.Left - Math.Max(first, 1));
                this.PendingWrap = false;
                break;

            case 's':
                this.saved = (this.Left, this.Top, this.PendingWrap);
                break;

            case 'u':
                (this.Left, this.Top, this.PendingWrap) = this.saved;
                break;

            case 'm':
                break;

            default:
                throw new NotSupportedException($"Unsupported control sequence '{final}'.");
        }
    }
}
