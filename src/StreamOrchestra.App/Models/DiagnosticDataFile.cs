namespace StreamOrchestra.App.Models;

public sealed record DiagnosticDataFile(
    string Name,
    string Path,
    bool Exists,
    long SizeBytes);
