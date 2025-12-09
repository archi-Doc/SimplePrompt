// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Arc.Collections;

namespace SimplePrompt;

internal class RowBuffer
{
    public const int PoolSize = 32;
    public const int InitialCapacity = 16;

    public static readonly ObjectPool<RowBuffer> RowBufferPool = new(() => new RowBuffer(), PoolSize);

    public static RowBuffer Rent()
    {
        var rowBuffer = RowBufferPool.Rent();
        return rowBuffer;
    }

    public static void Return(RowBuffer rowBuffer)
        => RowBufferPool.Return(rowBuffer);

    #region FieldAndProperty

    public bool IsKeyInputEnabled { get; set; }

    public int FixedLength { get; private set; }

    public int FixedWidth { get; private set; }

    public ReadOnlySpan<char> FixedCharSpan => this.charArray.AsSpan(0, this.FixedLength);

    public ReadOnlySpan<byte> FixedWidthSpan => this.widthArray.AsSpan(0, this.FixedLength);

    public int InputLength { get; private set; }

    public int InputWidth { get; private set; }

    public ReadOnlySpan<char> InputCharSpan => this.charArray.AsSpan(this.FixedLength, this.InputLength);

    public ReadOnlySpan<byte> InputWidthSpan => this.widthArray.AsSpan(this.FixedLength, this.InputLength);

    public int TotalLength => this.FixedLength + this.InputLength;

    public int TotalWidth => this.FixedWidth + this.InputWidth;

    private char[] charArray = new char[InitialCapacity];
    private byte[] widthArray = new byte[InitialCapacity];

    #endregion

    public void EnsureCapacity(int capacity)
    {
        if (this.charArray.Length < capacity)
        {
            var newSize = CollectionHelper.CalculatePowerOfTwoCapacity(capacity);

            Array.Resize(ref this.charArray, newSize);
            Array.Resize(ref this.widthArray, newSize);
        }
    }
}
