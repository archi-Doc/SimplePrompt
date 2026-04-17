// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.
/*
using Arc.Threading;

#pragma warning disable CA1001 // Types that own disposable fields should be disposable

namespace SimplePrompt;

public partial class SimpleConsole
{
    private const int InitialWindowWidth = 120;
    private const int InitialWindowHeight = 30;
    private const int MinimumWindowWidth = 30;
    private const int MinimumWindowHeight = 10;

    private const int MaxPendingJobs = 1024;
    private static readonly TimeSpan pollingInterval = TimeSpan.FromMilliseconds(10);

    private enum JobKind
    {
        Initialize,
        PrepareWindow,
        Write,
        WriteLine,
    }

    private sealed record class Job : ReusableThreadJob
    {
        public JobKind Kind { get; set; }

        public int CursorLeft { get; set; }

        public int CursorTop { get; set; }

        public string? Message { get; set; }

        public ConsoleColor Color { get; set; }

        public override void Reset()
        {
            this.Message = default;
            this.Color = default;
        }
    }

    private sealed class Worker : ReusableJobWorker<Job>
    {
        private readonly SimpleConsole simpleConsole;

        public Worker(SimpleConsole simpleConsole, ThreadCoreBase? parent)
            : base(parent, default, MaxPendingJobs, true)
        {
            this.simpleConsole = simpleConsole;
            this.PollingInterval = pollingInterval;
        }

        public bool TryAddAndWait(Job job)
        {
            if (this.NumberOfPendingJobs < MaxPendingJobs)
            {
                this.Add(job);
                job.Wait();
                return true;
            }
            else
            {
                return false;
            }
        }

        protected override void ProcessJob(Job job)
        {
            try
            {
                if (job.Kind == JobKind.Initialize)
                {
                    (this.simpleConsole._cursorLeft, this.simpleConsole._cursorTop) = Console.GetCursorPosition();
                    this.PrepareWindow();
                }
                else if (job.Kind == JobKind.PrepareWindow)
                {
                    this.PrepareWindow();
                }
                else if (job.Kind == JobKind.Write)
                {
                    this.simpleConsole.Write(job.Message, job.Color);
                }
                else if (job.Kind == JobKind.WriteLine)
                {
                    this.simpleConsole.WriteLine(job.Message, job.Color);
                }
            }
            catch
            {
            }
        }

        protected override void OnAfterProcessJob()
        {
            this.simpleConsole.ProcessReadLine();
        }

        protected override void OnTerminated()
        {
        }

        internal void PrepareWindow()
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

    private void RunJob(JobKind jobKind)
    {
        var job = worker.Rent();
        job.Kind = jobKind;
        worker.TryAddAndWait(job);
        worker.Return(job);
    }
}*/
