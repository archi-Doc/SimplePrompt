// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

namespace SimplePrompt;

/// <summary>
/// Represents a method that handles text input validation or transformation after the user submits input.
/// </summary>
/// <param name="text">The input text submitted by the user.</param>
/// <returns>
/// The validated or transformed text to be returned as the final result.
/// If <see langword="null"/> is returned, the input is rejected and the user can continue editing.
/// </returns>
public delegate string? TextInputHook(string text);
