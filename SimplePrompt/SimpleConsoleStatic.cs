// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

namespace SimplePrompt;

// Shortcuts to the cursor and window state of SimpleConsole.Instance.
// These values are tracked by SimpleConsole and are returned without querying the terminal.
public partial class SimpleConsole
{
    /// <summary>
    /// Gets the column position of the cursor (zero-based).
    /// </summary>
    public static int CursorLeft => SimpleConsole.Instance._cursorLeft;

    /// <summary>
    /// Gets the row position of the cursor within the window (zero-based).
    /// </summary>
    public static int CursorTop => SimpleConsole.Instance._cursorTop;

    /// <summary>
    /// Gets the width of the console window, in columns.<br/>
    /// It is refreshed periodically, and falls back to a default value when the window size cannot be obtained.
    /// </summary>
    public static int WindowWidth => SimpleConsole.Instance._windowWidth;

    /// <summary>
    /// Gets the height of the console window, in rows.<br/>
    /// It is refreshed periodically, and falls back to a default value when the window size cannot be obtained.
    /// </summary>
    public static int WindowHeight => SimpleConsole.Instance._windowHeight;

    /// <summary>
    /// Gets the position of the cursor.
    /// </summary>
    /// <returns>The zero-based column and row of the cursor.</returns>
    public static (int Left, int Top) GetCursorPosition()
        => (SimpleConsole.Instance._cursorLeft, SimpleConsole.Instance._cursorTop);
}
