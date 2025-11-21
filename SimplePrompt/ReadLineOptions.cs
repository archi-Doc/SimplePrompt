// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Arc.Threading;

#pragma warning disable SA1204 // Static elements should appear before instance elements

namespace SimplePrompt;

/// <summary>
/// Represents configuration options for reading input from the console.
/// </summary>
public record class ReadLineOptions
{
    /// <summary>
    /// Gets the color used for user input in the console.
    /// Default is <see cref="ConsoleColor.Yellow"/>.
    /// </summary>
    public ConsoleColor InputColor { get; init; } = ConsoleColor.Yellow;

    /// <summary>
    /// Gets the string displayed as the prompt for single-line input.<br/>
    /// Default is "&gt; ".
    /// </summary>
    public string Prompt { get; init; } = "> ";

    /// <summary>
    /// Gets the string displayed as the prompt for continuation lines in multiline input mode.<br/>
    /// Set this to <see langword="null"/> to disable multi-line input.<br/>
    /// Default is "# ".
    /// </summary>
    public string? MultilinePrompt { get; init; } = "# ";

    /// <summary>
    /// Gets the string identifier used to denote multiline input.<br/>
    /// Default is three double quotes (""").
    /// </summary>
    public string MultilineIdentifier { get; init; } = "\"\"\"";

    /// <summary>
    /// Gets a value indicating whether to cancel the ReadLine operation when the Escape key is pressed.
    /// Default is <see langword="false"/>.
    /// </summary>
    public bool CancelOnEscape { get; init; } = false;

    /// <summary>
    /// Gets a value indicating whether an empty line (pressing Enter with no characters entered) is treated as valid input.
    /// </summary>
    public bool AllowEmptyLineInput { get; init; } = false;

    public char MaskingCharacter { get; init; } = '*';
}
