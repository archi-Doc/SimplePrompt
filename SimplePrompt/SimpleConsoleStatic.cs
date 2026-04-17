// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

namespace SimplePrompt;

public partial class SimpleConsole
{
    public static int CursorLeft => SimpleConsole.GetOrCreate()._cursorLeft;

    public static int CursorTop => SimpleConsole.GetOrCreate()._cursorTop;

    public static int WindowWidth => SimpleConsole.GetOrCreate()._windowWidth;

    public static int WindowHeight => SimpleConsole.GetOrCreate()._windowHeight;

    public static (int Left, int Top) GetCursorPosition()
    {
        var simpleConsole = SimpleConsole.GetOrCreate();
        return (simpleConsole._cursorLeft, simpleConsole._cursorTop);
    }

    /*public static (int Left, int Top) GetCursorPosition()
    {
        int left, top;

        var simpleConsole = SimpleConsole.GetOrCreate();
        var worker = simpleConsole.worker;
        var job = worker.Rent();
        job.Kind = JobKind.GetCursorPosition;
        if (worker.TryAddAndWait(job))
        {
            left = job.CursorLeft;
            top = job.CursorTop;
        }
        else
        {
            left = simpleConsole._cursorLeft;
            top = simpleConsole._cursorTop;
        }

        worker.Return(job);

        return (left, top);
    }*/
}
