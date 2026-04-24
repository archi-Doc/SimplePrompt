// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

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

internal sealed class SimpleConsoleWorker : TaskCore
{
    private static readonly TimeSpan IntervalTimeSpan = TimeSpan.FromMilliseconds(10);

    private readonly SimpleConsole simpleConsole;

    private static async Task Process(object? parameter)
    {
        var worker = (SimpleConsoleWorker)parameter!;
        while (await worker.simpleConsole.Core.Delay(IntervalTimeSpan, default).ConfigureAwait(false))
        {
            worker.simpleConsole.Process();
        }

        worker.simpleConsole.Abort();
    }

    public SimpleConsoleWorker(SimpleConsole simpleConsole, ThreadCoreBase? parent, bool startImmediately = true)
        : base(parent, Process, startImmediately)
    {
        this.simpleConsole = simpleConsole;
    }
}
