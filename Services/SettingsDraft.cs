using System;
using System.Text.Json;
using TaskFolder.Models;

namespace TaskFolder.Services
{
    /// <summary>An editor baseline survives catalog refreshes; metadata-only changes don't invalidate it.</summary>
    internal sealed class SettingsDraft
    {
        private readonly string _baseline;
        internal SettingsDraft(AppSettings settings) => _baseline = JsonSerializer.Serialize(settings ?? throw new ArgumentNullException(nameof(settings)));
        internal bool IsCurrent(AppSettings settings) => _baseline == JsonSerializer.Serialize(settings);
    }
}
