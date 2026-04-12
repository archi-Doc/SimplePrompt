// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Arc.Threading;

namespace SimplePrompt;

internal sealed record class AltConsoleJob : ReusableTaskJob
{
    public AltConsoleJobKind Kind { get; set; }
}
