// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Arc.Threading;

#pragma warning disable CA1001 // Types that own disposable fields should be disposable

namespace SimplePrompt;

internal sealed class SimpleConsoleWorker : TaskCore
{
    private static readonly TimeSpan IntervalTimeSpan = TimeSpan.FromMilliseconds(10);

    private readonly SimpleConsole simpleConsole;

    private static async Task Process(object? parameter)
    {
        var worker = (SimpleConsoleWorker)parameter!;
        while (await worker.Delay(IntervalTimeSpan).ConfigureAwait(false))
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
