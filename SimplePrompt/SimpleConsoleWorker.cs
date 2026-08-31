// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

namespace SimplePrompt;

/// <summary>
/// Polls the console at a fixed interval and drives <see cref="SimpleConsole.Process"/>.
/// </summary>
internal sealed class SimpleConsoleWorker
{
    private static readonly TimeSpan IntervalTimeSpan = TimeSpan.FromMilliseconds(10);

    public SimpleConsoleWorker(SimpleConsole simpleConsole)
    {
        _ = Task.Run(async () =>
        {
            // A single PeriodicTimer avoids allocating a delay task on every iteration.
            using var timer = new PeriodicTimer(IntervalTimeSpan);
            while (true)
            {
                if (simpleConsole.ExecutionGroup is { } group)
                {
                    if (await group.Delay(IntervalTimeSpan).ConfigureAwait(false) != true)
                    {
                        break;
                    }
                }
                else if (!await timer.WaitForNextTickAsync().ConfigureAwait(false))
                {
                    break;
                }

                try
                {
                    simpleConsole.Process();
                }
                catch
                {// Never let a transient failure terminate the loop; that would hang every pending ReadLine().
                }
            }

            simpleConsole.Abort();
        });
    }
}
