namespace Cya2.Application.Services;

public sealed class ImportAuthorizationException : Exception
{
    public ImportAuthorizationException()
        : base("The import is no longer available.")
    {
    }
}