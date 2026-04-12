// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using System.Runtime.CompilerServices;
using Arc.Threading;

namespace SimplePrompt;

public static class AltConsole
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
        public Worker(ThreadCoreBase? parent)
            : base(parent, default, MaxPendingJobs, true)
        {
        }

        public override void ProcessJob(Job job)
        {
            try
            {
                if (job.Kind == JobKind.Initialize)
                {
                    (cursorLeft, cursorTop) = Console.GetCursorPosition();
                    windowWidth = Console.WindowWidth;
                    windowHeight = Console.WindowHeight;
                }
                else if (job.Kind == JobKind.CursorTop)
                {
                    cursorTop = Console.CursorTop;
                }
                else if (job.Kind == JobKind.CursorLeft)
                {
                    cursorLeft = Console.CursorLeft;
                }
                else if (job.Kind == JobKind.CursorPosition)
                {
                    (cursorLeft, cursorTop) = Console.GetCursorPosition();
                }
                else if (job.Kind == JobKind.WindowSize)
                {
                    windowWidth = Console.WindowWidth;
                    windowHeight = Console.WindowHeight;
                }
            }
            catch
            {
            }
        }
    }

    private static readonly Worker worker;
    private static bool initialized;
    private static int cursorLeft;
    private static int cursorTop;
    private static int windowWidth;
    private static int windowHeight;

    static AltConsole()
    {
        worker = new(ThreadCore.Root);

        cursorLeft = -1;
        cursorTop = -1;
    }

    private static void Initialize()
    {
        if (!initialized)
        {
            initialized = true;

            var job = new Job();
            job.Kind = JobKind.Initialize;
            worker.Add(job);
            job.Wait();
        }
    }

    public static int CursorTop
    {
        get
        {
            Initialize();
            return cursorTop;
        }
    }

    public static int CursorLeft
    {
        get
        {
            Initialize();
            return cursorLeft;
        }
    }

    public static int GetCursorTop()
    {
        RunJob(JobKind.CursorTop);
        return cursorTop;
    }

    public static int GetCursorLeft()
    {
        RunJob(JobKind.CursorLeft);
        return cursorLeft;
    }

    public static (int Left, int Top) GetCursorPosition()
    {
        RunJob(JobKind.CursorPosition);
        return (cursorLeft, cursorTop);
    }

    public static (int Width, int Height) GetWindowSize()
    {
        RunJob(JobKind.WindowSize);
        return (windowWidth, windowHeight);
    }

    private static void RunJob(JobKind jobKind)
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

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void TryAddAndWait(Job job)
    {
        if (worker.NumberOfPendingJobs < MaxPendingJobs)
        {
            worker.Add(job);
            job.Wait();
        }
    }
}
