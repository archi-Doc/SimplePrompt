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

        /*var st = Console.OpenStandardInput();
        var buffer = new byte[100];
        while (true)
        {
            // var n = st.Read(buffer.AsSpan(0, 1));
            var r = Interop.Sys.ReadConsoleInput(InputHandle, out var ir, 1, out int numEventsRead);
            Console.WriteLine(r);
        }*/

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
        // Console.Write("Enter to start:");
        // Console.ReadLine();

        var inputConsole = new InputConsole();
        inputConsole.Logger = product.Context.ServiceProvider.GetRequiredService<ILogger<InputConsole>>();

        // var sp = ConsoleHelper.GetForegroundColorEscapeCode(ConsoleColor.Red);
        // Console.Write($">> {sp}");
        // var st = Console.ReadLine();

        // Test();

        while (!ThreadCore.Root.IsTerminated)
        {
            var result = await inputConsole.ReadLine($"{Console.CursorTop}> ", "# "); // Success, Canceled, Terminated

            if (result.Kind == InputResultKind.Terminated)
            {
                break;
            }
            else if (result.Kind == InputResultKind.Canceled)
            {
                inputConsole.WriteLine("Canceled");
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
                    inputConsole.WriteLine("AAAAA");
                });
            }
            else
            {
                var text = BaseHelper.RemoveCrLf(result.Text);
                inputConsole.WriteLine($"Command: {text}");
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
