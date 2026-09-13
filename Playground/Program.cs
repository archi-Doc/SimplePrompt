// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using System.Runtime.CompilerServices;
using Arc;
using Arc.Threading;
using Arc.Unit;
using Microsoft.Extensions.DependencyInjection;
using SimplePrompt;

namespace Playground;

public enum YesOrNo
{
    Invalid,
    Yes,
    No,
}

internal sealed class Program
{
    private static ExecutionRoot? root;

    private static void WriteLineRaw(string? message = null)
        => Console.WriteLine(message);

    private static async Task<YesOrNo> RequestYesOrNoInternal(string message)
    {
        var description = message;
        if (!string.IsNullOrEmpty(description))
        {
            WriteLineRaw(description + " [Y/n]");
        }

        while (true)
        {
            var input = Console.ReadLine();
            if (input == null)
            {// Ctrl+C
                WriteLineRaw();
                return YesOrNo.Invalid; // throw new PanicException();
            }

            input = input.ToLower(System.Globalization.CultureInfo.InvariantCulture);
            if (input == "y" || input == "yes")
            {
                return YesOrNo.Yes;
            }
            else if (input == "n" || input == "no")
            {
                return YesOrNo.No;
            }
            else
            {
                WriteLineRaw("Yes or No [Y/n]");
            }
        }
    }

    public static async Task Main(string[] args)
    {
        AppCloseHandler.Register(() =>
        {// Closing the console window or terminating the process.
            root?.RequestTermination(); // Send a termination signal to the root.
            root?.WaitForTerminationAsync(TimeSpan.FromSeconds(2)).Wait();
        });

        Console.CancelKeyPress += (s, e) =>
        {// Ctrl+C pressed.
            e.Cancel = true;
            root?.RequestTermination(); // Send a termination signal to the root.
        };

        var builder = new UnitBuilder()
            .Configure(context =>
            {
                context.AddLogOutputResolver(x =>
                {
                    x.SetOutput<FileLogOutput<FileLogOutputOptions>>();
                    return;
                });
            })
            .PostConfigure(context =>
            {
                var logfile = "Logs/Log.txt";
                context.SetOptions(context.GetOrCreateOptions<FileLogOutputOptions>() with
                {
                    FilePath = Path.Combine(context.ProgramDirectory, logfile),
                    MaxLogCapacityInMegabytes = 1,
                });
            });

        var unit = builder.Build();
        root = unit.Context.ExecutionRoot;
        var logger = unit.Context.ServiceProvider.GetRequiredService<ILogger<DefaultLogSource>>();
        logger.GetWriter()?.Write("Start");
        Console.OutputEncoding = System.Text.Encoding.UTF8;

        var simpleConsole = SimpleConsole.Instance;
        simpleConsole.ExecutionGroup = root;
        simpleConsole.DefaultReadLineOptions = new ReadLineOptions()
        {
            // MaxInputLength = 4,
            Prompt = "> ",
            InputColor = ConsoleColor.Yellow,
            MultilineDelimiter = "|",
            AllowEmptyInput = true,
            CancelOnEscape = true,
            // MaskingCharacter = '?',
            KeyInputHook = KeyInputHookMethod,
        };

        var ctsStack = new Stack<CancellationTokenSource>();
        simpleConsole.KeyInputHook = (ref keyInfo) =>
        {
            if (keyInfo.Key == ConsoleKey.Q && keyInfo.Modifiers == ConsoleModifiers.Control)
            {// Ctrl+Q
                lock (ctsStack)
                {
                    if (ctsStack.TryPeek(out var cts))
                    {
                        cts.Cancel();
                        return KeyInputHookResult.Handled;
                    }
                }
            }

            return KeyInputHookResult.NotHandled;
        };

        Console.WriteLine("\u001b[90m[\u001b[39m\u001b[22m\u001b[40m\u001b[1m\u001b[37mINF\u001b[39m\u001b[22m\u001b[49m ITestInterface\u001b[90m] \u001b[39m\u001b[22m\u001b[1m\u001b[37mtttttttttttttttttttttttttttttttttttttttttttttttttttttt\u001b[39m\u001b[22m");

        Console.WriteLine(true);
        Console.WriteLine(1.23d);
        var top = SimpleConsole.CursorTop;
        var position = SimpleConsole.GetCursorPosition();

        while (!root.IsTerminated)
        {
            var options = simpleConsole.DefaultReadLineOptions with
            {
                // CancellationTokenSource = new(),
                KeyInputHook = (ref keyInfo) =>
                {
                    if (keyInfo.Key == ConsoleKey.C && keyInfo.Modifiers.HasFlag(ConsoleModifiers.Control))
                    {
                        root.RequestTermination(); // Send a termination signal to the root.
                        return KeyInputHookResult.Handled;
                    }

                    return KeyInputHookResult.NotHandled;
                }
            };

            using var currentCts = new CancellationTokenSource();
            lock (ctsStack)
            {
                ctsStack.Push(currentCts);
            }

            try
            {
                var secondary = simpleConsole.DefaultReadLineOptions with
                {
                    Prompt = "Secondary> ",
                };

                // _ = simpleConsole.ReadLineAsync(secondary);
                var result = await simpleConsole.ReadLineAsync(null, currentCts.Token);

                if (result.Kind == InputResultKind.Terminated)
                {
                    break;
                }
                else if (result.Kind == InputResultKind.Canceled)
                {
                    simpleConsole.WriteLine("Canceled", ConsoleColor.Red);
                    continue;
                }
                else if (string.Equals(result.Text, "exit", StringComparison.OrdinalIgnoreCase))
                {// exit
                    root.RequestTermination(); // Send a termination signal to the root.
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
                        simpleConsole.WriteLine("AAAAA", ConsoleColor.Green);
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
                else if (string.Equals(result.Text, "c", StringComparison.OrdinalIgnoreCase))
                {
                    try
                    {
                        simpleConsole.WriteLine("Freeze ->");
                        for (var i = 0; i < 30; i++)
                        {
                            Thread.Sleep(100);
                            if (currentCts.IsCancellationRequested)
                            {
                                simpleConsole.WriteLine("Canceled");
                                break;
                            }
                        }

                        // await Task.Delay(3_000, ctsStack.Peek().Token);
                        simpleConsole.WriteLine("<-");
                        simpleConsole.EnqueueLine("a");
                    }
                    catch
                    {
                        simpleConsole.WriteLine("Canceled2", ConsoleColor.Red);
                    }
                }
                else if (string.Equals(result.Text, "d", StringComparison.OrdinalIgnoreCase))
                {
                    var options2 = ReadLineOptions.SingleLine with
                    {
                        Prompt = "Nested>> ",
                    };

                    _ = Task.Run(async () =>
                    {
                        await Task.Delay(100); // Wait briefly to allow ReadLineAsync() to be nested.
                        var result = await simpleConsole.ReadLineAsync(options2);
                        Console.WriteLine($"Nested: {result.Text}");
                    });
                }
                else
                {
                    var text = BaseHelper.RemoveCrAndLfChars(result.Text);
                    simpleConsole.WriteLine($"Command: {text}");
                }

            }
            finally
            {
                lock (ctsStack)
                {
                    ctsStack.Pop();
                }
            }
        }

        await root.WaitForTerminationAsync(); // Wait for the termination infinitely.
        if (unit.Context.ServiceProvider.GetService<LogUnit>() is { } logUnit)
        {
            logger.GetWriter()?.Write("End");
            await logUnit.FlushAndTerminateAsync();
        }

        KeyInputHookResult KeyInputHookMethod(ref ConsoleKeyInfo keyInfo)
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
                    KeyInputHook = KeyInputHookMethod,
                };

                _ = Task.Run(async () =>
                {
                    await Task.Delay(100);
                    var result = await simpleConsole.ReadLineAsync(options2);
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
            else if (keyInfo.Key == ConsoleKey.F6)
            {
                Console.WriteLine(2.34d);
                Console.WriteLine();
                Console.WriteLine(false);
                return KeyInputHookResult.Handled;
            }
            else if (keyInfo.Key == ConsoleKey.F7)
            {
                _ = Task.Run(async () =>
                {
                    await Task.Delay(100);

                    simpleConsole.Write(true);
                    simpleConsole.WriteLine('y');
                    simpleConsole.Write(decimal.MaxValue);
                    simpleConsole.WriteLine(1.23d);
                    simpleConsole.Write(-123.456f);
                    simpleConsole.WriteLine(123);
                    simpleConsole.Write(123u);
                    simpleConsole.WriteLine(100L);
                    simpleConsole.Write(456ul);
                });

                return KeyInputHookResult.Handled;
            }
            else if (keyInfo.Key == ConsoleKey.F12)
            {
                if (Console.CursorTop >= 10)
                {
                    Console.CursorTop = 0;
                }
                else
                {
                    Console.CursorTop = 10;
                }

                return KeyInputHookResult.Handled;
            }

            return KeyInputHookResult.NotHandled;
        }

        async Task YesOrNoPrompt()
        {
            var options = ReadLineOptions.Multiline with
            {
                Prompt = "Yes or No?\r\n[Y/n] ",
                MultilineDelimiter = "|",
                MaxInputLength = 3,
                MaskingCharacter = '*',
                SubmitHook = text =>
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
            var result = await simpleConsole.ReadLineAsync(options);
            Console.WriteLine($"Yes or No: {result.Text}");
        }
    }
}
