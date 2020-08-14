// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using NuGet.PackageManagement;

namespace NuGet.VisualStudio.Internal.Contracts
{
    public sealed class ImplicitProjectAction
    {
        public string Id { get; }
        public string PackageId { get; }
        public string PackageVersion { get; }
        public NuGetProjectActionType ProjectActionType { get; }

        public ImplicitProjectAction(
            string id,
            string packageId,
            string packageVersion,
            NuGetProjectActionType projectActionType)
        {
            Id = id;
            PackageId = packageId;
            PackageVersion = packageVersion;
            ProjectActionType = projectActionType;
        }
    }
}
