namespace Cya2.Core.Interfaces;

/// <summary>
/// Tracks real-time progress for background import operations.
/// Implemented by the concrete ImportProgressService in the host project.
/// </summary>
public interface IImportProgressService
{
    void Start(string id, string importType);
    void AddStep(string id, string stepName, string status = "Starting...");
    void UpdateStep(string id, string stepName, string status, string? details = null);
    void CompleteStep(string id, string stepName, string completionStatus, string? details = null);
    void Report(string id, int totalRows, int insertedRows, int failedRows, string? status = null);
    void SetExpected(string id, int expectedRows);
    void SetStatus(string id, string status);
    void AddErrors(string id, IEnumerable<string> errors);
    void Complete(string id);
}
