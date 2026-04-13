// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Text;
using Arc.Threading;

namespace SimplePrompt;

public partial class SimpleConsole
{
    private const int MaxPendingJobs = 32;

    private enum JobKind
    {
        Initialize,
        CursorTop,
        CursorLeft,
        CursorPosition,
        WindowSize,
    }

    private sealed record class Job : ReusableThreadJob
    {
        public JobKind Kind { get; set; }
    }

    private sealed class Worker : ReusableJobWorker<Job>
    {
        private readonly SimpleConsole simpleConsole;

        public Worker(SimpleConsole simpleConsole, ThreadCoreBase? parent)
            : base(parent, default, MaxPendingJobs, true)
        {
            this.simpleConsole = simpleConsole;
        }

        public override void ProcessJob(Job job)
        {
            try
            {
                if (job.Kind == JobKind.Initialize)
                {
                    (this.simpleConsole.CursorLeft, this.simpleConsole.CursorTop) = Console.GetCursorPosition();
                    this.simpleConsole.WindowWidth = Console.WindowWidth;
                    this.simpleConsole.WindowHeight = Console.WindowHeight;
                }
                else if (job.Kind == JobKind.CursorTop)
                {
                    this.simpleConsole.CursorTop = Console.CursorTop;
                }
                else if (job.Kind == JobKind.CursorLeft)
                {
                    this.simpleConsole.CursorLeft = Console.CursorLeft;
                }
                else if (job.Kind == JobKind.CursorPosition)
                {
                    (this.simpleConsole.CursorLeft, this.simpleConsole.CursorTop) = Console.GetCursorPosition();
                }
                else if (job.Kind == JobKind.WindowSize)
                {
                    this.simpleConsole.WindowWidth = Console.WindowWidth;
                    this.simpleConsole.WindowHeight = Console.WindowHeight;
                }
            }
            catch
            {
            }
        }

        public override void OnAfterProcessJob()
        {
        }

        public override void OnTerminated()
        {
        }
    }

    readonly Worker worker;

    public int GetCursorTop()
    {
        RunJob(JobKind.CursorTop);
        return this.CursorTop;
    }

    public int GetCursorLeft()
    {
        RunJob(JobKind.CursorLeft);
        return this.CursorLeft;
    }

    public (int Left, int Top) GetCursorPosition()
    {
        RunJob(JobKind.CursorPosition);
        return (this.CursorLeft, this.CursorTop);
    }

    public (int Width, int Height) GetWindowSize()
    {
        this.RunJob(JobKind.WindowSize);
        return (this.WindowWidth, this.WindowHeight);
    }

    private void RunJob(JobKind jobKind)
    {
        var job = worker.Rent();
        job.Kind = jobKind;
        if (worker.NumberOfPendingJobs < MaxPendingJobs)
        {
            worker.Add(job);
            job.Wait();
        }
        worker.Return(job);

    }
}
