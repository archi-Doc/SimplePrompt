// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Arc;
using Arc.Collections;
using ValueLink;

namespace SimplePrompt.Internal;

[ValueLinkObject]
internal partial class SimpleTextSlice
{
    #region ObjectPool

    private const int PoolSize = 32;
    private static readonly ObjectPool<SimpleTextSlice> Pool = new(() => new(), PoolSize);

    public static SimpleTextSlice Rent(SimpleTextLine readLineBuffer)
    {
        var obj = Pool.Rent();
        obj.Initialize(readLineBuffer);
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

    public bool IsInput => this.InputStart >= 0;

    public int Start { get; private set; }

    public int InputStart { get; private set; }

    public int Length { get; private set; }

    public int Width { get; private set; }

    public ReadOnlySpan<char> CharSpan => this.SimpleTextLine.CharArray.AsSpan(this.Start, this.Length);

    public ReadOnlySpan<byte> WidthSpan => this.SimpleTextLine.WidthArray.AsSpan(this.Start, this.Length);

    #endregion

    [Link(Primary = true, Type = ChainType.LinkedList, Name = "Slice")]
    private SimpleTextSlice()
    {
        this.SimpleTextLine = default!;
    }

    public void Prepare(SimpleTextSlice.GoshujinClass goshujin, int start, int inputStart, int length, int width)
    {
        this.Goshujin = goshujin;
        this.Start = start;
        this.InputStart = inputStart;
        this.Length = length;
        this.Width = width;
    }

    public override string ToString()
    {
        return this.CharSpan.ToString();
    }

    private void Initialize(SimpleTextLine simpleTextLine)
    {
        this.SimpleTextLine = simpleTextLine;
    }

    private void Uninitialize()
    {
        this.Goshujin = default;
        this.SimpleTextLine = default!;
    }
}
