using System.Collections;
using System.Diagnostics;
using Microsoft.Win32;

namespace Sbd.DoStuff.WebApp.Services;

/// <summary>
/// Restarts the app by launching a fresh copy of this process and stopping the current one.
/// On Windows the new process gets its environment re-read from the registry, so variables
/// changed since this process started (user or machine) are picked up.
/// </summary>
public sealed class AppRestarter(IHostApplicationLifetime lifetime)
{
    /// <summary>Set on the new process so it waits for the old one to release the listening port.</summary>
    public const string ParentPidVariable = "DOSTUFF_RESTART_PARENT_PID";

    private const string MachineEnvironmentKey = @"SYSTEM\CurrentControlSet\Control\Session Manager\Environment";
    private const string UserEnvironmentKey = "Environment";

    public void Restart()
    {
        var processPath = Environment.ProcessPath ?? throw new InvalidOperationException("The process path is unknown.");
        var startInfo = new ProcessStartInfo(processPath) { UseShellExecute = false };

        // Under the `dotnet` host, argument 0 is the app dll; for an apphost exe it is the exe itself.
        var args = Environment.GetCommandLineArgs();
        var hostedByDotnet = Path.GetFileNameWithoutExtension(processPath).Equals("dotnet", StringComparison.OrdinalIgnoreCase);
        foreach (var arg in hostedByDotnet ? args : args.Skip(1))
        {
            startInfo.ArgumentList.Add(arg);
        }

        if (OperatingSystem.IsWindows())
        {
            RefreshEnvironment(startInfo);
        }

        startInfo.Environment[ParentPidVariable] = Environment.ProcessId.ToString();
        Process.Start(startInfo);
        lifetime.StopApplication();
    }

    /// <summary>Blocks until the process that requested this restart has exited (no-op on a normal start).</summary>
    public static void WaitForPreviousInstance()
    {
        var value = Environment.GetEnvironmentVariable(ParentPidVariable);
        if (value is null)
        {
            return;
        }

        // Don't leak the marker into child processes or a later restart's bookkeeping.
        Environment.SetEnvironmentVariable(ParentPidVariable, null);

        if (!int.TryParse(value, out var pid))
        {
            return;
        }

        try
        {
            using var parent = Process.GetProcessById(pid);
            parent.WaitForExit(TimeSpan.FromSeconds(30));
        }
        catch (ArgumentException)
        {
            // Already gone.
        }
    }

    private static void RefreshEnvironment(ProcessStartInfo startInfo)
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        var environment = startInfo.Environment;
        string? machinePath = null;
        string? userPath = null;

        Overlay(Registry.LocalMachine, MachineEnvironmentKey, out machinePath);
        Overlay(Registry.CurrentUser, UserEnvironmentKey, out userPath);

        // PATH is the concatenation of machine and user values rather than one overriding the other.
        var paths = new[] { machinePath, userPath }.Where(p => !string.IsNullOrEmpty(p)).ToList();
        if (paths.Count > 0)
        {
            environment["PATH"] = string.Join(';', paths);
        }

        void Overlay(RegistryKey hive, string subKey, out string? path)
        {
            path = null;
            using var key = hive.OpenSubKey(subKey);
            if (key is null)
            {
                return;
            }

            foreach (var name in key.GetValueNames())
            {
                if (key.GetValue(name) is not string value)
                {
                    continue;
                }

                if (name.Equals("PATH", StringComparison.OrdinalIgnoreCase))
                {
                    path = value;
                }
                else
                {
                    environment[name] = value;
                }
            }
        }
    }
}
