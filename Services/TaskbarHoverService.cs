using System;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Threading;
using TaskFolder.Utilities;

namespace TaskFolder.Services
{
    // All UIA objects stay on this MTA worker; Explorer must never block the UI thread.
    internal sealed class TaskbarHoverService : IDisposable
    {
        private readonly CancellationTokenSource _stop = new CancellationTokenSource();
        private readonly Thread _thread;
        private volatile bool _enabled;
        private Rectangle _lastButton;
        private readonly object _boundsLock = new object();
        private string _lastDiagnostic;
        internal event Action<Rectangle> Hovered;
        internal event Action<string> Diagnostic;
        internal bool Enabled { get => _enabled; set => _enabled = value; }
        internal Rectangle LastButton { get { lock (_boundsLock) return _lastButton; } }

        internal TaskbarHoverService()
        {
            _thread = new Thread(Run) { IsBackground = true, Name = "TaskFolder taskbar hover" };
            _thread.SetApartmentState(ApartmentState.MTA);
            _thread.Start();
        }

        private void Run()
        {
            var dwell = new TaskbarHoverDwell();
            TaskbarAutomation automation = null;
            try
            {
                while (!_stop.IsCancellationRequested)
                {
                    try
                    {
                        Rectangle button = Rectangle.Empty;
                        if (_enabled && GetCursorPos(out Point point) &&
                            (GetAsyncKeyState(1) & 0x8000) == 0 && TaskbarAutomation.IsTaskbarAt(point))
                        {
                            if (automation == null)
                                automation = new TaskbarAutomation { Diagnostic = Report };
                            button = automation.ButtonAt(point);
                            if (!button.IsEmpty) lock (_boundsLock) _lastButton = button;
                        }
                        if (dwell.Update(button, Environment.TickCount64))
                        {
                            Report("300 ms dwell reached; requesting menu.");
                            Hovered?.Invoke(button);
                        }
                    }
                    catch (Exception ex) when (ex is COMException || ex is InvalidOperationException)
                    {
                        dwell.Update(Rectangle.Empty, Environment.TickCount64);
                        // Explorer may be restarting; discard old COM proxies and retry later.
                        automation?.Dispose();
                        automation = null;
                        System.Diagnostics.Debug.WriteLine($"Taskbar hover: {ex.Message}");
                        Report($"Detection error: {ex.GetType().Name}: {ex.Message}");
                        if (_stop.Token.WaitHandle.WaitOne(1000)) break;
                    }
                    if (_stop.Token.WaitHandle.WaitOne(50)) break;
                }
            }
            finally
            {
                automation?.Dispose();
                _stop.Dispose();
            }
        }

        internal void Report(string message)
        {
            if (message == _lastDiagnostic) return;
            _lastDiagnostic = message;
            Diagnostic?.Invoke(message);
        }

        public void Dispose()
        {
            // A slow UIA call cannot hold application shutdown hostage.
            _enabled = false;
            try { _stop.Cancel(); }
            catch (ObjectDisposedException) { }
        }

        [DllImport("user32.dll")] private static extern bool GetCursorPos(out Point point);
        [DllImport("user32.dll")] private static extern short GetAsyncKeyState(int key);
    }

    internal sealed class TaskbarHoverDwell
    {
        private Rectangle _target;
        private long _entered;
        private bool _fired;

        internal bool Update(Rectangle target, long now)
        {
            if (target.IsEmpty || target != _target)
            {
                _target = target;
                _entered = now;
                _fired = false;
            }
            if (target.IsEmpty || _fired || now - _entered < 300) return false;
            _fired = true;
            return true;
        }
    }
}
