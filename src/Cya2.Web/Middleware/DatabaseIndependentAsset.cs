namespace cya2.Middleware;

public sealed class DatabaseIndependentAsset
{
    public static DatabaseIndependentAsset Instance { get; } = new();

    private DatabaseIndependentAsset() { }
}
