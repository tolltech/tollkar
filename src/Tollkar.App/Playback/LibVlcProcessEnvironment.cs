using System.Diagnostics;
using System.Reflection;
using System.Runtime.InteropServices;

namespace Tollkar.App.Playback;

internal static class LibVlcProcessEnvironment
{
    internal const string RestartMarker = "TOLLKAR_LIBVLC_RESTARTED";

    public static bool TryRestart(string[] arguments)
    {
        if (!OperatingSystem.IsMacOS() ||
            Environment.GetEnvironmentVariable(RestartMarker) == "1")
        {
            return false;
        }

        var runtime = FindLoadableRuntime();
        if (runtime is null || IsLibraryPathConfigured(runtime.LibraryDirectory)) return false;

        var processPath = Environment.ProcessPath;
        if (string.IsNullOrWhiteSpace(processPath)) return false;
        var startInfo = CreateStartInfo(
            processPath,
            Assembly.GetEntryAssembly()?.Location,
            arguments,
            runtime);
        try
        {
            using var process = Process.Start(startInfo);
            return process is not null;
        }
        catch (Exception exception) when (exception is InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            return false;
        }
    }

    internal static ProcessStartInfo CreateStartInfo(
        string processPath,
        string? entryAssemblyPath,
        IEnumerable<string> arguments,
        LibVlcRuntime runtime)
    {
        var startInfo = new ProcessStartInfo(processPath) { UseShellExecute = false };
        if (Path.GetFileNameWithoutExtension(processPath).Equals(
            "dotnet",
            StringComparison.OrdinalIgnoreCase))
        {
            if (string.IsNullOrWhiteSpace(entryAssemblyPath))
            {
                throw new ArgumentException(
                    "Entry assembly path is required for the dotnet host.",
                    nameof(entryAssemblyPath));
            }
            startInfo.ArgumentList.Add(entryAssemblyPath);
        }
        foreach (var argument in arguments) startInfo.ArgumentList.Add(argument);
        startInfo.Environment["DYLD_LIBRARY_PATH"] = runtime.LibraryDirectory;
        startInfo.Environment["VLC_PLUGIN_PATH"] = runtime.PluginsDirectory;
        startInfo.Environment[RestartMarker] = "1";
        return startInfo;
    }

    private static LibVlcRuntime? FindLoadableRuntime()
    {
        foreach (var runtime in LibVlcRuntimeLocator.FindAll())
        {
            try
            {
                var (vlcHandle, coreHandle) = LibVlcNativeLibrary.Load(runtime);
                NativeLibrary.Free(vlcHandle);
                NativeLibrary.Free(coreHandle);
                return runtime;
            }
            catch (Exception exception) when (
                exception is DllNotFoundException or BadImageFormatException)
            {
            }
        }
        return null;
    }

    private static bool IsLibraryPathConfigured(string libraryDirectory) =>
        (Environment.GetEnvironmentVariable("DYLD_LIBRARY_PATH") ?? string.Empty)
        .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries)
        .Contains(libraryDirectory, StringComparer.Ordinal);
}
