// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Arc;
using Arc.Collections;

namespace SimplePrompt.Internal;

internal class SimpleTextLine
{
    private const int PoolSize = 32;
    private const int InitialBufferSize = 256;

    #region ObjectPool

    private static readonly ObjectPool<SimpleTextLine> Pool = new(() => new(), PoolSize);

    public static SimpleTextLine Rent(SimpleConsole simpleConsole, int index, ReadOnlySpan<char> prompt)
    {
        var obj = Pool.Rent();
        obj.Initialize(simpleConsole, index, prompt);
        return obj;
    }

    public static void Return(SimpleTextLine obj)
    {
        obj.Uninitialize();
        Pool.Return(obj);
    }

    #endregion

    #region FiendAndProperty

    private readonly SimpleTextSlice.GoshujinClass slices = new();
    private SimpleConsole simpleConsole;
    private char[] charArray = new char[InitialBufferSize];
    private byte[] widthArray = new byte[InitialBufferSize];

    public int WindowWidth => this.simpleConsole.WindowWidth;

    public int WindowHeight => this.simpleConsole.WindowHeight;

    public int Index { get; private set; }

    public int Top { get; set; }

    public int Height { get; private set; }

    internal char[] CharArray => this.charArray;

    internal byte[] WidthArray => this.widthArray;

    #endregion

    private SimpleTextLine()
    {
        this.simpleConsole = default!;
    }

    internal (int Index, int Diff) UpdateHeight()
    {
        var previousHeight = this.Height;
        var totalWidth = this.GetWidth();
        if (totalWidth == 0)
        {
            this.Height = 1;
        }
        else
        {
            this.Height = (totalWidth - 0 + this.WindowWidth) / this.WindowWidth;
        }

        return (this.Index, this.Height - previousHeight);
    }

    private int GetWidth()
    {
        var width = 0;
        foreach (var slice in this.slices)
        {
            width += slice.Width;
        }

        return width;
    }

    private void EnsureBuffer(int capacity)
    {
        if (this.charArray.Length < capacity)
        {
            var newSize = CollectionHelper.CalculatePowerOfTwoCapacity(capacity);
            Array.Resize(ref this.charArray, newSize);
            Array.Resize(ref this.widthArray, newSize);
        }
    }

    private void SetPrompt(ReadOnlySpan<char> prompt)
    {
        // this.Uninitialize();

        this.EnsureBuffer(prompt.Length);
        prompt.CopyTo(this.charArray);
        for (var i = 0; i < prompt.Length; i++)
        {
            this.widthArray[i] = SimplePromptHelper.GetCharWidth(this.charArray[i]);
        }

        SimpleTextSlice slice;
        var start = 0;
        var windowWidth = this.simpleConsole.WindowWidth;
        while (start < prompt.Length)
        {// Immutable slices
            var width = 0;
            var end = start;
            while (end < prompt.Length)
            {
                if (width + this.widthArray[end] > windowWidth)
                {
                    break;
                }
                else
                {
                    width += this.widthArray[end];
                    end++;
                }
            }

            slice = SimpleTextSlice.Rent(this);
            slice.Prepare(this.slices, false, start, end - start, width);
            start = end;
        }

        // Mutable slice
        slice = SimpleTextSlice.Rent(this);
        slice.Prepare(this.slices, true, start, 0, 0);
    }

    private void Initialize(SimpleConsole simpleConsole, int index, ReadOnlySpan<char> prompt)
    {
        this.simpleConsole = simpleConsole;
        this.Index = index;
        this.SetPrompt(prompt);
    }

    private void Uninitialize()
    {
        foreach (var x in this.slices)
        {
            SimpleTextSlice.Return(x);
        }
    }
}
