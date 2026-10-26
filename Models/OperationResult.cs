using L = TaskFolder.Services.LocalizationService;
using System;
using System.Collections.Generic;

namespace TaskFolder.Models
{
    public enum OperationStatus
    {
        Success, Cancelled, ValidationError, Conflict, AccessDenied,
        StorageUnavailable, IoFailure, UnsupportedSchema, RecoveryRequired
    }

    public sealed class OperationResult
    {
        public OperationStatus Status { get; set; }
        public bool Success => Status == OperationStatus.Success;
        public Guid OperationId { get; set; }
        public long? NewRevision { get; set; }
        public string Code { get; set; }
        public List<string> ItemErrors { get; set; } = new List<string>();
        public string UndoToken { get; set; }

        public static OperationResult Fail(OperationStatus status, string code) =>
            new OperationResult { Status = status, Code = code };
    }

    public sealed class StatePersistenceException : Exception
    {
        public OperationResult Result { get; }
        public StatePersistenceException(OperationResult result)
            : base(Describe(result))
        { Result = result; }

        private static string Describe(OperationResult result) => result.Code == "migration.required"
            ? L.Text("Preview and approve legacy data migration before changing shortcuts. The legacy files have not been modified.")
            : result.Status switch
        {
            OperationStatus.Conflict => L.Text("Saved data changed. Reload the latest data before retrying; your pending changes have not been saved."),
            OperationStatus.UnsupportedSchema => L.Text("This saved data needs a compatible TaskFolder version. It has not been overwritten."),
            OperationStatus.RecoveryRequired => L.Text("Saved data needs recovery. The available last-good data is read-only; the original files have been preserved."),
            OperationStatus.AccessDenied => L.Text("TaskFolder could not access its data. Check access permissions and reload before retrying."),
            _ => L.Text("TaskFolder data could not be saved or verified ({0}). Check disk space and file locks, then reload before retrying.", result.Code)
        };
    }
}
