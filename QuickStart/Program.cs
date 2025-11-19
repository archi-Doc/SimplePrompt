// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Arc;
using Arc.Threading;
using Arc.Unit;
using SimplePrompt;

namespace QuickStart;

internal class Program
{
    public static async Task Main(string[] args)
    {
        AppDomain.CurrentDomain.ProcessExit += (s, e) =>
        {// Console window closing or process terminated.
            ThreadCore.Root.Terminate(); // Send a termination signal to the root.
            ThreadCore.Root.TerminationEvent.WaitOne(2_000); // Wait until the termination process is complete (#1).
        };

        Console.CancelKeyPress += (s, e) =>
        {// Ctrl+C pressed
            e.Cancel = true;
            ThreadCore.Root.Terminate(); // Send a termination signal to the root.
        };

        var simpleConsole = SimpleConsole.GetOrCreate(); // Get or create the singleton instance.
        simpleConsole.Configuration = new SimpleConsoleConfiguration()
        {// Set configuration options.
            InputColor = ConsoleColor.Yellow,
            MultilineIdentifier = "|",
            CancelReadLineOnEscape = true,
            AllowEmptyLineInput = true,
        };

        simpleConsole.WriteLine("Simple prompt example");
        simpleConsole.WriteLine("Esc:Cancel input, Ctrl+U:Clear input, Home:Move to start, End:Move to end");
        simpleConsole.WriteLine("Test:Delayed output, '|':Multi-line mode switch, Exit: Exit app");

        while (!ThreadCore.Root.IsTerminated)
        {
            var result = await simpleConsole.ReadLine($"> ", "# ");

            if (result.Kind == InputResultKind.Terminated)
            {// Ctrl+C pressed or termination requested
                break;
            }
            else if (result.Kind == InputResultKind.Canceled)
            {// Esc pressed
                simpleConsole.WriteLine("Canceled");
                continue;
            }
            else if (string.Equals(result.Text, "Exit", StringComparison.InvariantCultureIgnoreCase))
            {// Exit
                ThreadCore.Root.Terminate(); // Send a termination signal to the root.
                break;
            }
            else if (string.IsNullOrEmpty(result.Text))
            {// Enter pressed without input
                continue;
            }
            else if (string.Equals(result.Text, "Test", StringComparison.InvariantCultureIgnoreCase))
            {// Test command: Delayed output
                _ = Task.Run(async () =>
                {
                    simpleConsole.WriteLine("Test string");
                    ((IConsoleService)simpleConsole).Write("xxxxx");
                    await Task.Delay(1000);
                    simpleConsole.WriteLine("abcdefgabcdefgabcdefg"); // Displayed above the prompt
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

        await ThreadCore.Root.WaitForTerminationAsync(-1); // Wait for the termination infinitely.
        ThreadCore.Root.TerminationEvent.Set(); // The termination process is complete (#1).
    }
}
