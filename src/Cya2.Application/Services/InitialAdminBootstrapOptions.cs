namespace Cya2.Application.Services;

public sealed class InitialAdminBootstrapOptions
{
    public const string SectionName = "InitialAdminBootstrap";

    public string Email { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;

    public bool IsEnabled =>
        !string.IsNullOrWhiteSpace(Email) &&
        !string.IsNullOrWhiteSpace(Name);
}
