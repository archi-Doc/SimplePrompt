// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

namespace SimplePrompt.Internal;

internal readonly record struct ReadLineLocation
{
    public readonly short BufferIndex;

    public readonly short RowIndex;

    public readonly int ArrayIndex;

    public ReadLineLocation(short bufferIndex, short rowIndex, int arrayIndex)
    {
        this.BufferIndex = bufferIndex;
        this.RowIndex = rowIndex;
        this.ArrayIndex = arrayIndex;
    }
}
