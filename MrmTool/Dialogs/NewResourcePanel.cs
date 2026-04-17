using MrmLib;
using MrmTool.Common;
using MrmTool.Models;
using System.Diagnostics.CodeAnalysis;
using System.Text;
using System.Threading.Tasks;
using Windows.Storage;
using Windows.Storage.Pickers;
using Windows.UI;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Media;

namespace MrmTool.Dialogs
{
    /// <summary>
    /// 代码构造 UI。不继承 <see cref="Grid"/>：见 <see cref="RenamePanel"/> 说明。
    /// </summary>
    public sealed class NewResourcePanel
    {
        private static string L(string key) => Common.LocalizationService.GetString(key);
        private readonly PriFile _priFile;
        private readonly string? _parentName;
        private StorageFile? _fileToEmbed;

        private readonly TaskCompletionSource<CandidateItem?> _tcs = new();

        /// <summary>仅含框架类型的可视根。</summary>
        public Grid Root { get; }

        public Task<CandidateItem?> ResultTask => _tcs.Task;

        private readonly TextBlock titleText;
        private readonly TextBlock errorText;
        private readonly Button primaryButton;
        private readonly Image icon;
        private readonly TextBox nameBox;
        private readonly ComboBox typeBox;
        private readonly StackPanel stringContainer;
        private readonly StackPanel pathContainer;
        private readonly StackPanel dataContainer;
        private readonly TextBox editorTextBox;
        private readonly Button browseButton;
        private readonly ComboBox sourceBox;
        private readonly TextBox pathBox;

        public NewResourcePanel([NotNull] PriFile priFile, string? parentName = null)
        {
            _priFile = priFile;
            _parentName = parentName;

            Root = new Grid
            {
                HorizontalAlignment = HorizontalAlignment.Stretch,
                VerticalAlignment = VerticalAlignment.Stretch,
            };

            titleText = new TextBlock
            {
                Text = L("Panel.NewResource.Title"),
                FontSize = 20,
                Foreground = new SolidColorBrush(Colors.Black),
                Margin = new Thickness(0, 0, 0, 4),
            };

            icon = new Image
            {
                Width = 40,
                Height = 40,
                HorizontalAlignment = HorizontalAlignment.Left,
                Margin = new Thickness(0, 0, 0, 4),
                Visibility = Visibility.Collapsed,
            };

            nameBox = new TextBox
            {
                Text = "Files/New/Resource",
                IsSpellCheckEnabled = false,
                Margin = new Thickness(0, 0, 0, 8),
            };
            nameBox.TextChanged += nameBox_TextChanged;

            typeBox = new ComboBox { SelectedIndex = 0, Margin = new Thickness(0, 0, 0, 8) };
            typeBox.Items.Add(new ComboBoxItem { Content = L("Panel.Candidate.Type.String") });
            typeBox.Items.Add(new ComboBoxItem { Content = L("Panel.Candidate.Type.Path") });
            typeBox.Items.Add(new ComboBoxItem { Content = L("Panel.Candidate.Type.EmbeddedData") });
            typeBox.SelectionChanged += typeBox_SelectionChanged;

            var stringLabel = new TextBlock { Text = L("Panel.Candidate.Label.Content"), Foreground = new SolidColorBrush(Colors.Black), Margin = new Thickness(0, 0, 0, 6) };
            var importBtn = new Button { Content = L("Panel.Candidate.Button.ImportFile") };
            importBtn.Click += ImportButton_Click;
            stringContainer = new StackPanel { Visibility = Visibility.Collapsed, Children = { stringLabel, importBtn } };

            var pathLabel = new TextBlock { Text = L("Panel.Candidate.Label.FilePath"), Foreground = new SolidColorBrush(Colors.Black), Margin = new Thickness(0, 0, 0, 6) };
            pathBox = new TextBox
            {
                Text = "Assets/New/Resource.png",
                IsSpellCheckEnabled = false,
            };
            pathBox.TextChanged += pathBox_TextChanged;
            pathContainer = new StackPanel { Visibility = Visibility.Collapsed, Children = { pathLabel, pathBox } };

            var dataLabel = new TextBlock { Text = L("Panel.Candidate.Label.DataSource"), Foreground = new SolidColorBrush(Colors.Black), Margin = new Thickness(0, 0, 0, 6) };
            sourceBox = new ComboBox { SelectedIndex = 0 };
            sourceBox.Items.Add(new ComboBoxItem { Content = L("Panel.Candidate.Source.File") });
            sourceBox.Items.Add(new ComboBoxItem { Content = L("Panel.Candidate.Source.TextUtf8") });
            sourceBox.Items.Add(new ComboBoxItem { Content = L("Panel.Candidate.Source.TextUtf16") });
            sourceBox.SelectionChanged += sourceBox_SelectionChanged;
            dataContainer = new StackPanel { Visibility = Visibility.Collapsed, Children = { dataLabel, sourceBox } };

            editorTextBox = new TextBox
            {
                MinHeight = 120,
                TextWrapping = TextWrapping.Wrap,
                AcceptsReturn = true,
                Visibility = Visibility.Collapsed,
                Margin = new Thickness(0, 6, 0, 0),
            };

            browseButton = new Button { Visibility = Visibility.Collapsed, Margin = new Thickness(0, 8, 0, 0), Content = L("Panel.Candidate.Button.SelectFile") };
            browseButton.Click += browseButton_Click;

            var nameLabel = new TextBlock { Text = L("Panel.NewResource.Label.Name"), Foreground = new SolidColorBrush(Colors.Black), Margin = new Thickness(0, 0, 0, 8) };
            var typeLabel = new TextBlock { Text = L("Panel.NewResource.Label.Type"), Foreground = new SolidColorBrush(Colors.Black), Margin = new Thickness(0, 0, 0, 8) };

            var scrollStack = new StackPanel
            {
                VerticalAlignment = VerticalAlignment.Top,
                Children =
                {
                    icon, nameLabel, nameBox, typeLabel, typeBox,
                    stringContainer, pathContainer, dataContainer,
                    editorTextBox, browseButton,
                },
            };

            // 避免 ScrollViewer.Content（ContentControl）；长表单项依赖 MaxHeight 裁剪，必要时可后续换原生滚动条方案。
            var scrollHost = new Grid
            {
                MaxHeight = 480,
                VerticalAlignment = VerticalAlignment.Top,
            };
            scrollHost.Children.Add(scrollStack);

            errorText = new TextBlock
            {
                Foreground = new SolidColorBrush(Colors.Red),
                TextWrapping = TextWrapping.Wrap,
                Visibility = Visibility.Collapsed,
                Margin = new Thickness(0, 8, 0, 0),
            };

            primaryButton = new Button { Content = L("Panel.NewResource.Button.Create"), Margin = new Thickness(0, 0, 8, 0), MinHeight = 34, Padding = new Thickness(16, 6, 16, 6), FontSize = 14 };
            primaryButton.Click += PrimaryButton_Click;
            var cancelBtn = new Button { Content = L("Panel.NewResource.Button.Cancel"), MinHeight = 34, Padding = new Thickness(16, 6, 16, 6), FontSize = 14 };
            cancelBtn.Click += CancelButton_Click;
            var buttonRow = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Right,
                Margin = new Thickness(0, 8, 0, 0),
                Children = { primaryButton, cancelBtn },
            };

            var grid = new Grid();
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            Grid.SetRow(titleText, 0);
            Grid.SetRow(scrollHost, 1);
            Grid.SetRow(errorText, 2);
            Grid.SetRow(buttonRow, 3);
            grid.Children.Add(titleText);
            grid.Children.Add(scrollHost);
            grid.Children.Add(errorText);
            grid.Children.Add(buttonRow);

            var shell = new Grid
            {
                Background = new SolidColorBrush(Colors.White),
                Padding = new Thickness(14),
                MinWidth = 760,
            };
            shell.Children.Add(grid);
            Root.Children.Add(shell);

            Root.Loaded += Panel_Loaded;
        }

        private void Panel_Loaded(object sender, RoutedEventArgs e)
        {
            LoadControls((ResourceValueType)typeBox.SelectedIndex);
            if (_parentName is not null)
                nameBox.Text = $"{_parentName}/NewResource";
            TrySetIconFromName(nameBox.Text);
            ValidateInputs(nameBox.Text);
        }

        private void TrySetIconFromName(string text)
        {
            try
            {
                var type = text.DetermineResourceType();
                icon.Source = type.GetCorrespondingLargeIcon();
                icon.Visibility = Visibility.Visible;
            }
            catch
            {
                icon.Visibility = Visibility.Collapsed;
            }
        }

        private async void PrimaryButton_Click(object sender, RoutedEventArgs e)
        {
            errorText.Visibility = Visibility.Collapsed;
            var name = nameBox.Text;

            foreach (var candidate in _priFile.ResourceCandidates)
            {
                if (candidate.ResourceName.Equals(name, StringComparison.OrdinalIgnoreCase))
                {
                    errorText.Text = L("Panel.NewResource.Error.DuplicateName");
                    errorText.Visibility = Visibility.Visible;
                    return;
                }
            }

            CandidateItem? created = null;
            try
            {
                switch ((ResourceValueType)typeBox.SelectedIndex)
                {
                    case ResourceValueType.String:
                        created = ResourceCandidate.Create(name, ResourceValueType.String, editorTextBox.Text);
                        break;
                    case ResourceValueType.Path:
                        created = ResourceCandidate.Create(name, ResourceValueType.Path, pathBox.Text);
                        break;
                    case ResourceValueType.EmbeddedData:
                        var idx = sourceBox.SelectedIndex;
                        if (idx is 0)
                        {
                            if (_fileToEmbed is null)
                            {
                                errorText.Text = L("Panel.NewResource.Error.SelectEmbedFile");
                                errorText.Visibility = Visibility.Visible;
                                return;
                            }
                            created = ResourceCandidate.Create(name, await FileIO.ReadBufferAsync(_fileToEmbed));
                        }
                        else
                        {
                            using var data = idx is 1 ?
                                Encoding.UTF8.GetBuffer(editorTextBox.Text) :
                                Encoding.Unicode.GetBuffer(editorTextBox.Text);
                            created = ResourceCandidate.Create(name, data);
                        }
                        break;
                }
            }
            catch (Exception ex)
            {
                errorText.Text = ex.Message;
                errorText.Visibility = Visibility.Visible;
                return;
            }

            _tcs.TrySetResult(created);
        }

        private void CancelButton_Click(object sender, RoutedEventArgs e) => _tcs.TrySetResult(null);

        private void ValidateInputs(string name)
        {
            bool isContentValid = true;
            var type = (ResourceValueType)typeBox.SelectedIndex;
            if (type is ResourceValueType.Path)
                isContentValid = !string.IsNullOrWhiteSpace(pathBox.Text);
            else if (type is ResourceValueType.EmbeddedData)
            {
                isContentValid = sourceBox.SelectedIndex is 0 ?
                    _fileToEmbed is not null :
                    !string.IsNullOrEmpty(editorTextBox?.Text);
            }

            primaryButton.IsEnabled = isContentValid &&
                                     !string.IsNullOrWhiteSpace(name) &&
                                     !name.StartsWith('/') &&
                                     !name.EndsWith('/');
        }

        private void nameBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            var text = nameBox.Text;
            TrySetIconFromName(text);
            ValidateInputs(text);
        }

        private void LoadEditorTextBox()
        {
            editorTextBox.FontSize = Math.Max(editorTextBox.FontSize + 1, 12);
            editorTextBox.TextChanged -= EditorTextBox_TextChanged;
            editorTextBox.TextChanged += EditorTextBox_TextChanged;
        }

        private void UnloadEditorTextBox()
        {
            editorTextBox.TextChanged -= EditorTextBox_TextChanged;
            editorTextBox.Visibility = Visibility.Collapsed;
        }

        private void EditorTextBox_TextChanged(object sender, TextChangedEventArgs e) => ValidateInputs(nameBox.Text);

        private void LoadControls(ResourceValueType type)
        {
            switch (type)
            {
                case ResourceValueType.String:
                    pathContainer.Visibility = Visibility.Collapsed;
                    dataContainer.Visibility = Visibility.Collapsed;
                    browseButton.Visibility = Visibility.Collapsed;
                    stringContainer.Visibility = Visibility.Visible;
                    editorTextBox.Visibility = Visibility.Visible;
                    LoadEditorTextBox();
                    break;
                case ResourceValueType.Path:
                    dataContainer.Visibility = Visibility.Collapsed;
                    stringContainer.Visibility = Visibility.Collapsed;
                    browseButton.Visibility = Visibility.Collapsed;
                    pathContainer.Visibility = Visibility.Visible;
                    UnloadEditorTextBox();
                    break;
                case ResourceValueType.EmbeddedData:
                    pathContainer.Visibility = Visibility.Collapsed;
                    stringContainer.Visibility = Visibility.Collapsed;
                    dataContainer.Visibility = Visibility.Visible;
                    if (sourceBox.SelectedIndex is 0)
                    {
                        UnloadEditorTextBox();
                        browseButton.Visibility = Visibility.Visible;
                    }
                    else
                    {
                        browseButton.Visibility = Visibility.Collapsed;
                        editorTextBox.Visibility = Visibility.Visible;
                        LoadEditorTextBox();
                    }
                    break;
            }
        }

        private void typeBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            LoadControls((ResourceValueType)typeBox.SelectedIndex);
            ValidateInputs(nameBox.Text);
        }

        private void sourceBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (sourceBox.SelectedIndex is 0)
            {
                UnloadEditorTextBox();
                browseButton.Visibility = Visibility.Visible;
            }
            else
            {
                browseButton.Visibility = Visibility.Collapsed;
                editorTextBox.Visibility = Visibility.Visible;
                LoadEditorTextBox();
            }
            _fileToEmbed = null;
            ValidateInputs(nameBox.Text);
        }

        private void pathBox_TextChanged(object sender, TextChangedEventArgs e) => ValidateInputs(nameBox.Text);

        private async void browseButton_Click(object sender, RoutedEventArgs e)
        {
            var picker = new FileOpenPicker();
            picker.FileTypeFilter.Add("*");
            picker.CommitButtonText = L("Picker.Commit.Load");
            picker.Initialize();
            _fileToEmbed = await picker.PickSingleFileAsync();
            if (_fileToEmbed is not null)
                browseButton.Content = _fileToEmbed.Name;
            ValidateInputs(nameBox.Text);
        }

        private async void ImportButton_Click(object sender, RoutedEventArgs e)
        {
            var picker = new FileOpenPicker();
            picker.FileTypeFilter.Add("*");
            picker.CommitButtonText = L("Picker.Commit.Load");
            picker.Initialize();
            if (await picker.PickSingleFileAsync() is { } file)
            {
                try
                {
                    editorTextBox.Text = await FileIO.ReadTextAsync(file);
                }
                catch { }
            }
        }
    }
}
