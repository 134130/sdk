// Copyright (c) .NET Foundation and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Xml.Linq;
using FluentAssertions;
using Microsoft.DotNet.Cli.Utils;
using Microsoft.NET.TestFramework;
using Microsoft.NET.TestFramework.Assertions;
using Microsoft.NET.TestFramework.Commands;
using Microsoft.NET.TestFramework.ProjectConstruction;
using Xunit;
using Xunit.Abstractions;
using Xunit.Sdk;

namespace Microsoft.NET.Publish.Tests
{
    public class RuntimeIdentifiersTests : SdkTest
    {
        public RuntimeIdentifiersTests(ITestOutputHelper log) : base(log)
        {
        }

        //  Run on core MSBuild only as using a local packages folder hits long path issues on full MSBuild
        [CoreMSBuildOnlyFact]
        public void BuildWithRuntimeIdentifier()
        {
            var testProject = new TestProject()
            {
                Name = "BuildWithRid",
                TargetFrameworks = ToolsetInfo.CurrentTargetFramework,
                IsExe = true
            };

            var compatibleRid = EnvironmentInfo.GetCompatibleRid(testProject.TargetFrameworks);

            var runtimeIdentifiers = new[]
            {
                "win-x64",
                "linux-x64",
                compatibleRid
            };

            testProject.AdditionalProperties["RuntimeIdentifiers"] = string.Join(';', runtimeIdentifiers);

            //  Use a test-specific packages folder
            testProject.AdditionalProperties["RestorePackagesPath"] = @"$(MSBuildProjectDirectory)\..\pkg";

            var testAsset = _testAssetsManager.CreateTestProject(testProject);

            var restoreCommand = new RestoreCommand(testAsset);

            restoreCommand
                .Execute()
                .Should()
                .Pass();

            foreach (var runtimeIdentifier in runtimeIdentifiers)
            {
                var buildCommand = new BuildCommand(testAsset);

                buildCommand
                    .ExecuteWithoutRestore($"/p:RuntimeIdentifier={runtimeIdentifier}")
                    .Should()
                    .Pass();

                if (runtimeIdentifier == compatibleRid)
                {
                    var outputDirectory = buildCommand.GetOutputDirectory(testProject.TargetFrameworks, runtimeIdentifier: runtimeIdentifier);
                    var selfContainedExecutable = $"{testProject.Name}{Constants.ExeSuffix}";
                    string selfContainedExecutableFullPath = Path.Combine(outputDirectory.FullName, selfContainedExecutable);

                    new RunExeCommand(Log, selfContainedExecutableFullPath)
                        .Execute()
                        .Should()
                        .Pass()
                        .And
                        .HaveStdOutContaining("Hello World!");
                }
            }
        }

        [Fact]
        public void BuildWithUseCurrentRuntimeIdentifier()
        {
            var testProject = new TestProject()
            {
                Name = "BuildWithUseCurrentRuntimeIdentifier",
                TargetFrameworks = ToolsetInfo.CurrentTargetFramework,
                IsSdkProject = true,
                IsExe = true
            };

            testProject.AdditionalProperties["UseCurrentRuntimeIdentifier"] = "True";

            //  Use a test-specific packages folder
            testProject.AdditionalProperties["RestorePackagesPath"] = @"$(MSBuildProjectDirectory)\..\pkg";

            var testAsset = _testAssetsManager.CreateTestProject(testProject);
            var buildCommand = new BuildCommand(testAsset);

            buildCommand
                .Execute()
                .Should()
                .Pass();

            string targetFrameworkOutputDirectory = Path.Combine(buildCommand.GetNonSDKOutputDirectory().FullName, testProject.TargetFrameworks);
            string outputDirectoryWithRuntimeIdentifier = Directory.EnumerateDirectories(targetFrameworkOutputDirectory, "*", SearchOption.AllDirectories).FirstOrDefault();
            outputDirectoryWithRuntimeIdentifier.Should().NotBeNullOrWhiteSpace();

            var selfContainedExecutable = $"{testProject.Name}{Constants.ExeSuffix}";
            string selfContainedExecutableFullPath = Path.Combine(outputDirectoryWithRuntimeIdentifier, selfContainedExecutable);

            new RunExeCommand(Log, selfContainedExecutableFullPath)
                .Execute()
                .Should()
                .Pass()
                .And
                .HaveStdOutContaining("Hello World!");
        }

        //  Run on core MSBuild only as using a local packages folder hits long path issues on full MSBuild
        [CoreMSBuildOnlyTheory]
        [InlineData(false)]
        //  "No build" scenario doesn't currently work: https://github.com/dotnet/sdk/issues/2956
        //[InlineData(true)]
        public void PublishWithRuntimeIdentifier(bool publishNoBuild)
        {
            var testProject = new TestProject()
            {
                Name = "PublishWithRid",
                TargetFrameworks = ToolsetInfo.CurrentTargetFramework,
                IsExe = true
            };

            var compatibleRid = EnvironmentInfo.GetCompatibleRid(testProject.TargetFrameworks);

            var runtimeIdentifiers = new[]
            {
                "win-x64",
                "linux-x64",
                compatibleRid
            };

            testProject.AdditionalProperties["RuntimeIdentifiers"] = string.Join(';', runtimeIdentifiers);

            //  Use a test-specific packages folder
            testProject.AdditionalProperties["RestorePackagesPath"] = @"$(MSBuildProjectDirectory)\..\pkg";

            var testAsset = _testAssetsManager.CreateTestProject(testProject, identifier: publishNoBuild ? "nobuild" : string.Empty);

            var buildCommand = new BuildCommand(testAsset);

            buildCommand
                .Execute()
                .Should()
                .Pass();

            foreach (var runtimeIdentifier in runtimeIdentifiers)
            {
                var publishArgs = new List<string>()
                {
                    $"/p:RuntimeIdentifier={runtimeIdentifier}"
                };
                if (publishNoBuild)
                {
                    publishArgs.Add("/p:NoBuild=true");
                }

                var publishCommand = new PublishCommand(testAsset);
                publishCommand.Execute(publishArgs.ToArray())
                    .Should()
                    .Pass();

                if (runtimeIdentifier == compatibleRid)
                {
                    var outputDirectory = publishCommand.GetOutputDirectory(testProject.TargetFrameworks, runtimeIdentifier: runtimeIdentifier);
                    var selfContainedExecutable = $"{testProject.Name}{Constants.ExeSuffix}";
                    string selfContainedExecutableFullPath = Path.Combine(outputDirectory.FullName, selfContainedExecutable);

                    new RunExeCommand(Log, selfContainedExecutableFullPath)
                        .Execute()
                        .Should()
                        .Pass()
                        .And
                        .HaveStdOutContaining("Hello World!");

                }
            }
        }

        [Theory]
        [InlineData(false, false)] // publish rid overrides rid in project file if publishing
        [InlineData(true, false)] // publish rid doesnt override global rid
        [InlineData(true, true)] // publish rid doesnt override global rid, even if global
        public void PublishRuntimeIdentifierSetsRuntimeIdentifierAndDoesOrDoesntOverrideRID(bool runtimeIdentifierIsGlobal, bool publishRuntimeIdentifierIsGlobal)
        {
            string tfm = ToolsetInfo.CurrentTargetFramework;
            string publishRuntimeIdentifier = "win-x64";
            string runtimeIdentifier = "win-x86";

            var testProject = new TestProject()
            {
                IsExe = true,
                TargetFrameworks = tfm
            };
            if (!publishRuntimeIdentifierIsGlobal)
                testProject.AdditionalProperties["PublishRuntimeIdentifier"] = publishRuntimeIdentifier;
            if (!runtimeIdentifierIsGlobal)
                testProject.AdditionalProperties["RuntimeIdentifier"] = runtimeIdentifier;
            testProject.RecordProperties("RuntimeIdentifier");

            List<string> args = new List<string>
            {
                runtimeIdentifierIsGlobal ? $"/p:RuntimeIdentifier={runtimeIdentifier}" : "",
                publishRuntimeIdentifierIsGlobal ? $"/p:PublishRuntimeIdentifier={publishRuntimeIdentifier}" : ""
            };

            string identifier = $"PublishRuntimeIdentifierOverrides-{publishRuntimeIdentifierIsGlobal}-{runtimeIdentifierIsGlobal}";
            var testAsset = _testAssetsManager.CreateTestProject(testProject, identifier: identifier);
            var publishCommand = new DotnetPublishCommand(Log);
            publishCommand
                .WithWorkingDirectory(Path.Combine(testAsset.TestRoot, testProject.Name))
                .Execute(args.ToArray())
                .Should()
                .Pass();

            string expectedRid = runtimeIdentifierIsGlobal ? runtimeIdentifier : publishRuntimeIdentifier;
            var properties = testProject.GetPropertyValues(testAsset.TestRoot, configuration: "Release", targetFramework: tfm);
            var finalRid = properties["RuntimeIdentifier"];

            Assert.True(finalRid == expectedRid);
        }

        [WindowsOnlyFact]
        public void PublishRuntimeIdentifierOverridesUseCurrentRuntime()
        {
            string tfm = ToolsetInfo.CurrentTargetFramework;
            string publishRid = "linux-x64"; // linux is arbitrarily picked; just because it is different than a windows RID.
            var testProject = new TestProject()
            {
                IsExe = true,
                TargetFrameworks = tfm
            };

            testProject.AdditionalProperties["UseCurrentRuntimeIdentifier"] = "true";
            testProject.AdditionalProperties["PublishRuntimeIdentifier"] = publishRid;
            testProject.RecordProperties("RuntimeIdentifier");
            testProject.RecordProperties("NETCoreSdkPortableRuntimeIdentifier");

            var testAsset = _testAssetsManager.CreateTestProject(testProject);
            var publishCommand = new DotnetPublishCommand(Log);
            publishCommand
                .WithWorkingDirectory(Path.Combine(testAsset.TestRoot, MethodBase.GetCurrentMethod().Name))
                .Execute()
                .Should()
                .Pass();

            var properties = testProject.GetPropertyValues(testAsset.TestRoot, configuration: "Release", targetFramework: tfm);
            var finalRid = properties["RuntimeIdentifier"];
            var ucrRid = properties["NETCoreSdkPortableRuntimeIdentifier"];

            Assert.True(finalRid == publishRid);
            Assert.True(ucrRid != finalRid);
        }

        [Fact]
        public void ImplicitRuntimeIdentifierOptOutCorrectlyOptsOut()
        {
            var targetFramework = ToolsetInfo.CurrentTargetFramework;
            var runtimeIdentifier = EnvironmentInfo.GetCompatibleRid(targetFramework);
            var testProject = new TestProject()
            {
                IsExe = true,
                TargetFrameworks = targetFramework
            };
            testProject.AdditionalProperties["SelfContained"] = "true";
            testProject.AdditionalProperties["UseCurrentRuntimeIdentifier"] = "false";

            var testAsset = _testAssetsManager.CreateTestProject(testProject);

            var publishCommand = new DotnetPublishCommand(Log, Path.Combine(testAsset.TestRoot, testProject.Name));
            publishCommand
                .Execute()
                .Should()
                .Fail()
                .And
                .HaveStdOutContaining("NETSDK1191");
        }

        [Fact]
        public void DuplicateRuntimeIdentifiers()
        {
            var testProject = new TestProject()
            {
                Name = "DuplicateRuntimeIdentifiers",
                TargetFrameworks = ToolsetInfo.CurrentTargetFramework,
                IsExe = true
            };

            var compatibleRid = EnvironmentInfo.GetCompatibleRid(testProject.TargetFrameworks);

            testProject.AdditionalProperties["RuntimeIdentifiers"] = compatibleRid + ";" + compatibleRid;
            testProject.RuntimeIdentifier = compatibleRid;

            var testAsset = _testAssetsManager.CreateTestProject(testProject);

            var buildCommand = new BuildCommand(testAsset);

            buildCommand
                .Execute()
                .Should()
                .Pass();

        }

        [Fact]
        public void PublishSuccessfullyWithRIDRequiringPropertyAndRuntimeIdentifiersNoRuntimeIdentifier()
        {
            var targetFramework = ToolsetInfo.CurrentTargetFramework;
            var runtimeIdentifier = EnvironmentInfo.GetCompatibleRid(targetFramework);
            var testProject = new TestProject()
            {
                IsExe = true,
                TargetFrameworks = targetFramework
            };

            testProject.AdditionalProperties["RuntimeIdentifiers"] = runtimeIdentifier;
            testProject.AdditionalProperties["PublishReadyToRun"] = "true";
            var testAsset = _testAssetsManager.CreateTestProject(testProject);

            var publishCommand = new DotnetPublishCommand(Log, Path.Combine(testAsset.TestRoot, testProject.Name));
            publishCommand
                .Execute()
                .Should()
                .Pass();
        }
    }
}
