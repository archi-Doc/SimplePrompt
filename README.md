## SimplePrompt

![Nuget](https://img.shields.io/nuget/v/SimplePrompt) ![Build and Test](https://github.com/archi-Doc/SimplePrompt/workflows/Build%20and%20Test/badge.svg)

A simple console interface with advanced input handling capabilities including multiline support and custom prompts.



## Table of Contents

- [Requirements](#requirements)
- [Quick Start](#quick-start)
- [ReadLineOptions](#readlineoptions)
- [Features](#features)



## Requirements

**.NET 10** or later



## Quick Start

Install **SimplePrompt** using Package Manager Console.

```
Install-Package SimplePrompt
```

This is a small sample code to use **SimplePrompt**.

```c#
var simpleConsole = SimpleConsole.GetOrCreate(); // Create the singleton SimplePrompt instance. Note that all Console calls (such as Console.Out) will go through SimpleConsole.
simpleConsole.DefaultOptions = new ReadLineOptions()
{// Set the default ReadLine options.
    InputColor = ConsoleColor.Yellow,
    Prompt = "> ",
    MultilinePrompt = "# ",
    MultilineDelimiter = "|",
    CancelOnEscape = true,
    AllowEmptyLineInput = true,
};

Console.Out.Write("SimplePrompt example\r\n");
simpleConsole.WriteLine("Esc:Cancel input, Ctrl+U:Clear input, Home:Move to start, End:Move to end");
simpleConsole.WriteLine("Test:Delayed output, '|':Multi-line mode switch, Exit: Exit app");

while (true)
{
    var result = await simpleConsole.ReadLine();

    if (result.Kind == InputResultKind.Canceled)
    {// Esc pressed
        simpleConsole.WriteLine("Canceled");
        continue;
    }
    else if (string.Equals(result.Text, "Clear", StringComparison.OrdinalIgnoreCase))
    {// Clear
        simpleConsole.Clear(false);
        continue;
    }
    else if (string.Equals(result.Text, "Exit", StringComparison.OrdinalIgnoreCase))
    {// Exit
        break;
    }
    else if (string.IsNullOrEmpty(result.Text))
    {// Enter pressed without input
        continue;
    }
    else if (string.Equals(result.Text, "Test", StringComparison.OrdinalIgnoreCase))
    {// Test command: Delayed output
        _ = Task.Run(async () =>
        {
            simpleConsole.WriteLine("Test string");
            await Task.Delay(1000);
            simpleConsole.WriteLine("abcdefgabcdefgabcdefg"); // Displayed above the prompt
            await Task.Delay(1000);
            Console.Out.WriteLine("abcdefg0123456789abcdefg0123456789abcdefg0123456789"); // Output via Console.Out is also supported.
            Console.Out.Write("Write:Not displayed"); // Not displayed while ReadLine is waiting for input.
        });
    }
    else
    {// Echo the input
        var text = BaseHelper.RemoveCrLf(result.Text);
        simpleConsole.WriteLine($"Command: {text}");
    }
}
```

## ReadLineOptions

 **SimplePrompt** features are enabled by configuring `ReadLineOptions`.

```csharp
public record class ReadLineOptions
{
    public static readonly ReadLineOptions SingleLine = new()
    {
        MaxInputLength = 1024,
        MultilineDelimiter = null,
        LineContinuation = default,
        AllowEmptyLineInput = false,
    };

    public static readonly ReadLineOptions MultiLine = new()
    {
    };

    /// <summary>
    /// Gets the color used for user input in the console.
    /// Default is <see cref="ConsoleColor.Yellow"/>.
    /// </summary>
    public ConsoleColor InputColor { get; init; } = ConsoleColor.Yellow;

    /// <summary>
    /// Gets the maximum number of characters allowed for user input.<br/>
    /// Default is 64KB.
    /// </summary>
    public int MaxInputLength { get; init; } = 1024 * 64;

    /// <summary>
    /// Gets the string displayed as the prompt for single-line input.<br/>
    /// Default is "&gt; ".
    /// </summary>
    public string Prompt { get; init; } = "> ";

    /// <summary>
    /// Gets the string displayed as the prompt for continuation lines in multiline input mode.<br/>
    /// Default is "# ".
    /// </summary>
    public string MultilinePrompt { get; init; } = "# ";

    /// <summary>
    /// Gets the string identifier used to denote multiline input.<br/>
    /// Default is three double quotes (""").<br/>
    /// Set this to <see langword="null"/> to disable multi-line input.<br/>
    /// </summary>
    public string? MultilineDelimiter { get; init; } = "\"\"\"";

    /// <summary>
    /// Gets the character used to indicate that the current line should be continued onto the next line (e.g. '\').<br/>
    /// Default is <c><see langword="default"/></c> (no line continuation).
    /// </summary>
    public char LineContinuation { get; init; }

    /// <summary>
    /// Gets a value indicating whether to cancel the ReadLine operation when the Escape key is pressed.
    /// Default is <see langword="false"/>.
    /// </summary>
    public bool CancelOnEscape { get; init; }

    /// <summary>
    /// Gets a value indicating whether an empty line (pressing Enter with no characters entered) is treated as valid input.
    /// </summary>
    public bool AllowEmptyLineInput { get; init; }

    /// <summary>
    /// Gets the character used to mask user input in the console (e.g., for password entry).
    /// Default is 0 (no masking).
    /// </summary>
    public char MaskingCharacter { get; init; }

    /// <summary>
    /// Gets the hook for intercepting and processing key input during console reading operations.
    /// Default is <see langword="null"/> (no custom key input handling).<br/>
    /// If provided and returns <see langword="true"/>, the key input is considered handled and will not be processed further.
    /// </summary>
    public KeyInputHook? KeyInputHook { get; init; }

    /// <summary>
    /// Gets the hook for intercepting and processing text input during console reading operations.
    /// Default is <see langword="null"/> (no custom text input handling).<br/>
    /// If a valid string is returned, it is treated as valid text input and the function completes.<br/>
    /// If <see langword="null"/> is returned, the input is rejected and the user is prompted to enter it again.
    /// </summary>
    public TextInputHook? TextInputHook { get; init; }
}
```

## Features

### Nested ReadLine

### Queued Input
