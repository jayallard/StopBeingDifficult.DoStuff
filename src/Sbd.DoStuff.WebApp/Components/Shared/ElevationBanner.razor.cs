namespace Sbd.DoStuff.WebApp.Components.Shared;

public partial class ElevationBanner
{
    // Elevation is fixed for the life of the process. On Windows this is an elevated token; on Unix, root.
    private static readonly bool IsElevated = Environment.IsPrivilegedProcess;
}
