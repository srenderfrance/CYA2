namespace Cya2.Application.Diagnostics;

public static class ImportDiagnosticMessages
{
    public static string InvalidCell(int rowNumber, string fieldName)
        => $"Row {rowNumber}: {fieldName} is invalid.";

    public static string DuplicateValue(int rowNumber, string fieldName)
        => $"Row {rowNumber}: {fieldName} duplicates another imported value.";

    public static string ExistingValue(int rowNumber, string fieldName)
        => $"Row {rowNumber}: {fieldName} already exists.";

    public static string WorkbookReadFailure()
        => "Unable to read the workbook. The file may be invalid or inaccessible.";

    public static string ImportFailed()
        => "Import failed. Please try again or contact support.";

    public static string ExceptionCategory(Exception exception)
        => exception.GetType().Name;
}
