// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

namespace SimplePrompt;

/// <summary>
/// Polls the console at a fixed interval and drives <see cref="SimpleConsole.Process"/>.
/// </summary>
internal sealed class SimpleConsoleWorker
{
    private static readonly TimeSpan IntervalTimeSpan = TimeSpan.FromMilliseconds(10);
    private readonly SimpleConsole simpleConsole;
    private volatile bool isTerminated;

    public bool IsTerminated => this.isTerminated;

    public SimpleConsoleWorker(SimpleConsole simpleConsole)
    {
        this.simpleConsole = simpleConsole;
    }

    /// <summary>
    /// Starts polling. Call once after initialization: on Unix the worker reads stdin directly,
    /// which would race with the terminal's reply to the initial cursor position query.
    /// </summary>
    public void Start()
    {
        _ = Task.Run(async () =>
        {
            var simpleConsole = this.simpleConsole;

            // A single PeriodicTimer avoids allocating a delay task on every iteration.
            using var timer = new PeriodicTimer(IntervalTimeSpan);
            while (true)
            {
                var group = simpleConsole.ExecutionGroup;
                if (!await timer.WaitForNextTickAsync().ConfigureAwait(false) ||
                    group?.IsTerminated == true || simpleConsole.ExecutionGroup?.IsTerminated == true)
                {
                    break;
                }

                try
                {
                    simpleConsole.Process();
                }
                catch
                {// Never let a transient failure terminate the loop; that would hang every pending ReadLineAsync().
                }
            }

            this.isTerminated = true;
            simpleConsole.Abort();
        });
    }
}
