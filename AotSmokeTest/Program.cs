// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using System.Runtime.CompilerServices;
using Arc.Unit;
using SimplePrompt;
using SimplePrompt.Internal;

var terminal = args.Contains("--terminal", StringComparer.Ordinal);
var capturedWriter = args.Contains("--capture", StringComparer.Ordinal);
if (!args.Contains("--allow-jit", StringComparer.Ordinal))
{
    Check(!RuntimeFeature.IsDynamicCodeSupported, "The smoke test must run as a NativeAOT executable.");
}

var originalOutput = Console.Out;
using var output = new StringWriter();
if (!terminal || capturedWriter)
{
    Console.SetOut(output);
}

var console = SimpleConsole.Instance;
console.EnableColor = false;
console.DefaultOptions = ReadLineOptions.SingleLine;

if (terminal)
{
    Check(!Console.IsInputRedirected, "The terminal test requires a pseudo-terminal.");
    if (!OperatingSystem.IsWindows())
    {
        Check(console.RawConsole.UseStdin, "Unix native input initialization failed.");
        Check(TermInfo.DatabaseFactory.ReadActiveDatabase() is not null, "The terminal database could not be loaded.");
    }

    Console.Error.WriteLine("READY");
    var result = await Read(console.ReadLine());
    Check(result.IsSuccess && result.Text == "日本語😀abZ", "Terminal UTF-8 input or cursor editing failed.");
    Console.WriteLine("TERMINAL-OUTPUT");
    if (capturedWriter)
    {
        Check(output.ToString().Contains("TERMINAL-OUTPUT", StringComparison.Ordinal), "Output bypassed the captured writer.");
    }
}
else
{
    // Exercise dependency code, the worker and console redirection after trimming.
    console.EnqueueInput("日本語😀");
    var queued = await Read(console.ReadLine());
    Check(queued.IsSuccess && queued.Text == "日本語😀", "Queued Unicode input failed.");

    var edited = console.ReadLine();
    Type("a😀b");
    Key(ConsoleKey.Backspace);
    Key(ConsoleKey.Backspace);
    Type("c");
    Key(ConsoleKey.Enter);
    Check((await Read(edited)).Text == "ac", "Surrogate-pair editing failed.");

    var multiline = console.ReadLine(ReadLineOptions.Multiline);
    Type("\"\"\"");
    Key(ConsoleKey.Enter);
    Type("line\"\"\"");
    Key(ConsoleKey.Enter);
    Check((await Read(multiline)).Text == "\"\"\"\nline\"\"\"", "Multiline input failed.");

    var hooked = console.ReadLine(ReadLineOptions.SingleLine with { TextInputHook = text => text.ToUpperInvariant() });
    Type("hook");
    Key(ConsoleKey.Enter);
    Check((await Read(hooked)).Text == "HOOK", "Text input hook failed.");

    using var cancellation = new CancellationTokenSource();
    var canceled = console.ReadLine(cancellationToken: cancellation.Token);
    cancellation.Cancel();
    Check((await Read(canceled)).IsCanceled, "Cancellation failed.");

    var decoded = new RawConsole(console).DecodeKeys("\e[1;5A");
    Check(decoded.Count == 1 && decoded[0].Key == ConsoleKey.UpArrow && decoded[0].Modifiers == ConsoleModifiers.Control, "Terminal key decoding failed.");

    output.GetStringBuilder().Clear();
    Console.WriteLine("AOT-OUTPUT 日本語😀");
    console.WriteLine("COLOR-DISABLED", ConsoleColor.Yellow);
    var text = output.ToString();
    Check(text.Contains("AOT-OUTPUT 日本語😀", StringComparison.Ordinal), "Console.Out redirection failed.");
    Check(!text.Contains("\e[33m", StringComparison.Ordinal), "Disabled colors were emitted.");
}

originalOutput.WriteLine("NativeAOT smoke test passed.");

static void Check(bool condition, string message)
{
    if (!condition)
    {
        throw new InvalidOperationException(message);
    }
}

static Task<InputResult> Read(Task<InputResult> task)
    => task.WaitAsync(TimeSpan.FromSeconds(15));

void Type(string text)
{
    foreach (var character in text)
    {
        console.EnqueueKey(new ConsoleKeyInfo(character, default, false, false, false));
    }
}

void Key(ConsoleKey key)
    => console.EnqueueKey(new ConsoleKeyInfo(default, key, false, false, false));
