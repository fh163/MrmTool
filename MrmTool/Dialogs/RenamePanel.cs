using MrmTool.Models;
using System.Threading.Tasks;
using Windows.UI;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Media;

namespace MrmTool.Dialogs
{
    /// <summary>
    /// 替代 <see cref="RenameDialog"/>（ContentDialog），在页内模态中编辑名称。
    /// 不继承 <see cref="Grid"/>：托管 CoreWindow 下将「自定义 Grid 子类」加入树可能 E_NOINTERFACE；根节点用框架 <see cref="Grid"/>。
    /// </summary>
    public sealed class RenamePanel
    {
        private static string L(string key) => Common.LocalizationService.GetString(key);
        private readonly ResourceItem _item;
        private readonly bool _simpleRename;
        private readonly TaskCompletionSource<bool> _tcs = new();
        private readonly TextBox nameBox;
        private readonly Button primaryButton;

        /// <summary>仅含框架类型的可视根，供 <c>pageModalHost.Children.Add</c> 使用。</summary>
        public Grid Root { get; }

        public Task<bool> ResultTask => _tcs.Task;

        public RenamePanel(ResourceItem item, bool simpleRename)
        {
            _item = item;
            _simpleRename = simpleRename;

            Root = new Grid
            {
                HorizontalAlignment = HorizontalAlignment.Stretch,
                VerticalAlignment = VerticalAlignment.Stretch,
            };

            var titleText = new TextBlock
            {
                Text = L("Panel.Rename.Title"),
                FontSize = 20,
                Foreground = new SolidColorBrush(Colors.Black),
                Margin = new Thickness(0, 0, 0, 12),
            };

            var nameLabel = new TextBlock { Text = L("Panel.Rename.Label.Name"), VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 8, 0) };
            nameBox = new TextBox
            {
                Text = simpleRename ? item.DisplayName : item.Name,
                IsSpellCheckEnabled = false,
                PlaceholderText = L("Panel.Rename.Placeholder"),
            };
            nameBox.TextChanged += (_, _) => ValidateInput();

            var nameRow = new Grid();
            nameRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Auto) });
            nameRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            Grid.SetColumn(nameLabel, 0);
            Grid.SetColumn(nameBox, 1);
            nameRow.Children.Add(nameLabel);
            nameRow.Children.Add(nameBox);

            primaryButton = new Button { Content = L("Panel.Rename.Button.Rename"), Margin = new Thickness(0, 0, 8, 0) };
            primaryButton.Click += Primary_Click;
            var cancelBtn = new Button { Content = L("Common.Button.Cancel") };
            cancelBtn.Click += (_, _) => _tcs.TrySetResult(false);

            var buttonRow = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Right,
                Margin = new Thickness(0, 16, 0, 0),
                Children = { primaryButton, cancelBtn },
            };

            var inner = new StackPanel { Children = { titleText, nameRow, buttonRow } };

            // 避免 Border.Child / ContentControl.Content；托管 CoreWindow 下易 E_NOINTERFACE。
            var shell = new Grid
            {
                Background = new SolidColorBrush(Colors.White),
                Padding = new Thickness(16),
                MinWidth = 420,
            };
            shell.Children.Add(inner);
            Root.Children.Add(shell);

            Root.Loaded += (_, _) => ValidateInput();
        }

        private void ValidateInput()
        {
            var name = nameBox.Text;
            var ok = !string.IsNullOrWhiteSpace(name) &&
                     (_simpleRename ? !name.Contains('/', StringComparison.Ordinal) :
                         !name.StartsWith('/') &&
                         !name.EndsWith('/'));
            primaryButton.IsEnabled = ok;
        }

        private void Primary_Click(object sender, RoutedEventArgs e)
        {
            if (!primaryButton.IsEnabled)
                return;

            if (_simpleRename)
                _item.DisplayName = nameBox.Text;
            else
                _item.Name = nameBox.Text;

            _tcs.TrySetResult(true);
        }
    }
}
