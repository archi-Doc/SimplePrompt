// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Arc.Collections;

namespace SimplePrompt.Internal;

internal class ReadLineRow
{
    #region ObjectPool

    private const int PoolSize = 16;
    private static readonly ObjectPool<ReadLineRow> Pool = new(() => new(), PoolSize);

    public static ReadLineRow Rent(ReadLineBuffer readLineBuffer, short rowIndex)
    {
        var obj = Pool.Rent();
        obj.Initialize(readLineBuffer, rowIndex);
        return obj;
    }

    public static void Return(ReadLineRow obj)
    {
        Pool.Return(obj);
    }

    #endregion

    #region FiendAndProperty

    public ReadLineBuffer ReadLineBuffer { get; private set; }

    public bool IsMutable { get; private set; }

    public short RowIndex { get; private set; }

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

    #endregion

    private ReadLineRow()
    {
        this.ReadLineBuffer = default!;
    }

    private void Initialize(ReadLineBuffer readLineBuffer, short rowIndex)
    {
        this.ReadLineBuffer = readLineBuffer;
        this.RowIndex = rowIndex;
    }
}
