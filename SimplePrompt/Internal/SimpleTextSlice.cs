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

    public static SimpleTextSlice Rent(SimpleTextLine readLineBuffer, bool isMutable)
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

    public SimpleTextLine SimpleTextLine { get; private set; }

    public bool IsMutable { get; private set; }

    public int Start { get; set; }

    public short Length { get; set; }

    public short Width { get; set; }

    public ReadOnlySpan<char> CharSpan => this.SimpleTextLine.CharArray.AsSpan(this.Start, this.Length);

    public ReadOnlySpan<byte> WidthSpan => this.SimpleTextLine.WidthArray.AsSpan(this.Start, this.Length);

    #endregion

    [Link(Primary = true, Type = ChainType.LinkedList, Name = "Slice")]
    private SimpleTextSlice()
    {
        this.SimpleTextLine = default!;
    }

    public void Prepare(bool isMutable, int start, int length)
    {
        this.IsMutable = isMutable;
        this.Start = start;
        this.Length = (short)length;
        this.Width = (short)SimplePromptHelper.GetWidth(this.CharSpan);
    }

    private void Initialize(SimpleTextLine simpleTextLine, bool isMutable)
    {
        this.SimpleTextLine = simpleTextLine;
        this.IsMutable = isMutable;
    }

    private void Uninitialize()
    {
        this.Goshujin = default;
        this.SimpleTextLine = default!;
        this.IsMutable = false;
    }
}
