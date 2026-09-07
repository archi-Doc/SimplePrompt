// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using SimplePrompt;
using SimplePrompt.Internal;

namespace xUnitTest;

[Collection(SimpleConsoleTests.Name)]
public class AllocationTest(SimpleConsoleFixture fixture)
{
    [Fact]
    public void YesNoValidationDoesNotAllocate()
    {
        var hook = ReadLineOptions.YesNo.TextInputHook!;
        for (var i = 0; i < 100; i++)
        {
            hook(" YES ");
        }

        var before = GC.GetAllocatedBytesForCurrentThread();
        for (var i = 0; i < 1000; i++)
        {
            hook(" YES ");
        }

        var allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        Assert.Equal(0, allocated);
    }

    [Fact]
    public void SingleLineSubmissionOnlyAllocatesTheResultString()
    {
        var instance = ReadLineInstance.Rent(fixture.Console, ReadLineOptions.SingleLine, TestContext.Current.CancellationToken);
        try
        {
            instance.Prepare();
            instance.ProcessInput(default, ['x']);
            for (var i = 0; i < 100; i++)
            {
                instance.ProcessInput(SimplePromptHelper.EnterKeyInfo, default);
            }

            var before = GC.GetAllocatedBytesForCurrentThread();
            for (var i = 0; i < 1000; i++)
            {
                GC.KeepAlive(instance.ProcessInput(SimplePromptHelper.EnterKeyInfo, default));
            }

            var allocated = GC.GetAllocatedBytesForCurrentThread() - before;
            Assert.InRange(allocated, 1, 24 * 1000);
        }
        finally
        {
            ReadLineInstance.Return(instance);
        }
    }
}
