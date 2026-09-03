// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using System.Globalization;
using System.Text;
using SimplePrompt;
using SimplePrompt.Internal;

namespace xUnitTest;

[Collection(SimpleConsoleTests.Name)]
public class RegressionTest(SimpleConsoleFixture fixture)
{
    [Fact]
    public void ReturnedInstanceReleasesLinesAndOptions()
    {
        var instance = ReadLineInstance.Rent(fixture.Console, new() { Prompt = "pooled> " }, TestContext.Current.CancellationToken);
        instance.Prepare();
        Assert.NotEmpty(instance.LineList);
        ReadLineInstance.Return(instance);
        Assert.Empty(instance.LineList);
        Assert.Null(instance.OptionsSource);
    }

    [Fact]
    public void OptionsSnapshotDoesNotChangeWhenInstanceIsReinitialized()
    {
        var instance = ReadLineInstance.Rent(fixture.Console, new() { Prompt = "first" }, TestContext.Current.CancellationToken);
        var snapshot = instance.Options;
        try
        {
            instance.Initialize(fixture.Console, new() { Prompt = "second" }, TestContext.Current.CancellationToken);
            Assert.Equal("first", snapshot.Prompt);
        }
        finally
        {
            ReadLineInstance.Return(instance);
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void LongInputIsFullyRedrawn(bool masked)
    {
        var instance = ReadLineInstance.Rent(fixture.Console, new() { Prompt = string.Empty, MaskingCharacter = masked ? '*' : default }, TestContext.Current.CancellationToken);
        try
        {
            instance.Prepare();
            instance.ProcessInput(default, new string('x', 40_000).ToCharArray());
            fixture.ClearOutput();
            instance.Redraw();
            Assert.Equal(40_000, fixture.TakeOutput().Count(c => c == (masked ? '*' : 'x')));
        }
        finally
        {
            ReadLineInstance.Return(instance);
        }
    }

    [Fact]
    public void LongPromptIsFullyDisplayed()
    {
        var instance = ReadLineInstance.Rent(fixture.Console, new() { Prompt = new string('p', 40_000) }, TestContext.Current.CancellationToken);
        try
        {
            fixture.ClearOutput();
            instance.Prepare();
            Assert.Equal(40_000, fixture.TakeOutput().Count(c => c == 'p'));
        }
        finally
        {
            ReadLineInstance.Return(instance);
        }
    }

    [Fact]
    public void WrappedPromptStartsOnItsLastRow()
    {
        var instance = ReadLineInstance.Rent(fixture.Console, new() { Prompt = new string('p', SimpleConsole.WindowWidth + 3) }, TestContext.Current.CancellationToken);
        try
        {
            instance.Prepare();
            var line = instance.LineList[0];
            Assert.Equal(line.InitialRowIndex, instance.CurrentLocation.RowIndex);
            Assert.Equal(1, instance.CurrentLocation.RowIndex);
            Assert.Equal(line.PromptLength, instance.CurrentLocation.ArrayPosition);
        }
        finally
        {
            ReadLineInstance.Return(instance);
        }
    }

    [Fact]
    public void SurrogatePairDoesNotSplitAcrossDisplayRows()
    {
        var instance = ReadLineInstance.Rent(fixture.Console, new() { Prompt = string.Empty }, TestContext.Current.CancellationToken);
        try
        {
            instance.Prepare();
            instance.ProcessInput(default, (new string('a', SimpleConsole.WindowWidth - 1) + "😀z").ToCharArray());
            var line = instance.LineList[0];
            foreach (var row in line.Rows.Where(row => row.Length > 0))
            {
                Assert.False(char.IsHighSurrogate(line.CharArray[row.End - 1]));
                Assert.False(char.IsLowSurrogate(line.CharArray[row.Start]));
            }
        }
        finally
        {
            ReadLineInstance.Return(instance);
        }
    }

    [Fact]
    public void MovingRightAcrossWrapPreservesTheCaret()
    {
        var instance = ReadLineInstance.Rent(fixture.Console, new() { Prompt = string.Empty }, TestContext.Current.CancellationToken);
        try
        {
            instance.Prepare();
            instance.ProcessInput(default, new string('a', SimpleConsole.WindowWidth + 3).ToCharArray());
            instance.CurrentLocation.MoveFirst();
            for (var i = 0; i < SimpleConsole.WindowWidth + 1; i++)
            {
                instance.CurrentLocation.MoveRight();
            }

            Assert.Equal(SimpleConsole.WindowWidth + 1, instance.CurrentLocation.ArrayPosition);
            Assert.Equal(1, instance.CurrentLocation.RowIndex);
            Assert.Equal(1, instance.CurrentLocation.CursorPosition);
        }
        finally
        {
            ReadLineInstance.Return(instance);
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task SurrogateAtBatchBoundaryRemainsIntact(bool queued)
    {
        var text = new string('a', 1023) + "😀z";
        fixture.ClearOutput();
        var task = fixture.ReadLine();
        if (queued)
        {
            fixture.Console.EnqueueInput(text);
        }
        else
        {
            fixture.Type(text);
            fixture.Key(ConsoleKey.Enter);
        }

        Assert.Equal(text, await fixture.Wait(task));
        Assert.Contains("😀z", fixture.TakeOutput());
    }

    [Fact]
    public async Task EmptyContinuationLineCanBeEditedAndSubmitted()
    {
        var task = fixture.ReadLine(new() { LineContinuationCharacter = '\\', MultilineDelimiter = null });
        fixture.Type("a\\");
        fixture.Key(ConsoleKey.Enter);
        fixture.Type("b\\");
        fixture.Key(ConsoleKey.Enter);
        fixture.Key(ConsoleKey.UpArrow);
        fixture.Key(ConsoleKey.U, control: true);
        fixture.Key(ConsoleKey.DownArrow);
        fixture.Type("c");
        fixture.Key(ConsoleKey.Enter);
        Assert.Equal("ac", await fixture.Wait(task));
    }

    [Fact]
    public async Task NullStringBuilderWriteLineStillWritesNewline()
    {
        await fixture.WaitForIdle();
        fixture.ClearOutput();
        fixture.ConsoleOut.WriteLine((StringBuilder?)null);
        Assert.Contains('\n', fixture.TakeOutput());
    }

    [Fact]
    public async Task ConsoleReaderAcceptsEmptyLines()
    {
        var task = Task.Run(() => fixture.ConsoleIn.ReadLine());
        fixture.Console.EnqueueInput(null);
        Assert.Equal(string.Empty, await SimpleConsoleFixture.WaitAny(task));
    }

    [Fact]
    public async Task SurrogatePairMayArriveInSeparatePolls()
    {
        var task = fixture.ReadLine();
        fixture.Type("\uD83D");
        await SimpleConsoleFixture.Delay(50);
        fixture.Type("\uDE00x");
        fixture.Key(ConsoleKey.Backspace);
        fixture.Key(ConsoleKey.Backspace);
        fixture.Type("ok");
        fixture.Key(ConsoleKey.Enter);
        Assert.Equal("ok", await fixture.Wait(task));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task HookExceptionsCompleteTheReadAndAllowTheNextOne(bool textHook)
    {
        var expected = new InvalidOperationException("test hook failure");
        var options = textHook
            ? new ReadLineOptions { AllowEmptyInput = true, TextInputHook = _ => throw expected }
            : new ReadLineOptions { KeyInputHook = (ref ConsoleKeyInfo _) => throw expected };
        var task = fixture.ReadLine(options);
        fixture.Key(textHook ? ConsoleKey.Enter : ConsoleKey.F1);
        var actual = await Assert.ThrowsAsync<InvalidOperationException>(() => task.WaitAsync(SimpleConsoleFixture.Timeout, TestContext.Current.CancellationToken));
        Assert.Same(expected, actual);
        Assert.False(fixture.Console.IsReadLineInProgress);
        var next = fixture.ReadLine();
        fixture.Console.EnqueueInput("recovered");
        Assert.Equal("recovered", await fixture.Wait(next));
    }

    [Fact]
    public async Task AbortCompletesEveryPendingRead()
    {
        var outer = fixture.ReadLine(new() { Prompt = "outer> " });
        var inner = fixture.ReadLine(new() { Prompt = "inner> " });
        fixture.Console.Abort();
        Assert.True((await fixture.WaitResult(outer)).IsTerminated);
        Assert.True((await fixture.WaitResult(inner)).IsTerminated);
        Assert.False(fixture.Console.IsReadLineInProgress);
    }

    [Fact]
    public async Task DelimitedInputPreservesBlankLines()
    {
        var task = fixture.ReadLine(new() { MultilineDelimiter = "|" });
        fixture.Type("|");
        fixture.Key(ConsoleKey.Enter);
        fixture.Key(ConsoleKey.Enter);
        fixture.Type("end|");
        fixture.Key(ConsoleKey.Enter);
        Assert.Equal("|\n\nend|", await fixture.Wait(task));
    }

    [Fact]
    public async Task BackspaceRemovesLeadingZeroWidthCharacter()
    {
        var task = fixture.ReadLine(new() { Prompt = string.Empty });
        fixture.Type("\u0301a");
        fixture.Key(ConsoleKey.Home);
        fixture.Key(ConsoleKey.RightArrow);
        fixture.Key(ConsoleKey.Backspace);
        fixture.Key(ConsoleKey.Enter);
        Assert.Equal("a", await fixture.Wait(task));
    }

    [Fact]
    public void TextWriterKeepsItsCulture()
    {
        var culture = (CultureInfo)CultureInfo.InvariantCulture.Clone();
        culture.NumberFormat.NumberDecimalSeparator = ":";
        using var sink = new StringWriter(culture);
        using var writer = new SimpleTextWriter(fixture.Console, sink);
        Assert.Same(culture, writer.FormatProvider);
    }

    [Fact]
    public async Task MixedWidthEditingMatchesTheTextModel()
    {
        var random = new Random(1729);
        string[] characters = ["a", "b", "日", "😀"];
        for (var iteration = 0; iteration < 12; iteration++)
        {
            var expected = new List<string>();
            var cursor = 0;
            var task = fixture.ReadLine(new() { Prompt = string.Empty, MultilineDelimiter = null, AllowEmptyInput = true });
            // Begin with several wrapped rows before editing their boundaries.
            for (var i = 0; i < SimpleConsole.WindowWidth * 2; i++)
            {
                var character = characters[random.Next(characters.Length)];
                expected.Add(character);
                fixture.Type(character);
                cursor++;
            }

            for (var step = 0; step < 100; step++)
            {
                switch (random.Next(7))
                {
                    case 0:
                        var character = characters[random.Next(characters.Length)];
                        fixture.Type(character);
                        expected.Insert(cursor++, character);
                        break;
                    case 1:
                        fixture.Key(ConsoleKey.LeftArrow);
                        cursor = Math.Max(0, cursor - 1);
                        break;
                    case 2:
                        fixture.Key(ConsoleKey.RightArrow);
                        cursor = Math.Min(expected.Count, cursor + 1);
                        break;
                    case 3:
                        fixture.Key(ConsoleKey.Home);
                        cursor = 0;
                        break;
                    case 4:
                        fixture.Key(ConsoleKey.End);
                        cursor = expected.Count;
                        break;
                    case 5:
                        fixture.Key(ConsoleKey.Backspace);
                        if (cursor > 0)
                        {
                            expected.RemoveAt(--cursor);
                        }

                        break;
                    case 6:
                        fixture.Key(ConsoleKey.Delete);
                        if (cursor < expected.Count)
                        {
                            expected.RemoveAt(cursor);
                        }

                        break;
                }
            }

            fixture.Key(ConsoleKey.Enter);
            Assert.Equal(string.Concat(expected), await fixture.Wait(task));
            fixture.ClearOutput();
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ArrangeKeepsTheInputAndCaret(bool redraw)
    {
        var instance = ReadLineInstance.Rent(fixture.Console, new() { Prompt = "description\n> " }, TestContext.Current.CancellationToken);
        try
        {
            instance.Prepare();
            var input = new string('a', SimpleConsole.WindowWidth * 3) + "日😀";
            instance.ProcessInput(default, input.ToCharArray());
            instance.CurrentLocation.MoveLeft(true);
            var position = instance.CurrentLocation.ArrayPosition;
            new SimpleArrange(fixture.Console).Arrange(instance, SimpleConsole.GetCursorPosition(), redraw);
            Assert.Equal(input, instance.LineList[instance.FirstInputIndex].InputSpan.ToString());
            Assert.Equal(position, instance.CurrentLocation.ArrayPosition);
            instance.CurrentLocation.MoveHorizontal(true);
            instance.CurrentLocation.MoveHorizontal(false);
            Assert.False(char.IsLowSurrogate(instance.LineList[instance.FirstInputIndex].CharArray[instance.CurrentLocation.ArrayPosition]));
        }
        finally
        {
            ReadLineInstance.Return(instance);
        }
    }

    [Fact]
    public void ArrangeWithoutPreparedLinesResetsTheCaret()
    {
        var instance = ReadLineInstance.Rent(fixture.Console, new(), TestContext.Current.CancellationToken);
        try
        {
            instance.CurrentLocation.LineIndex = 100;
            new SimpleArrange(fixture.Console).Arrange(instance, (0, 0), false);
            Assert.Equal(0, instance.CurrentLocation.LineIndex);
        }
        finally
        {
            ReadLineInstance.Return(instance);
        }
    }

    [Fact]
    public void MixedWidthCaretMatchesCharacterBoundaries()
    {
        var random = new Random(1729);
        string[] characters = ["a", "b", "日", "😀"];
        for (var iteration = 0; iteration < 12; iteration++)
        {
            var instance = ReadLineInstance.Rent(fixture.Console, new() { Prompt = string.Empty }, TestContext.Current.CancellationToken);
            try
            {
                instance.Prepare();
                var expected = new List<string>();
                for (var i = 0; i < SimpleConsole.WindowWidth * 2; i++)
                {
                    expected.Add(characters[random.Next(characters.Length)]);
                }

                instance.ProcessInput(default, string.Concat(expected).ToCharArray());
                var cursor = expected.Count;
                for (var step = 0; step < 100; step++)
                {
                    var action = random.Next(7);
                    ConsoleKey key = ConsoleKey.None;
                    var inserted = string.Empty;
                    switch (action)
                    {
                        case 0:
                            inserted = characters[random.Next(characters.Length)];
                            expected.Insert(cursor++, inserted);
                            break;
                        case 1:
                            key = ConsoleKey.LeftArrow;
                            cursor = Math.Max(0, cursor - 1);
                            break;
                        case 2:
                            key = ConsoleKey.RightArrow;
                            cursor = Math.Min(expected.Count, cursor + 1);
                            break;
                        case 3:
                            key = ConsoleKey.Home;
                            cursor = 0;
                            break;
                        case 4:
                            key = ConsoleKey.End;
                            cursor = expected.Count;
                            break;
                        case 5:
                            key = ConsoleKey.Backspace;
                            if (cursor > 0)
                            {
                                expected.RemoveAt(--cursor);
                            }

                            break;
                        case 6:
                            key = ConsoleKey.Delete;
                            if (cursor < expected.Count)
                            {
                                expected.RemoveAt(cursor);
                            }

                            break;
                    }

                    instance.ProcessInput(new ConsoleKeyInfo(default, key, false, false, false), inserted.ToCharArray());
                    var rows = instance.LineList[0].Rows;
                    Assert.True(
                        rows[^1].End == instance.LineList[0].TotalLength,
                        $"Iteration {iteration}, step {step}, action {action}: rows {string.Join(", ", rows.Select(row => $"{row.Start}-{row.End}({row.Width})"))}, length {instance.LineList[0].TotalLength}");
                    var expectedPosition = expected.Take(cursor).Sum(x => x.Length);
                    Assert.True(
                        expectedPosition == instance.CurrentLocation.ArrayPosition,
                        $"Iteration {iteration}, step {step}, action {action}: expected position {expectedPosition}, actual {instance.CurrentLocation.ArrayPosition}");
                    Assert.Equal(string.Concat(expected), instance.LineList[0].InputSpan.ToString());
                }
            }
            finally
            {
                ReadLineInstance.Return(instance);
            }
        }
    }
}
