// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Arc;
using Arc.Unit;
using SimplePrompt;

namespace QuickStart;

internal class Program
{
    public static async Task Main(string[] args)
    {
        var simpleConsole = SimpleConsole.GetOrCreate(); // Create the singleton SimplePrompt instance. Note that all Console calls (such as Console.Out) will go through SimpleConsole.
        simpleConsole.DefaultOptions = new ReadLineOptions()
        {// Set ReadLine() options.
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
            else if (string.Equals(result.Text, "Exit", StringComparison.InvariantCultureIgnoreCase))
            {// Exit
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
    }
}
