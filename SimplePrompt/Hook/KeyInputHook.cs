// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

namespace SimplePrompt;

/// <summary>
/// Specifies how to handle a key after a hook runs.
/// </summary>
public enum KeyInputHookResult
{
    /// <summary>
    /// Continues normal processing of the key, including any changes made by the hook.
    /// </summary>
    NotHandled = 0,

    /// <summary>
    /// Discards the key without canceling the read operation.
    /// </summary>
    Handled = 1,

    /// <summary>
    /// Cancels the read when returned by <see cref="ReadLineOptions.KeyInputHook"/>.
    /// </summary>
    /// <remarks>The global <see cref="SimpleConsole.KeyInputHook"/> only discards the key for this result.</remarks>
    Cancel = 2,
}

/// <summary>
/// Intercepts a terminal or injected key before normal input processing.
/// </summary>
/// <param name="keyInfo">The key to inspect or replace.</param>
/// <returns>The action to take for this key.</returns>
/// <remarks>
/// Runs synchronously on the input worker; do not block waiting for another read.
/// Exceptions fault the read whose hook is running, or the active read for a global hook.
/// </remarks>
public delegate KeyInputHookResult KeyInputHook(ref ConsoleKeyInfo keyInfo);
