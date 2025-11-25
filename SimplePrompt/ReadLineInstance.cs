// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

namespace SimplePrompt;

internal record class ReadLineInstance
{
    public ReadLineOptions Options { get; }

    public ReadLineInstance(ReadLineOptions options)
    {
        this.Options = options;
    }
}
