// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

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
        MultilineDelimiter = null,
        LineContinuation = default,
        AllowEmptyLineInput = false,
    };

    public static readonly ReadLineOptions MultiLine = new()
    {
    };

    public static readonly ReadLineOptions YesNo = new()
    {
        MaxInputLength = 3,
        MultilineDelimiter = default,
        CancelOnEscape = false,
        TextInputHook = text =>
        {
            var st = text.Trim().ToLowerInvariant();
            if (st == "y" || st == "yes" || st == "n" || st == "no")
            {
                return text;
            }

            return null;
        },
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
    public string? MultilineDelimiter { get; init; } = "\"\"\"";

    /// <summary>
    /// Gets the character used to indicate that the current line should be continued onto the next line (e.g. '\').<br/>
    /// Default is <c><see langword="default"/></c> (no line continuation).
    /// </summary>
    public char LineContinuation { get; init; }

    /// <summary>
    /// Gets a value indicating whether to cancel the ReadLine operation when the Escape key is pressed.
    /// Default is <see langword="false"/>.
    /// </summary>
    public bool CancelOnEscape { get; init; }

    /// <summary>
    /// Gets a value indicating whether an empty line (pressing Enter with no characters entered) is treated as valid input.
    /// </summary>
    public bool AllowEmptyLineInput { get; init; }

    /// <summary>
    /// Gets the character used to mask user input in the console (e.g., for password entry).
    /// Default is 0 (no masking).
    /// </summary>
    public char MaskingCharacter { get; init; }

    /// <summary>
    /// Gets the hook for intercepting and processing key input during console reading operations.
    /// Default is <see langword="null"/> (no custom key input handling).<br/>
    /// If provided and returns <see langword="true"/>, the key input is considered handled and will not be processed further.
    /// </summary>
    public KeyInputHook? KeyInputHook { get; init; }

    /// <summary>
    /// Gets the hook for intercepting and processing text input during console reading operations.
    /// Default is <see langword="null"/> (no custom text input handling).<br/>
    /// If a valid string is returned, it is treated as valid text input and the function completes.<br/>
    /// If <see langword="null"/> is returned, the input is rejected and the user is prompted to enter it again.
    /// </summary>
    public TextInputHook? TextInputHook { get; init; }
}
