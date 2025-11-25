// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using System.Runtime.CompilerServices;
using Arc;
using Arc.Unit;
using SimplePrompt.Internal;

namespace SimplePrompt;

internal record class ReadLineInstance
{
    public ReadLineOptions Options { get; }

    internal char[] WindowBuffer => this.simpleConsole.WindowBuffer;

    internal RawConsole RawConsole => this.simpleConsole.RawConsole;

    private readonly SimpleConsole simpleConsole;
    private readonly char[] charBuffer = new char[CharBufferSize];
    private List<InputBuffer> buffers = new();
    private int editableBufferIndex;
    private bool multilineMode;

    public ReadLineInstance(SimpleConsole simpleConsole, ReadLineOptions options)
    {
        this.simpleConsole = simpleConsole;
        this.Options = options with { }; // Clone
    }

    public void PrepareInputBuffer()
    {
        var prompt = this.Options.Prompt.AsSpan();
        var bufferIndex = 0;
        InputBuffer buffer;
        while (prompt.Length >= 0)
        {
            var index = BaseHelper.IndexOfLfOrCrLf(prompt, out var newLineLength);
            if (index < 0)
            {
                buffer = this.simpleConsole.RentBuffer(bufferIndex++, prompt.ToString());
                prompt = default;
            }
            else
            {
                buffer = this.simpleConsole.RentBuffer(bufferIndex++, prompt.Slice(0, index).ToString());
                prompt = prompt.Slice(index + newLineLength);
            }

            this.buffers.Add(buffer);
            buffer.Top = this.simpleConsole.CursorTop;
            buffer.UpdateHeight(false);

            var span = this.simpleConsole.WindowBuffer.AsSpan();
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

            this.RawConsole.WriteInternal(this.WindowBuffer.AsSpan(0, this.WindowBuffer.Length - span.Length));

            if (prompt.Length == 0)
            {
                this.editableBufferIndex = bufferIndex - 1;
                this.simpleConsole.MoveCursor2(buffer.PromtWidth);
                this.simpleConsole.TrimCursor();
                this.simpleConsole.SetCursorPosition(this.simpleConsole.CursorLeft, this.simpleConsole.CursorTop, CursorOperation.None);
                break;
            }
        }
    }

    private void Clear()
    {
        this.multilineMode = false;
        foreach (var buffer in this.buffers)
        {
            this.simpleConsole.ReturnBuffer(buffer);
        }

        this.buffers.Clear();
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
