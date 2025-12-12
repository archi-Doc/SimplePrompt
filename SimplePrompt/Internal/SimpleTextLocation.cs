// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

namespace SimplePrompt.Internal;

internal readonly record struct SimpleTextLocation
{
    public readonly int LineIndex;

    public readonly int LinePosition;

    public SimpleTextLocation(int lineIndex, int linePosition)
    {
        this.LineIndex = lineIndex;
        this.LinePosition = linePosition;
    }
}
