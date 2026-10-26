using System;
using System.Text.Json.Serialization;

namespace TaskFolder.Models
{
    public sealed class ProfileState
    {
        public static readonly Guid DefaultId = new Guid("dac091ee-6e60-4720-8dcc-ff607125c2d0");
        [JsonRequired] public Guid Id { get; set; }
        [JsonRequired] public string Name { get; set; }
        [JsonRequired] public string RelativeRoot { get; set; }
        public bool Suppressed { get; set; }
    }
}
