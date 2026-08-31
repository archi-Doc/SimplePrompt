// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

namespace SimplePrompt.Internal;

/// <summary>
/// Specifies how the input lines of a ReadLine operation are combined.
/// </summary>
internal enum ReadLineMode
{
    /// <summary>
    /// A single line. Enter completes the input.
    /// </summary>
    Singleline,

    /// <summary>
    /// Multiple lines started by <see cref="ReadLineOptions.MultilineDelimiter"/>. The lines are joined with a newline.
    /// </summary>
    Delimiter,

    /// <summary>
    /// Multiple lines started by <see cref="ReadLineOptions.LineContinuationCharacter"/>.
    /// The continuation characters are removed and the lines are joined without a newline.
    /// </summary>
    LineContinuation,
}
