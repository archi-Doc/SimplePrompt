// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

namespace SimplePrompt;

/// <summary>
/// Represents a method that validates or transforms the text when the user submits the input.
/// </summary>
/// <param name="text">The input text submitted by the user.</param>
/// <returns>
/// The validated or transformed text to be returned as the final result.<br/>
/// If <see langword="null"/> is returned, the input is discarded and a new prompt is displayed so that the user can enter it again.
/// </returns>
public delegate string? TextInputHook(string text);
