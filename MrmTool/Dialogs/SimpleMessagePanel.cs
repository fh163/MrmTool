using MrmTool.Common;
using System.Collections.Generic;
using System.Threading.Tasks;
using Windows.UI;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Media;

namespace MrmTool.Dialogs
{
    /// <summary>页内模态提示（替代 Win32 MessageBox），根节点为框架 <see cref="Grid"/>。</summary>
    public sealed class SimpleMessagePanel
    {
        private static string L(string key) => LocalizationService.GetString(key);
        private readonly TaskCompletionSource<int> _tcs = new();

        public Grid Root { get; }

        public Task<int> ResultTask => _tcs.Task;

        private SimpleMessagePanel(string title, string body, bool error, IReadOnlyList<(string Label, int Code)> buttons)
        {
            Root = new Grid
            {
                HorizontalAlignment = HorizontalAlignment.Stretch,
                VerticalAlignment = VerticalAlignment.Stretch,
            };

            var titleBlock = new TextBlock
            {
                Text = title,
                FontSize = 17,
                FontWeight = Windows.UI.Text.FontWeights.SemiBold,
                Foreground = new SolidColorBrush(error ? Colors.DarkRed : Colors.Black),
                Margin = new Thickness(0, 0, 0, 8),
            };

            var bodyBlock = new TextBlock
            {
                Text = body,
                TextWrapping = TextWrapping.Wrap,
                Foreground = new SolidColorBrush(Colors.Black),
            };
            var scroll = new ScrollViewer
            {
                MaxHeight = 380,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                Content = bodyBlock,
            };

            var buttonRow = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Right,
                Margin = new Thickness(0, 12, 0, 0),
            };

            for (var i = 0; i < buttons.Count; i++)
            {
                var (label, code) = buttons[i];
                var b = new Button
                {
                    Content = label,
                    Margin = new Thickness(i == 0 ? 0 : 12, 0, 0, 0),
                    MinWidth = 88,
                    MinHeight = 36,
                    Padding = new Thickness(16, 8, 16, 8),
                    FontSize = 14,
                };
                var captured = code;
                b.Click += (_, _) => _tcs.TrySetResult(captured);
                buttonRow.Children.Add(b);
            }

            var inner = new StackPanel { Children = { titleBlock, scroll, buttonRow } };
            var shell = new Grid
            {
                Background = new SolidColorBrush(Colors.White),
                Padding = new Thickness(16, 14, 16, 14),
                MinWidth = 380,
                MaxWidth = 560,
            };
            shell.Children.Add(inner);
            Root.Children.Add(shell);
        }

        public static SimpleMessagePanel CreateOk(string title, string body, bool error = false) =>
            new(title, body, error, [(L("Common.Button.Ok"), NativeUi.IDOK)]);

        /// <summary>左「确认」、右「取消」。确认 = <see cref="NativeUi.IDYES"/>，取消 = <see cref="NativeUi.IDNO"/>。</summary>
        public static SimpleMessagePanel CreateConfirmCancel(string title, string body) =>
            new(title, body, error: false, [(L("Common.Button.Confirm"), NativeUi.IDYES), (L("Common.Button.Cancel"), NativeUi.IDNO)]);

        /// <summary>左「确认」保存并关闭，中「取消」返回程序，右「退出」不保存并关闭（与原先 是/取消/否 语义一致）。</summary>
        public static SimpleMessagePanel CreateSaveCancelExit(string title, string body) =>
            new(title, body, error: false, [(L("Common.Button.Confirm"), NativeUi.IDYES), (L("Common.Button.Cancel"), NativeUi.IDCANCEL), (L("Common.Button.Exit"), NativeUi.IDNO)]);
    }
}
