namespace StreamOrchestra.App.Models;

public enum RecordingCompletion
{
    Completed,
    Stopped,
    Failed
}

public sealed record RecordingResult(RecordingCompletion Completion, int ExitCode, string Message);
