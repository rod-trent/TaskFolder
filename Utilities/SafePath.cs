using System;
using System.IO;
using System.Linq;

namespace TaskFolder.Utilities
{
    internal static class SafePath
    {
        internal static string Resolve(string root, string relative)
        {
            if (string.IsNullOrWhiteSpace(relative) || Path.IsPathRooted(relative))
                throw new InvalidDataException("A managed path must be relative and nonempty.");
            string[] parts = relative.Replace('/', '\\').Split('\\');
            foreach (string part in parts)
            {
                string device = part.Split('.')[0];
                bool reserved = new[] { "CON", "PRN", "AUX", "NUL" }.Contains(device, StringComparer.OrdinalIgnoreCase)
                    || (device.Length == 4 && (device.StartsWith("COM", StringComparison.OrdinalIgnoreCase)
                        || device.StartsWith("LPT", StringComparison.OrdinalIgnoreCase)) && "123456789¹²³".Contains(device[3]));
                if (part.Length == 0 || part == "." || part == ".." || part.EndsWith(' ') || part.EndsWith('.')
                    || part.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 || reserved)
                    throw new InvalidDataException("Unsafe managed path component.");
            }
            string fullRoot = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
            string full = Path.GetFullPath(Path.Combine(fullRoot, Path.Combine(parts)));
            if (!full.StartsWith(fullRoot, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("Managed path escapes its root.");
            // Check all existing ancestors too: a junction at the data/profile root is unsafe.
            string current = full;
            while (!string.IsNullOrEmpty(current))
            {
                try
                {
                    if ((File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0)
                        throw new InvalidDataException("Managed paths cannot traverse reparse points.");
                }
                catch (FileNotFoundException) { }
                catch (DirectoryNotFoundException) { }
                current = Path.GetDirectoryName(current);
            }
            return full;
        }
    }
}
