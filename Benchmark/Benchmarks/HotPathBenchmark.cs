// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using System.Text;
using BenchmarkDotNet.Attributes;
using SimplePrompt;
using SimplePrompt.Internal;

namespace Benchmark;

[MemoryDiagnoser]
[ShortRunJob]
[System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "CA1001", Justification = "BenchmarkDotNet calls GlobalCleanup to release the writer and pooled input instance.")]
public class HotPathBenchmark
{
    private readonly StringBuilder builder = new("An output message without an intermediate string.");
    private readonly object number = 123456;
    private ReadLineInstance instance = default!;
    private TextWriter writer = default!;

    [GlobalSetup]
    public void Setup()
    {
        var output = Console.Out;
        Console.SetOut(TextWriter.Null);
        var console = SimpleConsole.Instance;
        Console.SetOut(output);
        this.writer = new SimpleTextWriter(console, TextWriter.Null);
        this.instance = ReadLineInstance.Rent(console, ReadLineOptions.SingleLine, default);
        this.instance.Prepare();
        this.instance.ProcessInput(default, ['x']);
    }

    [GlobalCleanup]
    public void Cleanup()
    {
        this.writer.Dispose();
        ReadLineInstance.Return(this.instance);
    }

    [Benchmark]
    public string? ValidateYesNo()
        => ReadLineOptions.YesNo.TextInputHook!(" YES ");

    [Benchmark]
    public string? SubmitSingleLine()
        => this.instance.ProcessInput(SimplePromptHelper.EnterKeyInfo, default);

    [Benchmark]
    public void WriteStringBuilder()
        => this.writer.Write(this.builder);

    [Benchmark]
    public void WriteBoxedNumber()
        => this.writer.Write(this.number);
}
