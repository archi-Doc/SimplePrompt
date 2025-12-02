// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using System.Text;

namespace SimplePrompt.Internal;

internal class SimpleTextWriter : TextWriter
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

    public override void Write(char value)
    {
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
