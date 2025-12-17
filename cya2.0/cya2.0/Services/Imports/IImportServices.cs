using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace cya2.Services.Imports
{
    internal interface IAccountingImportService
    {
        Task<ImportResult> ImportAsync(Stream file, CancellationToken ct);
    }

    internal interface IDonationImportService
    {
        Task<ImportResult> ImportAsync(Stream file, CancellationToken ct);
    }
}
