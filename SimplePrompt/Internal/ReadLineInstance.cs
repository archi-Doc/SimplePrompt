// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using System.Runtime.CompilerServices;
using Arc;
using Arc.Unit;
using CrossChannel;

namespace SimplePrompt.Internal;

internal class ReadLineInstance
{
    private const int CharBufferSize = 1024;

    public ReadLineOptions Options => this.options;

    public RawConsole RawConsole => this.simpleConsole.RawConsole;

    public List<ReadLineBuffer> BufferList { get; private set; } = new();

    public bool MultilineMode { get; private set; }

    public int EditableBufferIndex { get; private set; }

    private readonly SimpleConsole simpleConsole;
    private readonly char[] charBuffer;
    private ReadLineOptions options = new();

    public ReadLineInstance(SimpleConsole simpleConsole)
    {
        this.simpleConsole = simpleConsole;
        this.charBuffer = new char[CharBufferSize];
    }

    public void Initialize(ReadLineOptions options)
    {
        GhostCopy.Copy(ref options, ref this.options);
    }

    public void Prepare()
    {
        var prompt = this.Options.Prompt.AsSpan();
        var bufferIndex = 0;
        char[]? windowBuffer = null;
        while (prompt.Length >= 0)
        {
            var index = BaseHelper.IndexOfLfOrCrLf(prompt, out var newLineLength);
            ReadLineBuffer buffer;
            if (index < 0)
            {
                buffer = this.simpleConsole.RentBuffer(this, bufferIndex++, prompt.ToString());
                prompt = default;
            }
            else
            {
                buffer = this.simpleConsole.RentBuffer(this, bufferIndex++, prompt.Slice(0, index).ToString());
                prompt = prompt.Slice(index + newLineLength);
            }

            this.BufferList.Add(buffer);
            buffer.Top = this.simpleConsole.CursorTop;
            buffer.UpdateHeight(false);

            windowBuffer = SimpleConsole.RentWindowBuffer();
            var span = windowBuffer.AsSpan();
            TryCopy(buffer.Prompt.AsSpan(), ref span);
            if (prompt.Length == 0)
            {
                TryCopy(ConsoleHelper.EraseToEndOfLineSpan, ref span);
                this.simpleConsole.CursorTop += buffer.Height - 1;
            }
            else
            {
                TryCopy(ConsoleHelper.EraseToEndOfLineAndNewLineSpan, ref span);
                this.simpleConsole.CursorTop += buffer.Height;
            }

            this.RawConsole.WriteInternal(windowBuffer.AsSpan(0, windowBuffer.Length - span.Length));

            if (prompt.Length == 0)
            {// Last buffer
                this.EditableBufferIndex = bufferIndex - 1;
                this.simpleConsole.MoveCursor2(buffer.PromtWidth);
                this.simpleConsole.TrimCursor();
                this.simpleConsole.SetCursorPosition(this.simpleConsole.CursorLeft, this.simpleConsole.CursorTop, CursorOperation.None);
                break;
            }
        }

        if (windowBuffer is not null)
        {
            SimpleConsole.ReturnWindowBuffer(windowBuffer);
        }
    }

    public void HeightChanged(int index, int dif)
    {
        var cursorTop = this.simpleConsole.CursorTop;
        var cursorLeft = this.simpleConsole.CursorLeft;

        for (var i = index + 1; i < this.BufferList.Count; i++)
        {
            var buffer = this.BufferList[i];
            buffer.Top += dif;
            buffer.Write(0, -1, 0, 0, true);
        }

        if (dif < 0)
        {
            var buffer = this.BufferList[this.BufferList.Count - 1];
            var top = buffer.Top + buffer.Height;
            this.ClearLine(top);
        }

        this.simpleConsole.SetCursorPosition(cursorLeft, cursorTop, CursorOperation.Show);
    }

    public void TryDeleteBuffer(int index)
    {
        if (index < 0 ||
            index >= (this.BufferList.Count - 1))
        {
            return;
        }

        var dif = -this.BufferList[index].Height;
        this.BufferList.RemoveAt(index);
        for (var i = index; i < this.BufferList.Count; i++)
        {
            var buffer = this.BufferList[i];
            buffer.Index = i;
            buffer.Top += dif;
            buffer.Write(0, -1, 0, 0, true);
        }

        this.ClearLastLine(dif);
        this.simpleConsole.SetCursor(this.BufferList[index]);
    }

    public void Clear()
    {
        this.MultilineMode = false;
        this.EditableBufferIndex = 0;
        foreach (var buffer in this.BufferList)
        {
            this.simpleConsole.ReturnBuffer(buffer);
        }

        this.BufferList.Clear();
    }

    private void ClearLastLine(int dif)
    {
        var buffer = this.BufferList[this.BufferList.Count - 1];
        var top = buffer.Top + buffer.Height;
        for (var i = 0; i < -dif; i++)
        {
            this.ClearLine(top + i);
        }
    }

    private void ClearLine(int top)
    {
        var windowBuffer = SimpleConsole.RentWindowBuffer();
        var buffer = windowBuffer.AsSpan();
        var written = 0;
        ReadOnlySpan<char> span;

        /*span = ConsoleHelper.SaveCursorSpan;
        span.CopyTo(buffer);
        buffer = buffer.Slice(span.Length);
        written += span.Length;*/

        span = ConsoleHelper.SetCursorSpan;
        span.CopyTo(buffer);
        buffer = buffer.Slice(span.Length);
        written += span.Length;

        var x = top + 1;
        var y = 0 + 1;
        x.TryFormat(buffer, out var w);
        buffer = buffer.Slice(w);
        written += w;
        buffer[0] = ';';
        buffer = buffer.Slice(1);
        written += 1;
        y.TryFormat(buffer, out w);
        buffer = buffer.Slice(w);
        written += w;
        buffer[0] = 'H';
        buffer = buffer.Slice(1);
        written += 1;

        span = ConsoleHelper.EraseEntireLineSpan;
        span.CopyTo(buffer);
        buffer = buffer.Slice(span.Length);
        written += span.Length;

        /*span = ConsoleHelper.RestoreCursorSpan;
        span.CopyTo(buffer);
        buffer = buffer.Slice(span.Length);
        written += span.Length;*/

        this.RawConsole.WriteInternal(windowBuffer.AsSpan(0, written));
        SimpleConsole.ReturnWindowBuffer(windowBuffer);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static bool TryCopy(ReadOnlySpan<char> source, ref Span<char> destination)
    {
        if (source.Length > destination.Length)
        {
            return false;
        }

        source.CopyTo(destination);
        destination = destination.Slice(source.Length);
        return true;
    }
}
