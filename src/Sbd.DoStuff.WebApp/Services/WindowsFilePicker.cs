using System.Diagnostics;
using System.Text;

namespace Sbd.DoStuff.WebApp.Services;

/// <summary>
/// Shows the standard Windows Open File dialog by running it in a short-lived Windows PowerShell
/// process (Windows Forms needs an STA thread, and this avoids a WinForms dependency in the web app).
/// </summary>
public sealed class WindowsFilePicker : IFilePicker
{
    // The current path travels in an environment variable, not the script text, so no quoting is needed.
    private const string Script = """
        Add-Type -AssemblyName System.Windows.Forms
        [Console]::OutputEncoding = [System.Text.Encoding]::UTF8
        $owner = New-Object System.Windows.Forms.Form
        $owner.TopMost = $true
        $dialog = New-Object System.Windows.Forms.OpenFileDialog
        $dialog.Filter = 'All files (*.*)|*.*'
        $dialog.CheckFileExists = $true
        $current = $env:SBD_PICK_FILE_CURRENT
        if ($current) {
            $directory = Split-Path -Path $current -Parent -ErrorAction SilentlyContinue
            if ($directory -and (Test-Path -LiteralPath $directory -PathType Container)) {
                $dialog.InitialDirectory = $directory
                $dialog.FileName = Split-Path -Path $current -Leaf
            }
        }
        if ($dialog.ShowDialog($owner) -eq [System.Windows.Forms.DialogResult]::OK) {
            [Console]::Out.Write($dialog.FileName)
        }
        """;

    public bool IsSupported => OperatingSystem.IsWindows();

    public async Task<string?> PickFileAsync(string? currentPath, CancellationToken cancellationToken = default)
    {
        if (!IsSupported)
        {
            throw new PlatformNotSupportedException("Browsing for a file is only supported on Windows.");
        }

        var startInfo = new ProcessStartInfo("powershell.exe")
        {
            RedirectStandardOutput = true,
            StandardOutputEncoding = Encoding.UTF8,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        startInfo.ArgumentList.Add("-STA");
        startInfo.ArgumentList.Add("-NoProfile");
        startInfo.ArgumentList.Add("-NonInteractive");
        startInfo.ArgumentList.Add("-ExecutionPolicy");
        startInfo.ArgumentList.Add("Bypass");
        startInfo.ArgumentList.Add("-EncodedCommand");
        startInfo.ArgumentList.Add(Convert.ToBase64String(Encoding.Unicode.GetBytes(Script)));
        startInfo.Environment["SBD_PICK_FILE_CURRENT"] = currentPath ?? string.Empty;

        using var process = Process.Start(startInfo)
            ?? throw new InvalidOperationException("Could not start PowerShell to show the file dialog.");
        try
        {
            var output = await process.StandardOutput.ReadToEndAsync(cancellationToken);
            await process.WaitForExitAsync(cancellationToken);
            var path = output.Trim();
            return path.Length == 0 ? null : path;
        }
        catch (OperationCanceledException)
        {
            process.Kill(entireProcessTree: true);
            throw;
        }
    }
}
