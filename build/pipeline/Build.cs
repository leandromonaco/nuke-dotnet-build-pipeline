using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using Nuke.Common;
using Nuke.Common.CI;
using Nuke.Common.Execution;
using Nuke.Common.Git;
using Nuke.Common.IO;
using Nuke.Common.ProjectModel;
using Nuke.Common.Tooling;
using Nuke.Common.Tools.DotNet;
using Nuke.Common.Tools.GitVersion;
using Nuke.Common.Tools.MSBuild;
using Nuke.Common.Tools.NerdbankGitVersioning;
using Nuke.Common.Tools.NuGet;
using Nuke.Common.Tools.Octopus;
using Nuke.Common.Utilities.Collections;
using static Nuke.Common.EnvironmentInfo;
using static Nuke.Common.IO.FileSystemTasks;
using static Nuke.Common.IO.PathConstruction;
using static Nuke.Common.Tools.DotNet.DotNetTasks;


[CheckBuildProjectConfigurations]
[ShutdownDotNetAfterServerBuild]
class Build : NukeBuild
{
    /// Support plugins are available for:
    ///   - JetBrains ReSharper        https://nuke.build/resharper
    ///   - JetBrains Rider            https://nuke.build/rider
    ///   - Microsoft VisualStudio     https://nuke.build/visualstudio
    ///   - Microsoft VSCode           https://nuke.build/vscode

    public static int Main() => Execute<Build>(x => x.Pack);

    [Parameter("Configuration to build - Default is 'Debug' (local) or 'Release' (server)")]
    //readonly Configuration Configuration = IsLocalBuild ? Configuration.Debug : Configuration.Release;
    readonly Configuration Configuration = Configuration.Release;

    [Solution] readonly Solution Solution;
    //[GitRepository] readonly GitRepository GitRepository;
    //[GitVersion(Framework = "netcoreapp3.1")] readonly GitVersion GitVersion;

    AbsolutePath SolutionDirectory => RootDirectory.Parent;
    AbsolutePath SourceDirectory => SolutionDirectory / "src";
    AbsolutePath TestsDirectory => SolutionDirectory / "test";
    AbsolutePath OutputDirectory => SolutionDirectory / "output";

    Target Clean => _ => _
        .Executes(() =>
        {
            SourceDirectory.GlobDirectories("**/bin", "**/obj").ForEach(DeleteDirectory);
            TestsDirectory.GlobDirectories("**/bin", "**/obj").ForEach(DeleteDirectory);
            EnsureCleanDirectory(OutputDirectory);
        });

    Target Restore => _ => _
        .DependsOn(Clean)
        .Executes(() =>
        {
            NuGetTasks.NuGetRestore(s => s
                .SetSolutionDirectory(SolutionDirectory));
        });

    Dictionary<string, string> versions = new Dictionary<string, string>();

    Target Versioning => _ => _
    .DependsOn(Restore)
          .Executes(() =>
          {

              var projectFiles = SourceDirectory.GlobFiles("**/*.csproj");
              foreach (var projectFile in projectFiles)
              {
                  FileInfo fileInfo = new FileInfo(projectFile);
                  var versionResult = NerdbankGitVersioningTasks.NerdbankGitVersioningGetVersion(v => v.SetProcessWorkingDirectory(projectFile.Parent).SetProcessArgumentConfigurator(a => a.Add("-f json"))).Result;
                  NerdbankGitVersioningTasks.NerdbankGitVersioningSetVersion(v => v.SetProject(projectFile)
                                                                                   .SetVersion(versionResult.Version));
                  versions.Add(fileInfo.Name, versionResult.Version);
              }

          });

    Target Compile => _ => _
    .DependsOn(Versioning)
    .Executes(() =>
    {
        MSBuildTasks.MSBuild(s => s
            .SetTargetPath(SolutionDirectory)
            .SetConfiguration(Configuration)
            );
    });

    Target Pack => _ => _
    .DependsOn(Compile)
    .Executes(() =>
    {
        var projectFiles = SourceDirectory.GlobFiles("**/*.csproj");
        foreach (var projectFile in projectFiles)
        {
            FileInfo fileInfo = new FileInfo(projectFile);
            var packageId = fileInfo.Name.Replace(".csproj", string.Empty);

            var sourcePath = $"{projectFile.Parent}\\bin";
            var targetPath = $"{OutputDirectory}\\{packageId}";

            //if it's a .NET Core Project, override source path
            var directories = Directory.GetDirectories(sourcePath, "*net*", SearchOption.AllDirectories);
            if (directories.Length > 0)
            {
                sourcePath = directories[directories.Length-1];
            }

            CopyDirectoryRecursively(sourcePath, targetPath);
                      

            OctopusTasks.OctopusPack(o => o.SetBasePath(targetPath)
                                           .SetOutputFolder(OutputDirectory)
                                           .SetId(packageId)
                                           .SetVersion(versions.GetValueOrDefault(fileInfo.Name)));

            DeleteDirectory(targetPath);

        }
       

    });

    //Target Test => _ => _
    //.DependsOn(Compile)
    //.Executes(() =>
    //{
    //    DotNetTest(s => s
    //        .SetProjectFile(Solution)
    //        .SetConfiguration(Configuration)
    //        .EnableNoRestore()
    //        .EnableNoBuild());
    //});

    //Target Pack => _ => _
    //    .DependsOn(Compile)
    //    .Executes(() =>
    //    {
    //        MSBuildTasks.p
    //    });
}
