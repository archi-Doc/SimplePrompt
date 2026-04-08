// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using System;
using System.Collections.Generic;
using System.Text;
using Arc.Collections;
using Arc.Threading;

namespace SimplePrompt;

public static class AltConsole
{
    private const int QueueCapacity = 32;

    private readonly record struct Work
    {
        ManualResetEventSlim manualResetEventSlim = new();

    }

    private static CircularQueue<Work> queue = new(QueueCapacity);
    private static int cursorLeft;
    private static int cursorTop;

    static AltConsole()
    {
        var worker = new TaskWorkerSlim<TestTaskWorkSlim>(ThreadCore.Root, async (worker, work) =>
        {
            // if (!await worker.Delay(1000))
            if (!worker.Sleep(1000))
            {
                return AbortOrComplete.Abort;
            }

            Console.WriteLine($"Complete: {work.Id}, {work.Name}");
            return AbortOrComplete.Complete;
        });
    }

    public static int CursorTop
    {
        get
        {
            return cursorTop;
        }
    }
}
