using System;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;

namespace TaskFolder.Utilities
{
    internal static class ShortcutDefinitionEditor
    {
        internal static byte[] CreateLink(string dataRoot, string targetPath, string displayName)
        {
            RequireSta();
            string staging = StagingPath(dataRoot);
            object shell = Activator.CreateInstance(Type.GetTypeFromProgID("WScript.Shell"));
            object shortcut = null;
            try
            {
                dynamic factory = shell;
                shortcut = factory.CreateShortcut(staging);
                dynamic link = shortcut;
                link.TargetPath = targetPath;
                link.WorkingDirectory = Path.GetDirectoryName(targetPath);
                link.Description = $"Launch {displayName}";
                link.Save();
                return File.ReadAllBytes(staging);
            }
            finally
            {
                if (shortcut != null) Marshal.FinalReleaseComObject(shortcut);
                Marshal.FinalReleaseComObject(shell);
            }
        }

        internal static byte[] RetargetLink(string dataRoot, byte[] original, string targetPath)
        {
            RequireSta();
            string sourcePath = StagingPath(dataRoot);
            using (var input = new FileStream(sourcePath, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            { input.Write(original); input.Flush(true); }
            string staging = StagingPath(dataRoot);
            WindowsShellLink.RetargetCopy(sourcePath, staging, targetPath);
            return File.ReadAllBytes(staging);
        }

        internal static byte[] RetargetUrl(byte[] original, string targetUrl)
        {
            if (string.IsNullOrEmpty(targetUrl) || targetUrl.IndexOfAny(new[] { '\r', '\n', '\0' }) >= 0)
                throw new ArgumentException("The URL must fit on one line.", nameof(targetUrl));
            if (!Uri.TryCreate(targetUrl, UriKind.Absolute, out var uri) || (uri.Scheme != "https" && uri.Scheme != "http"))
                throw new ArgumentException("Enter an absolute HTTP or HTTPS URL.", nameof(targetUrl));
            Encoding encoding = new UTF8Encoding(false, true);
            int prefix = 0;
            if (original.Length >= 2 && original[0] == 255 && original[1] == 254)
            { encoding = Encoding.Unicode; prefix = 2; }
            else if (original.Length >= 2 && original[0] == 254 && original[1] == 255)
            { encoding = Encoding.BigEndianUnicode; prefix = 2; }
            else if (original.Length >= 3 && original[0] == 239 && original[1] == 187 && original[2] == 191)
            { encoding = new UTF8Encoding(true, true); prefix = 3; }
            string text = encoding.GetString(original, prefix, original.Length - prefix);
            string newline = text.Contains("\r\n") ? "\r\n" : "\n";
            var lines = text.Replace("\r\n", "\n").Split('\n').ToList();
            int section = lines.FindIndex(line => line.Trim().Equals("[InternetShortcut]", StringComparison.OrdinalIgnoreCase));
            if (section < 0) throw new InvalidDataException("The file has no InternetShortcut section.");
            int end = lines.FindIndex(section + 1, line => line.TrimStart().StartsWith('['));
            if (end < 0) end = lines.Count;
            var urls = Enumerable.Range(section + 1, end - section - 1)
                .Where(i => lines[i].TrimStart().StartsWith("URL=", StringComparison.OrdinalIgnoreCase)).ToArray();
            if (urls.Length > 1) throw new InvalidDataException("The file has ambiguous URL fields.");
            if (urls.Length == 1) lines[urls[0]] = "URL=" + targetUrl;
            else lines.Insert(section + 1, "URL=" + targetUrl);
            byte[] body = encoding.GetBytes(string.Join(newline, lines));
            byte[] preamble = prefix == 0 ? Array.Empty<byte>() : encoding.GetPreamble();
            return preamble.Concat(body).ToArray();
        }

        private static string StagingPath(string dataRoot)
        {
            string relative = Path.Combine("Staging", Guid.NewGuid().ToString("N") + ".lnk");
            string path = SafePath.Resolve(dataRoot, relative);
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            return path;
        }

        private static void RequireSta()
        {
            if (Thread.CurrentThread.GetApartmentState() != ApartmentState.STA)
                throw new InvalidOperationException("Shell shortcuts must be prepared on the STA UI thread.");
        }
    }
}
