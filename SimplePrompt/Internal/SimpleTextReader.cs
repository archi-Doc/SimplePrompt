// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

namespace SimplePrompt.Internal;

internal sealed class SimpleTextReader : TextReader
{
    public ReadLineOptions ReadLineOptions { get; }

    public SimpleConsole SimpleConsole { get; }

    public TextReader UnderlyingTextReader { get; }

    public SimpleTextReader(SimpleConsole simpleConsole, TextReader inner)
    {
        this.SimpleConsole = simpleConsole;
        this.UnderlyingTextReader = inner;
        this.ReadLineOptions = ReadLineOptions.SingleLine with
        {
            Prompt = string.Empty,
        };
    }

    public override string? ReadLine()
    {
        var result = this.SimpleConsole.ReadLine(this.ReadLineOptions).Result;
        return result.Text;
    }
}
