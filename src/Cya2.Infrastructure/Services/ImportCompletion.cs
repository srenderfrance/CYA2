using Cya2.Application.Interfaces;
using Cya2.Core.Interfaces;
using Microsoft.Extensions.Logging;

namespace Cya2.Infrastructure.Services;

internal static class ImportCompletion
{
    public static void Notify(ImportResult result, string progressId, CacheDataDomains domains,
        IImportCacheInvalidator invalidator, IImportProgressService progress, ILogger logger)
    {
        result.PublicationCommitted = true;
        try { invalidator.Invalidate(domains); }
        catch
        {
            result.Warnings.Add("The upload was committed, but cache refresh failed. Reload and verify the data before continuing; do not re-upload solely because of this warning.");
            logger.LogWarning("Committed import cache refresh failed.");
        }
        try
        {
            progress.CompleteStep(progressId, "Data Import", "Completed", $"{result.InsertedRows:N0} records imported");
            progress.SetStatus(progressId, result.Warnings.Count == 0 ? "Import committed" : string.Join(" ", result.Warnings));
            progress.Complete(progressId);
        }
        catch
        {
            result.Warnings.Add("The upload was committed, but progress reporting could not be completed. Verify the data before retrying.");
            logger.LogWarning("Committed import progress delivery failed.");
        }
    }
}
