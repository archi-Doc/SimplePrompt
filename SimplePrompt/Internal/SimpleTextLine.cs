// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Arc.Collections;

namespace SimplePrompt.Internal;

internal class SimpleTextLine
{
    private const int PoolSize = 32;
    private const int InitialBufferSize = 256;

    #region ObjectPool

    private static readonly ObjectPool<SimpleTextLine> Pool = new(() => new(), PoolSize);

    public static SimpleTextLine Rent(SimpleConsole simpleConsole, string prompt)
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

    public string? Prompt { get; private set; }

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

    private void SetPrompt(string? prompt)
    {
        this.Prompt = prompt;
        if (this.Prompt is null)
        {
            return;
        }

        var remaining = this.Prompt.Length;
        this.EnsureBuffer(remaining);
        this.Prompt.AsSpan().CopyTo(this.charArray);
        for (var i = 0; i < remaining; i++)
        {
            this.widthArray[i] = SimplePromptHelper.GetCharWidth(this.charArray[i]);
        }

        var position = 0;

        while (remaining > 0)
        {
        }
    }

    private void Initialize(SimpleConsole simpleConsole, string? prompt)
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
