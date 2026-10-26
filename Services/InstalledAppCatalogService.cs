using System;
using System.Threading;
using System.Threading.Tasks;
using System.Collections.Generic;
using TaskFolder.Models;
using TaskFolder.Utilities;

namespace TaskFolder.Services
{
    internal static class StaWorker
    {
        internal static Task<T> Run<T>(Func<T> action, CancellationToken cancellation)
        {
            var result = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);
            var thread = new Thread(() => { try { cancellation.ThrowIfCancellationRequested(); var value = action(); cancellation.ThrowIfCancellationRequested(); result.TrySetResult(value); }
                catch (OperationCanceledException) { result.TrySetCanceled(cancellation); } catch (Exception ex) { result.TrySetException(ex); } });
            thread.IsBackground = true; thread.SetApartmentState(ApartmentState.STA); thread.Start(); return result.Task;
        }
    }
    internal sealed class InstalledAppCatalogService
    {
        internal const bool PackagedProviderVerified = false; // Enable only after the native persisted-identity acceptance check passes.
        internal Task<int> LoadAsync(Action<CatalogApp> found, CancellationToken cancellation) => StaWorker.Run(() => {
            var items = new List<CatalogApp>();
            int count = ShellAppCatalog.Enumerate(app => { if (app.Provider == "Packaged" && !PackagedProviderVerified) return; items.Add(app); found(app); }, cancellation);
            foreach (var app in items) { cancellation.ThrowIfCancellationRequested(); if (app.LinkPath == null) continue;
                try { app.Icon = IconExtractor.GetShortcutIcon(app.LinkPath); if (cancellation.IsCancellationRequested) { app.Icon?.Dispose(); app.Icon = null; cancellation.ThrowIfCancellationRequested(); } found(app); }
                catch (System.IO.IOException) { } }
            return count;
        }, cancellation);
    }
}
