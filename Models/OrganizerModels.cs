using System;
using System.Collections.Generic;

namespace TaskFolder.Models
{
    internal sealed class OrganizerRow
    {
        public Guid Id { get; set; }
        public Guid ProfileId { get; set; }
        public string Name { get; set; }
        public string RelativePath { get; set; }
        public bool IsFolder { get; set; }
        public ShortcutKind? Kind { get; set; }
        public ShortcutStatus Status { get; set; }
        public bool IsPinned { get; set; }
    }
    internal sealed class OrganizerPreview
    {
        public OperationRequest Request { get; set; }
        public OperationResult Result { get; set; }
        public string[] Items { get; set; } = Array.Empty<string>();
    }
    internal sealed class OrganizerDragSelection
    {
        public string Token { get; set; }
        public long Revision { get; set; }
        public Guid ProfileId { get; set; }
        public Guid[] Ids { get; set; }
    }
    internal sealed class AliasDraft
    {
        public Guid Id { get; set; }
        public string[] Baseline { get; set; }
        public string Text { get; set; }
        public bool Dirty { get; set; }
    }
}
