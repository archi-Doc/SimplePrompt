// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

namespace SimplePrompt;

// Shortcuts to the cursor and window state of SimpleConsole.Instance.
// These values are tracked by SimpleConsole and are returned without querying the terminal.
public partial class SimpleConsole
{
    /// <summary>
    /// Gets the tracked zero-based cursor column.
    /// </summary>
    public static int CursorLeft => SimpleConsole.Instance._cursorLeft;

    /// <summary>
    /// Gets the tracked zero-based cursor row within the window.
    /// </summary>
    public static int CursorTop => SimpleConsole.Instance._cursorTop;

    /// <summary>
    /// Gets the periodically refreshed window width in columns, with a minimum of 30.
    /// </summary>
    public static int WindowWidth => SimpleConsole.Instance._windowWidth;

    /// <summary>
    /// Gets the periodically refreshed window height in rows, with a minimum of 10.
    /// </summary>
    public static int WindowHeight => SimpleConsole.Instance._windowHeight;

    /// <summary>
    /// Gets the tracked cursor position without querying the terminal.
    /// </summary>
    /// <returns>The zero-based column and row of the cursor.</returns>
    public static (int Left, int Top) GetCursorPosition()
        => (SimpleConsole.Instance._cursorLeft, SimpleConsole.Instance._cursorTop);
}
