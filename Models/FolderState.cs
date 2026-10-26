using System;
using System.Text.Json.Serialization;

namespace TaskFolder.Models
{
    public sealed class FolderState
    {
        [JsonRequired] public Guid Id { get; set; }
        [JsonRequired] public Guid ProfileId { get; set; }
        [JsonRequired] public string RelativePath { get; set; }
        public int SortOrder { get; set; } = int.MaxValue;
        public bool Suppressed { get; set; }
    }
}
