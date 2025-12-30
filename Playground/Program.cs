// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using System.Runtime.InteropServices;
using Arc;
using Arc.Threading;
using Arc.Unit;
using Microsoft.Extensions.DependencyInjection;
using SimplePrompt;

namespace Playground;

internal sealed class Program
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
        simpleConsole.DefaultOptions = new ReadLineOptions()
        {
            // MaxInputLength = 4,
            Prompt = "Prompt\n>>> ",
            InputColor = ConsoleColor.Yellow,
            MultilineDelimiter = "|",
            AllowEmptyLineInput = true,
            CancelOnEscape = true,
            // MaskingCharacter = '?',
            KeyInputHook = keyInfo => KeyInputHook(keyInfo),
        };

        Console.WriteLine("\u001b[90m[\u001b[39m\u001b[22m\u001b[40m\u001b[1m\u001b[37mINF\u001b[39m\u001b[22m\u001b[49m ITestInterface\u001b[90m] \u001b[39m\u001b[22m\u001b[1m\u001b[37mtttttttttttttttttttttttttttttttttttttttttttttttttttttt\u001b[39m\u001b[22m");

        _ = Task.Run(async () =>
        {
            await Task.Delay(1000);
            simpleConsole.EnqueueInput("Queued");
            await Task.Delay(1000);
            simpleConsole.EnqueueInput(null);
        });
        

        while (!ThreadCore.Root.IsTerminated)
        {
            var options = simpleConsole.DefaultOptions with
            {
            };

            var result = await simpleConsole.ReadLine(options);

            if (result.Kind == InputResultKind.Terminated)
            {
                break;
            }
            else if (result.Kind == InputResultKind.Canceled)
            {
                simpleConsole.WriteLine("Canceled");
                continue;
            }
            else if (string.Equals(result.Text, "exit", StringComparison.OrdinalIgnoreCase))
            {// exit
                ThreadCore.Root.Terminate(); // Send a termination signal to the root.
                break;
            }
            else if (string.Equals(result.Text, "clear", StringComparison.OrdinalIgnoreCase))
            {// clear
                simpleConsole.Clear(false);
                continue;
            }
            else if (string.IsNullOrEmpty(result.Text))
            {// continue
                continue;
            }
            else if (string.Equals(result.Text, "a", StringComparison.OrdinalIgnoreCase))
            {
                _ = Task.Run(async () =>
                {
                    await Task.Delay(1000);
                    simpleConsole.WriteLine("AAAAA");
                });
            }
            else if (string.Equals(result.Text, "b", StringComparison.OrdinalIgnoreCase))
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
        }

        await ThreadCore.Root.WaitForTerminationAsync(-1); // Wait for the termination infinitely.
        if (product.Context.ServiceProvider.GetService<UnitLogger>() is { } unitLogger)
        {
            logger.TryGet()?.Log("End");
            await unitLogger.FlushAndTerminate();
        }

        ThreadCore.Root.TerminationEvent.Set(); // The termination process is complete (#1).

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
                var options2 = ReadLineOptions.SingleLine with
                {
                    Prompt = "Nested>>> ",
                    KeyInputHook = keyInfo => KeyInputHook(keyInfo),
                };

                _ = Task.Run(async () =>
                {
                    await Task.Delay(100);
                    var result = await simpleConsole.ReadLine(options2);
                    Console.WriteLine($"Nested: {result.Text}");
                });

                return KeyInputHookResult.Handled;
            }
            else if (keyInfo.Key == ConsoleKey.F4)
            {
                simpleConsole.Clear(false);
                return KeyInputHookResult.Handled;
            }
            else if (keyInfo.Key == ConsoleKey.F5)
            {
                _ = YesOrNoPrompt();
                return KeyInputHookResult.Handled;
            }

            return KeyInputHookResult.NotHandled;
        }

        async Task YesOrNoPrompt()
        {
            var options = ReadLineOptions.MultiLine with
            {
                Prompt = "Yes or No?\r\n[Y/n] ",
                MultilineDelimiter = "|",
                MaxInputLength = 3,
                MaskingCharacter = '*',
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
}
