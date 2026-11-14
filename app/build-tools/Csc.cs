extern alias OriginalCompiler;
using System;
using System.Diagnostics;
using System.IO;
using System.Text;

namespace Microsoft.CodeAnalysis.BuildTasks;

/// <summary>Runs the SDK compiler with inherited handles when sandboxed async pipes are unavailable.</summary>
public sealed class Csc : OriginalCompiler::Microsoft.CodeAnalysis.BuildTasks.Csc
{
    protected override int ExecuteTool(string pathToTool, string responseFileCommands, string commandLineCommands)
    {
        var responsePath = Path.Combine(Path.GetFullPath("obj"), "compiler-" + Guid.NewGuid().ToString("N") + ".rsp");
        Directory.CreateDirectory(Path.GetDirectoryName(responsePath)!);
        File.WriteAllText(responsePath, responseFileCommands, new UTF8Encoding(false));
        try
        {
            using var compiler = Process.Start(new ProcessStartInfo(pathToTool, commandLineCommands + " @\"" + responsePath + "\"")
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = false,
                RedirectStandardError = false,
                RedirectStandardInput = false
            });
            if (compiler == null) return -1;
            compiler.WaitForExit();
            return compiler.ExitCode;
        }
        finally { File.Delete(responsePath); }
    }
}
