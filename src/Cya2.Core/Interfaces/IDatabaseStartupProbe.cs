namespace Cya2.Core.Interfaces;

/// <summary>
/// Performs the bounded startup connectivity check used to decide whether
/// database-backed operations may be enabled.
/// </summary>
public interface IDatabaseStartupProbe
{
    bool Check(CancellationToken cancellationToken = default);
}
