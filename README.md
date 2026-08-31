## SimplePrompt

![Nuget](https://img.shields.io/nuget/v/SimplePrompt) ![Build and Test](https://github.com/archi-Doc/SimplePrompt/workflows/Build%20and%20Test/badge.svg)

A simple console interface with advanced input handling capabilities including multiline support and custom prompts.

- Line editing with cursor movement, multiline input, and masked input.
- Output from background tasks is displayed above the prompt without breaking the input.
- `Console.Out` and `Console.In` are redirected, so existing console code keeps working.
- Full-width characters (CJK) and surrogate pairs are measured and edited correctly.



## Table of Contents

- [Requirements](#requirements)
- [Quick Start](#quick-start)
- [ReadLineOptions](#readlineoptions)
- [Key Bindings](#key-bindings)
- [Features](#features)
  - [Output While Reading](#output-while-reading)
  - [Multi-line Input](#multi-line-input)
  - [Masked Input](#masked-input)
  - [Input Hooks](#input-hooks)
  - [Nested ReadLine()](#nested-readline)
  - [Queued Input](#queued-input)
  - [Cancellation](#cancellation)
- [SimpleConsole Members](#simpleconsole-members)



## Requirements

**.NET 10** or later.

Colors and cursor control are performed with ANSI escape sequences, so a VT-capable terminal is required (Windows Terminal, Linux and macOS terminals). On Unix, stdin is read directly so that key sequences can be decoded without blocking.



## Quick Start

Install **SimplePrompt** using Package Manager Console.

```
Install-Package SimplePrompt
```

This is a small sample code to use **SimplePrompt**.

```c#
using Arc;
using Arc.Unit;
using SimplePrompt;

var simpleConsole = SimpleConsole.Instance; // Get the singleton SimplePrompt instance. Note that all Console calls (such as Console.Out) will go through SimpleConsole.
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
            simpleConsole.WriteLine("abcdefgabcdefgabcdefg", ConsoleColor.Green); // Displayed above the prompt
            await Task.Delay(1000);
            Console.Out.WriteLine("abcdefg0123456789abcdefg0123456789abcdefg0123456789"); // Output via Console.Out is also supported.
        });
    }
    else
    {// Echo the input
        var text = BaseHelper.RemoveCrLf(result.Text);
        simpleConsole.WriteLine($"Command: {text}");
    }
}
```

`ReadLine()` returns an `InputResult` (`Arc.Unit`).

| Member | Description |
| --- | --- |
| `Text` | The input text. It is empty when the operation did not succeed. |
| `Kind` | `Success`, `Canceled` (Esc or a canceled token) or `Terminated` (the execution group was terminated). |
| `IsSuccess` / `IsCanceled` / `IsTerminated` | Shortcuts for testing `Kind`. |



## ReadLineOptions

**SimplePrompt** features are enabled by configuring `ReadLineOptions`. It is an immutable record, so a variation is created with a `with` expression. Pass it to `ReadLine(options)`, or assign it to `SimpleConsole.DefaultOptions` to change the default.

```csharp
var options = ReadLineOptions.SingleLine with
{
    Prompt = "Name>> ",
    MaxInputLength = 32,
};

var result = await simpleConsole.ReadLine(options);
```

| Property | Type | Default | Description |
| --- | --- | --- | --- |
| `Prompt` | `string` | `"> "` | The prompt string. It may contain newlines; the last line becomes the input line. |
| `InputColor` | `ConsoleColor` | `Yellow` | The color of the user input. |
| `MaxInputLength` | `int` | `65536` | The maximum number of input characters. The newline between input lines is counted as one character, and characters exceeding the limit are discarded. |
| `AllowEmptyLineInput` | `bool` | `false` | Whether pressing Enter with no input completes the operation. When `false`, Enter is ignored until at least one character is entered. |
| `CancelOnEscape` | `bool` | `false` | Whether the Escape key cancels the operation. |
| `MaskingCharacter` | `char` | `'\0'` | The character echoed instead of the input (e.g. for a password). The result still contains the actual text. |
| `MultilineDelimiter` | `string?` | `"""` | The string which switches multiline input on and off. `null` disables multiline input. |
| `MultilinePrompt` | `string` | `"# "` | The prompt for the second and subsequent lines of multiline input. |
| `LineContinuation` | `char` | `'\0'` | The character which continues the line onto the next line (e.g. `'\\'`). `'\0'` disables it. |
| `KeyInputHook` | `KeyInputHook?` | `null` | Called for every key before it is processed. See [Input Hooks](#input-hooks). |
| `TextInputHook` | `TextInputHook?` | `null` | Called when the input is submitted, to validate or transform it. See [Input Hooks](#input-hooks). |

Presets are provided for common cases.

| Preset | Description |
| --- | --- |
| `ReadLineOptions.SingleLine` | Single line input (multiline disabled, max 1024 characters, empty input not accepted). |
| `ReadLineOptions.MultiLine` | The default settings, where multiline input is enabled by the `"""` delimiter. |
| `ReadLineOptions.YesNo` | Accepts only "y", "yes", "n" or "no" (case-insensitive); any other input is asked again. |



## Key Bindings

| Key | Action |
| --- | --- |
| Enter | Completes the input, or adds a new line during multiline input. |
| Esc | Cancels the input (only when `CancelOnEscape` is `true`). |
| Backspace | Deletes the character before the cursor. On an empty continuation line, deletes the line. |
| Delete | Deletes the character at the cursor. |
| Home / End | Moves to the beginning / end of the current line. |
| ← / → | Moves the cursor by one character (a surrogate pair moves as a single character). |
| ↑ / ↓ | Moves between lines during multiline input. |
| Ctrl+U | Clears the current line. |

Tab and Insert are reserved for future use (input completion and overtype mode) and are currently ignored.



## Features

### Output While Reading

While `ReadLine()` is waiting for input, output from any thread is displayed above the prompt, and the prompt and the text being edited are redrawn below it.

```csharp
simpleConsole.WriteLine("Displayed above the prompt", ConsoleColor.Green);
Console.Out.WriteLine("Console.Out is redirected, so this works as well.");
```

`SimpleConsole.Instance` replaces `Console.Out` and `Console.In`, so `Console.Write()`, `Console.WriteLine()` and `Console.In.ReadLine()` all go through **SimplePrompt**. Set `EnableColor` to `false` to suppress every color sequence, and use `UnderlyingTextWriter` to write directly to the original `Console.Out`.



### Multi-line Input

When a line contains an odd number of `MultilineDelimiter`, multiline input starts; the next odd occurrence ends it. The lines are joined with a newline and the delimiters remain in the result.

```
> """
# line1
# line2"""      ->  """\nline1\nline2"""
```

Alternatively, `LineContinuation` continues the input while a line ends with the specified character. The continuation characters are removed and the lines are joined without a newline.

```csharp
var options = ReadLineOptions.SingleLine with { LineContinuation = '\\' };
```

```
> abc\
# def           ->  abcdef
```



### Masked Input

Set `MaskingCharacter` to hide the input, for example when reading a password.

```csharp
var options = ReadLineOptions.SingleLine with { MaskingCharacter = '*' };
var result = await simpleConsole.ReadLine(options); // The console shows '*', result.Text holds the actual input.
```



### Input Hooks

`KeyInputHook` is called for every key before it is processed. The key can be rewritten through the `ref` parameter, discarded, or used to cancel the operation.

```csharp
var options = ReadLineOptions.SingleLine with
{
    KeyInputHook = (ref ConsoleKeyInfo keyInfo) =>
    {
        if (keyInfo.Key == ConsoleKey.F1)
        {
            return KeyInputHookResult.Cancel; // Cancels ReadLine().
        }

        return KeyInputHookResult.NotHandled; // Processes the key normally.
    },
};
```

`SimpleConsole.KeyInputHook` is the console-wide equivalent, and is called before the hook of `ReadLineOptions`. It is applied to the keys injected by `EnqueueKey()` as well. Note that a key is simply discarded when the hook returns anything other than `NotHandled`.

`TextInputHook` is called when the input is submitted. Return the (possibly transformed) text to accept it, or `null` to discard the input and ask again.

```csharp
var options = ReadLineOptions.SingleLine with
{
    TextInputHook = text => int.TryParse(text, out _) ? text : null, // Accepts a number only.
};
```



### Nested ReadLine()

While waiting in `ReadLine()`, you can call the next `ReadLine()` and nest it.
 When the second `ReadLine()` finishes, the original `ReadLine()` is restored.

```csharp
var result = await simpleConsole.ReadLine(options, currentCts.Token);
if (string.Equals(result.Text, "d", StringComparison.OrdinalIgnoreCase))
{
    var options2 = ReadLineOptions.SingleLine with
    {
        Prompt = "Nested>> ",
    };

    _ = Task.Run(async () =>
    {
        await Task.Delay(100); // Wait briefly to allow ReadLine() to be nested.
        var result = await simpleConsole.ReadLine(options2);
        Console.WriteLine($"Nested: {result.Text}");
    });
}
```

Calling `ReadLine()` again with the same `ReadLineOptions` instance does not nest; it returns the task of the operation already in progress.



### Queued Input

By calling `EnqueueInput()`, you can emulate user input as if the user had typed it and pressed Enter. The queued text is consumed while the current input is empty; otherwise it stays queued for the next `ReadLine()`.

```csharp
simpleConsole.EnqueueInput("a");
```

`EnqueueKey()` injects a single key, which is processed exactly like a key pressed by the user.

```csharp
simpleConsole.EnqueueKey(new ConsoleKeyInfo('a', ConsoleKey.A, false, false, false));
```



### Cancellation

An input operation can be canceled in several ways, and the result is `InputResultKind.Canceled`.

```csharp
using var cts = new CancellationTokenSource();
var result = await simpleConsole.ReadLine(options, cts.Token); // Canceled by the token,
                                                              // by the Escape key (CancelOnEscape),
                                                              // or by KeyInputHookResult.Cancel.
```

If `SimpleConsole.ExecutionGroup` (`Arc.Threading`) is set, terminating the group stops the input polling loop and completes the pending operations with `InputResultKind.Terminated`.

```csharp
simpleConsole.ExecutionGroup = root;
```



## SimpleConsole Members

| Member | Description |
| --- | --- |
| `Instance` | The singleton instance. Creating it redirects `Console.Out` and `Console.In`. |
| `ReadLine(options, cancellationToken)` | Reads a line of input. |
| `Write()` / `WriteLine()` | Writes a value with an optional `ConsoleColor`. Overloads for the primitive types, `string` and `ReadOnlySpan<char>` are provided. |
| `Clear(clearBuffer)` | Clears the console and redraws the prompt. |
| `EnqueueInput(message)` / `EnqueueKey(keyInfo)` | Injects input programmatically. |
| `DefaultOptions` | The options used when `ReadLine()` is called without options. |
| `KeyInputHook` | The console-wide key input hook. |
| `EnableColor` | Whether color escape sequences are emitted. |
| `IsReadLineInProgress` | Whether an input operation is in progress. |
| `TryGetCurrentReadLineOptions(out options)` | Gets the options of the operation which is currently accepting input. |
| `UnderlyingTextWriter` | The original `Console.Out`. |
| `ExecutionGroup` | The execution group which controls the lifetime of the input polling loop. |
| `CursorLeft` / `CursorTop` / `WindowWidth` / `WindowHeight` / `GetCursorPosition()` | Static shortcuts to the cursor and window state tracked by **SimplePrompt**. |
