// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Benchmark;
using BenchmarkDotNet.Attributes;
using SimplePrompt;

namespace Benchmark;

[Config(typeof(BenchmarkConfig))]
public class ConsoleBenchmark
{
    public ConsoleBenchmark()
    {
    }

    [Benchmark]
    public int CursorTop()
    {
        return Console.CursorTop;
    }

    [Benchmark]
    public int CursorTopAlt()
    {
        return AltConsole.CursorTop;
    }
}
