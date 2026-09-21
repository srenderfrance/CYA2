using System.Threading.Channels;

namespace Cya2.Application.Interfaces;

public sealed record ImportWorkItem(
    string ImportType,
    string PreviewId,
    string ProgressId,
    byte[] Data);

public interface IImportWorkQueue
{
    ValueTask EnqueueAsync(ImportWorkItem workItem, CancellationToken cancellationToken = default);
}

