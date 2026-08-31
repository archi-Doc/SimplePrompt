// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

namespace SimplePrompt.Internal;

/// <summary>
/// Specifies the visibility operation performed together with a cursor move.
/// </summary>
internal enum CursorOperation
{
    /// <summary>
    /// The cursor visibility is left unchanged.
    /// </summary>
    None,

    /// <summary>
    /// The cursor is shown after the move.
    /// </summary>
    Show,

    /// <summary>
    /// The cursor is hidden after the move.
    /// </summary>
    Hide,

    /// <summary>
    /// The cursor position is updated even if it is unchanged. Currently behaves the same as <see cref="None"/>.
    /// </summary>
    ForceSet,
}
