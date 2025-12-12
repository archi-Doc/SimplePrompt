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

    public static SimpleTextLine Rent(SimpleConsole simpleConsole, ReadOnlySpan<char> prompt)
    {
        var obj = Pool.Rent();
        obj.Initialize(simpleConsole, prompt);
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

    internal char[] CharArray => this.charArray;

    internal byte[] WidthArray => this.widthArray;

    // public string? Prompt { get; private set; }

    #endregion

    private SimpleTextLine()
    {
        this.simpleConsole = default!;
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
        this.Uninitialize();

        var remaining = prompt.Length;
        this.EnsureBuffer(remaining);
        prompt.CopyTo(this.charArray);
        for (var i = 0; i < remaining; i++)
        {
            this.widthArray[i] = SimplePromptHelper.GetCharWidth(this.charArray[i]);
        }

        SimpleTextSlice slice;
        while (remaining > 0)
        {// Immutable slice
            var position = prompt.Length - remaining;

            slice = this.NewSlice(remaining);
            slice.Goshujin = this.slices;
        }

        // Mutable slice
        slice = SimpleTextSlice.Rent(this, true);
        slice.Goshujin = this.slices;
    }

    private void Initialize(SimpleConsole simpleConsole, ReadOnlySpan<char> prompt)
    {
        this.simpleConsole = simpleConsole;
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
