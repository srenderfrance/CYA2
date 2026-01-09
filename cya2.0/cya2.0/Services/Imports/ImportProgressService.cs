using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;

namespace cya2.Services.Imports
{
    public sealed class ImportProgress
    {
        public int TotalRows { get; set; }
        public int InsertedRows { get; set; }
        public int FailedRows { get; set; }
        public List<string> Errors { get; } = new List<string>();
        public bool IsComplete { get; set; }
    }

    public sealed class ImportProgressService
    {
        private readonly ConcurrentDictionary<string, ImportProgress> _store = new();

        public void Start(string id)
        {
            var prog = new ImportProgress();
            _store[id] = prog;
        }

        public void Report(string id, int totalRows, int insertedRows, int failedRows)
        {
            if (!_store.TryGetValue(id, out var prog)) return;
            prog.TotalRows = totalRows;
            prog.InsertedRows = insertedRows;
            prog.FailedRows = failedRows;
        }

        public void AddErrors(string id, IEnumerable<string> errors)
        {
            if (!_store.TryGetValue(id, out var prog)) return;
            if (errors == null) return;
            lock (prog.Errors)
            {
                prog.Errors.AddRange(errors.Where(e => !string.IsNullOrEmpty(e)));
            }
        }

        public void Complete(string id)
        {
            if (_store.TryGetValue(id, out var prog)) prog.IsComplete = true;
        }

        public ImportProgress? Get(string id)
        {
            if (_store.TryGetValue(id, out var prog)) return prog;
            return null;
        }
    }
}
