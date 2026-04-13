// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

namespace SimplePrompt;

public partial class SimpleConsole
{
    public static int CursorLeft => SimpleConsole.GetOrCreate()._cursorLeft;

    public static int CursorTop => SimpleConsole.GetOrCreate()._cursorTop;

    public static int WindowWidth => SimpleConsole.GetOrCreate()._windowWidth;

    public static int WindowHeight => SimpleConsole.GetOrCreate()._windowHeight;
}
