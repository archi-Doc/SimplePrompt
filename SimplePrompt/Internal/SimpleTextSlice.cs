// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Arc.Collections;
using ValueLink;

namespace SimplePrompt.Internal;

[ValueLinkObject]
internal partial class SimpleTextSlice
{
    #region ObjectPool

    private const int PoolSize = 32;
    private static readonly ObjectPool<SimpleTextSlice> Pool = new(() => new(), PoolSize);

    public static SimpleTextSlice Rent(ReadLineBuffer readLineBuffer, bool isMutable)
    {
        var obj = Pool.Rent();
        obj.Initialize(readLineBuffer, isMutable);
        return obj;
    }

    public static void Return(SimpleTextSlice obj)
    {
        obj.Uninitialize();
        Pool.Return(obj);
    }

    #endregion

    #region FiendAndProperty

    public ReadLineBuffer ReadLineBuffer { get; private set; }

    public bool IsMutable { get; private set; }

    public int StartIndex { get; set; }

    public short Length { get; set; }

    public short Width { get; set; }

    public ReadOnlySpan<char> CharSpan => this.ReadLineBuffer.CharArray.AsSpan(this.StartIndex, this.Length);

    public ReadOnlySpan<byte> WidthSpan => this.ReadLineBuffer.WidthArray.AsSpan(this.StartIndex, this.Length);

    #endregion

    [Link(Primary = true, Type = ChainType.LinkedList, Name = "Slice")]
    private SimpleTextSlice()
    {
        this.ReadLineBuffer = default!;
    }

    private void Initialize(ReadLineBuffer readLineBuffer, bool isMutable)
    {
        this.ReadLineBuffer = readLineBuffer;
        this.IsMutable = isMutable;
    }

    private void Uninitialize()
    {
        this.Goshujin = default;
        this.ReadLineBuffer = default!;
        this.IsMutable = false;
    }
}
