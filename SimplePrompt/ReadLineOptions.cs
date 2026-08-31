// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

#pragma warning disable SA1204 // Static elements should appear before instance elements

namespace SimplePrompt;

/// <summary>
/// Represents configuration options for reading input from the console.<br/>
/// This is an immutable record; use a <c>with</c> expression to create a modified copy.
/// </summary>
public record class ReadLineOptions
{
    /// <summary>
    /// Options for a single line of input: multiline input is disabled and an empty input is not accepted.
    /// </summary>
    public static readonly ReadLineOptions SingleLine = new()
    {
        MaxInputLength = 1024,
        MultilineDelimiter = null,
        LineContinuation = default,
        AllowEmptyLineInput = false,
    };

    /// <summary>
    /// Options with the default settings, where multiline input is enabled by the <c>"""</c> delimiter.
    /// </summary>
    public static readonly ReadLineOptions MultiLine = new()
    {
    };

    /// <summary>
    /// Options for a yes/no question.<br/>
    /// Only "y", "yes", "n" and "no" (case-insensitive) are accepted; any other input is rejected and asked again.
    /// </summary>
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
    /// Gets the color used for user input in the console.<br/>
    /// Default is <see cref="ConsoleColor.Yellow"/>.
    /// </summary>
    public ConsoleColor InputColor { get; init; } = ConsoleColor.Yellow;

    /// <summary>
    /// Gets the maximum number of characters allowed for user input.<br/>
    /// The newline between input lines is counted as one character, and the characters which exceed the limit are discarded.<br/>
    /// Default is 65536 (64K) characters.
    /// </summary>
    public int MaxInputLength { get; init; } = 1024 * 64;

    /// <summary>
    /// Gets the string displayed as the prompt.<br/>
    /// It may contain newlines; in that case the last line becomes the input line.<br/>
    /// Default is "&gt; ".
    /// </summary>
    public string Prompt { get; init; } = "> ";

    /// <summary>
    /// Gets the string displayed as the prompt for the second and subsequent lines in multiline input.<br/>
    /// Default is "# ".
    /// </summary>
    public string MultilinePrompt { get; init; } = "# ";

    /// <summary>
    /// Gets the string which switches multiline input on and off.<br/>
    /// When a line contains an odd number of delimiters, multiline input starts (or ends, if it has already started).<br/>
    /// The lines are joined with a newline, and the delimiters remain in the result.<br/>
    /// Default is three double quotes (""").<br/>
    /// Set this to <see langword="null"/> to disable multiline input.
    /// </summary>
    public string? MultilineDelimiter { get; init; } = "\"\"\"";

    /// <summary>
    /// Gets the character which indicates that the current line continues onto the next line (e.g. '\').<br/>
    /// The continuation characters are removed and the lines are joined without a newline.<br/>
    /// Default is <c><see langword="default"/></c> (no line continuation).
    /// </summary>
    public char LineContinuation { get; init; }

    /// <summary>
    /// Gets a value indicating whether to cancel the ReadLine operation when the Escape key is pressed.<br/>
    /// Default is <see langword="false"/>.
    /// </summary>
    public bool CancelOnEscape { get; init; }

    /// <summary>
    /// Gets a value indicating whether an empty line (pressing Enter with no characters entered) is treated as valid input.<br/>
    /// When <see langword="false"/>, Enter is ignored until at least one character is entered.<br/>
    /// Default is <see langword="false"/>.
    /// </summary>
    public bool AllowEmptyLineInput { get; init; }

    /// <summary>
    /// Gets the character used to mask user input in the console (e.g., for password entry).<br/>
    /// The input is echoed with this character, while the result still contains the actual text.<br/>
    /// Default is 0 (no masking).
    /// </summary>
    public char MaskingCharacter { get; init; }

    /// <summary>
    /// Gets the hook for intercepting key input during this ReadLine operation.<br/>
    /// The key can be rewritten through the <see langword="ref"/> parameter, discarded by returning
    /// <see cref="KeyInputHookResult.Handled"/>, or the operation can be canceled by returning <see cref="KeyInputHookResult.Cancel"/>.<br/>
    /// It is called after <see cref="SimpleConsole.KeyInputHook"/>.<br/>
    /// Default is <see langword="null"/> (no custom key input handling).
    /// </summary>
    public KeyInputHook? KeyInputHook { get; init; }

    /// <summary>
    /// Gets the hook for validating or transforming the text when the user submits the input.<br/>
    /// If a string is returned, it becomes the result of the ReadLine operation.<br/>
    /// If <see langword="null"/> is returned, the input is discarded and the user is prompted to enter it again.<br/>
    /// Default is <see langword="null"/> (no custom text input handling).
    /// </summary>
    public TextInputHook? TextInputHook { get; init; }
}
