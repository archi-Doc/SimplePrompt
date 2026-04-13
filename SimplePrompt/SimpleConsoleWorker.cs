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
                    (this.simpleConsole._cursorLeft, this.simpleConsole._cursorTop) = Console.GetCursorPosition();
                    this.WindowSize();
                }
                else if (job.Kind == JobKind.CursorTop)
                {
                    this.simpleConsole._cursorTop = Console.CursorTop;
                }
                else if (job.Kind == JobKind.CursorLeft)
                {
                    this.simpleConsole._cursorLeft = Console.CursorLeft;
                }
                else if (job.Kind == JobKind.CursorPosition)
                {
                    (this.simpleConsole._cursorLeft, this.simpleConsole._cursorTop) = Console.GetCursorPosition();
                }
                else if (job.Kind == JobKind.WindowSize)
                {
                    this.WindowSize();
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

        private void WindowSize()
        {
            var windowWidth = InitialWindowWidth;
            var windowHeight = InitialWindowHeight;

            try
            {
                windowWidth = Console.WindowWidth;
                windowHeight = Console.WindowHeight;
            }
            catch
            {
            }

            if (windowWidth < MinimumWindowWidth)
            {
                windowWidth = MinimumWindowWidth;
            }

            if (windowHeight < MinimumWindowHeight)
            {
                windowHeight = MinimumWindowHeight;
            }

            this.simpleConsole._windowWidth = windowWidth;
            this.simpleConsole._windowHeight = windowHeight;
        }
    }

    readonly Worker worker;

    /*public int GetCursorTop()
    {
        RunJob(JobKind.CursorTop);
        return this._cursorTop;
    }

    public int GetCursorLeft()
    {
        RunJob(JobKind.CursorLeft);
        return this._cursorLeft;
    }

    public (int Left, int Top) GetCursorPosition()
    {
        RunJob(JobKind.CursorPosition);
        return (this._cursorLeft, this._cursorTop);
    }

    public (int Width, int Height) GetWindowSize()
    {
        this.RunJob(JobKind.WindowSize);
        return (this._windowWidth, this._windowHeight);
    }*/

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
