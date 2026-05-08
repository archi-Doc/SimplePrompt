// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

namespace SimplePrompt;

public partial class SimpleConsole
{
    public static int CursorLeft => SimpleConsole.Instance._cursorLeft;

    public static int CursorTop => SimpleConsole.Instance._cursorTop;

    public static int WindowWidth => SimpleConsole.Instance._windowWidth;

    public static int WindowHeight => SimpleConsole.Instance._windowHeight;

    public static (int Left, int Top) GetCursorPosition()
    {
        return (SimpleConsole.Instance._cursorLeft, SimpleConsole.Instance._cursorTop);
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
