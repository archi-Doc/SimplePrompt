// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Text;
using Arc.Collections;
using Arc.Threading;

namespace SimplePrompt;

public static class AltConsole
{
    private const int MaxPendingJobs = 32;

    private sealed class Worker : ReusableJobWorker<AltConsoleJob>
    {
        public Worker(ThreadCoreBase? parent)
            : base(parent, default, MaxPendingJobs, true)
        {
        }

        public override void ProcessJob(AltConsoleJob job)
        {
            if (job.Kind == AltConsoleJobKind.Initial)
            {
                cursorLeft = Console.CursorLeft;
                cursorTop = Console.CursorTop;
            }
            else if (job.Kind == AltConsoleJobKind.CursorTop)
            {
                cursorTop = Console.CursorTop;
            }
            else if (job.Kind == AltConsoleJobKind.CursorLeft)
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
    }

    public static int CursorTop
    {
        get
        {
            var job = worker.Rent();
            job.Kind = AltConsoleJobKind.CursorTop;
            TryAddAndWait(job);
            worker.Return(job);
            return cursorTop;
        }
    }

    public static int CursorLeft
    {
        get
        {
            var job = worker.Rent();
            job.Kind = AltConsoleJobKind.CursorLeft;
            TryAddAndWait(job);
            worker.Return(job);
            return cursorLeft;
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void TryAddAndWait(AltConsoleJob job)
    {
        if (worker.NumberOfPendingJobs < MaxPendingJobs)
        {
            worker.Add(job);
            job.Wait();
        }
    }
}
