// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Arc.Threading;
using static SimplePrompt.SimpleConsole;

#pragma warning disable SA1204 // Static elements should appear before instance elements

namespace SimplePrompt;

/// <summary>
/// Represents configuration options for reading input from the console.
/// </summary>
public record class ReadLineOptions
{
    public static readonly ReadLineOptions SingleLine = new()
    {
        MaxInputLength = 1024,
        MultilineIdentifier = null,
        AllowEmptyLineInput = false,
    };

    /// <summary>
    /// Gets the color used for user input in the console.
    /// Default is <see cref="ConsoleColor.Yellow"/>.
    /// </summary>
    public ConsoleColor InputColor { get; init; } = ConsoleColor.Yellow;

    /// <summary>
    /// Gets the maximum number of characters allowed for user input.<br/>
    /// Default is 64KB.
    /// </summary>
    public int MaxInputLength { get; init; } = 1024 * 64;

    /// <summary>
    /// Gets the string displayed as the prompt for single-line input.<br/>
    /// Default is "&gt; ".
    /// </summary>
    public string Prompt { get; init; } = "> ";

    /// <summary>
    /// Gets the string displayed as the prompt for continuation lines in multiline input mode.<br/>
    /// Default is "# ".
    /// </summary>
    public string MultilinePrompt { get; init; } = "# ";

    /// <summary>
    /// Gets the string identifier used to denote multiline input.<br/>
    /// Default is three double quotes (""").<br/>
    /// Set this to <see langword="null"/> to disable multi-line input.<br/>
    /// </summary>
    public string? MultilineIdentifier { get; init; } = "\"\"\"";

    /// <summary>
    /// Gets a value indicating whether to cancel the ReadLine operation when the Escape key is pressed.
    /// Default is <see langword="false"/>.
    /// </summary>
    public bool CancelOnEscape { get; init; } = false;

    /// <summary>
    /// Gets a value indicating whether an empty line (pressing Enter with no characters entered) is treated as valid input.
    /// </summary>
    public bool AllowEmptyLineInput { get; init; } = false;

    /// <summary>
    /// Gets the character used to mask user input in the console (e.g., for password entry).
    /// Default is 0 (no masking).
    /// </summary>
    public char MaskingCharacter { get; init; } = default;

    /// <summary>
    /// Gets the hook for intercepting and processing key input during console reading operations.
    /// Default is <see langword="null"/> (no custom key input handling).<br/>
    /// If provided and returns <see langword="true"/>, the key input is considered handled and will not be processed further.
    /// </summary>
    public KeyInputHook? KeyInputHook { get; init; } = default;
}
