// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Arc;
using Arc.Threading;
using Arc.Unit;
using Microsoft.Extensions.DependencyInjection;
using SimplePrompt;

namespace Playground;

internal class Program
{
    public static async Task Main(string[] args)
    {
        AppCloseHandler.Set(() =>
        {// Console window closing or process terminated.
            ThreadCore.Root.Terminate(); // Send a termination signal to the root.
            ThreadCore.Root.TerminationEvent.WaitOne(2_000); // Wait until the termination process is complete (#1).
        });

        Console.CancelKeyPress += (s, e) =>
        {// Ctrl+C pressed
            e.Cancel = true;
            ThreadCore.Root.Terminate(); // Send a termination signal to the root.
        };

        var builder = new UnitBuilder()
            .Configure(context =>
            {
                context.AddLoggerResolver(x =>
                {
                    x.SetOutput<FileLogger<FileLoggerOptions>>();
                    return;
                });
            })
            .PostConfigure(context =>
            {
                var logfile = "Logs/Log.txt";
                context.SetOptions(context.GetOptions<FileLoggerOptions>() with
                {
                    Path = Path.Combine(context.ProgramDirectory, logfile),
                    MaxLogCapacity = 1,
                });
            });

        var product = builder.Build();
        var logger = product.Context.ServiceProvider.GetRequiredService<ILogger<DefaultLog>>();
        logger.TryGet()?.Log("Start");
        Console.OutputEncoding = System.Text.Encoding.UTF8;

        var simpleConsole = SimpleConsole.GetOrCreate();
        Console.WriteLine(Environment.OSVersion.ToString());

        // Tests
        // await TestConsoleMode(simpleConsole);
        await TestMultilinePrompt(simpleConsole);

        await ThreadCore.Root.WaitForTerminationAsync(-1); // Wait for the termination infinitely.
        if (product.Context.ServiceProvider.GetService<UnitLogger>() is { } unitLogger)
        {
            logger.TryGet()?.Log("End");
            await unitLogger.FlushAndTerminate();
        }

        ThreadCore.Root.TerminationEvent.Set(); // The termination process is complete (#1).
    }

    private static async Task TestMultilinePrompt(SimpleConsole simpleConsole)
    {
        while (!ThreadCore.Root.IsTerminated)
        {
            var options = simpleConsole.DefaultOptions with
            {// Multiline prompt example
                Prompt = "Description (n or F3:Nested, y or F4:Yes or No)\r\n\n<---\nInput> ",
                // Prompt = "Input> ",
                KeyInputHook = keyInfo => KeyInputHook(keyInfo),
            };

            var result = await simpleConsole.ReadLine(options);

            if (!await ProcessInputResult(simpleConsole, result))
            {
                break;
            }
            else if (string.Equals(result.Text, "n", StringComparison.InvariantCultureIgnoreCase))
            {
                _ = NestedPrompt();
            }
            else if (string.Equals(result.Text, "y", StringComparison.InvariantCultureIgnoreCase))
            {
                _ = YesOrNoPrompt();
            }
        }

        KeyInputHookResult KeyInputHook(ConsoleKeyInfo keyInfo)
        {
            if (keyInfo.Key == ConsoleKey.F1)
            {
                simpleConsole.WriteLine("Inserted text");
                return KeyInputHookResult.Handled;
            }
            else if (keyInfo.Key == ConsoleKey.F2)
            {
                simpleConsole.WriteLine("Text1\nText2");
                return KeyInputHookResult.Handled;
            }
            else if (keyInfo.Key == ConsoleKey.F3)
            {
                _ = NestedPrompt();
                return KeyInputHookResult.Handled;
            }
            else if (keyInfo.Key == ConsoleKey.F4)
            {
                _ = YesOrNoPrompt();
                return KeyInputHookResult.Handled;
            }

            return KeyInputHookResult.NotHandled;
        }

        async Task NestedPrompt()
        {
            var options2 = ReadLineOptions.SingleLine with
            {
                Prompt = "Nested>>> ",
                KeyInputHook = keyInfo => KeyInputHook(keyInfo),
            };

            await Task.Delay(100);
            var result = await simpleConsole.ReadLine(options2);
            Console.WriteLine($"Nested: {result.Text}");
        }

        async Task YesOrNoPrompt()
        {
            var options = ReadLineOptions.MultiLine with
            {
                Prompt = "Yes or No?\r\n[Y/n] ",
                MultilineIdentifier = "|",
                MaxInputLength = 5,
                TextInputHook = text =>
                {
                    var lower = text.ToLowerInvariant();
                    if (lower == "y" || lower == "n" || lower == "yes" || lower == "no")
                    {
                        return text;
                    }

                    return null;
                },
            };

            await Task.Delay(100);
            var result = await simpleConsole.ReadLine(options);
            Console.WriteLine($"Yes or No: {result.Text}");
        }
    }

    private static async Task TestConsoleMode(SimpleConsole simpleConsole)
    {
        Interop.SetConsoleMode(); // Causes "Press any key to close this window..." issue.

        while (!ThreadCore.Root.IsTerminated)
        {
            var options = simpleConsole.DefaultOptions with
            {
                Prompt = "Input>Input>Input>Input>Input>Input>Input>Input>Input>Input>Input>Input>Input>Input>Input>Input>Input>Input>Input>Input>Input>Input>Input> ",
                MultilinePrompt = ">> ",
                MultilineIdentifier = "...",
                InputColor = ConsoleColor.Cyan,
                CancelOnEscape = false,
                AllowEmptyLineInput = true,
                MaxInputLength = 20,
                MaskingCharacter = '$',
                KeyInputHook = keyInfo =>
                {
                    if (keyInfo.Key == ConsoleKey.F1)
                    {
                        simpleConsole.WriteLine("Inserted text");
                        return KeyInputHookResult.Handled;
                    }
                    else if (keyInfo.Key == ConsoleKey.F2)
                    {
                        simpleConsole.WriteLine("Text1\nText2");
                        return KeyInputHookResult.Handled;
                    }
                    else if (keyInfo.Key == ConsoleKey.F3)
                    {
                        return KeyInputHookResult.Handled;
                    }

                    return KeyInputHookResult.NotHandled;
                },
            };

            var result = await simpleConsole.ReadLine(options);

            if (!await ProcessInputResult(simpleConsole, result))
            {
                break;
            }
        }
    }

    private static async Task<bool> ProcessInputResult(SimpleConsole simpleConsole, InputResult result)
    {
        if (result.Kind == InputResultKind.Terminated)
        {
            return false;
        }
        else if (result.Kind == InputResultKind.Canceled)
        {
            simpleConsole.WriteLine("Canceled");
        }
        else if (string.Equals(result.Text, "exit", StringComparison.InvariantCultureIgnoreCase))
        {// exit
            ThreadCore.Root.Terminate(); // Send a termination signal to the root.
            return false;
        }
        else if (string.IsNullOrEmpty(result.Text))
        {
        }
        else if (string.Equals(result.Text, "a", StringComparison.InvariantCultureIgnoreCase))
        {
            _ = Task.Run(async () =>
            {
                await Task.Delay(1000);
                simpleConsole.WriteLine("AAAAA");
            });
        }
        else if (string.Equals(result.Text, "b", StringComparison.InvariantCultureIgnoreCase))
        {
            _ = Task.Run(async () =>
            {
                await Task.Delay(1000);
                Console.WriteLine("ABC123ABC123\r\nABC123ABC123\nABC123ABC123");
            });
        }
        else
        {
            var text = BaseHelper.RemoveCrLf(result.Text);
            simpleConsole.WriteLine($"Command: {text}");
        }

        // continue
        return true;
    }
}
