// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using SimplePrompt;
using SimplePrompt.Internal;

namespace xUnitTest;

[Collection(SimpleConsoleTests.Name)]
public class TerminalIoRegressionTest(SimpleConsoleFixture fixture)
{
    [Fact]
    public void WriterReportsUnderlyingEncoding()
    {
        using var sink = new StringWriter();
        using var writer = new SimpleTextWriter(fixture.Console, sink);
        Assert.Same(sink.Encoding, writer.Encoding);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ArraySlicesFollowTextWriterArgumentContract(bool newLine)
    {
        using var writer = new SimpleTextWriter(fixture.Console, fixture.Sink);
        Assert.Throws<ArgumentNullException>("buffer", () => Write(null!, 0, 0));
        Assert.Throws<ArgumentOutOfRangeException>("index", () => Write(['a'], -1, 0));
        Assert.Throws<ArgumentOutOfRangeException>("count", () => Write(['a'], 0, -1));
        Assert.Throws<ArgumentException>(() => Write(['a'], 2, 0));
        Assert.Throws<ArgumentException>(() => Write(['a'], 0, 2));
        Assert.Throws<ArgumentException>(() => Write(['a'], int.MaxValue, int.MaxValue));

        fixture.ClearOutput();
        Write(['a', 'b', 'c'], 1, 1);
        Assert.Contains('b', fixture.TakeOutput());

        void Write(char[] buffer, int index, int count)
        {
            if (newLine)
            {
                writer.WriteLine(buffer, index, count);
            }
            else
            {
                writer.Write(buffer, index, count);
            }
        }
    }

    [Fact]
    public void MovingRightPastMarginCombiningMarkPreservesInputPosition()
    {
        var instance = ReadLineInstance.Rent(fixture.Console, new() { Prompt = string.Empty }, TestContext.Current.CancellationToken);
        try
        {
            instance.Prepare();
            var width = SimpleConsole.WindowWidth;
            instance.ProcessInput(default, new string('a', width) + "\u0301b");
            var location = instance.CurrentLocation;
            location.MoveFirst();
            for (var i = 0; i < width; i++)
            {
                location.MoveRight();
            }

            Assert.Equal(width, location.ArrayPosition);
            Assert.Equal(0, location.RowIndex);
            Assert.Equal(width, location.CursorPosition);

            location.Restore(CursorOperation.None);
            Assert.Equal(width, location.CursorPosition);

            location.MoveRight();
            Assert.Equal(width + 1, location.ArrayPosition);
            Assert.Equal(1, location.RowIndex);
            Assert.Equal(0, location.CursorPosition);

            location.MoveRight();
            Assert.Equal(width + 2, location.ArrayPosition);
            Assert.Equal(1, location.CursorPosition);
            location.MoveLeft(false);
            Assert.Equal(width + 1, location.ArrayPosition);
        }
        finally
        {
            ReadLineInstance.Return(instance);
        }
    }

    [Theory]
    [InlineData(0, 2)]
    [InlineData(0, 3)]
    [InlineData(1, 2)]
    [InlineData(1, 3)]
    [InlineData(2, 2)]
    [InlineData(2, 3)]
    public void CursorOperationsApplyAtUnchangedCoordinates(int target, int operation)
    {
        var instance = ReadLineInstance.Rent(fixture.Console, new() { Prompt = string.Empty }, TestContext.Current.CancellationToken);
        try
        {
            instance.Prepare();
            instance.CurrentLocation.Reset();
            fixture.ClearOutput();
            var cursorOperation = (CursorOperation)operation;
            switch (target)
            {
                case 0:
                    instance.CurrentLocation.Reset(cursorOperation);
                    break;
                case 1:
                    instance.CurrentLocation.Restore(cursorOperation);
                    break;
                case 2:
                    instance.ResetCursor(cursorOperation);
                    break;
            }

            var output = fixture.TakeOutput();
            Assert.Contains('H', output); // An explicit cursor-position sequence is emitted.
            if (cursorOperation == CursorOperation.Hide)
            {
                Assert.Contains("\e[?25l", output);
            }
        }
        finally
        {
            ReadLineInstance.Return(instance);
        }
    }

    [Fact]
    public void RestoringScrolledRowPreservesLogicalCaret()
    {
        var instance = ReadLineInstance.Rent(fixture.Console, new() { Prompt = string.Empty }, TestContext.Current.CancellationToken);
        try
        {
            instance.Prepare();
            instance.ProcessInput(default, "abc");
            instance.LineList[0].Top = -1;
            instance.CurrentLocation.Restore(CursorOperation.None);

            Assert.Equal(3, instance.CurrentLocation.ArrayPosition);
            Assert.Equal(3, instance.CurrentLocation.CursorPosition);
            Assert.Equal((3, 0), SimpleConsole.GetCursorPosition());
        }
        finally
        {
            ReadLineInstance.Return(instance);
        }
    }

    [Fact]
    public void EmptyAndInvalidLocationsAreHandled()
    {
        var instance = ReadLineInstance.Rent(fixture.Console, new() { Prompt = string.Empty }, TestContext.Current.CancellationToken);
        try
        {
            var location = instance.CurrentLocation;
            location.ChangeLine(1);
            Assert.False(location.TryGetLine(out _));

            instance.Prepare();
            location.LineIndex = -1;
            Assert.False(location.TryGetLine(out _));
            Assert.False(location.TryGetLineAndRow(out _, out _));
            location.LocationToCursor();
            Assert.Equal(0, location.LineIndex);

            location.RowIndex = -1;
            Assert.False(location.TryGetLineAndRow(out _, out _));
            location.LocationToCursor();
            Assert.Equal(0, location.RowIndex);
        }
        finally
        {
            ReadLineInstance.Return(instance);
        }
    }
}
