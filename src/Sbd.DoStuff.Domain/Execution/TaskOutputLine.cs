namespace Sbd.DoStuff.Domain.Execution;

public enum OutputStream
{
    StandardOutput,
    StandardError,
    System,
}

public sealed record TaskOutputLine(DateTimeOffset Timestamp, OutputStream Stream, string Text);
