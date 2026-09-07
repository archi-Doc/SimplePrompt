// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using SimplePrompt;
using SimplePrompt.Internal;

namespace xUnitTest;

[Collection(SimpleConsoleTests.Name)]
public class EditingBoundaryTest(SimpleConsoleFixture fixture)
{
    [Theory]
    [InlineData(2)]
    [InlineData(3)]
    public void BatchEndingAtWrapPlacesCaretOnEmptyRow(int rows)
    {
        var instance = ReadLineInstance.Rent(fixture.Console, new() { Prompt = string.Empty }, TestContext.Current.CancellationToken);
        try
        {
            instance.Prepare();
            var text = new string('a', SimpleConsole.WindowWidth * rows);
            instance.ProcessInput(default, text.ToCharArray());
            Assert.Equal(rows, instance.CurrentLocation.RowIndex);
            Assert.Equal(0, instance.CurrentLocation.CursorPosition);
            instance.ProcessInput(default, ['z']);
            Assert.Equal(text + "z", instance.LineList[0].InputSpan.ToString());
        }
        finally
        {
            ReadLineInstance.Return(instance);
        }
    }

    [Fact]
    public void InsertionBeforeWideCharacterTracksItsNewRow()
    {
        var instance = ReadLineInstance.Rent(fixture.Console, new() { Prompt = string.Empty }, TestContext.Current.CancellationToken);
        try
        {
            instance.Prepare();
            instance.ProcessInput(default, (new string('a', SimpleConsole.WindowWidth - 2) + "日").ToCharArray());
            instance.CurrentLocation.MoveLeft(true);
            instance.ProcessInput(default, ['b']);
            Assert.Equal(1, instance.CurrentLocation.RowIndex);
            Assert.Equal(0, instance.CurrentLocation.CursorPosition);
            instance.ProcessInput(new(default, ConsoleKey.Delete, false, false, false), default);
            Assert.Equal(new string('a', SimpleConsole.WindowWidth - 2) + "b", instance.LineList[0].InputSpan.ToString());
        }
        finally
        {
            ReadLineInstance.Return(instance);
        }
    }

    [Fact]
    public void ClearWrappedPromptKeepsFollowingLinesAdjacent()
    {
        var options = new ReadLineOptions
        {
            Prompt = new string('p', SimpleConsole.WindowWidth + 1),
            MultilineDelimiter = "|",
        };
        var instance = ReadLineInstance.Rent(fixture.Console, options, TestContext.Current.CancellationToken);
        try
        {
            instance.Prepare();
            instance.ProcessInput(SimplePromptHelper.EnterKeyInfo, ['|']);
            instance.CurrentLocation.ChangeLine(-1);
            instance.ProcessInput(new(default, ConsoleKey.U, false, false, true), default);
            var first = instance.LineList[0];
            Assert.Equal(2, first.Height);
            Assert.Equal(first.Top + first.Height, instance.LineList[1].Top);
            Assert.Equal(first.PromptLength, instance.CurrentLocation.ArrayPosition);
        }
        finally
        {
            ReadLineInstance.Return(instance);
        }
    }

    [Theory]
    [InlineData("y", "y")]
    [InlineData(" YES ", " YES ")]
    [InlineData("\tNo\t", "\tNo\t")]
    [InlineData("\u3000Y\u3000", "\u3000Y\u3000")]
    [InlineData("", null)]
    [InlineData("yesterday", null)]
    public void YesNoValidationPreservesText(string input, string? expected)
        => Assert.Equal(expected, ReadLineOptions.YesNo.TextInputHook!(input));

    [Theory]
    [InlineData(40000, 65536)]
    [InlineData(40000, 31)]
    [InlineData(40000, 40001)]
    public async Task QueuedInputIsLimitedWithoutSplittingSurrogates(int length, int limit)
    {
        var input = new string('a', length) + "😀z";
        var task = fixture.ReadLine(ReadLineOptions.SingleLine with { MaxInputLength = limit });
        fixture.Console.EnqueueInput(input);
        var expectedLength = Math.Min(input.Length, limit);
        if (char.IsHighSurrogate(input[expectedLength - 1]))
        {
            expectedLength--;
        }

        Assert.Equal(input[..expectedLength], await fixture.Wait(task));
    }
}
