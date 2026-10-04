using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using Cya2.Core.Interfaces;

namespace cya2.Services.Imports
{
    public sealed class ImportStep
    {
        public string Name { get; set; } = string.Empty;
        public string Status { get; set; } = string.Empty;
        public bool IsCompleted { get; set; }
        public bool IsActive { get; set; }
        public string Details { get; set; } = string.Empty;
    }

    public sealed class ImportProgress
    {
        internal DateTime CreatedAtUtc { get; set; }
        internal DateTime LastUpdatedAtUtc { get; set; }
        public int TotalRows { get; set; }
        public int InsertedRows { get; set; }
        public int FailedRows { get; set; }
        public int ExpectedRows { get; set; }
        public string Status { get; set; } = string.Empty;
        public List<string> Errors { get; } = new List<string>();
        public bool IsComplete { get; set; }
        public List<ImportStep> Steps { get; } = new List<ImportStep>();
        public string ImportType { get; set; } = string.Empty; // "Donations" or "Accounting"
    }

    public sealed class ImportProgressService : IImportProgressService
    {
        private readonly ConcurrentDictionary<string, ImportProgress> _store = new();

        public void Start(string id)
        {
            var now = DateTime.UtcNow;
            var prog = new ImportProgress { CreatedAtUtc = now, LastUpdatedAtUtc = now };
            _store[id] = prog;
        }

        public void Start(string id, int expectedRows)
        {
            var now = DateTime.UtcNow;
            var prog = new ImportProgress { ExpectedRows = expectedRows, CreatedAtUtc = now, LastUpdatedAtUtc = now };
            _store[id] = prog;
        }

        public void Start(string id, string importType)
        {
            var now = DateTime.UtcNow;
            var prog = new ImportProgress { ImportType = importType, CreatedAtUtc = now, LastUpdatedAtUtc = now };
            _store[id] = prog;
        }

        public void AddStep(string id, string stepName, string status = "Starting...")
        {
            if (!_store.TryGetValue(id, out var prog)) return;
            prog.LastUpdatedAtUtc = DateTime.UtcNow;

            // Mark previous step as inactive
            foreach (var step in prog.Steps)
            {
                step.IsActive = false;
            }

            // Add new step as active
            prog.Steps.Add(new ImportStep
            {
                Name = stepName,
                Status = status,
                IsActive = true,
                IsCompleted = false
            });
        }

        public void UpdateStep(string id, string stepName, string status, string? details = null)
        {
            if (!_store.TryGetValue(id, out var prog)) return;
            prog.LastUpdatedAtUtc = DateTime.UtcNow;

            var step = prog.Steps.FirstOrDefault(s => s.Name == stepName);
            if (step != null)
            {
                step.Status = status;
                if (details != null) step.Details = details;
            }
        }

        public void CompleteStep(string id, string stepName, string completionStatus, string? details = null)
        {
            if (!_store.TryGetValue(id, out var prog)) return;
            prog.LastUpdatedAtUtc = DateTime.UtcNow;

            var step = prog.Steps.FirstOrDefault(s => s.Name == stepName);
            if (step != null)
            {
                step.Status = completionStatus;
                step.IsCompleted = true;
                step.IsActive = false;
                if (details != null) step.Details = details;
            }
        }

        public void Report(string id, int totalRows, int insertedRows, int failedRows)
        {
            if (!_store.TryGetValue(id, out var prog)) return;
            prog.LastUpdatedAtUtc = DateTime.UtcNow;
            prog.TotalRows = totalRows;
            prog.InsertedRows = insertedRows;
            prog.FailedRows = failedRows;
        }

        public void Report(string id, int totalRows, int insertedRows, int failedRows, string? status)
        {
            if (!_store.TryGetValue(id, out var prog)) return;
            prog.LastUpdatedAtUtc = DateTime.UtcNow;
            prog.TotalRows = totalRows;
            prog.InsertedRows = insertedRows;
            prog.FailedRows = failedRows;
            if (!string.IsNullOrEmpty(status)) prog.Status = status;
        }

        public void SetExpected(string id, int expectedRows)
        {
            if (_store.TryGetValue(id, out var prog)) { prog.ExpectedRows = expectedRows; prog.LastUpdatedAtUtc = DateTime.UtcNow; }
        }

        public void SetStatus(string id, string status)
        {
            if (_store.TryGetValue(id, out var prog)) { prog.Status = status; prog.LastUpdatedAtUtc = DateTime.UtcNow; }
        }

        public void AddErrors(string id, IEnumerable<string> errors)
        {
            if (!_store.TryGetValue(id, out var prog)) return;
            prog.LastUpdatedAtUtc = DateTime.UtcNow;
            if (errors == null) return;
            lock (prog.Errors)
            {
                prog.Errors.AddRange(errors.Where(e => !string.IsNullOrEmpty(e)));
            }
        }

        public void Complete(string id)
        {
            if (_store.TryGetValue(id, out var prog))
            {
                prog.IsComplete = true;
                prog.LastUpdatedAtUtc = DateTime.UtcNow;
                if (string.IsNullOrEmpty(prog.Status)) prog.Status = "Complete";

                // Mark all steps as inactive
                foreach (var step in prog.Steps)
                {
                    step.IsActive = false;
                }
            }
        }

        public bool Remove(string id) => _store.TryRemove(id, out _);

        public int RemoveExpired(DateTime utcNow, TimeSpan lifetime)
        {
            var removed = 0;
            foreach (var entry in _store)
            {
                var age = utcNow - entry.Value.LastUpdatedAtUtc;
                if (!entry.Value.IsComplete && age < lifetime)
                {
                    continue;
                }

                if (age >= lifetime && _store.TryRemove(new KeyValuePair<string, ImportProgress>(entry.Key, entry.Value)))
                {
                    removed++;
                }
            }

            return removed;
        }

        public ImportProgress? Get(string id)
        {
            if (_store.TryGetValue(id, out var prog)) return prog;
            return null;
        }
    }
}
