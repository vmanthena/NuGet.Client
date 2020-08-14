// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using NuGet.PackageManagement;

namespace NuGet.VisualStudio.Internal.Contracts
{
    public sealed class ProjectAction
    {
        public string Id { get; }
        public IReadOnlyList<ImplicitProjectAction> ImplicitActions { get; }
        public string PackageId { get; }
        public string PackageVersion { get; }
        public NuGetProjectActionType ProjectActionType { get; }
        public string ProjectId { get; }

        public ProjectAction(
            string id,
            string projectId,
            string packageId,
            string packageVersion,
            NuGetProjectActionType projectActionType,
            IReadOnlyList<ImplicitProjectAction> implicitActions)
        {
            Id = id;
            ProjectId = projectId;
            PackageId = packageId;
            PackageVersion = packageVersion;
            ProjectActionType = projectActionType;
            ImplicitActions = implicitActions ?? Array.Empty<ImplicitProjectAction>();
        }
    }
}
