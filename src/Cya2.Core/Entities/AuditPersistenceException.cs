namespace Cya2.Core.Entities;

public sealed class AuditPersistenceException : Exception
{
    public AuditPersistenceException() : base("The required audit record could not be saved.") { }
}
