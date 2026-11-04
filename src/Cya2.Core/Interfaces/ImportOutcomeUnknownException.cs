namespace Cya2.Core.Interfaces;

public sealed class ImportOutcomeUnknownException : Exception
{
    public ImportOutcomeUnknownException() : base("The import outcome could not be confirmed. Refresh the data and reconcile the latest snapshot before retrying.") { }
}
