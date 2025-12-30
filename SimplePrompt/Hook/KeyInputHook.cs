// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

namespace SimplePrompt;

/// <summary>
/// Represents the result of a key input hook.
/// </summary>
public enum KeyInputHookResult
{
    /// <summary>
    /// The key input was not handled by the hook and should be processed normally.
    /// </summary>
    NotHandled = 0,

    /// <summary>
    /// The key input was handled by the hook and should not be processed further.
    /// </summary>
    Handled = 1,

    /// <summary>
    /// The key input was handled by the hook and the current input operation should be canceled.
    /// </summary>
    Cancel = 2,
}

/// <summary>
/// Represents a method that handles key input events during console read operations.
/// </summary>
/// <param name="keyInfo">The <see cref="ConsoleKeyInfo"/> containing information about the pressed key.</param>
/// <returns>The hook result.</returns>
public delegate KeyInputHookResult KeyInputHook(ConsoleKeyInfo keyInfo);
