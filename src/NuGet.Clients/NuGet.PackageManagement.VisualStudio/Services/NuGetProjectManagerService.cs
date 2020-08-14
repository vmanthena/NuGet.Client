// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

#nullable enable

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft;
using Microsoft.ServiceHub.Framework;
using Microsoft.ServiceHub.Framework.Services;
using Microsoft.VisualStudio.Threading;
using NuGet.Configuration;
using NuGet.Packaging;
using NuGet.Packaging.Core;
using NuGet.ProjectManagement;
using NuGet.Protocol.Core.Types;
using NuGet.Resolver;
using NuGet.VisualStudio;
using NuGet.VisualStudio.Internal.Contracts;

namespace NuGet.PackageManagement.VisualStudio
{
    public sealed class NuGetProjectManagerService : INuGetProjectManagerService
    {
        private readonly ServiceActivationOptions _options;
        private readonly IServiceBroker _serviceBroker;
        private readonly AuthorizationServiceClient _authorizationServiceClient;

        private AsyncLazy<IDeleteOnRestartManager> _deleteOnRestartManager;
        private AsyncLazy<ISettings> _settings;
        private AsyncLazy<ISolutionManager> _solutionManager;
        private AsyncLazy<ISourceRepositoryProvider> _sourceRepositoryProvider;
        private NuGetPackageManager? _packageManager;
        private SourceCacheContext? _sourceCacheContext;
        private Dictionary<string, ResolvedAction>? _resolvedActions;
        private PackageIdentity? _packageIdentity;

        public NuGetProjectManagerService(ServiceActivationOptions options, IServiceBroker sb, AuthorizationServiceClient ac)
        {
            Assumes.NotNull(sb);
            Assumes.NotNull(ac);

            _options = options;
            _serviceBroker = sb;
            _authorizationServiceClient = ac;

            _deleteOnRestartManager = new AsyncLazy<IDeleteOnRestartManager>(
                () => ServiceLocator.GetInstanceAsync<IDeleteOnRestartManager>(),
                NuGetUIThreadHelper.JoinableTaskFactory);
            _settings = new AsyncLazy<ISettings>(
                () => ServiceLocator.GetInstanceAsync<ISettings>(),
                NuGetUIThreadHelper.JoinableTaskFactory);
            _solutionManager = new AsyncLazy<ISolutionManager>(
               () => ServiceLocator.GetInstanceAsync<ISolutionManager>(),
               NuGetUIThreadHelper.JoinableTaskFactory);
            _sourceRepositoryProvider = new AsyncLazy<ISourceRepositoryProvider>(
                () => ServiceLocator.GetInstanceAsync<ISourceRepositoryProvider>(),
                NuGetUIThreadHelper.JoinableTaskFactory);
        }

        public async ValueTask<IReadOnlyCollection<IProjectContextInfo>> GetProjectsAsync(CancellationToken cancellationToken)
        {
            var solutionManager = await ServiceLocator.GetInstanceAsync<IVsSolutionManager>();
            Assumes.NotNull(solutionManager);

            NuGetProject[] projects = (await solutionManager.GetNuGetProjectsAsync()).ToArray();
            var projectContexts = new List<IProjectContextInfo>(projects.Length);

            foreach (NuGetProject nugetProject in projects)
            {
                var projectContext = await ProjectContextInfo.CreateAsync(nugetProject, cancellationToken);
                projectContexts.Add(projectContext);
            }

            return projectContexts;
        }

        public async ValueTask<IProjectContextInfo> GetProjectAsync(string projectId, CancellationToken cancellationToken)
        {
            Assumes.NotNullOrEmpty(projectId);

            NuGetProject project = await GetNuGetProjectMatchingProjectIdAsync(projectId);

            return await ProjectContextInfo.CreateAsync(project, cancellationToken);
        }

        public async ValueTask<IReadOnlyCollection<PackageReference>> GetInstalledPackagesAsync(IReadOnlyCollection<string> projectIds, CancellationToken cancellationToken)
        {
            Assumes.NotNullOrEmpty(projectIds);

            NuGetProject[] projects = await GetProjectsAsync(projectIds);

            // Read package references from all projects.
            IEnumerable<Task<IEnumerable<PackageReference>>> tasks = projects.Select(project => project.GetInstalledPackagesAsync(cancellationToken));
            IEnumerable<PackageReference>[] packageReferences = await Task.WhenAll(tasks);

            return packageReferences.SelectMany(e => e).ToArray();
        }

        public async ValueTask<object> GetMetadataAsync(string projectId, string key, CancellationToken cancellationToken)
        {
            Assumes.NotNullOrEmpty(projectId);
            Assumes.NotNullOrEmpty(key);

            NuGetProject project = await GetNuGetProjectMatchingProjectIdAsync(projectId);

            return project.GetMetadata<object>(key);
        }

        public async ValueTask<(bool, object)> TryGetMetadataAsync(string projectId, string key, CancellationToken cancellationToken)
        {
            Assumes.NotNullOrEmpty(projectId);
            Assumes.NotNullOrEmpty(key);

            NuGetProject project = await GetNuGetProjectMatchingProjectIdAsync(projectId);

            bool success = project.TryGetMetadata(key, out object value);

            return (success, value);
        }

        public async ValueTask BeginOperationAsync()
        {
            ISourceRepositoryProvider? sourceRepositoryProvider = await _sourceRepositoryProvider.GetValueAsync();
            ISettings settings = await _settings.GetValueAsync();
            ISolutionManager solutionManager = await _solutionManager.GetValueAsync();
            IDeleteOnRestartManager deleteOnRestartManager = await _deleteOnRestartManager.GetValueAsync();

            Assumes.NotNull(sourceRepositoryProvider);
            Assumes.NotNull(settings);
            Assumes.NotNull(solutionManager);
            Assumes.NotNull(deleteOnRestartManager);

            _packageManager ??= new NuGetPackageManager(
                sourceRepositoryProvider,
                settings,
                solutionManager,
                deleteOnRestartManager);
            _sourceCacheContext = new SourceCacheContext();
            _resolvedActions = new Dictionary<string, ResolvedAction>();
            _packageIdentity = null;
        }

        public ValueTask EndOperationAsync()
        {
            _sourceCacheContext?.Dispose();
            _sourceCacheContext = null;
            _resolvedActions = null;
            _packageIdentity = null;

            return new ValueTask();
        }

        public async ValueTask ExecuteActionsAsync(IReadOnlyList<ProjectAction> actions, CancellationToken cancellationToken)
        {
            Assumes.NotNullOrEmpty(actions);
            Assumes.NotNull(_packageManager);
            Assumes.NotNullOrEmpty(_resolvedActions);

            INuGetProjectContext projectContext = await ServiceLocator.GetInstanceAsync<INuGetProjectContext>();

            Assumes.NotNull(projectContext);

            if (IsDirectInstall(actions))
            {
                NuGetPackageManager.SetDirectInstall(_packageIdentity, projectContext);
            }

            var nugetProjectActions = new List<NuGetProjectAction>();

            foreach (ProjectAction action in actions)
            {
                if (_resolvedActions.TryGetValue(action.Id, out ResolvedAction resolvedAction))
                {
                    nugetProjectActions.Add(resolvedAction.Action);
                }
            }

            Assumes.NotNullOrEmpty(nugetProjectActions);

            IEnumerable<NuGetProject> projects = nugetProjectActions.Select(action => action.Project);

            await _packageManager.ExecuteNuGetProjectActionsAsync(
                projects,
                nugetProjectActions,
                projectContext,
                _sourceCacheContext,
                cancellationToken);

            NuGetPackageManager.ClearDirectInstall(projectContext);
        }

        public async ValueTask<IReadOnlyList<ProjectAction>> GetInstallActionsAsync(
            string projectId,
            PackageIdentity packageIdentity,
            VersionConstraints versionConstraints,
            bool includePrelease,
            DependencyBehavior dependencyBehavior,
            IReadOnlyList<string> packageSourceNames,
            CancellationToken cancellationToken)
        {
            Assumes.NotNullOrEmpty(projectId);
            Assumes.NotNull(packageIdentity);
            Assumes.NotNullOrEmpty(packageSourceNames);
            Assumes.NotNull(_packageManager);
            Assumes.NotNull(_sourceCacheContext);
            Assumes.NotNull(_resolvedActions);
            Assumes.Null(_packageIdentity);

            _packageIdentity = packageIdentity;

            IReadOnlyList<SourceRepository> sourceRepositories = await GetSourceRepositoriesAsync(packageSourceNames);

            Assumes.NotNullOrEmpty(sourceRepositories);

            INuGetProjectContext projectContext = await ServiceLocator.GetInstanceAsync<INuGetProjectContext>();
            (bool success, NuGetProject? project) = await TryGetNuGetProjectMatchingProjectIdAsync(projectId);

            Assumes.True(success);
            Assumes.NotNull(project);

            var resolutionContext = new ResolutionContext(
                dependencyBehavior,
                includePrelease,
                includeUnlisted: false,
                versionConstraints,
                new GatherCache(),
                _sourceCacheContext);

            IEnumerable<NuGetProjectAction> actions = await _packageManager.PreviewInstallPackageAsync(
                project,
                _packageIdentity,
                resolutionContext,
                projectContext,
                sourceRepositories,
                secondarySources: null,
                cancellationToken);

            var projectActions = new List<ProjectAction>();

            foreach (NuGetProjectAction action in actions)
            {
                List<ImplicitProjectAction>? implicitActions = null;

                if (action is BuildIntegratedProjectAction buildIntegratedAction)
                {
                    implicitActions = new List<ImplicitProjectAction>();

                    foreach (NuGetProjectAction? buildAction in buildIntegratedAction.GetProjectActions())
                    {
                        var implicitAction = new ImplicitProjectAction(
                            CreateProjectActionId(),
                            buildAction.PackageIdentity.Id,
                            buildAction.PackageIdentity.Version.ToString(),
                            buildAction.NuGetProjectActionType);

                        implicitActions.Add(implicitAction);
                    }
                }

                var resolvedAction = new ResolvedAction(project, action);
                var projectAction = new ProjectAction(
                    CreateProjectActionId(),
                    projectId,
                    packageIdentity.Id,
                    packageIdentity.Version.ToString(),
                    action.NuGetProjectActionType,
                    implicitActions);

                _resolvedActions[projectAction.Id] = resolvedAction;

                projectActions.Add(projectAction);
            }

            return projectActions;
        }

        public async ValueTask<IReadOnlyList<ProjectAction>> GetUninstallActionsAsync(
            string projectId,
            string packageId,
            bool removeDependencies,
            bool forceRemove,
            CancellationToken cancellationToken)
        {
            Assumes.NotNullOrEmpty(projectId);
            Assumes.NotNullOrEmpty(packageId);
            Assumes.NotNull(_packageManager);
            Assumes.NotNull(_sourceCacheContext);
            Assumes.NotNull(_resolvedActions);
            Assumes.Null(_packageIdentity);

            INuGetProjectContext projectContext = await ServiceLocator.GetInstanceAsync<INuGetProjectContext>();
            (bool success, NuGetProject? project) = await TryGetNuGetProjectMatchingProjectIdAsync(projectId);

            Assumes.True(success);
            Assumes.NotNull(project);

            var projectActions = new List<ProjectAction>();
            var uninstallationContext = new UninstallationContext(removeDependencies, forceRemove);

            IEnumerable<NuGetProjectAction> actions = await _packageManager.PreviewUninstallPackageAsync(
                project,
                packageId,
                uninstallationContext,
                projectContext,
                cancellationToken);

            foreach (NuGetProjectAction action in actions)
            {
                var resolvedAction = new ResolvedAction(project, action);
                var projectAction = new ProjectAction(
                    CreateProjectActionId(),
                    projectId,
                    packageId,
                    action.PackageIdentity.Version.ToString(),
                    action.NuGetProjectActionType,
                    implicitActions: null);

                _resolvedActions[projectAction.Id] = resolvedAction;

                projectActions.Add(projectAction);
            }

            return projectActions;
        }

        public async ValueTask<bool> IsProjectUpgradeableAsync(string projectId, CancellationToken cancellationToken)
        {
            Assumes.NotNullOrEmpty(projectId);

            NuGetProject project = await GetNuGetProjectMatchingProjectIdAsync(projectId);

            return await NuGetProjectUpgradeUtility.IsNuGetProjectUpgradeableAsync(project);
        }

        public async ValueTask<IReadOnlyCollection<IProjectContextInfo>> GetUpgradeableProjectsAsync(
            IReadOnlyCollection<string> projectIds,
            CancellationToken cancellationToken)
        {
            Assumes.NotNullOrEmpty(projectIds);

            NuGetProject[] projects = await GetProjectsAsync(projectIds);

            var upgradeableProjects = new List<IProjectContextInfo>();

            IEnumerable<NuGetProject> capableProjects = projects
                .Where(project =>
                    project.ProjectStyle == ProjectModel.ProjectStyle.PackagesConfig &&
                    project.ProjectServices.Capabilities.SupportsPackageReferences);

            // get all packages.config based projects with no installed packages
            foreach (NuGetProject project in capableProjects)
            {
                IEnumerable<PackageReference> installedPackages = await project.GetInstalledPackagesAsync(cancellationToken);

                if (!installedPackages.Any())
                {
                    IProjectContextInfo upgradeableProject = await ProjectContextInfo.CreateAsync(project, cancellationToken);

                    upgradeableProjects.Add(upgradeableProject);
                }
            }

            return upgradeableProjects;
        }

        public async ValueTask<IProjectContextInfo> UpgradeProjectToPackageReferenceAsync(
            string projectId, CancellationToken cancellationToken)
        {
            Assumes.NotNullOrEmpty(projectId);

            IVsSolutionManager solutionManager = await ServiceLocator.GetInstanceAsync<IVsSolutionManager>();

            Assumes.NotNull(solutionManager);

            NuGetProject project = await GetNuGetProjectMatchingProjectIdAsync(projectId);
            NuGetProject newProject = await solutionManager.UpgradeProjectToPackageReferenceAsync(project);

            return await ProjectContextInfo.CreateAsync(newProject, cancellationToken);
        }

        public async ValueTask<IReadOnlyCollection<IProjectContextInfo>> GetProjectsWithDeprecatedDotnetFrameworkAsync(CancellationToken cancellationToken)
        {
            Assumes.NotNull(_packageManager);
            Assumes.NotNullOrEmpty(_resolvedActions);

            IEnumerable<NuGetProject> affectedProjects = DotnetDeprecatedPrompt.GetAffectedProjects(_resolvedActions.Values);

            IEnumerable<Task<IProjectContextInfo>> tasks = affectedProjects
                .Select(affectedProject => ProjectContextInfo.CreateAsync(affectedProject, cancellationToken).AsTask());

            await Task.WhenAll(tasks);

            return tasks.Select(task => task.Result).ToArray();
        }

        public void Dispose()
        {
            _authorizationServiceClient.Dispose();
            _sourceCacheContext?.Dispose();
            GC.SuppressFinalize(this);
        }

        private static string CreateProjectActionId()
        {
            return Guid.NewGuid().ToString("N", CultureInfo.InvariantCulture);
        }

        private async ValueTask<NuGetProject> GetNuGetProjectMatchingProjectIdAsync(string projectId)
        {
            var solutionManager = await ServiceLocator.GetInstanceAsync<IVsSolutionManager>();
            Assumes.NotNull(solutionManager);

            NuGetProject project = (await solutionManager.GetNuGetProjectsAsync())
                .First(p => projectId.Equals(p.GetMetadata<string>(NuGetProjectMetadataKeys.ProjectId), StringComparison.OrdinalIgnoreCase));

            return project;
        }

        private static async Task<NuGetProject[]> GetProjectsAsync(IReadOnlyCollection<string> projectIds)
        {
            Assumes.NotNullOrEmpty(projectIds);

            var solutionManager = await ServiceLocator.GetInstanceAsync<IVsSolutionManager>();
            Assumes.NotNull(solutionManager);

            NuGetProject[] projects = (await solutionManager.GetNuGetProjectsAsync())
                .Where(p => projectIds.Contains(p.GetMetadata<string>(NuGetProjectMetadataKeys.ProjectId)))
                .ToArray();

            return projects;
        }

        private async ValueTask<IReadOnlyList<SourceRepository>> GetSourceRepositoriesAsync(IReadOnlyList<string> packageSourceNames)
        {
            ISourceRepositoryProvider sourceRepositoryProvider = await _sourceRepositoryProvider.GetValueAsync();
            var sourceRepositories = new List<SourceRepository>();
            Dictionary<string, SourceRepository>? allSourceRepositories = sourceRepositoryProvider.GetRepositories()
                .ToDictionary(sr => sr.PackageSource.Name, sr => sr);

            foreach (string packageSourceName in packageSourceNames)
            {
                if (allSourceRepositories.TryGetValue(packageSourceName, out SourceRepository sourceRepository))
                {
                    sourceRepositories.Add(sourceRepository);
                }
            }

            return sourceRepositories;
        }

        private bool IsDirectInstall(IReadOnlyList<ProjectAction> projectActions)
        {
            return _packageIdentity != null
                && projectActions.Any(projectAction => projectAction.ProjectActionType == NuGetProjectActionType.Install);
        }

        private async ValueTask<(bool, NuGetProject?)> TryGetNuGetProjectMatchingProjectIdAsync(string projectId)
        {
            var solutionManager = await ServiceLocator.GetInstanceAsync<IVsSolutionManager>();
            Assumes.NotNull(solutionManager);

            NuGetProject project = (await solutionManager.GetNuGetProjectsAsync())
                .FirstOrDefault(p => projectId.Equals(p.GetMetadata<string>(NuGetProjectMetadataKeys.ProjectId), StringComparison.OrdinalIgnoreCase));

            return (project != null, project);
        }
    }
}
