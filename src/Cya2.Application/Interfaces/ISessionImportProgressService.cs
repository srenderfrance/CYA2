namespace Cya2.Application.Interfaces;

public interface ISessionImportProgressService
{
    string? ImportProgressId { get; }
    event Action<string>? ImportProgressIdChanged;
    void SetImportProgressId(string id);
}
