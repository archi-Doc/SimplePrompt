// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

#pragma warning disable SA1204 // Static elements should appear before instance elements

namespace SimplePrompt;

/// <summary>
/// Configuration settings for the simple console prompt.
/// </summary>
public record class SimpleConsoleConfiguration
{
    /// <summary>
    /// Gets the color used for user input in the console.
    /// Default is <see cref="ConsoleColor.Yellow"/>.
    /// </summary>
    public ConsoleColor InputColor { get; init; } = ConsoleColor.Yellow;

    /// <summary>
    /// Gets the string identifier used to denote multiline input.
    /// Default is three double quotes (""").
    /// </summary>
    public string MultilineIdentifier { get; init; } = "\"\"\"";
}
