// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

namespace SimplePrompt.Internal;

internal class ReadLineRow
{
    public ReadLineBuffer ReadLineBuffer { get; }

    public short RowIndex { get; }

    public int StartIndex { get; set; }

    public short Length { get; set; }

    public short Width { get; set; }

    public ReadLineRow(ReadLineBuffer readLineBuffer, short rowIndex)
    {
        this.ReadLineBuffer = readLineBuffer;
        this.RowIndex = rowIndex;
    }
}
