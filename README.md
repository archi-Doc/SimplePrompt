## SimplePrompt

![Nuget](https://img.shields.io/nuget/v/SimplePrompt) ![Build and Test](https://github.com/archi-Doc/SimplePrompt/workflows/Build%20and%20Test/badge.svg)

A simple console interface with advanced input handling capabilities including multiline support and custom prompts.



## Table of Contents

- [Requirements](#requirements)
- [Quick Start](#quick-start)



## Requirements

**.NET 10** or later



## Quick Start

Install **SimplePrompt** using Package Manager Console.

```
Install-Package SimplePrompt
```

This is a small sample code to use **SimplePrompt**.

```c#
var simpleConsole = SimpleConsole.GetOrCreate(); // Get or create the singleton SimplePrompt instance.
simpleConsole.Configuration = new SimpleConsoleConfiguration()
{// Set configuration options.
    InputColor = ConsoleColor.Yellow,
    MultilineIdentifier = "|",
    CancelReadLineOnEscape = true,
    AllowEmptyLineInput = true,
};

simpleConsole.WriteLine("SimplePrompt example");
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
            Console.Out.Write("xxxxx"); // Only supports line-by-line output.
        });
    }
    else
    {// Echo the input
        var text = BaseHelper.RemoveCrLf(result.Text);
        simpleConsole.WriteLine($"Command: {text}");
    }
}
```

