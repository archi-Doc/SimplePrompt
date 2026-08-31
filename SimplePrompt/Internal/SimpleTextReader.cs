// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

namespace SimplePrompt.Internal;

/// <summary>
/// A <see cref="TextReader"/> installed as <see cref="Console.In"/> so that Console.ReadLine() goes through <see cref="SimpleConsole"/>.
/// </summary>
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
        => this.SimpleConsole.ReadLine(this.ReadLineOptions).GetAwaiter().GetResult().Text;
}
