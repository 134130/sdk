// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

#nullable enable

using System.Collections.Generic;

using Microsoft.Build.Graph;
using Microsoft.Extensions.Tools.Internal;

namespace Microsoft.DotNet.Watcher.Tools
{
    internal sealed class DotNetWatchContext
    {
        public required bool HotReloadEnabled { get; init; }

        public IReporter Reporter { get; init; } = NullReporter.Singleton;

        public ProcessSpec ProcessSpec { get; init; } = default!;

        public FileSet FileSet { get; set; } = default!;

        public int Iteration { get; set; } = -1;

        public FileItem? ChangedFile { get; set; }

        public bool RequiresMSBuildRevaluation { get; set; }

        public bool SuppressMSBuildIncrementalism { get; set; }

        public BrowserRefreshServer? BrowserRefreshServer { get; set; }

        public LaunchSettingsProfile LaunchSettingsProfile { get; init; } = default!;

        public ProjectGraph? ProjectGraph { get; set; }

        public string? TargetFramework { get; init; }

        public IReadOnlyList<(string name, string value)>? BuildProperties { get; init; }
    }
}
