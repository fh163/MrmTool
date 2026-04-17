using System;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading;
using TerraFX.Interop.Windows;
using static TerraFX.Interop.Windows.Windows;
using MrmTool;

namespace MrmTool.Common
{
    internal static class CrashLogger
    {
        private static readonly object _gate = new();
        private static readonly string _sessionLogFile = CreateSessionLogFile();
        private static int _firstChanceE_NOINTERFACE_LogCount;
        private const int FirstChanceE_NOINTERFACE_LogMax = 64;

        internal static string LogDirectory
        {
            get
            {
                var dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "MrmTool", "logs");
                Directory.CreateDirectory(dir);
                return dir;
            }
        }

        internal static string LogException(Exception ex, string context)
        {
            try
            {
                var sb = new StringBuilder();
                sb.AppendLine($"Time: {DateTime.Now:O}");
                sb.AppendLine($"Context: {context}");
                sb.AppendLine($"Exception: {ex.GetType().FullName}");
                sb.AppendLine($"Message: {ex.Message}");
                sb.AppendLine($"HResult: 0x{ex.HResult:X8}");

                try
                {
                    // XamlParseException may expose line/position, use reflection to avoid hard dependency.
                    var t = ex.GetType();
                    var line = t.GetProperty("LineNumber")?.GetValue(ex);
                    var pos = t.GetProperty("LinePosition")?.GetValue(ex);
                    var uri = t.GetProperty("BaseUri")?.GetValue(ex);
                    if (line is not null || pos is not null || uri is not null)
                    {
                        sb.AppendLine($"Xaml: Line={line ?? "?"}, Position={pos ?? "?"}, BaseUri={uri ?? "?"}");
                    }
                }
                catch { }

                sb.AppendLine("StackTrace:");
                sb.AppendLine(ex.StackTrace ?? "(null)");
                if (string.IsNullOrEmpty(ex.StackTrace))
                {
                    try
                    {
                        sb.AppendLine("Diagnostics.StackTrace(ex, fNeedFileInfo: true):");
                        sb.AppendLine(new StackTrace(ex, true).ToString());
                    }
                    catch { }
                    sb.AppendLine("Environment.StackTrace:");
                    sb.AppendLine(Environment.StackTrace);
                }
                try
                {
                    sb.AppendLine("LogSite.StackTrace (current thread, when logging):");
                    sb.AppendLine(new StackTrace(1, true).ToString());
                }
                catch { }
                sb.AppendLine();

                sb.AppendLine("ToString():");
                sb.AppendLine(ex.ToString());

                for (Exception? cur = ex.InnerException; cur is not null; cur = cur.InnerException)
                {
                    sb.AppendLine("--- InnerException ---");
                    sb.AppendLine(cur.ToString());
                }

                lock (_gate)
                {
                    File.AppendAllText(_sessionLogFile, sb.ToString(), Encoding.UTF8);
                }

                return _sessionLogFile;
            }
            catch
            {
                return string.Empty;
            }
        }

        private const int HResultE_NOINTERFACE = unchecked((int)0x80004002);

        /// <summary>
        /// 在 CLR 查找 catch 之前记录调用栈。E_NOINTERFACE 可能以 InvalidCastException、COMException 等形式出现。
        /// </summary>
        internal static void TryLogFirstChanceIfNoInterface(Exception ex)
        {
            // 未处理异常里常见「不支持此接口」，但首次机会阶段 Message/HResult 可能仍与最终对象不一致；记录所有 InvalidCast。
            if (ex is not InvalidCastException && ex.HResult != HResultE_NOINTERFACE)
                return;

            var n = Interlocked.Increment(ref _firstChanceE_NOINTERFACE_LogCount);
            if (n > FirstChanceE_NOINTERFACE_LogMax)
            {
                Interlocked.Decrement(ref _firstChanceE_NOINTERFACE_LogCount);
                return;
            }

            try
            {
                var sb = new StringBuilder();
                sb.AppendLine($"Time: {DateTime.Now:O}");
                sb.AppendLine($"FirstChance (HResult=0x{ex.HResult:X8}): {ex.GetType().FullName}");
                sb.AppendLine(new StackTrace(1, true).ToString());
                sb.AppendLine(ex.ToString());
                sb.AppendLine("----");

                lock (_gate)
                {
                    var path = Path.Combine(LogDirectory, "firstchance_e_nointerface.log");
                    File.AppendAllText(path, sb.ToString(), Encoding.UTF8);
                }
            }
            catch
            {
                Interlocked.Decrement(ref _firstChanceE_NOINTERFACE_LogCount);
            }
        }

        /// <summary>模态打开/卸载步骤诊断（同步写入，便于定位 UnloadObject / Children.Add 等）。</summary>
        internal static void LogModalStep(string step, Exception? ex = null)
        {
            try
            {
                Directory.CreateDirectory(LogDirectory);
                var sb = new StringBuilder();
                sb.AppendLine($"{DateTime.Now:O} {step}");
                if (ex is not null)
                {
                    sb.AppendLine(ex.ToString());
                    try
                    {
                        sb.AppendLine(new StackTrace(1, true).ToString());
                    }
                    catch { }
                }
                var path = Path.Combine(LogDirectory, "modal_diag.log");
                lock (_gate)
                {
                    File.AppendAllText(path, sb.ToString(), Encoding.UTF8);
                }
                try
                {
                    using var fs = new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.Read);
                    fs.Flush(true);
                }
                catch { }
            }
            catch { }
        }

        private static string CreateSessionLogFile()
        {
            try
            {
                var dir = LogDirectory;
                int max = 0;
                foreach (var path in Directory.GetFiles(dir, "latest_v*.log"))
                {
                    var name = Path.GetFileNameWithoutExtension(path);
                    var idx = name.LastIndexOf('v');
                    if (idx >= 0 && int.TryParse(name[(idx + 1)..], out var n) && n > max)
                        max = n;
                }

                var next = max + 1;
                var file = Path.Combine(dir, $"latest_v{next:000}.log");
                File.WriteAllText(file, $"Session started: {DateTime.Now:O}\r\n\r\n", Encoding.UTF8);
                return file;
            }
            catch
            {
                return Path.Combine(LogDirectory, "latest_v000.log");
            }
        }

        /// <summary>
        /// 同步弹出错误（仅原生 MessageBox）。托管 CoreWindow 下 WinRT MessageDialog 常触发 E_NOINTERFACE；
        /// UnhandledException 里使用 async/await 易导致二次未处理异常与闪退。
        /// </summary>
        internal static void ShowErrorDialog(string title, Exception ex, string context)
        {
            var path = LogException(ex, context);
            var prefix = LocalizationService.GetString("Crash.Error.Prefix");
            var logLabel = LocalizationService.GetString("Crash.Error.LogPath");
            var msg = string.IsNullOrEmpty(path)
                ? $"{prefix}{ex.GetType().Name}\r\n{ex.Message}"
                : $"{prefix}{ex.GetType().Name}\r\n{ex.Message}\r\n\r\n{logLabel}{path}";

            TryShowNativeMessageBox(msg, title);
        }

        private static unsafe void TryShowNativeMessageBox(string msg, string title)
        {
            try
            {
                fixed (char* pText = msg)
                fixed (char* pTitle = title)
                {
                    MessageBoxW(Program.WindowHandle, pText, pTitle, 0);
                }
            }
            catch { }
        }
    }
}

