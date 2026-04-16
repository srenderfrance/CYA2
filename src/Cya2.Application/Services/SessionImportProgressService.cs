using Cya2.Application.Interfaces;

namespace Cya2.Application.Services;

public class SessionImportProgressService : ISessionImportProgressService
{
    public string? ImportProgressId { get; private set; }

    public event Action<string>? ImportProgressIdChanged;

    public void SetImportProgressId(string id)
    {
        ImportProgressId = id;
        ImportProgressIdChanged?.Invoke(id);
    }
}
