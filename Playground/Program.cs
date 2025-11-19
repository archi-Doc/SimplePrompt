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

        Console.WriteLine(Environment.OSVersion.ToString());

        var simpleConsole = new SimpleConsole();
        simpleConsole.Logger = product.Context.ServiceProvider.GetRequiredService<ILogger<SimpleConsole>>();

        while (!ThreadCore.Root.IsTerminated)
        {
            var result = await simpleConsole.ReadLine($"{Console.CursorTop}> ", "# "); // Success, Canceled, Terminated

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
