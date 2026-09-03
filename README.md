# SimplePrompt

![NuGet](https://img.shields.io/nuget/v/SimplePrompt) ![Build and Test](https://github.com/archi-Doc/SimplePrompt/actions/workflows/test.yml/badge.svg)

A .NET console library for editable prompts, multiline input, and output above an active prompt.

- Line editing with custom prompts, input limits, and masking.
- Background output that preserves the prompt and text being edited.
- Input validation, key hooks, nested reads, and programmatic input.
- CJK display widths, surrogate-pair editing, and NativeAOT support.

## Contents

- [Requirements](#requirements)
- [Quick Start](#quick-start)
- [Console Integration](#console-integration)
- [ReadLineOptions](#readlineoptions)
- [Key Bindings](#key-bindings)
- [Features](#features)
  - [Output While Reading](#output-while-reading)
  - [Multiline Input](#multiline-input)
  - [Masked Input](#masked-input)
  - [Input Hooks](#input-hooks)
  - [Nested Reads](#nested-reads)
  - [Queued Input](#queued-input)
  - [Cancellation and Shutdown](#cancellation-and-shutdown)
  - [Screen and Cursor](#screen-and-cursor)
- [SimpleConsole Members](#simpleconsole-members)
- [NativeAOT](#nativeaot)
- [Testing and Coverage](#testing-and-coverage)

## Requirements

SimplePrompt targets **.NET 10**. Use .NET SDK 10.0.400 or later to build this repository with its current dependency analyzers.

Interactive input requires a terminal that supports ANSI/VT cursor control, such as Windows Terminal or a Linux/macOS terminal. On Unix, SimplePrompt reads terminal input directly and decodes key sequences.

## Quick Start

Add the package to a console application:

```sh
dotnet add package SimplePrompt
```

```csharp
using Arc.Unit;
using SimplePrompt;

var simpleConsole = SimpleConsole.Instance;
simpleConsole.DefaultOptions = ReadLineOptions.SingleLine with
{
    Prompt = "Command> ",
    CancelOnEscape = true,
};

simpleConsole.WriteLine("Enter a command, press Escape to cancel, or type exit.");
while (true)
{
    var result = await simpleConsole.ReadLine();
    if (result.Kind == InputResultKind.Terminated)
    {
        break;
    }

    if (result.Kind == InputResultKind.Canceled)
    {
        simpleConsole.WriteLine("Canceled.");
        continue;
    }

    if (string.Equals(result.Text, "exit", StringComparison.OrdinalIgnoreCase))
    {
        break;
    }

    simpleConsole.WriteLine($"Command: {result.Text}");
}
```

`ReadLine()` returns `Task<InputResult>`. `InputResult` is provided by `Arc.Unit`:

| Member | Meaning |
| --- | --- |
| `Text` | Submitted text on success; empty for cancellation or termination. |
| `Kind` | `Success`, `Canceled`, or `Terminated`. |
| `IsSuccess` / `IsCanceled` / `IsTerminated` | Shortcuts for testing `Kind`. |

The following examples assume `simpleConsole = SimpleConsole.Instance` and `using SimplePrompt;`.

## Console Integration

Accessing `SimpleConsole.Instance` initializes the singleton once, attempts to select UTF-8 output, and replaces `Console.Out` and `Console.In`.

- `Console.Write()`, `Console.WriteLine()`, and writes through `Console.Out` use SimplePrompt's output handling.
- Synchronous `Console.ReadLine()` and `Console.In.ReadLine()` use single-line input with no prompt, a 1024-code-unit limit, and empty input allowed. They use their own options, independently of `DefaultOptions`.
- The installed reader only overrides synchronous `ReadLine`; it does not forward general character or block reads to the original reader.
- `Console.Error`, `Console.ReadKey()`, and `Console.KeyAvailable` keep their original behavior. Direct key reads do not consume queued input and may compete with SimplePrompt's input worker.

`SimpleConsole` implements `Arc.Unit.IConsoleService`. Its interface `ReadLine` uses `DefaultOptions`; interface `ReadKey` and `KeyAvailable` call the corresponding `Console` APIs directly.

Rendered output goes to the writer captured at initialization, including redirected standard output. Cursor escape sequences can still appear in redirected output. Reading a file or pipe through redirected stdin is not supported by `SimpleConsole.ReadLine()`; preserve the original `Console.In` before initialization if your application needs to read it.

## ReadLineOptions

`ReadLineOptions` is an immutable record. Use `with` to customize a preset, then pass it to `ReadLine(options)` or assign it to `DefaultOptions`.

```csharp
var options = ReadLineOptions.SingleLine with
{
    Prompt = "Name> ",
    MaxInputLength = 32,
};
var result = await simpleConsole.ReadLine(options);
```

These defaults apply to `new ReadLineOptions()`:

| Property | Default | Meaning |
| --- | --- | --- |
| `Prompt` | `"> "` | Input prompt. May contain newlines; input starts on its last line. |
| `InputColor` | `ConsoleColor.Yellow` | Input text color. |
| `MaxInputLength` | `65536` | Input limit in UTF-16 code units; excess input is discarded. |
| `AllowEmptyInput` | `false` | Allows Enter to submit input with no characters. Blank lines within nonempty multiline input are allowed regardless. |
| `CancelOnEscape` | `false` | Cancels the read when Escape is processed. |
| `MaskingCharacter` | `'\0'` | Display mask; zero disables masking. Does not change the result text. |
| `MultilineDelimiter` | `"""` (three double quotes) | Delimiter for multiline mode. Null or empty disables delimiter mode only. |
| `MultilinePrompt` | `"# "` | Prompt for subsequent input lines. |
| `LineContinuationCharacter` | `'\0'` | Trailing character that continues input onto the next line; zero disables continuation. |
| `KeyInputHook` | `null` | Per-read key interception. See [Input Hooks](#input-hooks). |
| `TextInputHook` | `null` | Submission validation or transformation. See [Input Hooks](#input-hooks). |

`MaxInputLength` excludes prompts, counts a surrogate pair as two code units, and counts each separator between input lines as one. This also applies to continuation lines whose separators are removed from the final result. Text returned by `TextInputHook` is not subject to this limit.

| Preset | Settings |
| --- | --- |
| `SingleLine` | Multiline modes disabled, limit 1024, empty input rejected. |
| `Multiline` | Default options, including the `"""` delimiter. |
| `YesNo` | Single-line input, limit 3, accepting y/yes/n/no after trimming and ignoring case. Returns accepted text unchanged; invalid input prompts again. |

## Key Bindings

| Key | Action |
| --- | --- |
| Enter | Submits input or continues multiline input. |
| Escape | Cancels the read when `CancelOnEscape` is enabled. |
| Backspace | Deletes the preceding character; removes an empty input line when it is not the first. |
| Delete | Deletes the character at the cursor; removes an empty input line when it is neither the first nor the last. |
| Home / End | Moves to the start / end of the current logical input line. |
| Left / Right | Moves by one character, keeping surrogate pairs together. |
| Up / Down | Moves between displayed rows and input lines in multiline mode. |
| Ctrl+U | Clears the current input line. |

Tab, Insert, and command history are not implemented. Editing preserves surrogate pairs but does not treat every Unicode grapheme cluster as a single unit; rendered widths also depend on the terminal.

## Features

### Output While Reading

While a read is pending, output from any thread is placed above the prompt, then the prompt, input, and caret are restored.

```csharp
simpleConsole.WriteLine("Background task completed.", ConsoleColor.Green);
Console.Out.WriteLine("Output through Console.Out works too.");
```

During a pending read, a nonempty `Write()` also ends the output line before redrawing the prompt. Without a pending read, `Write()` does not append a newline. Numeric overloads use the underlying writer's format provider.

Omit the color argument to leave the output color unchanged. `EnableColor = false` suppresses SimplePrompt-generated color sequences, but does not strip caller-supplied ANSI sequences or disable cursor control. Direct writes to `UnderlyingTextWriter` bypass prompt redrawing and cursor tracking.

### Multiline Input

On Enter, an odd number of `MultilineDelimiter` occurrences on the first input line starts delimiter mode. An odd count on a later line ends it. Lines are joined with `\n`, preserving delimiters and blank lines.

```text
> """
# first line
# second line"""
```

The result is `"""\nfirst line\nsecond line"""`.

Alternatively, configure a continuation character:

```csharp
var options = ReadLineOptions.SingleLine with
{
    LineContinuationCharacter = '\\',
};
var result = await simpleConsole.ReadLine(options);
```

```text
> abc\
# def
```

The result is `abcdef`: continuation lines are joined without newlines, removing trailing continuation markers from nonfinal lines. A line without the marker completes the input. Starting from `SingleLine` keeps delimiter mode disabled.

### Masked Input

Use a printable, single-column masking character:

```csharp
var options = ReadLineOptions.SingleLine with
{
    Prompt = "Password> ",
    MaskingCharacter = '*',
};
var result = await simpleConsole.ReadLine(options);
```

Masking preserves input display width, so a wide character can produce multiple mask characters. `result.Text` contains the actual input.

### Input Hooks

`SimpleConsole.KeyInputHook` processes terminal keys and `EnqueueKey()` events before the per-read hook, including while idle. It may rewrite the key through its `ref` parameter. Both `Handled` and `Cancel` discard the key at this global level without canceling a read.

`ReadLineOptions.KeyInputHook` runs after the global hook, key normalization, and the `CancelOnEscape` check. It can return `NotHandled` to continue, `Handled` to discard the key, or `Cancel` to cancel the read:

```csharp
var options = ReadLineOptions.SingleLine with
{
    KeyInputHook = (ref ConsoleKeyInfo keyInfo) =>
        keyInfo.Key == ConsoleKey.F1
            ? KeyInputHookResult.Cancel
            : KeyInputHookResult.NotHandled,
};
var result = await simpleConsole.ReadLine(options);
```

With `CancelOnEscape` enabled, Escape cancels before the per-read hook runs; the global hook can still intercept it. Text queued with `EnqueueInput()` bypasses both key hooks.

`TextInputHook` receives submitted text after multiline processing, input limits, and the empty-input check. Return a string to accept or transform it, or null to clear the input and prompt again:

```csharp
var options = ReadLineOptions.SingleLine with
{
    TextInputHook = text => int.TryParse(text, out _) ? text : null,
};
var result = await simpleConsole.ReadLine(options);
```

The transformed text is not checked again for length or emptiness. Hooks run synchronously on the input worker; keep them short. A thrown exception faults the active read task and is rethrown when it is awaited.

### Nested Reads

A new read can start while another is pending. The latest read receives input, and the earlier read resumes when it completes:

```csharp
var outer = simpleConsole.ReadLine(ReadLineOptions.SingleLine with { Prompt = "Outer> " });
var inner = simpleConsole.ReadLine(ReadLineOptions.SingleLine with { Prompt = "Inner> " });

var innerResult = await inner;
var outerResult = await outer;
```

After checking termination and cancellation, passing the same options object as an existing read returns that read's task. It retains the original cancellation token. Distinct options objects can create nested reads even when their values are equal. Omitting options uses the current `DefaultOptions` object.

Each new read copies its options. `TryGetCurrentReadLineOptions(out var options)` returns that active snapshot, or false with null when no read is pending.

### Queued Input

`EnqueueInput()` queues literal text followed by one submission attempt. It is consumed only when the active read's input is empty; otherwise it remains queued. Input limits, multiline rules, and `TextInputHook` still apply.

```csharp
simpleConsole.EnqueueInput("example");
var result = await simpleConsole.ReadLine(ReadLineOptions.SingleLine);
```

Null or empty text attempts an empty submission. Embedded newlines are literal text, not separate Enter events. Use `EnqueueKey()` for editing keys or an Enter event; these pass through the key hooks:

```csharp
simpleConsole.EnqueueKey(new ConsoleKeyInfo('a', ConsoleKey.A, false, false, false));
simpleConsole.EnqueueKey(new ConsoleKeyInfo('\r', ConsoleKey.Enter, false, false, false));
```

`BufferKeyInputWhileIdle` defaults to true and retains up to 32768 key events received while no read is pending. Excess keys are discarded. Setting it to false discards keys processed while idle. This setting does not affect queued text.

### Cancellation and Shutdown

A canceled token, Escape with `CancelOnEscape`, or `Cancel` from the per-read key hook completes the read with `InputResultKind.Canceled`. The returned task is not canceled and does not throw `OperationCanceledException` for these normal cancellation paths.

```csharp
using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
var result = await simpleConsole.ReadLine(
    ReadLineOptions.SingleLine with { CancelOnEscape = true }, cts.Token);
```

Optionally assign an `Arc.Threading.ExecutionGroup` to `ExecutionGroup` to control the worker's lifetime. Terminating that group stops input polling and completes pending reads with `InputResultKind.Terminated`. This is permanent shutdown: assigning another group does not restart the worker. With no group assigned, the worker runs until process exit.

### Screen and Cursor

`Clear(false)` erases the visible screen with an ANSI sequence. `Clear(true)` calls `Console.Clear()`. Both redraw active input; clearing terminal scrollback is not guaranteed.

`CursorLeft`, `CursorTop`, and `GetCursorPosition()` return tracked zero-based coordinates. They do not query the terminal when read. `WindowWidth` and `WindowHeight` are refreshed periodically, with minimum values of 30 columns and 10 rows. Accessing these static members also initializes the singleton.

## SimpleConsole Members

| Member | Purpose |
| --- | --- |
| `Instance` | Lazily initialized singleton. |
| `ReadLine(options, cancellationToken)` | Starts or retrieves a pending read and returns `Task<InputResult>`. |
| `Write(...)` / `WriteLine(...)` | Writes with optional foreground color; supports bool, char, decimal, double, float, int, uint, long, ulong, string, and `ReadOnlySpan<char>`. |
| `Clear(clearBuffer)` | Clears the screen and redraws active input. |
| `EnqueueInput(text)` / `EnqueueKey(keyInfo)` | Queues text or a key event. |
| `DefaultOptions` | Options for reads without explicit options. Initially a new `ReadLineOptions`. |
| `KeyInputHook` | Global key interception. |
| `EnableColor` | Enables library-generated color sequences; initially true. |
| `BufferKeyInputWhileIdle` | Retains idle key input; initially true. |
| `IsReadLineInProgress` | Reports whether any read is pending. |
| `TryGetCurrentReadLineOptions(out options)` | Retrieves the active read's options snapshot. |
| `UnderlyingTextWriter` | Output writer captured at initialization. |
| `ExecutionGroup` | Optional worker lifetime control. |
| `CursorLeft` / `CursorTop` / `GetCursorPosition()` | Static access to tracked cursor coordinates. |
| `WindowWidth` / `WindowHeight` | Static access to cached window dimensions. |

## NativeAOT

SimplePrompt enables `IsAotCompatible` and the .NET trimming and AOT analyzers. Enable `<PublishAot>true</PublishAot>` in the consuming application's project, or pass the property when publishing:

```sh
dotnet publish QuickStart/QuickStart.csproj -c Release -r win-x64 -p:PublishAot=true
```

Publish on the target operating system, using a matching runtime identifier such as `win-x64`, `linux-x64`, or `osx-arm64`. Install the platform's [NativeAOT build prerequisites](https://learn.microsoft.com/dotnet/core/deploying/native-aot/#prerequisites).

`AotSmokeTest` analyzes the entire SimplePrompt assembly, treats trimming and AOT compiler warnings as errors, and exercises input, hooks, Unicode editing, cancellation, and redirected output in the native executable:

```sh
dotnet publish AotSmokeTest/AotSmokeTest.csproj -c Release -r win-x64 -o artifacts/aot -warnaserror
./artifacts/aot/AotSmokeTest.exe
```

On Linux or macOS, publish for that platform and run `./artifacts/aot/AotSmokeTest`. Also verify terminal input and redirected output through a pseudo-terminal:

```sh
python3 AotSmokeTest/terminal_test.py artifacts/aot/AotSmokeTest
```

CI defines NativeAOT jobs for Windows, Linux, and macOS. Unix input uses .NET's `System.Native` console functions; the pseudo-terminal checks exercise that integration and terminfo loading.

## Testing and Coverage

Run from the repository root:

```sh
dotnet test --project xUnitTest/xUnitTest.csproj -c Release
dotnet tool restore
dotnet coverage collect -s xUnitTest/coverage.config.xml -f cobertura -o artifacts/coverage.cobertura.xml "dotnet xUnitTest/bin/Release/net10.0/xUnitTest.dll"
```

The [coverage configuration](xUnitTest/coverage.config.xml) measures the SimplePrompt library. CI uploads the Cobertura report as the `code-coverage` artifact. Use the report's line and branch results to identify untested paths; the NativeAOT and pseudo-terminal checks above cover additional runtime integration.
