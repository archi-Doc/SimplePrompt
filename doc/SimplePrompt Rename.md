# SimplePrompt Rename

Public API renames made after version 0.27.0. Behavior is unchanged; only names changed.

## Renames

| # | Kind | Old | New |
|---|---|---|---|
| 1 | Method | `SimpleConsole.ReadLine(options, cancellationToken)` | `SimpleConsole.ReadLineAsync(options, cancellationToken)` |
| 2 | Delegate | `TextInputHook` | `SubmitHook` |
| 3 | Property | `ReadLineOptions.TextInputHook` | `ReadLineOptions.SubmitHook` |
| 4 | Property | `ReadLineOptions.MultilinePrompt` | `ReadLineOptions.ContinuationPrompt` |
| 5 | Method | `SimpleConsole.EnqueueInput(text)` | `SimpleConsole.EnqueueLine(text)` |
| 6 | Parameter | `SimpleConsole.Clear(bool clearBuffer)` | `SimpleConsole.Clear(bool clearScreenBuffer)` |
| 7 | Property | `SimpleConsole.DefaultOptions` | `SimpleConsole.DefaultReadLineOptions` |
| 8 | Parameter | `SimpleConsole.Write/WriteLine(string? message, ...)` and `(ReadOnlySpan<char> message, ...)` | `value` |

## Migration

Apply these replacements to C# sources that reference SimplePrompt:

| Find | Replace |
|---|---|
| `.ReadLine(` on a `SimpleConsole` instance | `.ReadLineAsync(` |
| `TextInputHook` | `SubmitHook` |
| `MultilinePrompt` | `ContinuationPrompt` |
| `EnqueueInput(` | `EnqueueLine(` |
| `DefaultOptions` (on `SimpleConsole`) | `DefaultReadLineOptions` |
| `clearBuffer:` | `clearScreenBuffer:` |
| `message:` (in `Write`/`WriteLine` calls) | `value:` |

Notes:

- #6 and #8 only break callers that use named arguments.
- Do not rename `Console.ReadLine()` or `TextReader.ReadLine()`; only `SimpleConsole.ReadLine` changed.
- `IConsoleService.ReadLineAsync(CancellationToken)` from `Arc.Unit` is unchanged.
- XML doc `cref` references also need updating, e.g. `ReadLine(ReadLineOptions?, CancellationToken)` -> `ReadLineAsync(ReadLineOptions?, CancellationToken)`.
- Source file `SimplePrompt/Hook/TextInputHook.cs` was renamed to `SimplePrompt/Hook/SubmitHook.cs`.

## Example

Before:

```csharp
simpleConsole.DefaultOptions = ReadLineOptions.SingleLine with
{
    MultilinePrompt = "... ",
    TextInputHook = text => int.TryParse(text, out _) ? text : null,
};
simpleConsole.EnqueueInput("42");
var result = await simpleConsole.ReadLine();
simpleConsole.Clear(clearBuffer: true);
```

After:

```csharp
simpleConsole.DefaultReadLineOptions = ReadLineOptions.SingleLine with
{
    ContinuationPrompt = "... ",
    SubmitHook = text => int.TryParse(text, out _) ? text : null,
};
simpleConsole.EnqueueLine("42");
var result = await simpleConsole.ReadLineAsync();
simpleConsole.Clear(clearScreenBuffer: true);
```
