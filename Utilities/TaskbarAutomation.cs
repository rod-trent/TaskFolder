using System;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Text;

namespace TaskFolder.Utilities
{
    // Minimal native UIA client: no WPF or third-party runtime dependency.
    // Vtable order follows Microsoft's UIAutomationClient.h. Unused slots are retained.
    internal sealed class TaskbarAutomation : IDisposable
    {
        private readonly IAutomation _automation = (IAutomation)new CUIAutomation();
        private readonly IWalker _walker;
        internal Action<string> Diagnostic { get; set; }

        internal TaskbarAutomation() => _walker = _automation.GetControlViewWalker();

        internal Rectangle ButtonAt(Point point)
        {
            if (!IsTaskbarAt(point)) return Rectangle.Empty;
            IElement element = _automation.ElementFromPoint(point);
            try
            {
                for (int depth = 0; element != null && depth < 8; depth++)
                {
                    string id = element.GetCurrentPropertyValue(30011) as string; // AutomationId
                    string name = element.GetCurrentPropertyValue(30005) as string;
                    object type = element.GetCurrentPropertyValue(30003); // ControlType
                    bool matches = type is int controlType && controlType == 50000 && MatchesIdentity(id, name);
                    if ((id?.Contains("TaskFolder", StringComparison.OrdinalIgnoreCase) ?? false) ||
                        (name?.StartsWith("TaskFolder", StringComparison.OrdinalIgnoreCase) ?? false))
                        Diagnostic?.Invoke($"Candidate: id='{id}', name='{name}', type={type}, matched={matches}");
                    if (matches)
                    {
                        if (element.GetCurrentPropertyValue(30001) is double[] r && r.Length == 4)
                        {
                            var bounds = Rectangle.Round(new RectangleF((float)r[0], (float)r[1],
                                (float)r[2], (float)r[3]));
                            if (bounds.Contains(point)) return bounds;
                        }
                    }
                    IElement parent = _walker.GetParentElement(element);
                    Marshal.ReleaseComObject(element);
                    element = parent;
                }
                return Rectangle.Empty;
            }
            finally { if (element != null) Marshal.ReleaseComObject(element); }
        }

        internal static bool MatchesIdentity(string automationId, string name)
        {
            string id = automationId?.Trim();
            if (!string.IsNullOrEmpty(id))
            {
                if (id.StartsWith("Appid:", StringComparison.OrdinalIgnoreCase))
                    id = id.Substring("Appid:".Length).Trim();
                return string.Equals(id, Services.WindowsIntegrationService.AppUserModelId,
                    StringComparison.OrdinalIgnoreCase);
            }
            // A decorated/localized label is irrelevant when the stable AppID is available.
            // An explicit different ID must never be accepted merely because its label matches.
            return string.Equals(name?.Trim(), "TaskFolder", StringComparison.OrdinalIgnoreCase);
        }

        internal static bool IsTaskbarAt(Point point)
        {
            IntPtr root = GetAncestor(WindowFromPoint(point), 2); // GA_ROOT
            var name = new StringBuilder(64);
            GetClassName(root, name, name.Capacity);
            return name.ToString() is "Shell_TrayWnd" or "Shell_SecondaryTrayWnd";
        }

        public void Dispose()
        {
            Marshal.ReleaseComObject(_walker);
            Marshal.ReleaseComObject(_automation);
        }

        [DllImport("user32.dll")] private static extern IntPtr WindowFromPoint(Point point);
        [DllImport("user32.dll")] private static extern IntPtr GetAncestor(IntPtr window, uint flags);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        private static extern int GetClassName(IntPtr window, StringBuilder name, int length);

        [ComImport, Guid("FF48DBA4-60EF-4201-AA87-54103EEF594E")]
        private class CUIAutomation { }

        [ComImport, Guid("30CBE57D-D9D0-452A-AB13-7AC5AC4825EE"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IAutomation
        {
            void CompareElements(IntPtr a, IntPtr b, out int same);
            void CompareRuntimeIds(IntPtr a, IntPtr b, out int same);
            IElement GetRootElement();
            IElement ElementFromHandle(IntPtr window);
            IElement ElementFromPoint(Point point);
            IElement GetFocusedElement();
            IElement GetRootElementBuildCache(IntPtr cache);
            IElement ElementFromHandleBuildCache(IntPtr window, IntPtr cache);
            IElement ElementFromPointBuildCache(Point point, IntPtr cache);
            IElement GetFocusedElementBuildCache(IntPtr cache);
            IWalker CreateTreeWalker(IntPtr condition);
            IWalker GetControlViewWalker();
        }

        [ComImport, Guid("D22108AA-8AC5-49A5-837B-37BBB3D7591E"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IElement
        {
            void SetFocus();
            void GetRuntimeId(out IntPtr id);
            IElement FindFirst(int scope, IntPtr condition);
            void FindAll(int scope, IntPtr condition, out IntPtr elements);
            IElement FindFirstBuildCache(int scope, IntPtr condition, IntPtr cache);
            void FindAllBuildCache(int scope, IntPtr condition, IntPtr cache, out IntPtr elements);
            IElement BuildUpdatedCache(IntPtr cache);
            [return: MarshalAs(UnmanagedType.Struct)] object GetCurrentPropertyValue(int property);
        }

        [ComImport, Guid("4042C624-389C-4AFC-A630-9DF854A541FC"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IWalker
        {
            IElement GetParentElement(IElement element);
        }
    }
}
