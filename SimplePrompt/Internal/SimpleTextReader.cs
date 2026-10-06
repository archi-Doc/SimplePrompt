// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

namespace SimplePrompt.Internal;

/// <summary>
/// Routes <see cref="Console.ReadLine"/> through <see cref="SimpleConsole"/>.
/// </summary>
/// <remarks>Returns <see langword="null"/> when the console worker has terminated.</remarks>
internal sealed class SimpleTextReader : TextReader
{
    public ReadLineOptions ReadLineOptions { get; }

    public SimpleConsole SimpleConsole { get; }

    public SimpleTextReader(SimpleConsole simpleConsole)
    {
        this.SimpleConsole = simpleConsole;
        this.ReadLineOptions = ReadLineOptions.SingleLine with
        {
            Prompt = string.Empty,
            AllowEmptyInput = true,
        };
    }

    public override string? ReadLine()
    {
        var result = this.SimpleConsole.ReadLineAsync(this.ReadLineOptions).GetAwaiter().GetResult();
        return result.IsTerminated ? null : result.Text;
    }
}
