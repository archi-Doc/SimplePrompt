// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

#pragma warning disable SA1204 // Static elements should appear before instance elements

namespace SimplePrompt;

public record class SimpleConsoleConfiguration
{
    public ConsoleColor InputColor { get; init; } = ConsoleColor.Yellow;

    public string MultilineIdentifier { get; init; } = "\"\"\"";
}
