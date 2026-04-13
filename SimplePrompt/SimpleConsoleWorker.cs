// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Arc.Threading;

namespace SimplePrompt;

public partial class SimpleConsole
{
    private const int MaxPendingJobs = 1024;

    private enum JobKind
    {
        Initialize,
        GetCursorPosition,
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
                else if (job.Kind == JobKind.GetCursorPosition)
                {
                    var position = Console.GetCursorPosition();
                    job.CursorLeft = position.Left;
                    job.CursorTop = position.Top;
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
            this.simpleConsole.WriteLine("After");
        }

        protected override void OnTerminated()
        {
        }

        private void PrepareWindow()
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
