using System;
using System.IO;
using MrmTool;
using TerraFX.Interop.Windows;
using static TerraFX.Interop.Windows.Windows;

namespace MrmTool.Common
{
    /// <summary>
    /// 托管 CoreWindow 下避免使用 WinRT <c>ContentDialog</c> / <c>MessageDialog</c>（易 E_NOINTERFACE）。
    /// </summary>
    internal static class NativeUi
    {
        private const uint MB_OK = 0;
        private const uint MB_YESNO = 4;
        private const uint MB_YESNOCANCEL = 3;
        private const uint MB_ICONQUESTION = 0x20;
        private const uint MB_ICONERROR = 0x10;
        private const uint MB_ICONINFORMATION = 0x40;

        internal const int IDOK = 1;
        internal const int IDYES = 6;
        internal const int IDNO = 7;
        internal const int IDCANCEL = 2;

        private static unsafe int MessageBox(HWND owner, string text, string title, uint type)
        {
            fixed (char* pText = text)
            fixed (char* pTitle = title)
            {
                return MessageBoxW(owner, pText, pTitle, type);
            }
        }

        internal static void MessageOk(string title, string message, bool error = false)
        {
            var flags = MB_OK | (error ? MB_ICONERROR : MB_ICONINFORMATION);
            _ = MessageBox(Program.WindowHandle, message, title, flags);
        }

        internal static bool MessageYesNo(string title, string message)
        {
            var r = MessageBox(Program.WindowHandle, message, title, MB_YESNO | MB_ICONQUESTION);
            return r == IDYES;
        }

        /// <summary>是 = <see cref="IDYES"/>，否 = <see cref="IDNO"/>，取消 = <see cref="IDCANCEL"/>。</summary>
        internal static int MessageYesNoCancel(string title, string message)
        {
            return MessageBox(Program.WindowHandle, message, title, MB_YESNOCANCEL | MB_ICONQUESTION);
        }

        internal static string? TryGetEmbeddedNoticeText()
        {
            try
            {
                var map = Program._resourceMap;
                if (map is not null)
                {
                    foreach (var key in new[] { "Strings/NOTICE", "NOTICE", "/Strings/NOTICE" })
                    {
                        try
                        {
                            var value = map[key].Resolve().ValueAsString;
                            if (!string.IsNullOrWhiteSpace(value))
                                return value;
                        }
                        catch
                        {
                        }
                    }
                }

                var noticePath = Path.Combine(AppContext.BaseDirectory, "NOTICE.txt");
                if (File.Exists(noticePath))
                    return File.ReadAllText(noticePath);
            }
            catch
            {
            }

            return null;
        }
    }
}
