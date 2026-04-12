// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using System.Runtime.CompilerServices;
using Arc.Threading;

namespace SimplePrompt;

public static class AltConsole
{
    private const int MaxPendingJobs = 32;

    private enum JobKind
    {
        Initial,
        CursorTop,
        CursorLeft,
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
            if (job.Kind == JobKind.Initial)
            {
                cursorLeft = Console.CursorLeft;
                cursorTop = Console.CursorTop;
            }
            else if (job.Kind == JobKind.CursorTop)
            {
                cursorTop = Console.CursorTop;
            }
            else if (job.Kind == JobKind.CursorLeft)
            {
                cursorLeft = Console.CursorLeft;
            }
        }
    }

    private static readonly Worker worker;
    private static int cursorLeft;
    private static int cursorTop;

    static AltConsole()
    {
        worker = new(ThreadCore.Root);

        var job = new Job();
        job.Kind = JobKind.Initial;
        worker.Add(job);
        job.Wait();
    }

    public static int CursorTop => cursorTop;

    public static int CursorLeft => cursorLeft;

    public static void UpdateCursorTop()
        => RunJob(JobKind.CursorTop);

    public static void UpdateCursorLeft()
        => RunJob(JobKind.CursorLeft);

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
