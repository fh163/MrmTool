using Common;
using Microsoft.System;
using MrmTool.Common;
using MrmTool.Polyfills;
using MrmTool.Resources;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using TerraFX.Interop.Windows;
using TerraFX.Interop.WinRT;
using Windows.ApplicationModel.Core;
using Windows.ApplicationModel.Resources.Core;
using Windows.UI.Core;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Media;
using Windows.UI.Xaml.Resources;
using WinRT;
using Windows.Foundation;

using static MrmTool.Common.ErrorHelpers;
using static MrmTool.NativeUtils;
using static TerraFX.Interop.Windows.Windows;
using static TerraFX.Interop.Windows.WM;
using static TerraFX.Interop.Windows.WS;

namespace MrmTool
{
    internal static class Program
    {
        private static App? _xamlApp = null;
        public static ResourceMap? _resourceMap = null;
        private static HWND _coreHwnd;
        public static HWND WindowHandle;
        private static CoreDispatcher? _dispatcher;
        private static int _closePromptInProgress = 0;
        private const int DragRegionHeight = 56;
        /// <summary>标题栏左侧视为“菜单/路径等可交互区”的宽度（像素），需覆盖菜单栏 + 路径文本，避免被误判为拖动区。</summary>
        private const int MenuInteractiveRegionWidth = 1200;
        private const int RightControlRegionWidth = 160;
        /// <summary>标题栏内至少保留的可拖动宽度（像素），避免 Menu 区宽度过大时拖带区间为空。</summary>
        private const int MinCaptionDragStripWidth = 120;
        /// <summary>可拖拽缩放的边框命中宽度（像素）；用于父 HWND 的 WM_NCHITTEST 及 CoreWindow 子类中边缘 HTTRANSPARENT 转发。</summary>
        private const int ResizeBorderHitPixels = 8;
        private const nuint CoreNcSubclassId = 5429031u;
        private const int CoreTopOverlap = 0;
        private const int CoreLeftOverlap = 0;
        private const int CoreRightOverlap = 0;
        private const int CoreBottomOverlap = 0;
        private const uint DwmaWindowCornerPreference = 33;
        private const uint DwmwcpRound = 2;
        /// <summary>Win32 WS_THICKFRAME：与 WS_SIZEBOX 同值；无此位时即便 WM_NCHITTEST 返回 HTLEFT/HTRIGHT 等，系统也常不进入可拖拽缩放。</summary>
        private const uint WsThickFrame = 0x0004_0000;
        private const int InitialWindowWidth = 1320;
        private const int InitialWindowHeight = 860;

        public static bool IsWebViewAvailable = false;
        public static Func<Task<bool>>? ConfirmCloseAsync;

        #nullable disable
        public static App Application => _xamlApp;
        public static ResourceMap ResourceMap => _resourceMap;
        #nullable enable

        [STAThread]
        static unsafe void Main()
        {
            try
            {
                IsWebViewAvailable = PatchWebViewAppModelChecks();
                ComWrappersSupport.InitializeComWrappers();

                // 早于 App 构造注册，尽可能捕获 WinRT 边界抛出的 InvalidCast（部分情况下 App 内注册过晚或消息不含「不支持」）。
                AppDomain.CurrentDomain.FirstChanceException += static (_, ev) =>
                {
                    CrashLogger.TryLogFirstChanceIfNoInterface(ev.Exception);
                };

                var resourceManager = NativeUtils.InitializeResourceManager();
                try
                {
                    _resourceMap = resourceManager.MainResourceMap;
                }
                catch (Exception ex)
                {
                    _resourceMap = null;
                }

                if (Features.IsXamlRootAvailable)
                {
                    _xamlApp = new App();
                }
                else
                {
                    var data = NativeUtils.GetSwitchContextData();
                    if (data is not null)
                    {
                        data->OsMaxVersionTested = 0x000a00004a610000; // Windows 10 2004, build 19041
                    }

                    var priv = Windows.UI.Xaml.Application.As<IFrameworkApplicationStaticsPrivate>();
                    var callback = ABI.Windows.UI.Xaml.ApplicationInitializationCallback.CreateMarshaler2((args) => { _xamlApp = new App(); });
                    CoSetASTATestMode(ASTA_TEST_MODE_FLAGS.ROINITIALIZEASTA_ALLOWED);
                    priv.StartInCoreWindowHostingMode(new() { TransparentBackground = 1 }, (void*)callback.GetAbi());
                    MarshalInspectable<object>.DisposeMarshaler(callback);
                    ArgumentNullException.ThrowIfNull(_xamlApp);
                }

            ReadOnlySpan<char> className = ['M', 'r', 'm', 'T', 'o', 'o', 'l', 'C', 'l', 'a', 's', 's', '\0'];
            ReadOnlySpan<char> windowName = ['M', 'r', 'm', 'T', 'o', 'o', 'l', '\0'];

            char* lpszClassName = (char*)Unsafe.AsPointer(in MemoryMarshal.GetReference(className));
            char* lpWindowName = (char*)Unsafe.AsPointer(in MemoryMarshal.GetReference(windowName));

            WNDCLASSW wc;
            wc.lpfnWndProc = &WndProc;
            wc.hInstance = GetModuleHandleW(null);
            wc.lpszClassName = lpszClassName;
            ThrowLastErrorIfNull(RegisterClassW(&wc));

            BOOL dwmFrameEnabled = TRUE;
            if (FAILED(DwmIsCompositionEnabled(&dwmFrameEnabled)))
            {
                dwmFrameEnabled = TRUE;
            }

            // 勿加 WS_VISIBLE：否则在 CoreWindow/XAML 就绪前会长时间显示空宿主框，并在顶栏留下白条感。
            WindowHandle = CreateWindowExW(dwmFrameEnabled ? WS_EX_NOREDIRECTIONBITMAP : 0u,
                                           lpszClassName,
                                           lpWindowName,
                                           WS_POPUP | WS_MINIMIZEBOX | WS_MAXIMIZEBOX | WS_SYSMENU | WsThickFrame,
                                           CW_USEDEFAULT,
                                           CW_USEDEFAULT,
                                           CW_USEDEFAULT,
                                           CW_USEDEFAULT,
                                           HWND.NULL,
                                           HMENU.NULL,
                                           wc.hInstance,
                                           null);

            ThrowLastErrorIfDefault(WindowHandle);
            unsafe
            {
                MONITORINFO mi = default;
                mi.cbSize = (uint)sizeof(MONITORINFO);
                HMONITOR mon = MonitorFromWindow(WindowHandle, 1u);
                int x = 120;
                int y = 80;
                bool gotWork = false;
                int workW = -1;
                int workH = -1;
                if (GetMonitorInfoW(mon, &mi) != FALSE)
                {
                    gotWork = true;
                    workW = mi.rcWork.right - mi.rcWork.left;
                    workH = mi.rcWork.bottom - mi.rcWork.top;
                    x = mi.rcWork.left + (workW - InitialWindowWidth) / 2;
                    y = mi.rcWork.top + (workH - InitialWindowHeight) / 2;
                }

                SetWindowPos(
                    WindowHandle,
                    HWND.NULL,
                    x,
                    y,
                    InitialWindowWidth,
                    InitialWindowHeight,
                    SWP.SWP_NOZORDER | SWP.SWP_NOACTIVATE);
            }

            LoadLibraryA((sbyte*)Unsafe.AsPointer(in "twinapi.appcore.dll"u8.GetPinnableReference()));
            LoadLibraryA((sbyte*)Unsafe.AsPointer(in "threadpoolwinrt.dll"u8.GetPinnableReference()));

            nint pCoreWindow;

            char empty = '\0';

            ThrowIfFailed(NativeUtils.PrivateCreateCoreWindow(
                    NativeUtils.CoreWindowType.IMMERSIVE_HOSTED,
                    &empty,
                    0, 0, 0, 0,
                    0,
                    WindowHandle,
                    NativeUtils.IID_ICoreWindow,
                    &pCoreWindow));

            DispatcherHelper.SetSynchronizationContext();

            CoreWindow coreWindow = CoreWindow.FromAbi(pCoreWindow);
            Marshal.Release(pCoreWindow);
            _dispatcher = coreWindow.Dispatcher;

            nint pCoreApplicationView = 0;
            try
            {
                pCoreApplicationView = CoreApplication.As<ICoreApplicationPrivate2>().CreateNonImmersiveView();
            }
            catch
            {
                pCoreApplicationView = (nint)LegacyNonImmersiveView.Create();
            }

            CoreApplicationView view = CoreApplicationView.FromAbi(pCoreApplicationView);
            Marshal.Release(pCoreApplicationView);

            FrameworkView frameworkView = new();
            frameworkView.Initialize(view);
            frameworkView.SetWindow(coreWindow);

            HWND coreHwnd;
            using ComPtr<ICoreWindowInterop> interop = default;
            ThrowIfFailed(((IUnknown*)((IWinRTObject)coreWindow).NativeObject.ThisPtr)->QueryInterface(__uuidof<ICoreWindowInterop>(), (void**)interop.GetAddressOf()));
            ThrowIfFailed(interop.Get()->get_WindowHandle(&coreHwnd));

            _coreHwnd = coreHwnd;

            RECT clientRect;
            GetClientRect(WindowHandle, &clientRect);

            SetParent(coreHwnd, WindowHandle);
            SetWindowLongW(coreHwnd, GWL.GWL_STYLE, WS_CHILD | WS_VISIBLE);
            LayoutCoreChildInHostClient(clientRect.right - clientRect.left, clientRect.bottom - clientRect.top);
                TryInstallCoreWindowNcForward();

                CustomXamlResourceLoader.Current = new XamlResourceLoader();

                Frame frame = new();
                Window.Current.Content = frame;

                // 首帧合成完成前不要显示宿主 HWND，否则会先看到空框再跳到 PriPage（Navigate/布局多在下一拍完成）。
                EventHandler<object>? onFirstRender = null;
                onFirstRender = (_, _) =>
                {
                    CompositionTarget.Rendering -= onFirstRender;
                    NativeUtils.TryApplyHostWindowDwmChrome(WindowHandle);
                    ShowWindow(WindowHandle, SW.SW_SHOW);
                    _ = UpdateWindow(WindowHandle);
                };
                CompositionTarget.Rendering += onFirstRender;

                // 直接进入主界面；未加载 PRI 时由 PriPage 左侧占位按钮引导打开文件
                frame.Navigate(typeof(PriPage));

                frameworkView.Run();
            }
            catch (Exception ex)
            {
                throw;
            }
        }

        [UnmanagedCallersOnly]
        private unsafe static LRESULT WndProc(HWND hWnd, uint message, WPARAM wParam, LPARAM lParam)
        {
            switch (message)
            {
                case WM_CREATE:
                    NativeUtils.EnableDarkModeSupport(hWnd);
                    NativeUtils.EnsureTitleBarTheme(hWnd);
                    TryEnableRoundedCorners(hWnd);
                    NativeUtils.TryApplyHostWindowDwmChrome(hWnd);
                    break;
                case WM_ERASEBKGND:
                    unsafe
                    {
                        if ((nint)hWnd != (nint)WindowHandle)
                        {
                            return DefWindowProcW(hWnd, message, wParam, lParam);
                        }

                        var hdc = (HDC)(nint)wParam;
                        if (hdc == HDC.NULL)
                        {
                            return DefWindowProcW(hWnd, message, wParam, lParam);
                        }

                        RECT cr;
                        if (GetClientRect(hWnd, &cr) == FALSE)
                        {
                            return DefWindowProcW(hWnd, message, wParam, lParam);
                        }

                        uint fill = NativeUtils.GetHostWindowGutterColorref(hWnd);
                        HBRUSH br = CreateSolidBrush(fill);
                        _ = FillRect(hdc, &cr, br);
                        _ = DeleteObject((HGDIOBJ)(nint)br);
                        return (LRESULT)1;
                    }
                case WM_CLOSE:
                    if (ConfirmCloseAsync is null || _dispatcher is null)
                    {
                        DestroyWindow(hWnd);
                        break;
                    }

                    if (Interlocked.Exchange(ref _closePromptInProgress, 1) != 0)
                    {
                        // A prompt is already in progress.
                        return 0;
                    }

                    _ = _dispatcher.RunAsync(Windows.UI.Core.CoreDispatcherPriority.Normal, () =>
                    {
                        CloseWithPromptAsync();
                    });

                    return 0;
                case WM_SIZE:
                    if ((nint)_coreHwnd != 0)
                    {
                        LayoutCoreChildInHostClient((int)(ushort)LOWORD(lParam), (int)(ushort)HIWORD(lParam));
                        SendMessageW(_coreHwnd, message, wParam, lParam);
                    }

                    break;
                case WM_NCHITTEST:
                    {
                        var hit = HitTestBorderAndDrag(hWnd, lParam);
                        return hit;
                    }
                case WM_NCCALCSIZE:
                    if (wParam != 0)
                        return 0;
                    break;
                case WM_GETMINMAXINFO:
                    unsafe
                    {
                        _ = DefWindowProcW(hWnd, message, wParam, lParam);
                        var mmi = (MINMAXINFO*)(nint)lParam;
                        MONITORINFO mi = default;
                        mi.cbSize = (uint)sizeof(MONITORINFO);
                        HMONITOR monitor = MonitorFromWindow(hWnd, 2u);
                        bool gotMonitor = GetMonitorInfoW(monitor, &mi) != FALSE;
                        if (gotMonitor)
                        {
                            mmi->ptMaxPosition.x = mi.rcWork.left - mi.rcMonitor.left;
                            mmi->ptMaxPosition.y = mi.rcWork.top - mi.rcMonitor.top;
                            mmi->ptMaxSize.x = mi.rcWork.right - mi.rcWork.left;
                            mmi->ptMaxSize.y = mi.rcWork.bottom - mi.rcWork.top;
                        }
                    }
                    return 0;
                case WM_NCLBUTTONDOWN:
                    return DefWindowProcW(hWnd, message, wParam, lParam);
                case WM_SYSCOMMAND:
                    return DefWindowProcW(hWnd, message, wParam, lParam);
                case WM_ENTERSIZEMOVE:
                    return DefWindowProcW(hWnd, message, wParam, lParam);
                case WM_EXITSIZEMOVE:
                    return DefWindowProcW(hWnd, message, wParam, lParam);
                case WM_NCLBUTTONUP:
                    return DefWindowProcW(hWnd, message, wParam, lParam);
                case WM_LBUTTONUP:
                    return DefWindowProcW(hWnd, message, wParam, lParam);
                case WM_SETTINGCHANGE:
                    if ((BOOL)lParam && new string((char*)lParam) == "ImmersiveColorSet")
                        NativeUtils.EnsureTitleBarTheme(hWnd);

                    goto case WM_THEMECHANGED;
                case WM_THEMECHANGED:
                    NativeUtils.TryApplyHostWindowDwmChrome(hWnd);
                    if ((nint)_coreHwnd != 0)
                    {
                        SendMessageW(_coreHwnd, message, wParam, lParam);
                    }

                    break;
                case WM_SETFOCUS:
                    SetFocus(_coreHwnd);
                    break;
                case WM_DWMNCRENDERINGCHANGED:
                    SetWindowLongW(hWnd, GWL.GWL_EXSTYLE, (BOOL)wParam ?
                        WS_EX_NOREDIRECTIONBITMAP :
                        NULL);
                    break;
                case WM_DESTROY:
                    TryRemoveCoreWindowNcForward();
                    _xamlApp = null;
                    PostQuitMessage(0);
                    break;
                default:
                    return DefWindowProcW(hWnd, message, wParam, lParam);
            }
            return 0;
        }

        /// <summary>将 CoreWindow 子 HWND 铺满父客户区；边缘命中由 <see cref="CoreWindowSubclassProc"/> 经 HTTRANSPARENT 交给父 HWND。</summary>
        private static unsafe void LayoutCoreChildInHostClient(int clientW, int clientH)
        {
            SetWindowPos(
                _coreHwnd,
                HWND.NULL,
                -CoreLeftOverlap,
                -CoreTopOverlap,
                clientW + CoreLeftOverlap + CoreRightOverlap,
                clientH + CoreTopOverlap + CoreBottomOverlap,
                SWP.SWP_NOZORDER | SWP.SWP_SHOWWINDOW | SWP.SWP_NOACTIVATE);
        }

        [DllImport("comctl32.dll", ExactSpelling = true)]
        private static extern BOOL SetWindowSubclass(HWND hWnd, nint pfnSubclass, nuint uIdSubclass, nuint dwRefData);

        [DllImport("comctl32.dll", ExactSpelling = true)]
        private static extern BOOL RemoveWindowSubclass(HWND hWnd, nint pfnSubclass, nuint uIdSubclass);

        [DllImport("comctl32.dll", ExactSpelling = true)]
        private static extern LRESULT DefSubclassProc(HWND hWnd, uint uMsg, WPARAM wParam, LPARAM lParam, nuint uIdSubclass, nuint dwRefData);

        private static unsafe void TryInstallCoreWindowNcForward()
        {
            if ((nint)_coreHwnd == 0)
            {
                return;
            }

            LoadLibraryA((sbyte*)Unsafe.AsPointer(in "comctl32.dll"u8.GetPinnableReference()));
            delegate* unmanaged<HWND, uint, WPARAM, LPARAM, nuint, nuint, LRESULT> p = &CoreWindowSubclassProc;
            _ = SetWindowSubclass(_coreHwnd, (nint)p, CoreNcSubclassId, 0);
        }

        private static unsafe void TryRemoveCoreWindowNcForward()
        {
            if ((nint)_coreHwnd == 0)
            {
                return;
            }

            delegate* unmanaged<HWND, uint, WPARAM, LPARAM, nuint, nuint, LRESULT> p = &CoreWindowSubclassProc;
            _ = RemoveWindowSubclass(_coreHwnd, (nint)p, CoreNcSubclassId);
        }

        [UnmanagedCallersOnly]
        private static LRESULT CoreWindowSubclassProc(HWND hwnd, uint msg, WPARAM wParam, LPARAM lParam, nuint uIdSubclass, nuint dwRefData)
        {
            if (msg == WM_NCHITTEST)
            {
                unsafe
                {
                    POINT pt;
                    pt.x = (short)((nuint)lParam & 0xFFFF);
                    pt.y = (short)(((nuint)lParam >> 16) & 0xFFFF);
                    if (ScreenToClient(hwnd, &pt) != FALSE)
                    {
                        RECT cr;
                        if (GetClientRect(hwnd, &cr) != FALSE)
                        {
                            int cw = cr.right - cr.left;
                            int ch = cr.bottom - cr.top;
                            int b = ResizeBorderHitPixels;
                            int rightCap = RightControlRegionWidth;

                            bool leftStrip = pt.x < b;
                            bool rightStrip = pt.x >= cw - b;
                            bool topCenterStrip = pt.y < b && pt.x >= b && pt.x < cw - rightCap;
                            bool bottomStrip = pt.y >= ch - b;
                            if (leftStrip || rightStrip || topCenterStrip || bottomStrip)
                            {
                                return (LRESULT)(nint)(-1);
                            }
                        }
                    }
                }
            }

            return DefSubclassProc(hwnd, msg, wParam, lParam, uIdSubclass, dwRefData);
        }

        private static async void CloseWithPromptAsync()
        {
            bool shouldClose = true;
            try
            {
                shouldClose = ConfirmCloseAsync is not null ? await ConfirmCloseAsync() : true;
            }
            catch { }

            Interlocked.Exchange(ref _closePromptInProgress, 0);

            if (shouldClose)
            {
                DestroyWindow(WindowHandle);
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static void Exit() => SendMessageW(WindowHandle, WM_CLOSE, 0, 0);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static void Minimize() => ShowWindow(WindowHandle, SW.SW_MINIMIZE);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static bool IsMaximized() => IsZoomed(WindowHandle) != FALSE;

        internal static void ToggleMaximize()
        {
            ShowWindow(WindowHandle, IsMaximized() ? SW.SW_RESTORE : SW.SW_MAXIMIZE);
        }

        internal static void Restart()
        {
            try
            {
                var exePath = Process.GetCurrentProcess().MainModule?.FileName ?? Environment.ProcessPath;
                if (!string.IsNullOrWhiteSpace(exePath) && File.Exists(exePath))
                {
                    // Prefer CreateProcessW: works without shell association and is reliable in hosted CoreWindow scenarios.
                    if (TryCreateProcess(exePath, AppContext.BaseDirectory, out var err))
                    {
                        Exit();
                        return;
                    }

                    NativeUi.MessageOk(
                        "Restart Failed",
                        $"Failed to restart the application automatically (CreateProcess err={err}).\r\n{exePath}\r\nPlease reopen MrmTool manually.",
                        error: true);
                    Exit();
                    return;
                }
            }
            catch
            {
                try
                {
                    NativeUi.MessageOk("Restart Failed", "Failed to restart the application automatically. Please reopen MrmTool manually.", error: true);
                }
                catch { }
            }

            Exit();
        }

        private static unsafe bool TryCreateProcess(string exePath, string? workingDirectory, out int error)
        {
            error = 0;
            try
            {
                var cmd = $"\"{exePath}\"";
                fixed (char* lpCmdLine = cmd)
                fixed (char* lpWorkDir = workingDirectory)
                {
                    STARTUPINFOW si = default;
                    si.cb = (uint)sizeof(STARTUPINFOW);

                    PROCESS_INFORMATION pi = default;

                    // Use cmdLine only; applicationName null allows Windows to parse quoted path correctly.
                    BOOL ok = CreateProcessW(
                        lpApplicationName: null,
                        lpCommandLine: lpCmdLine,
                        lpProcessAttributes: null,
                        lpThreadAttributes: null,
                        bInheritHandles: FALSE,
                        dwCreationFlags: 0,
                        lpEnvironment: null,
                        lpCurrentDirectory: workingDirectory is null ? null : lpWorkDir,
                        lpStartupInfo: &si,
                        lpProcessInformation: &pi);

                    if (ok == FALSE)
                    {
                        error = Marshal.GetLastWin32Error();
                        return false;
                    }

                    // Close handles to avoid leaks.
                    _ = CloseHandle(pi.hThread);
                    _ = CloseHandle(pi.hProcess);
                    return true;
                }
            }
            catch
            {
                error = -1;
                return false;
            }
        }

        internal static unsafe bool TryGetCursorScreenPosition(out int x, out int y)
        {
            POINT cursor;
            bool ok = GetCursorPos(&cursor) != FALSE;
            x = cursor.x;
            y = cursor.y;
            return ok;
        }

        internal static void MoveWindowToScreen(int x, int y)
        {
            SetWindowPos(
                WindowHandle,
                HWND.NULL,
                x,
                y,
                0,
                0,
                SWP.SWP_NOZORDER | SWP.SWP_NOACTIVATE | SWP.SWP_NOSIZE);
        }

        internal static unsafe bool TryGetWindowTopLeft(out int left, out int top)
        {
            RECT rc;
            bool ok = GetWindowRect(WindowHandle, &rc) != FALSE;
            left = rc.left;
            top = rc.top;
            return ok;
        }

        internal static unsafe bool TryGetWindowRect(out int left, out int top, out int width, out int height)
        {
            RECT rc;
            bool ok = GetWindowRect(WindowHandle, &rc) != FALSE;
            left = rc.left;
            top = rc.top;
            width = rc.right - rc.left;
            height = rc.bottom - rc.top;
            return ok;
        }

        internal static unsafe void BeginWindowDrag()
        {
            const nuint HTCAPTION = 2;
            const nuint SCMOVE = 0xF010;
            ReleaseCapture();

            SendMessageW(WindowHandle, WM_SYSCOMMAND, SCMOVE | HTCAPTION, 0);
        }

        private static unsafe LRESULT HitTestBorderAndDrag(HWND hWnd, LPARAM lParam)
        {
            RECT rc;
            if (GetWindowRect(hWnd, &rc) == FALSE)
                return 1;

            int x = (short)((nuint)lParam & 0xFFFF);
            int y = (short)(((nuint)lParam >> 16) & 0xFFFF);
            int relX = x - rc.left;
            int relY = y - rc.top;
            int width = rc.right - rc.left;
            int winH = rc.bottom - rc.top;

            const int HTCLIENT = 1;
            const int HTCAPTION = 2;
            const int HTLEFT = 10;
            const int HTRIGHT = 11;
            const int HTTOP = 12;
            const int HTTOPLEFT = 13;
            const int HTTOPRIGHT = 14;
            const int HTBOTTOM = 15;
            const int HTBOTTOMLEFT = 16;
            const int HTBOTTOMRIGHT = 17;

            int rb = ResizeBorderHitPixels;
            if (rb * 2 > width || rb * 2 > winH)
                rb = Math.Max(1, Math.Min(width, winH) / 8);

            int hit = HTCLIENT;
            bool maximized = IsZoomed(hWnd) != FALSE;

            if (!maximized)
            {
                if (relY >= winH - rb)
                {
                    if (relX < rb)
                        hit = HTBOTTOMLEFT;
                    else if (relX >= width - rb)
                        hit = HTBOTTOMRIGHT;
                    else
                        hit = HTBOTTOM;
                }
                else if (relY < rb)
                {
                    if (relX < rb)
                        hit = HTTOPLEFT;
                    else if (relX >= width - rb)
                        hit = HTTOPRIGHT;
                    else
                        hit = HTTOP;
                }
                else if (relX < rb)
                {
                    hit = HTLEFT;
                }
                else if (relX >= width - rb)
                {
                    hit = HTRIGHT;
                }
            }

            int captionRightExclusive = width - RightControlRegionWidth;
            int captionLeftMinX = captionRightExclusive - MinCaptionDragStripWidth;
            if (captionLeftMinX < 0)
                captionLeftMinX = 0;

            int menuExclusiveEnd = Math.Min(MenuInteractiveRegionWidth, captionLeftMinX);

            if (hit == HTCLIENT &&
                relY >= 0 &&
                relY < DragRegionHeight &&
                relX >= menuExclusiveEnd &&
                relX < captionRightExclusive)
                hit = HTCAPTION;

            return hit;
        }

        private static unsafe void TryEnableRoundedCorners(HWND hWnd)
        {
            uint preference = DwmwcpRound;
            _ = DwmSetWindowAttribute(
                hWnd,
                DwmaWindowCornerPreference,
                &preference,
                (uint)sizeof(uint));
        }

    }
}
