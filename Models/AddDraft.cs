using System;
using System.Collections.Generic;

namespace TaskFolder.Models
{
    internal enum AddInputKind { ExistingShortcut, Path, Url, Command, Catalog }
    internal sealed class AddInput
    {
        internal AddInputKind Kind;
        internal string Name, Source, Arguments, WorkingDirectory;
        internal CatalogApp Catalog;
    }
    internal sealed class CatalogApp
    {
        internal string Name, Identity, LinkPath, ShellName, Target, Arguments, WorkingDirectory, Provider;
        internal System.Drawing.Icon Icon;
        public override string ToString() => Name + " · " + Provider;
    }
    internal sealed class AddPreview
    {
        internal OperationRequest Request;
        internal OperationResult Result;
        internal List<string> Summary = new List<string>();
        internal List<string> Duplicates = new List<string>();
    }
}
