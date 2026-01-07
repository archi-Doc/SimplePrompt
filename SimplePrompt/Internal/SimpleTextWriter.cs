// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using System.Text;

namespace SimplePrompt.Internal;

internal sealed class SimpleTextWriter : TextWriter
{
    public SimpleConsole SimpleConsole { get; }

    public TextWriter UnderlyingTextWriter { get; }

    public SimpleTextWriter(SimpleConsole simpleConsole, TextWriter inner)
    {
        this.SimpleConsole = simpleConsole;
        this.UnderlyingTextWriter = inner;
    }

    public override Encoding Encoding => System.Text.Encoding.UTF8;

    public override void WriteLine(string? value)
        => this.SimpleConsole.WriteLine(value);

    public override void Write(string? value)
        => this.SimpleConsole.Write(value);

    public override void WriteLine(double value)
        => this.SimpleConsole.WriteLine(value.ToString(this.FormatProvider));

    public override void Write(double value)
        => this.SimpleConsole.Write(value.ToString(this.FormatProvider));

    public override void WriteLine(bool value)
        => this.SimpleConsole.WriteLine(value.ToString());

    public override void Write(bool value)
        => this.SimpleConsole.Write(value.ToString());

    public override void Write(char value)
    {// coi char char[]? decimal float int uint long ulong object? ReadOnlySpan<char>
        if (!this.SimpleConsole.IsReadLineInProgress)
        {
            this.UnderlyingTextWriter.Write(value);
        }
    }

    public override void Write(char[] buffer, int index, int count)
    {
        if (!this.SimpleConsole.IsReadLineInProgress)
        {
            this.UnderlyingTextWriter.Write(buffer, index, count);
        }
    }

    public override void WriteLine()
        => this.SimpleConsole.WriteLine();

    public override void Flush()
        => this.UnderlyingTextWriter.Flush();
}
