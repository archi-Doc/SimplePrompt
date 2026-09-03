// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

#pragma warning disable SA1204 // Static elements should appear before instance elements

namespace SimplePrompt;

/// <summary>
/// Configures console input, prompts, and validation.
/// </summary>
/// <remarks>Use a <c>with</c> expression to create a modified copy.</remarks>
public record class ReadLineOptions
{
    /// <summary>
    /// Provides single-line input with a 1024-code-unit limit and no empty submissions.
    /// </summary>
    public static readonly ReadLineOptions SingleLine = new()
    {
        MaxInputLength = 1024,
        MultilineDelimiter = null,
        LineContinuationCharacter = default,
        AllowEmptyInput = false,
    };

    /// <summary>
    /// Provides the default options, including the <c>"""</c> multiline delimiter.
    /// </summary>
    public static readonly ReadLineOptions Multiline = new()
    {
    };

    /// <summary>
    /// Provides single-line input accepting only y, yes, n, or no, ignoring case and surrounding whitespace.
    /// </summary>
    /// <remarks>Limits input to three UTF-16 code units and returns accepted text unchanged.</remarks>
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
    /// Gets the input text color. Defaults to <see cref="ConsoleColor.Yellow"/>.
    /// </summary>
    public ConsoleColor InputColor { get; init; } = ConsoleColor.Yellow;

    /// <summary>
    /// Gets the input limit in UTF-16 code units. Defaults to 65536.
    /// </summary>
    /// <remarks>
    /// Counts each separator between input lines as one code unit, including in continuation mode.
    /// Excess input is discarded. Prompts and text produced by <see cref="TextInputHook"/> are not counted.
    /// </remarks>
    public int MaxInputLength { get; init; } = 1024 * 64;

    /// <summary>
    /// Gets the input prompt. Defaults to <c>&gt; </c>.
    /// </summary>
    /// <remarks>May contain newlines; input starts on the last prompt line.</remarks>
    public string Prompt { get; init; } = "> ";

    /// <summary>
    /// Gets the prompt for subsequent input lines. Defaults to <c># </c>.
    /// </summary>
    public string MultilinePrompt { get; init; } = "# ";

    /// <summary>
    /// Gets the multiline delimiter. Defaults to three double quotes (<c>"""</c>).
    /// </summary>
    /// <remarks>
    /// On Enter, an odd delimiter count on the first input line starts delimiter mode;
    /// an odd count on a later line ends it. Lines are joined with <c>\n</c>, retaining delimiters.
    /// A null or empty delimiter disables this mode; <see cref="LineContinuationCharacter"/> is independent.
    /// </remarks>
    public string? MultilineDelimiter { get; init; } = "\"\"\"";

    /// <summary>
    /// Gets the trailing character that continues input onto the next line. Defaults to <c>\0</c> (disabled).
    /// </summary>
    /// <remarks>Continuation lines are joined without newlines, removing their trailing continuation markers.</remarks>
    public char LineContinuationCharacter { get; init; }

    /// <summary>
    /// Gets a value indicating whether Escape cancels the read operation. Defaults to <see langword="false"/>.
    /// </summary>
    public bool CancelOnEscape { get; init; }

    /// <summary>
    /// Gets a value indicating whether Enter can submit empty input. Defaults to <see langword="false"/>.
    /// </summary>
    /// <remarks>Checked before <see cref="TextInputHook"/>; blank lines within nonempty multiline input are allowed.</remarks>
    public bool AllowEmptyInput { get; init; }

    /// <summary>
    /// Gets the character displayed instead of input. Defaults to <c>\0</c> (no masking).
    /// </summary>
    /// <remarks>Use a printable, single-column character. Masking preserves display width and does not change the returned text.</remarks>
    public char MaskingCharacter { get; init; }

    /// <summary>
    /// Gets the key hook for this read operation. Defaults to <see langword="null"/>.
    /// </summary>
    /// <remarks>
    /// Runs after <see cref="SimpleConsole.KeyInputHook"/>, key normalization, and the <see cref="CancelOnEscape"/> check.
    /// May rewrite the key, return <see cref="KeyInputHookResult.Handled"/> to discard it,
    /// or return <see cref="KeyInputHookResult.Cancel"/> to cancel the read.
    /// Text from <see cref="SimpleConsole.EnqueueInput"/> bypasses key hooks.
    /// </remarks>
    public KeyInputHook? KeyInputHook { get; init; }

    /// <summary>
    /// Gets the submission validation or transformation hook. Defaults to <see langword="null"/>.
    /// </summary>
    /// <remarks>Returns the final text, or null to clear the input and prompt again. Exceptions fault the read task.</remarks>
    public TextInputHook? TextInputHook { get; init; }
}
