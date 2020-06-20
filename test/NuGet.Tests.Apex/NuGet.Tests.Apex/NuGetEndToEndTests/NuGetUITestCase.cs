// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Diagnostics;
using System.IO;
using Microsoft.Test.Apex.VisualStudio.Solution;
using Xunit;
using Xunit.Abstractions;

namespace NuGet.Tests.Apex
{
    public class NuGetUITestCase : SharedVisualStudioHostTestClass, IClassFixture<VisualStudioHostFixtureFactory>
    {
        private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(10);
        private static readonly TimeSpan Interval = TimeSpan.FromSeconds(2);

        public NuGetUITestCase(VisualStudioHostFixtureFactory visualStudioHostFixtureFactory, ITestOutputHelper output)
            : base(visualStudioHostFixtureFactory, output)
        {
        }

        [StaFact]
        public void SearchPackageFromUI()
        {
            // Arrange
            EnsureVisualStudioHost();
            var dte = VisualStudio.Dte;
            var solutionService = VisualStudio.Get<SolutionService>();

            solutionService.CreateEmptySolution();
            var project = solutionService.AddProject(ProjectLanguage.CSharp, ProjectTemplate.ClassLibrary, ProjectTargetFramework.V46, "TestProject");
            VisualStudio.ClearOutputWindow();

            // Act
            dte.ExecuteCommand("Project.ManageNuGetPackages");
            var nugetTestService = GetNuGetTestService();
            var uiwindow = nugetTestService.GetUIWindowfromProject(project);
            uiwindow.SwitchTabToBrowse();
            uiwindow.SeachPackgeFromUI("newtonsoft.json");

            // Assert
            VisualStudio.AssertNoErrors();
        }

        [StaFact]
        public void InstallPackageFromUI()
        {
            // Arrange
            EnsureVisualStudioHost();
            var dte = VisualStudio.Dte;
            var solutionService = VisualStudio.Get<SolutionService>();

            solutionService.CreateEmptySolution();
            var project = solutionService.AddProject(ProjectLanguage.CSharp, ProjectTemplate.ClassLibrary, ProjectTargetFramework.V46, "TestProject");
            VisualStudio.ClearOutputWindow();

            // Act
            dte.ExecuteCommand("Project.ManageNuGetPackages");
            var nugetTestService = GetNuGetTestService();
            var uiwindow = nugetTestService.GetUIWindowfromProject(project);
            uiwindow.InstallPackageFromUI("newtonsoft.json", "9.0.1");

            // Assert
            CommonUtility.AssertPackageInPackagesConfig(VisualStudio, project, "newtonsoft.json", "9.0.1", XunitLogger);
        }

        [StaFact]
        public void InstallPackageToProjectsFromUI()
        {
            // Arrange
            EnsureVisualStudioHost();
            var dte = VisualStudio.Dte;
            var solutionService = VisualStudio.Get<SolutionService>();

            solutionService.CreateEmptySolution();
            var project = solutionService.AddProject(ProjectLanguage.CSharp, ProjectTemplate.ClassLibrary, ProjectTargetFramework.V46, "TestProject");
            var nuProject = solutionService.AddProject(ProjectLanguage.CSharp, ProjectTemplate.ClassLibrary, ProjectTargetFramework.V46, "NuProject");

            // Act
            dte.ExecuteCommand("Project.ManageNuGetPackages");
            var nugetTestService = GetNuGetTestService();
            var uiwindow = nugetTestService.GetUIWindowfromProject(nuProject);
            uiwindow.InstallPackageFromUI("newtonsoft.json", "9.0.1");
            VisualStudio.SelectProjectInSolutionExplorer(project.Name);
            dte.ExecuteCommand("Project.ManageNuGetPackages");

            VisualStudio.ClearOutputWindow();
            var uiwindow2 = nugetTestService.GetUIWindowfromProject(project);
            uiwindow2.InstallPackageFromUI("newtonsoft.json", "9.0.1");

            // Assert
            CommonUtility.AssertPackageInPackagesConfig(VisualStudio, project, "newtonsoft.json", "9.0.1", XunitLogger);
            CommonUtility.AssertPackageInPackagesConfig(VisualStudio, nuProject, "newtonsoft.json", "9.0.1", XunitLogger);
        }

        [StaFact]
        public void UninstallPackageFromUI()
        {
            // Arrange
            EnsureVisualStudioHost();
            var dte = VisualStudio.Dte;
            var solutionService = VisualStudio.Get<SolutionService>();

            solutionService.CreateEmptySolution();
            var project = solutionService.AddProject(ProjectLanguage.CSharp, ProjectTemplate.ClassLibrary, ProjectTargetFramework.V46, "TestProject");
            FileInfo packagesConfigFile = GetPackagesConfigFile(project);
            dte.ExecuteCommand("Project.ManageNuGetPackages");
            var nugetTestService = GetNuGetTestService();
            var uiwindow = nugetTestService.GetUIWindowfromProject(project);
            uiwindow.InstallPackageFromUI("newtonsoft.json", "9.0.1");

            WaitForFileExists(packagesConfigFile);

            VisualStudio.ClearWindows();

            // Act
            uiwindow.UninstallPackageFromUI("newtonsoft.json");

            WaitForFileNotExists(packagesConfigFile);

            // Assert
            CommonUtility.AssertPackageNotInPackagesConfig(VisualStudio, project, "newtonsoft.json", XunitLogger);
        }

        [StaFact(Skip = "https://github.com/NuGet/Home/issues/9701")]
        public void UpdatePackageFromUI()
        {
            // Arrange
            EnsureVisualStudioHost();
            var dte = VisualStudio.Dte;
            var solutionService = VisualStudio.Get<SolutionService>();

            solutionService.CreateEmptySolution();
            var project = solutionService.AddProject(ProjectLanguage.CSharp, ProjectTemplate.ClassLibrary, ProjectTargetFramework.V46, "TestProject");
            VisualStudio.ClearWindows();

            // Act
            dte.ExecuteCommand("Project.ManageNuGetPackages");
            var nugetTestService = GetNuGetTestService();
            var uiwindow = nugetTestService.GetUIWindowfromProject(project);
            uiwindow.InstallPackageFromUI("newtonsoft.json", "9.0.1");

            // Act
            VisualStudio.ClearWindows();
            uiwindow.UpdatePackageFromUI("newtonsoft.json", "10.0.3");

            // Assert
            CommonUtility.AssertPackageInPackagesConfig(VisualStudio, project, "newtonsoft.json", "10.0.3", XunitLogger);
        }

        private static FileInfo GetPackagesConfigFile(ProjectTestExtension project)
        {
            var projectFile = new FileInfo(project.FullPath);

            return new FileInfo(Path.Combine(projectFile.DirectoryName, "packages.config"));
        }

        private static void WaitForFileExists(FileInfo file)
        {
            Omni.Common.WaitFor.IsTrue(
                () => File.Exists(file.FullName),
                Timeout,
                Interval,
                $"{file.FullName} did not exist within {Timeout}.");
        }

        private static void WaitForFileNotExists(FileInfo file)
        {
            Omni.Common.WaitFor.IsTrue(
                () => !File.Exists(file.FullName),
                Timeout,
                Interval,
                $"{file.FullName} still existed after {Timeout}.");
        }
    }
}