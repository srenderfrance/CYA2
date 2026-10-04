using Cya2.Application.Diagnostics;
using Xunit;

namespace Cya2.Application.Tests;

public sealed class ImportDiagnosticMessagesTests
{
    [Fact]
    public void InvalidCellDoesNotIncludeCellValue()
    {
        var message = ImportDiagnosticMessages.InvalidCell(12, "Amount");

        Assert.Equal("Row 12: Amount is invalid.", message);
        Assert.DoesNotContain("1234.56", message);
    }

    [Fact]
    public void ImportFailureDoesNotIncludeExceptionDetails()
    {
        var message = ImportDiagnosticMessages.ImportFailed();

        Assert.Equal("Import failed. Please try again or contact support.", message);
        Assert.DoesNotContain("connection", message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ExistingValueMessageDoesNotIncludeSensitiveValue()
    {
        var message = ImportDiagnosticMessages.ExistingValue(4, "Account Number");

        Assert.Equal("Row 4: Account Number already exists.", message);
        Assert.DoesNotContain("account-123", message, StringComparison.OrdinalIgnoreCase);
    }
}
