using System.Collections.Generic;
using System;
using System.Threading.Tasks;
using Windows.UI;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Media;

namespace MrmTool.Dialogs
{
    /// <summary>
    /// 批量导入/导出语言筛选面板。
    /// </summary>
    public sealed class BatchLanguagePanel
    {
        private static string L(string key) => Common.LocalizationService.GetString(key);
        private readonly TaskCompletionSource<string?> _tcs = new();
        private readonly ComboBox _languageBox;

        public Grid Root { get; }
        public Task<string?> ResultTask => _tcs.Task;

        public BatchLanguagePanel(string title, string confirmText, IReadOnlyList<string> languageValues, string? defaultLanguage = null)
        {
            Root = new Grid
            {
                HorizontalAlignment = HorizontalAlignment.Stretch,
                VerticalAlignment = VerticalAlignment.Stretch,
            };

            var titleText = new TextBlock
            {
                Text = title,
                FontSize = 20,
                Foreground = new SolidColorBrush(Colors.Black),
                Margin = new Thickness(0, 0, 0, 8),
            };

            var desc = new TextBlock
            {
                Text = L("Panel.BatchLanguage.Desc"),
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 0, 0, 10),
                Foreground = new SolidColorBrush(Colors.Black),
            };

            _languageBox = new ComboBox
            {
                Margin = new Thickness(0, 0, 0, 12),
                SelectedIndex = 0,
            };
            _languageBox.Items.Add(new ComboBoxItem { Content = L("Panel.BatchLanguage.All") });
            foreach (var lang in languageValues)
                _languageBox.Items.Add(new ComboBoxItem { Content = lang });
            if (!string.IsNullOrWhiteSpace(defaultLanguage))
            {
                for (var i = 1; i < _languageBox.Items.Count; i++)
                {
                    if (_languageBox.Items[i] is ComboBoxItem cb &&
                        cb.Content is string lang &&
                        string.Equals(lang, defaultLanguage, StringComparison.OrdinalIgnoreCase))
                    {
                        _languageBox.SelectedIndex = i;
                        break;
                    }
                }
            }

            var confirm = new Button
            {
                Content = confirmText,
                Margin = new Thickness(0, 0, 8, 0),
                MinHeight = 34,
                Padding = new Thickness(16, 6, 16, 6),
            };
            confirm.Click += (_, _) =>
            {
                if (_languageBox.SelectedIndex <= 0)
                {
                    _tcs.TrySetResult(null);
                    return;
                }

                if (_languageBox.SelectedItem is ComboBoxItem item &&
                    item.Content is string lang &&
                    !string.IsNullOrWhiteSpace(lang))
                {
                    _tcs.TrySetResult(lang);
                    return;
                }

                _tcs.TrySetResult(null);
            };

            var cancel = new Button
            {
                Content = L("Common.Button.Cancel"),
                MinHeight = 34,
                Padding = new Thickness(16, 6, 16, 6),
            };
            cancel.Click += (_, _) => _tcs.TrySetResult(string.Empty);

            var btnRow = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Right,
            };
            btnRow.Children.Add(confirm);
            btnRow.Children.Add(cancel);

            var stack = new StackPanel
            {
                VerticalAlignment = VerticalAlignment.Top,
            };
            stack.Children.Add(titleText);
            stack.Children.Add(desc);
            stack.Children.Add(_languageBox);
            stack.Children.Add(btnRow);

            var shell = new Grid
            {
                Background = new SolidColorBrush(Colors.White),
                Padding = new Thickness(14),
                MinWidth = 560,
            };
            shell.Children.Add(stack);
            Root.Children.Add(shell);
        }
    }
}
