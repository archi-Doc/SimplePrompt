// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using System;
using System.Threading;
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

        while (!ThreadCore.Root.IsTerminated)
        {
            Console.Write("d");
            Thread.Sleep(100);
            // await Task.Delay(100/*DelayInMilliseconds*/).ConfigureAwait(false);
            Console.Write("e");
        }

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
            InputColor = ConsoleColor.Yellow,
            MultilineIdentifier = "|",
            CancelOnEscape = true,
            // MaskingCharacter = '?',
        };

        // ThreadPool.GetMinThreads(out var worker, out var io);
        // Console.WriteLine($"Worker:{worker} Io:{io}");
        // ThreadPool.SetMinThreads(Math.Max(worker, 8), io)

        Console.WriteLine(Environment.OSVersion.ToString());

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
            else if (string.Equals(result.Text, "exit", StringComparison.InvariantCultureIgnoreCase))
            {// exit
                ThreadCore.Root.Terminate(); // Send a termination signal to the root.
                break;
            }
            else if (string.IsNullOrEmpty(result.Text))
            {// continue
                continue;
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
        }

        await ThreadCore.Root.WaitForTerminationAsync(-1); // Wait for the termination infinitely.
        if (product.Context.ServiceProvider.GetService<UnitLogger>() is { } unitLogger)
        {
            logger.TryGet()?.Log("End");
            await unitLogger.FlushAndTerminate();
        }

        ThreadCore.Root.TerminationEvent.Set(); // The termination process is complete (#1).
    }
}
