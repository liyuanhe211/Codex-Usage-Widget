using System;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Web.Script.Serialization;

namespace UsageRings {
 internal sealed class AutostartCoordinator {
  internal DateTime NextLaunchAt = DateTime.MinValue;
  internal DateTime LastLaunchAt = DateTime.MinValue;
  internal string LastError;

  internal bool EnsureRunning(bool desktopRunning, bool widgetRunning, DateTime now, Action launch) {
   if (!desktopRunning || widgetRunning || now < NextLaunchAt) return false;
   NextLaunchAt = now.AddSeconds(15);
   try {
    launch();
    LastLaunchAt = now;
    LastError = null;
    return true;
   } catch (Exception error) {
    LastError = error.Message;
    return false;
   }
  }
 }

 internal static class AutostartProgram {
  private static readonly string StopEventName = "Local\\CodexUsageRings.Autostart.Stop." + Environment.UserName;
  private static readonly string SupervisorMutexName = "Local\\CodexUsageRings.Autostart." + Environment.UserName;
  private static readonly string WidgetMutexName = "Local\\CodexUsageRings.Native." + Environment.UserName;

  internal static bool IsSupportedDesktopPath(string path) {
   return !String.IsNullOrEmpty(path) &&
    (path.IndexOf("\\WindowsApps\\OpenAI.Codex_", StringComparison.OrdinalIgnoreCase) >= 0 ||
     (path.EndsWith("\\Codex.exe", StringComparison.OrdinalIgnoreCase) &&
      path.IndexOf("\\OpenAI\\Codex", StringComparison.OrdinalIgnoreCase) >= 0));
  }

  internal static bool IsDesktopRunning() {
   int currentSession;
   using (var current = Process.GetCurrentProcess()) currentSession = current.SessionId;
   foreach (var name in new[] { "ChatGPT", "Codex" }) {
    foreach (var process in Process.GetProcessesByName(name)) {
     using (process) {
      try {
       if (process.SessionId == currentSession && process.MainWindowHandle != IntPtr.Zero &&
        IsSupportedDesktopPath(process.MainModule.FileName)) return true;
      } catch { }
     }
    }
   }
   return false;
  }

  internal static bool IsWidgetRunning() {
   Mutex existing;
   if (!Mutex.TryOpenExisting(WidgetMutexName, out existing)) return false;
   existing.Dispose();
   return true;
  }

  private static void LaunchWidget(string root, string node) {
   var executable = Path.Combine(root, "native", "build", "CodexUsageRings.exe");
   if (!File.Exists(executable)) throw new FileNotFoundException("Native widget executable is missing.", executable);
   if (!File.Exists(node)) throw new FileNotFoundException("Node.js executable is missing.", node);
   using (var process = Process.Start(new ProcessStartInfo(executable, "--node \"" + node + "\"") {
    WorkingDirectory = root, UseShellExecute = false, CreateNoWindow = true
   })) {
    if (process == null) throw new InvalidOperationException("Could not start the native widget.");
   }
  }

  [STAThread]
  public static int Main(string[] arguments) {
   using (var stop = new EventWaitHandle(false, EventResetMode.ManualReset, StopEventName)) {
    if (arguments.Length == 1 && arguments[0] == "--stop") {
     stop.Set();
     Mutex existing;
     if (Mutex.TryOpenExisting(SupervisorMutexName, out existing)) {
      using (existing) {
       bool acquired = false;
       try { acquired = existing.WaitOne(5000); }
       catch (AbandonedMutexException) { acquired = true; }
       if (!acquired) return 1;
       existing.ReleaseMutex();
      }
     }
     return 0;
    }
    bool created;
    using (var singleton = new Mutex(true, SupervisorMutexName, out created)) {
     if (!created) return 0;
     stop.Reset();
     string root = Path.GetFullPath(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "..", ".."));
     string node = null;
     for (int index = 0; index < arguments.Length - 1; index++) {
      if (arguments[index] == "--node") node = arguments[++index];
     }
     var coordinator = new AutostartCoordinator();
     var statusPath = Path.Combine(root, "_Claude_tmp", "Autostart_State_Private.json");
     var nextStatusAt = DateTime.MinValue;
     while (!stop.WaitOne(0)) {
      bool desktopRunning = false, widgetRunning = false;
      var now = DateTime.UtcNow;
      try {
       desktopRunning = IsDesktopRunning();
       widgetRunning = IsWidgetRunning();
       coordinator.EnsureRunning(desktopRunning, widgetRunning, now, delegate { LaunchWidget(root, node); });
      } catch (Exception error) { coordinator.LastError = error.Message; }
      if (now >= nextStatusAt) {
       nextStatusAt = now.AddSeconds(5);
       try {
        Directory.CreateDirectory(Path.GetDirectoryName(statusPath));
        File.WriteAllText(statusPath, new JavaScriptSerializer().Serialize(new {
         updatedAt = now.ToString("o"), supervisorProcessId = Process.GetCurrentProcess().Id,
         desktopRunning = desktopRunning, widgetRunning = widgetRunning,
         lastLaunchAt = coordinator.LastLaunchAt == DateTime.MinValue ? null : coordinator.LastLaunchAt.ToString("o"),
         nextLaunchAt = coordinator.NextLaunchAt == DateTime.MinValue ? null : coordinator.NextLaunchAt.ToString("o"),
         lastError = coordinator.LastError
        }), new System.Text.UTF8Encoding(false));
       } catch { }
      }
      if (stop.WaitOne(2000)) break;
     }
     return 0;
    }
   }
  }
 }
}