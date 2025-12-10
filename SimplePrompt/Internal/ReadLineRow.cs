// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

namespace SimplePrompt.Internal;

internal class ReadLineRow
{
    public ReadLineBuffer ReadLineBuffer { get; }

    public short RowIndex { get; }

    public int StartIndex { get; set; }

    public short ImmutableLength { get; set; }

    public short ImmutableWidth { get; set; }

    public short MutableLength { get; set; }

    public short MutableWidth { get; set; }

    public short TotalLength => (short)(this.ImmutableLength + this.MutableLength);

    public short TotalWidth => (short)(this.ImmutableWidth + this.MutableWidth);

    public ReadOnlySpan<char> ImmutableCharSpan => this.ReadLineBuffer.CharArray.AsSpan(this.StartIndex, this.ImmutableLength);

    public ReadOnlySpan<byte> ImmutableWidthSpan => this.ReadLineBuffer.WidthArray.AsSpan(this.StartIndex, this.ImmutableLength);

    public ReadOnlySpan<char> MutableCharSpan => this.ReadLineBuffer.CharArray.AsSpan(this.StartIndex + this.ImmutableLength, this.MutableLength);

    public ReadOnlySpan<byte> MutableWidthSpan => this.ReadLineBuffer.WidthArray.AsSpan(this.StartIndex + this.ImmutableLength, this.MutableLength);

    public ReadOnlySpan<char> CharSpan => this.ReadLineBuffer.CharArray.AsSpan(this.StartIndex, this.ImmutableLength + this.MutableLength);

    public ReadOnlySpan<byte> WidthSpan => this.ReadLineBuffer.WidthArray.AsSpan(this.StartIndex, this.ImmutableLength + this.MutableLength);

    public ReadLineRow(ReadLineBuffer readLineBuffer, short rowIndex)
    {
        this.ReadLineBuffer = readLineBuffer;
        this.RowIndex = rowIndex;
    }
}
