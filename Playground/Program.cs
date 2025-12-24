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
        try
        {
#pragma warning disable CA1416 // Validate platform compatibility
            _ = PosixSignalRegistration.Create(PosixSignal.SIGWINCH, _ =>
            {
                Console.WriteLine($"SIGWINCH Height:{Console.WindowHeight} Width:{Console.WindowWidth} Top:{Console.CursorTop}");
            });
#pragma warning restore CA1416 // Validate platform compatibility
        }
        catch
        {
        }

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
            CancelOnEscape = true,
            // MaskingCharacter = '?',
            KeyInputHook = keyInfo => KeyInputHook(keyInfo),
        };

        _ = Task.Run(async () =>
        {
            while (!ThreadCore.Root.IsTerminated)
            {
                await ThreadCore.Root.Delay(5000);

                if (ThreadCore.Root.IsTerminated)
                {
                    break;
                }
                else
                {
                    Console.WriteLine("12345 - ABCDEF - あいうえお");
                }
            }
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

            return KeyInputHookResult.NotHandled;
        }
    }
}
