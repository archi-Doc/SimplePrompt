// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Arc;
using Arc.Threading;

#pragma warning disable CA1001 // Types that own disposable fields should be disposable

namespace SimplePrompt;

/*internal sealed class SimpleConsoleWorker : ThreadCore
{
    private readonly SimpleConsole simpleConsole;

    private static void Process(object? parameter)
    {
        var worker = (SimpleConsoleWorker)parameter!;
        while (!worker.simpleConsole.Core.IsTerminated)
        {
            worker.simpleConsole.Process();

            Thread.Sleep(10);
        }

        worker.simpleConsole.Abort();
    }

    public SimpleConsoleWorker(SimpleConsole simpleConsole, ThreadCoreBase? parent, bool startImmediately = true)
        : base(parent, Process, startImmediately)
    {
        this.simpleConsole = simpleConsole;
        // this.Thread.IsBackground = true;
    }
}*/

/*internal sealed class SimpleConsoleWorker : TaskCore<SimpleConsoleWorker>
{
    private static readonly TimeSpan IntervalTimeSpan = TimeSpan.FromMilliseconds(10);

    private readonly SimpleConsole simpleConsole;

    private static async Task Process(SimpleConsoleWorker worker)
    {
        while (await worker.Delay(IntervalTimeSpan).ConfigureAwait(false))
        {
            worker.simpleConsole.Process();
        }

        worker.simpleConsole.Abort();
    }

    public SimpleConsoleWorker(ExecutionRoot root, SimpleConsole simpleConsole)
        : base(root.BaseGroup, Process)
    {
        this.simpleConsole = simpleConsole;
    }
}*/

internal sealed class SimpleConsoleWorker
{
    private static readonly TimeSpan IntervalTimeSpan = TimeSpan.FromMilliseconds(10);

    public SimpleConsoleWorker(SimpleConsole simpleConsole)
    {
        _ = Task.Run(async () =>
        {
            while (true)
            {
                if (simpleConsole.ExecutionGroup is { } group)
                {
                    if (await group.Delay(IntervalTimeSpan).ConfigureAwait(false) != true)
                    {
                        break;
                    }
                }
                else
                {
                    try
                    {
                        await Task.Delay(IntervalTimeSpan).ConfigureAwait(false);
                    }
                    catch
                    {
                        break;
                    }
                }

                simpleConsole.Process();
            }

            simpleConsole.Abort();
        });
    }
}
