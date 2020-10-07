using System;
using System.IO;
using System.Threading.Tasks;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using CliWrap;
using CliWrap.Buffered;
using Microsoft.Build.Evaluation;

namespace msbuild_issue_repro
{
    class Program
    {
        static async Task Main(string[] args)
        {
            await SetMsBuildExeVariableAsync().ConfigureAwait(false);

            var slnPath = Path.GetFullPath(Path.Combine(Path.GetDirectoryName(typeof(Program).Assembly.Location)!, "..", ".."))!;
            Environment.CurrentDirectory = slnPath;
            var projectPath = Path.Combine(slnPath!, "issue1","msbuild_issue_repro.csproj");
            if (!File.Exists(projectPath))
                throw new FileNotFoundException($"Can't find msbuild_issue_repro.csproj check path: {projectPath}");

            var projectCollection = new ProjectCollection();
            // issue one
            var prj = new Project(projectPath, new Dictionary<string, string>(), projectCollection.DefaultToolsVersion, projectCollection);

            var dotnet = TryFindDotNetExePath() ?? throw new FileNotFoundException("Can't found dotnet executable, try to set DOTNET_ROOT", "dotnet");
            // another issue about returned exit code (should be 0, but `dotnet restore` returns 1)
            var cmd = await (Cli.Wrap(dotnet)
                .WithArguments("restore")
                .WithValidation(CommandResultValidation.ZeroExitCode)
                | (_stdout, _stderr))
                .ExecuteAsync().Task.ConfigureAwait(false);

            Console.WriteLine($"Done. Result: {cmd.ExitCode}");
        }

        private static readonly Stream _stdout = Console.OpenStandardOutput();
        private static readonly Stream _stderr = Console.OpenStandardError();


        public static async Task SetMsBuildExeVariableAsync()
        {
            using var cts = new CancellationTokenSource(millisecondsDelay: 7_000);
            try
            {
                var dotnet = TryFindDotNetExePath() ?? throw new FileNotFoundException("Can't found dotnet executable, try to set DOTNET_ROOT", "dotnet");
                var info = await Cli.Wrap(dotnet).WithArguments("--info").ExecuteBufferedAsync(cts.Token).Task.ConfigureAwait(false);
                if (info.StandardOutput == null)
                {
                    throw new Exception("dotnet --info doesn't return base path for msbuild dll");
                }
                var path = info.StandardOutput.Split('\n', StringSplitOptions.RemoveEmptyEntries)
                    .Select(x => x.Trim())
                    .Single(x => x.StartsWith("Base Path:", StringComparison.Ordinal))
                    .Substring("Base Path:".Length).Trim();

                Environment.SetEnvironmentVariable("MSBUILD_EXE_PATH", Path.Combine(path, "MSBuild.dll"));
                Environment.SetEnvironmentVariable("MSBuildExtensionsPath", path);
                Environment.SetEnvironmentVariable("MSBuildSDKsPath", Path.Combine(path, "Sdks"));
            }
            catch (Exception ex)
            {
                throw new Exception("Can't find MSBuild.dll path.", ex);
            }
        }
        static string? TryFindDotNetExePath()
        {
            var dotnet = "dotnet";
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
                dotnet += ".exe";

            var mainModule = System.Diagnostics.Process.GetCurrentProcess().MainModule;
            if (!string.IsNullOrEmpty(mainModule?.FileName) && Path.GetFileName(mainModule.FileName)!.Equals(dotnet, StringComparison.OrdinalIgnoreCase))
                return mainModule.FileName;

            var environmentVariable = Environment.GetEnvironmentVariable("DOTNET_ROOT");
            if (!string.IsNullOrEmpty(environmentVariable))
                return Path.Combine(environmentVariable, dotnet);

            var paths = Environment.GetEnvironmentVariable("PATH");
            if (paths == null)
                return null;

            foreach (var path in paths.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
            {
                var fullPath = Path.Combine(path, dotnet);
                if (File.Exists(fullPath))
                    return fullPath;
            }
            return null;
        }
    }
}

