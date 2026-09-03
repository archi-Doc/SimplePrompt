// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

namespace SimplePrompt;

/// <summary>
/// Validates or transforms submitted input.
/// </summary>
/// <param name="text">The input text submitted by the user.</param>
/// <returns>
/// The final text, or <see langword="null"/> to clear the input and prompt again.
/// </returns>
/// <remarks>
/// Runs synchronously on the input worker after input length and empty-input checks.
/// Returned text is not checked again. Exceptions fault the read task.
/// </remarks>
public delegate string? TextInputHook(string text);
