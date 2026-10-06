// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

namespace SimplePrompt;

/// <summary>
/// Validates or transforms submitted input.
/// </summary>
/// <param name="text">The submitted text after multiline processing.</param>
/// <returns>
/// The final text, or <see langword="null"/> to start fresh input below the rejected submission.
/// </returns>
/// <remarks>
/// Runs synchronously on the input worker after input length and empty-input checks.
/// Returned text is not checked again for length or emptiness. Exceptions fault the read task.
/// </remarks>
public delegate string? SubmitHook(string text);
