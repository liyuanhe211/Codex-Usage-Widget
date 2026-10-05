using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows.Automation;

namespace UsageRings {
    internal sealed class ButtonAnchor {
        public IntPtr Window;
        public Rectangle WindowBounds;
        public Rectangle ButtonBounds;
        public Rectangle ModelBounds;
        public Rectangle ToolbarBounds;
        public string ButtonName;
        public double Scale;
    }

    internal static class NativeAnchor {
        internal delegate bool EnumWindowsCallback(IntPtr window, IntPtr parameter);
        [StructLayout(LayoutKind.Sequential)]
        internal struct NativeRectangle {
            public int Left, Top, Right, Bottom;
            public Rectangle ToRectangle() { return Rectangle.FromLTRB(Left, Top, Right, Bottom); }
        }
        [DllImport("user32.dll")] internal static extern bool SetProcessDpiAwarenessContext(IntPtr context);
        [DllImport("user32.dll")] internal static extern bool EnumWindows(EnumWindowsCallback callback, IntPtr parameter);
        [DllImport("user32.dll")] internal static extern bool EnumChildWindows(IntPtr window, EnumWindowsCallback callback, IntPtr parameter);
        [DllImport("user32.dll", CharSet=CharSet.Unicode)] internal static extern int GetClassName(IntPtr window, StringBuilder className, int maximum);
        [DllImport("user32.dll")] internal static extern bool IsWindowVisible(IntPtr window);
        [DllImport("user32.dll")] internal static extern bool IsIconic(IntPtr window);
        [DllImport("user32.dll")] internal static extern bool GetWindowRect(IntPtr window, out NativeRectangle bounds);
        [DllImport("user32.dll")] internal static extern uint GetWindowThreadProcessId(IntPtr window, out uint processId);
        [DllImport("user32.dll")] internal static extern IntPtr GetForegroundWindow();
        [DllImport("user32.dll")] internal static extern IntPtr WindowFromPoint(Point point);
        [DllImport("user32.dll")] internal static extern IntPtr GetAncestor(IntPtr window, uint flags);
        [DllImport("user32.dll")] internal static extern uint GetDpiForWindow(IntPtr window);
        [DllImport("user32.dll")] internal static extern IntPtr GetWindow(IntPtr window, uint command);
        [DllImport("user32.dll")] internal static extern IntPtr GetDC(IntPtr window);
        [DllImport("user32.dll")] internal static extern int ReleaseDC(IntPtr window, IntPtr context);
        [DllImport("gdi32.dll")] internal static extern uint GetPixel(IntPtr context, int x, int y);
        [DllImport("dwmapi.dll")] private static extern int DwmGetWindowAttribute(IntPtr window, int attribute, out int value, int size);

        internal static bool IsCodexWindow(IntPtr window) {
            try {
                uint processId;
                GetWindowThreadProcessId(window, out processId);
                using (var process = Process.GetProcessById((int)processId)) {
                    var path = process.MainModule.FileName;
                    return path.IndexOf("\\WindowsApps\\OpenAI.Codex_", StringComparison.OrdinalIgnoreCase) >= 0
                        || (path.EndsWith("\\Codex.exe", StringComparison.OrdinalIgnoreCase)
                            && path.IndexOf("\\OpenAI\\Codex", StringComparison.OrdinalIgnoreCase) >= 0);
                }
            } catch { return false; }
        }

        internal static List<IntPtr> FindWindows() {
            var windows = new List<IntPtr>();
            EnumWindows(delegate(IntPtr window, IntPtr parameter) {
                int cloaked = 0;
                if (IsWindowVisible(window) && GetWindow(window, 4) == IntPtr.Zero
                    && (DwmGetWindowAttribute(window, 14, out cloaked, 4) != 0 || cloaked == 0)
                    && IsCodexWindow(window)) windows.Add(window);
                return true;
            }, IntPtr.Zero);
            return windows;
        }

        internal static string ReadPageTitle(IntPtr window) {
            // Only inspect the app's root document identity, never conversation text.
            var root = AutomationElement.FromHandle(window);
            var documents = root.FindAll(TreeScope.Descendants,
                new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.Document));
            string title = null;
            foreach (AutomationElement document in documents) {
                if (document.Current.AutomationId != "RootWebArea") continue;
                object pattern;
                if (!document.TryGetCurrentPattern(ValuePattern.Pattern, out pattern)
                    || !((ValuePattern)pattern).Current.Value.StartsWith("app://", StringComparison.Ordinal)) continue;
                var currentTitle = document.Current.Name;
                if (String.IsNullOrWhiteSpace(currentTitle)) continue;
                if (title != null) return null;
                title = currentTitle;
            }
            return title;
        }

        internal static bool IsMicrophoneName(string name) {
            if (String.IsNullOrEmpty(name)) return false;
            var lower = name.ToLowerInvariant();
            return lower.Contains("dictat") || lower.Contains("microphone")
                || lower.Contains("麦克风") || lower.Contains("听写") || lower.Contains("语音输入");
        }

        private static bool IsModelName(string name) {
            if (String.IsNullOrEmpty(name)) return false;
            var lower = name.ToLowerInvariant();
            return lower.StartsWith("gpt-") || lower.StartsWith("gpt ")
                || lower.Contains("select model") || lower == "model" || lower.StartsWith("模型");
        }

        internal static ButtonAnchor FindButton(IntPtr window, List<string> diagnostics) {
            ButtonAnchor composer;
            return FindButton(window, diagnostics, out composer);
        }

        internal static ButtonAnchor FindButton(IntPtr window, List<string> diagnostics, out ButtonAnchor composer) {
            composer = null;
            NativeRectangle nativeBounds;
            if (!GetWindowRect(window, out nativeBounds) || IsIconic(window)) return null;
            var windowBounds = nativeBounds.ToRectangle();
            var roots = new List<AutomationElement> { AutomationElement.FromHandle(window) };
            EnumChildWindows(window, delegate(IntPtr child, IntPtr parameter) {
                var className = new StringBuilder(256);
                GetClassName(child, className, className.Capacity);
                if (className.ToString().Contains("Chrome_RenderWidgetHostHWND")) {
                    try { roots.Add(AutomationElement.FromHandle(child)); } catch { }
                }
                return true;
            }, IntPtr.Zero);
            ButtonAnchor result = null;
            var modelButtons = new List<Rectangle>();
            var attachmentButtons = new List<Rectangle>();
            foreach (var searchRoot in roots) {
                var buttons = searchRoot.FindAll(TreeScope.Descendants,
                    new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.Button));
                foreach (AutomationElement button in buttons) {
                    try {
                        var name = button.Current.Name;
                        if (String.IsNullOrEmpty(name)) name = button.Current.HelpText;
                        var microphone = IsMicrophoneName(name);
                        var model = IsModelName(name);
                        var attachment = name.StartsWith("Add files", StringComparison.OrdinalIgnoreCase)
                            || name.StartsWith("Attach", StringComparison.OrdinalIgnoreCase) || name.StartsWith("添加文件");
                        if (!microphone && !model && !attachment) continue;
                        var bounds = button.Current.BoundingRectangle;
                        var rectangle = new Rectangle((int)Math.Round(bounds.X), (int)Math.Round(bounds.Y),
                            (int)Math.Round(bounds.Width), (int)Math.Round(bounds.Height));
                        if (button.Current.IsOffscreen || rectangle.Width < 8 || rectangle.Height < 8
                            || rectangle.Width > 900 || rectangle.Height > 120 || !windowBounds.Contains(rectangle)
                            || rectangle.Top < windowBounds.Top + windowBounds.Height / 2) continue;
                        if (diagnostics != null) diagnostics.Add(name + " | " + rectangle);
                        if (model) modelButtons.Add(rectangle);
                        if (attachment) attachmentButtons.Add(rectangle);
                        if (!microphone || rectangle.Width > 120) continue;
                        if (result == null || rectangle.Bottom > result.ButtonBounds.Bottom) {
                            uint dpi = 96;
                            try { dpi = GetDpiForWindow(window); } catch { }
                            result = new ButtonAnchor { Window = window, WindowBounds = windowBounds,
                                ButtonBounds = rectangle, ButtonName = name, Scale = Math.Max(1, dpi / 96.0) };
                        }
                    } catch (ElementNotAvailableException) { }
                }
            }
            NativeRectangle finalBounds;
            if (!GetWindowRect(window, out finalBounds) || finalBounds.ToRectangle() != windowBounds) return null;
            uint windowDpi = 96;
            try { windowDpi = GetDpiForWindow(window); } catch { }
            double displayScale = Math.Max(1, windowDpi / 96.0);
            composer = FindComposer(window, windowBounds, modelButtons, attachmentButtons, displayScale);
            if (result == null) return null;
            var microphoneBounds = result.ButtonBounds;
            int closestRight = Int32.MinValue;
            foreach (var model in modelButtons) {
                if (model.Left >= microphoneBounds.Left || model.Right > microphoneBounds.Right
                    || Math.Abs(model.Top + model.Height / 2 - microphoneBounds.Top - microphoneBounds.Height / 2)
                        > microphoneBounds.Height / 2) continue;
                if (model.Right > closestRight) { result.ModelBounds = model; closestRight = model.Right; }
            }
            int composerLeft = Math.Max(windowBounds.Left, microphoneBounds.Left - (int)Math.Round(680 * result.Scale));
            foreach (var attachment in attachmentButtons) {
                if (attachment.Right >= microphoneBounds.Left
                    || Math.Abs(attachment.Top + attachment.Height / 2 - microphoneBounds.Top - microphoneBounds.Height / 2)
                        > microphoneBounds.Height / 2) continue;
                composerLeft = attachment.Left;
                break;
            }
            result.ToolbarBounds = Rectangle.FromLTRB(composerLeft, microphoneBounds.Top, microphoneBounds.Right, microphoneBounds.Bottom);
            return result;
        }

        internal static ButtonAnchor FindComposer(IntPtr window, Rectangle windowBounds,
            List<Rectangle> models, List<Rectangle> attachments, double scale) {
            ButtonAnchor composer = null;
            foreach (var model in models) {
                if (model.Width < 80 * scale) continue;
                foreach (var attachment in attachments) {
                    if (attachment.Right >= model.Left || Math.Abs(attachment.Top + attachment.Height / 2
                        - model.Top - model.Height / 2) > model.Height / 2) continue;
                    if (composer != null && composer.ModelBounds.Bottom >= model.Bottom) continue;
                    int padding = (int)Math.Round(4 * scale);
                    var search = Rectangle.Intersect(windowBounds, Rectangle.FromLTRB(model.Right - padding,
                        model.Top - padding, model.Right + (int)Math.Round(120 * scale), model.Bottom + padding));
                    composer = new ButtonAnchor { Window = window, WindowBounds = windowBounds,
                        ButtonBounds = search, ModelBounds = model,
                        ToolbarBounds = Rectangle.FromLTRB(attachment.Left, model.Top, search.Right, model.Bottom),
                        Scale = scale };
                }
            }
            return composer;
        }

        internal static ButtonAnchor NormalizeButton(ButtonAnchor anchor) {
            var button = anchor.ButtonBounds;
            int size = (int)Math.Round(32 * anchor.Scale);
            return new ButtonAnchor { Window = anchor.Window, WindowBounds = anchor.WindowBounds,
                ButtonBounds = new Rectangle(button.Left + button.Width / 2 - size / 2,
                    button.Top + button.Height / 2 - size / 2, size, size),
                ModelBounds = anchor.ModelBounds, ToolbarBounds = anchor.ToolbarBounds,
                ButtonName = anchor.ButtonName, Scale = anchor.Scale };
        }

        internal static ButtonAnchor ForPlacement(ButtonAnchor anchor, bool besideModel) {
            var bounds = anchor.ButtonBounds;
            if (besideModel && !anchor.ModelBounds.IsEmpty) {
                int size = Math.Min(bounds.Width, bounds.Height);
                int gap = Math.Max(4, (int)Math.Round(4 * anchor.Scale));
                var target = new Rectangle(anchor.ModelBounds.Left - size - gap,
                    anchor.ModelBounds.Top + (anchor.ModelBounds.Height - size) / 2, size, size);
                if (anchor.WindowBounds.Contains(target)) bounds = target;
            }
            return new ButtonAnchor { Window = anchor.Window, WindowBounds = anchor.WindowBounds,
                ButtonBounds = bounds, ModelBounds = anchor.ModelBounds, ToolbarBounds = anchor.ToolbarBounds,
                ButtonName = anchor.ButtonName, Scale = anchor.Scale };
        }

        internal static Color ReadBackground(ButtonAnchor anchor, Color previous) {
            return NativeTheme.ReadBackground(anchor, previous);
        }

    }

    internal sealed class AnchorTracker {
        private ButtonAnchor accepted, pending;

        private static bool SamePosition(ButtonAnchor first, ButtonAnchor second) {
            return first != null && second != null && first.Window == second.Window
                && first.WindowBounds.Size == second.WindowBounds.Size && first.Scale == second.Scale
                && Math.Abs(first.ButtonBounds.Left - first.WindowBounds.Left
                    - second.ButtonBounds.Left + second.WindowBounds.Left) <= 2
                && Math.Abs(first.ButtonBounds.Top - first.WindowBounds.Top
                    - second.ButtonBounds.Top + second.WindowBounds.Top) <= 2;
        }

        internal ButtonAnchor Update(ButtonAnchor next) {
            if (next == null) { pending = null; return null; }
            next = NativeAnchor.NormalizeButton(next);
            if (SamePosition(accepted, next)) {
                var button = accepted.ButtonBounds;
                button.Offset(next.WindowBounds.Left - accepted.WindowBounds.Left,
                    next.WindowBounds.Top - accepted.WindowBounds.Top);
                next.ButtonBounds = button;
                accepted = next; pending = null;
                return next;
            }
            bool directButton = NativeAnchor.IsMicrophoneName(next.ButtonName)
                && next.ButtonName != "microphone (verified icon)";
            if ((accepted == null && directButton) || SamePosition(pending, next)) {
                accepted = next; pending = null;
                return next;
            }
            pending = next;
            return null;
        }
    }
}
