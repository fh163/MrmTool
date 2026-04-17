using MrmLib;
using MrmTool.Common;
using MrmTool.Models;
using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Windows.Storage;
using Windows.Storage.Pickers;
using Windows.Storage.Streams;
using Windows.UI;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Media;
using WinRT;

namespace MrmTool.Dialogs
{
    /// <summary>
    /// 代码构造 UI。不继承 <see cref="Grid"/>：见 <see cref="RenamePanel"/> 说明。
    /// </summary>
    public sealed class CandidateEditorPanel
    {
        private static string L(string key) => Common.LocalizationService.GetString(key);
        private readonly CandidateItem? _candidate;
        private readonly ResourceItem _resourceItem;
        private readonly bool _isText;

        private IBuffer? _sourceBuffer;
        private StorageFile? _fileToEmbed;

        private bool _sourceChangedOnce;
        private bool _qualifierGridLoaded;
        private bool _isCreatingNewQualifier;
        private bool _isInitializing;

        public ObservableCollection<Qualifier> Qualifiers { get; } = [];

        private readonly TaskCompletionSource<CandidateItem?> _tcs = new();

        /// <summary>仅含框架类型的可视根。</summary>
        public Grid Root { get; }

        public Task<CandidateItem?> ResultTask => _tcs.Task;

        private readonly TextBlock titleText;
        private readonly TextBlock errorText;
        private readonly Button primaryButton;
        private readonly Button secondaryButton;
        private readonly StackPanel candidateGrid;
        private readonly StackPanel qualifierGrid;
        private readonly ComboBox typeBox;
        private readonly StackPanel stringContainer;
        private readonly StackPanel pathContainer;
        private readonly StackPanel dataContainer;
        private readonly TextBox editorTextBox;
        private readonly Button browseButton;
        private readonly ComboBox sourceBox;
        private readonly TextBox pathBox;
        private readonly ListView qualifiersList;
        private readonly Button deleteQualifierButton;
        private readonly ComboBox attributeBox;
        private readonly ComboBox operatorBox;
        private readonly TextBox valueBox;
        private readonly TextBox priorityBox;
        private readonly TextBox fallbackBox;

        public CandidateEditorPanel(ResourceItem resourceItem, CandidateItem? candidate = null)
        {
            _candidate = candidate;
            _resourceItem = resourceItem;
            _isText = resourceItem.Type.IsText;

            Root = new Grid
            {
                HorizontalAlignment = HorizontalAlignment.Stretch,
                VerticalAlignment = VerticalAlignment.Stretch,
            };

            titleText = new TextBlock
            {
                Text = L("Panel.Candidate.New"),
                FontSize = 20,
                Foreground = new SolidColorBrush(Colors.Black),
                Margin = new Thickness(0, 0, 0, 4),
            };

            typeBox = new ComboBox { SelectedIndex = 0, Margin = new Thickness(0, 0, 0, 8) };
            typeBox.Items.Add(new ComboBoxItem { Content = L("Panel.Candidate.Type.String") });
            typeBox.Items.Add(new ComboBoxItem { Content = L("Panel.Candidate.Type.Path") });
            typeBox.Items.Add(new ComboBoxItem { Content = L("Panel.Candidate.Type.EmbeddedData") });
            typeBox.SelectionChanged += typeBox_SelectionChanged;

            var stringLabel = new TextBlock { Text = L("Panel.Candidate.Label.Content"), Foreground = new SolidColorBrush(Colors.Black), Margin = new Thickness(0, 2, 0, 8) };
            var importBtn = new Button { Content = L("Panel.Candidate.Button.ImportFile") };
            importBtn.Click += ImportButton_Click;
            stringContainer = new StackPanel { Visibility = Visibility.Collapsed };
            stringContainer.Children.Add(stringLabel);
            stringContainer.Children.Add(importBtn);

            var pathLabel = new TextBlock { Text = L("Panel.Candidate.Label.FilePath"), Foreground = new SolidColorBrush(Colors.Black), Margin = new Thickness(0, 0, 0, 6) };
            pathBox = new TextBox
            {
                Text = "Assets/New/Resource.png",
                IsSpellCheckEnabled = false,
            };
            pathBox.TextChanged += pathBox_TextChanged;
            pathContainer = new StackPanel { Visibility = Visibility.Collapsed };
            pathContainer.Children.Add(pathLabel);
            pathContainer.Children.Add(pathBox);

            var dataLabel = new TextBlock { Text = L("Panel.Candidate.Label.DataSource"), Foreground = new SolidColorBrush(Colors.Black), Margin = new Thickness(0, 0, 0, 6) };
            sourceBox = new ComboBox { SelectedIndex = 0 };
            sourceBox.Items.Add(new ComboBoxItem { Content = L("Panel.Candidate.Source.File") });
            sourceBox.Items.Add(new ComboBoxItem { Content = L("Panel.Candidate.Source.TextUtf8") });
            sourceBox.Items.Add(new ComboBoxItem { Content = L("Panel.Candidate.Source.TextUtf16") });
            sourceBox.SelectionChanged += sourceBox_SelectionChanged;
            dataContainer = new StackPanel { Visibility = Visibility.Collapsed };
            dataContainer.Children.Add(dataLabel);
            dataContainer.Children.Add(sourceBox);

            editorTextBox = new TextBox
            {
                MinHeight = 160,
                TextWrapping = TextWrapping.Wrap,
                AcceptsReturn = true,
                Visibility = Visibility.Collapsed,
                Margin = new Thickness(0, 6, 0, 0),
            };

            browseButton = new Button { Visibility = Visibility.Collapsed, Margin = new Thickness(0, 8, 0, 0), Content = L("Panel.Candidate.Button.SelectFile") };
            browseButton.Click += browseButton_Click;

            var typeLabel = new TextBlock { Text = L("Panel.Candidate.Label.Type"), Foreground = new SolidColorBrush(Colors.Black), Margin = new Thickness(0, 4, 0, 8) };

            var qualHeader = new TextBlock { Text = L("Panel.Candidate.Label.Qualifier"), Foreground = new SolidColorBrush(Colors.Black), Margin = new Thickness(0, 14, 0, 10) };
            deleteQualifierButton = new Button { IsEnabled = false, Content = L("Panel.Candidate.Button.Delete"), Margin = new Thickness(0, 0, 8, 0) };
            deleteQualifierButton.Click += deleteQualifierButton_Click;
            var addQualBtn = new Button { Content = L("Panel.Candidate.Button.New") };
            addQualBtn.Click += addQualifierButton_Click;
            var qualBtnRow = new StackPanel { Orientation = Orientation.Horizontal };
            qualBtnRow.Children.Add(deleteQualifierButton);
            qualBtnRow.Children.Add(addQualBtn);

            qualifiersList = new ListView
            {
                Height = 100,
                Margin = new Thickness(0, 6, 0, 0),
            };
            qualifiersList.SelectionChanged += qualifiersList_SelectionChanged;

            candidateGrid = new StackPanel();
            candidateGrid.Children.Add(typeLabel);
            candidateGrid.Children.Add(typeBox);
            candidateGrid.Children.Add(stringContainer);
            candidateGrid.Children.Add(pathContainer);
            candidateGrid.Children.Add(dataContainer);
            candidateGrid.Children.Add(editorTextBox);
            candidateGrid.Children.Add(browseButton);
            candidateGrid.Children.Add(qualHeader);
            candidateGrid.Children.Add(qualBtnRow);
            candidateGrid.Children.Add(qualifiersList);

            attributeBox = new ComboBox { Margin = new Thickness(0, 0, 0, 8) };
            foreach (var s in new[]
                     {
                         L("Qualifier.Attribute.Language"), L("Qualifier.Attribute.Contrast"), L("Qualifier.Attribute.Scale"),
                         L("Qualifier.Attribute.HomeRegion"), L("Qualifier.Attribute.TargetSize"), L("Qualifier.Attribute.LayoutDirection"),
                         L("Qualifier.Attribute.Theme"), L("Qualifier.Attribute.AlternateForm"), L("Qualifier.Attribute.DXFeatureLevel"),
                         L("Qualifier.Attribute.Configuration"), L("Qualifier.Attribute.DeviceFamily")
                     })
                attributeBox.Items.Add(new ComboBoxItem { Content = s });
            attributeBox.SelectedIndex = 0;
            attributeBox.SelectionChanged += qualifierComboBox_TextChanged;

            operatorBox = new ComboBox { Margin = new Thickness(0, 0, 0, 8) };
            foreach (var s in new[]
                     {
                         L("Qualifier.Operator.False"), L("Qualifier.Operator.True"), L("Qualifier.Operator.AttributeDefined"),
                         L("Qualifier.Operator.AttributeUndefined"), L("Qualifier.Operator.NotEqual"), L("Qualifier.Operator.NoMatch"),
                         L("Qualifier.Operator.Less"), L("Qualifier.Operator.LessOrEqual"), L("Qualifier.Operator.Greater"),
                         L("Qualifier.Operator.GreaterOrEqual"), L("Qualifier.Operator.Match"), L("Qualifier.Operator.Equal")
                     })
                operatorBox.Items.Add(new ComboBoxItem { Content = s });
            operatorBox.SelectedIndex = 10;
            operatorBox.SelectionChanged += qualifierComboBox_TextChanged;

            valueBox = new TextBox { Text = "en-US", IsSpellCheckEnabled = false, Margin = new Thickness(0, 0, 0, 8) };
            valueBox.TextChanged += qualifierBox_TextChanged;
            priorityBox = new TextBox { Text = "0", IsSpellCheckEnabled = false, Margin = new Thickness(0, 0, 0, 8) };
            priorityBox.TextChanged += qualifierBox_TextChanged;
            fallbackBox = new TextBox { Text = "0.0", IsSpellCheckEnabled = false };
            fallbackBox.TextChanged += qualifierBox_TextChanged;

            qualifierGrid = new StackPanel { Visibility = Visibility.Collapsed };
            qualifierGrid.Loaded += qualifierGrid_Loaded;
            qualifierGrid.Children.Add(new TextBlock { Text = L("Panel.Candidate.Label.Attribute"), Foreground = new SolidColorBrush(Colors.Black), Margin = new Thickness(0, 0, 0, 8) });
            qualifierGrid.Children.Add(attributeBox);
            qualifierGrid.Children.Add(new TextBlock { Text = L("Panel.Candidate.Label.Operator"), Foreground = new SolidColorBrush(Colors.Black), Margin = new Thickness(0, 0, 0, 8) });
            qualifierGrid.Children.Add(operatorBox);
            qualifierGrid.Children.Add(new TextBlock { Text = L("Panel.Candidate.Label.Value"), Foreground = new SolidColorBrush(Colors.Black), Margin = new Thickness(0, 0, 0, 8) });
            qualifierGrid.Children.Add(valueBox);
            qualifierGrid.Children.Add(new TextBlock { Text = L("Panel.Candidate.Label.Priority"), Foreground = new SolidColorBrush(Colors.Black), Margin = new Thickness(0, 0, 0, 8) });
            qualifierGrid.Children.Add(priorityBox);
            qualifierGrid.Children.Add(new TextBlock { Text = L("Panel.Candidate.Label.Fallback"), Foreground = new SolidColorBrush(Colors.Black), Margin = new Thickness(0, 0, 0, 8) });
            qualifierGrid.Children.Add(fallbackBox);

            var innerStack = new StackPanel { VerticalAlignment = VerticalAlignment.Top };
            innerStack.Children.Add(candidateGrid);
            innerStack.Children.Add(qualifierGrid);

            var scrollHost = new Grid
            {
                MaxHeight = 520,
                VerticalAlignment = VerticalAlignment.Top,
            };
            scrollHost.Children.Add(innerStack);

            errorText = new TextBlock
            {
                Foreground = new SolidColorBrush(Colors.Red),
                TextWrapping = TextWrapping.Wrap,
                Visibility = Visibility.Collapsed,
                Margin = new Thickness(0, 8, 0, 0),
            };

            primaryButton = new Button { Content = L("Panel.Candidate.Button.Create"), Margin = new Thickness(0, 0, 8, 0), MinHeight = 34, Padding = new Thickness(16, 6, 16, 6), FontSize = 14 };
            primaryButton.Click += PrimaryButton_Click;
            secondaryButton = new Button { Content = L("Common.Button.Cancel"), MinHeight = 34, Padding = new Thickness(16, 6, 16, 6), FontSize = 14 };
            secondaryButton.Click += SecondaryButton_Click;
            var buttonRow = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Right,
                Margin = new Thickness(0, 6, 0, 0),
            };
            buttonRow.Children.Add(primaryButton);
            buttonRow.Children.Add(secondaryButton);

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
                MinWidth = 820,
            };
            shell.Children.Add(grid);
            Root.Children.Add(shell);

            RefreshQualifiersList();

            if (candidate is not null)
            {
                titleText.Text = L("Panel.Candidate.Edit");
                primaryButton.Content = L("Panel.Candidate.Button.Save");
            }

            Root.Loaded += Panel_Loaded;
        }

        private void RefreshQualifiersList()
        {
            qualifiersList.ItemsSource = null;
            qualifiersList.ItemsSource = Qualifiers.Select(q => q.Format()).ToList();
        }

        private void Panel_Loaded(object sender, RoutedEventArgs e)
        {
            _isInitializing = true;
            if (_candidate is not null)
            {
                var type = _candidate.ValueType;
                var oldIndex = typeBox.SelectedIndex;
                typeBox.SelectedIndex = (int)type;
                if ((int)type == oldIndex)
                    LoadControls(type);

                switch (type)
                {
                    case ResourceValueType.String:
                        editorTextBox.Text = _candidate.StringValue;
                        break;
                    case ResourceValueType.Path:
                        pathBox.Text = _candidate.StringValue;
                        break;
                    case ResourceValueType.EmbeddedData:
                        if (_isText)
                        {
                            _sourceChangedOnce = true;
                            sourceBox.SelectedIndex = 1;
                            var data = _candidate.Candidate.DataValueReference;
                            unsafe
                            {
                                editorTextBox.Text = Encoding.UTF8.GetString(data.GetData(), (int)data.Length);
                            }
                        }
                        else
                        {
                            _sourceChangedOnce = true;
                            sourceBox.SelectedIndex = 0;
                            _sourceBuffer = _candidate.Candidate.DataValueReference;
                            browseButton.Content = L("Panel.Candidate.Button.EmbeddedDataLoaded");
                        }
                        break;
                }

                foreach (var qualifier in _candidate.Candidate.Qualifiers)
                    Qualifiers.Add(qualifier);

                RefreshQualifiersList();
            }
            else if (_resourceItem.Candidates.Count > 0)
            {
                var type = (int)_resourceItem.Candidates[0].ValueType;
                var oldIndex = typeBox.SelectedIndex;
                typeBox.SelectedIndex = type;
                if (type == oldIndex)
                    LoadControls((ResourceValueType)type);
            }
            else
            {
                LoadControls((ResourceValueType)typeBox.SelectedIndex);
            }

            ValidateInputs();
            _isInitializing = false;
        }

        private async void PrimaryButton_Click(object sender, RoutedEventArgs e)
        {
            errorText.Visibility = Visibility.Collapsed;

            if (_isCreatingNewQualifier)
            {
                try
                {
                    Qualifiers.Add(Qualifier.Create(
                        (QualifierAttribute)attributeBox.SelectedIndex,
                        (QualifierOperator)operatorBox.SelectedIndex,
                        valueBox.Text,
                        int.Parse(priorityBox.Text),
                        double.Parse(fallbackBox.Text)));
                    RefreshQualifiersList();
                    _isCreatingNewQualifier = false;
                    RestoreCandidateUI();
                }
                catch (Exception ex)
                {
                    errorText.Text = string.Format(L("Panel.Candidate.Error.AddQualifierFailed"), ex.Message);
                    errorText.Visibility = Visibility.Visible;
                }
                return;
            }

            int qualifiersCount = Qualifiers.Count;
            var candidates = _resourceItem.Candidates;

            foreach (var candidate in candidates)
            {
                if (candidate == _candidate)
                    continue;

                var candidateQualifiers = candidate.CandidateQualifiers;
                if (candidateQualifiers.Count != qualifiersCount)
                    continue;

                bool isMatch = true;
                foreach (var qualifier in Qualifiers)
                {
                    bool found = false;
                    foreach (var candidateQualifier in candidateQualifiers)
                    {
                        if (qualifier.Matches(candidateQualifier))
                        {
                            found = true;
                            break;
                        }
                    }
                    if (!found)
                    {
                        isMatch = false;
                        break;
                    }
                }

                if (isMatch)
                {
                    errorText.Text = L("Panel.Candidate.Error.DuplicateQualifierCandidate");
                    errorText.Visibility = Visibility.Visible;
                    return;
                }
            }

            try
            {
                if (_candidate is not null)
                {
                    switch ((ResourceValueType)typeBox.SelectedIndex)
                    {
                        case ResourceValueType.String:
                            _candidate.SetValue(ResourceValueType.String, editorTextBox.Text);
                            break;
                        case ResourceValueType.Path:
                            _candidate.SetValue(ResourceValueType.Path, pathBox.Text);
                            break;
                        case ResourceValueType.EmbeddedData:
                            var idx = sourceBox.SelectedIndex;
                            if (idx is 0)
                                _candidate.DataValueBuffer = _sourceBuffer ?? await FileIO.ReadBufferAsync(_fileToEmbed);
                            else
                            {
                                using var data = idx is 1 ?
                                    Encoding.UTF8.GetBuffer(editorTextBox.Text) :
                                    Encoding.Unicode.GetBuffer(editorTextBox.Text);
                                _candidate.DataValueBuffer = data;
                            }
                            break;
                    }
                    _candidate.CandidateQualifiers = Qualifiers;
                    _tcs.TrySetResult(_candidate);
                }
                else
                {
                    var name = _resourceItem.Name;
                    CandidateItem? created = null;
                    switch ((ResourceValueType)typeBox.SelectedIndex)
                    {
                        case ResourceValueType.String:
                            created = ResourceCandidate.Create(name, ResourceValueType.String, editorTextBox.Text, Qualifiers);
                            break;
                        case ResourceValueType.Path:
                            created = ResourceCandidate.Create(name, ResourceValueType.Path, pathBox.Text, Qualifiers);
                            break;
                        case ResourceValueType.EmbeddedData:
                            var idx = sourceBox.SelectedIndex;
                            if (idx is 0)
                            {
                                created = ResourceCandidate.Create(name, _sourceBuffer ??
                                    await FileIO.ReadBufferAsync(_fileToEmbed), Qualifiers);
                            }
                            else
                            {
                                using var data = idx is 1 ?
                                    Encoding.UTF8.GetBuffer(editorTextBox.Text) :
                                    Encoding.Unicode.GetBuffer(editorTextBox.Text);
                                created = ResourceCandidate.Create(name, data, Qualifiers);
                            }
                            break;
                    }
                    _tcs.TrySetResult(created);
                }
            }
            catch (Exception ex)
            {
                errorText.Text = ex.Message;
                errorText.Visibility = Visibility.Visible;
            }
        }

        private void SecondaryButton_Click(object sender, RoutedEventArgs e)
        {
            if (_isCreatingNewQualifier)
            {
                _isCreatingNewQualifier = false;
                RestoreCandidateUI();
                return;
            }
            _tcs.TrySetResult(null);
        }

        private void RestoreCandidateUI()
        {
            _isCreatingNewQualifier = false;
            qualifierGrid.Visibility = Visibility.Collapsed;
            candidateGrid.Visibility = Visibility.Visible;
            titleText.Text = _candidate is not null ? L("Panel.Candidate.Edit") : L("Panel.Candidate.New");
            primaryButton.Content = _candidate is not null ? L("Panel.Candidate.Button.Save") : L("Panel.Candidate.Button.Create");
            secondaryButton.Content = L("Common.Button.Cancel");
            ValidateInputs();
        }

        private void ValidateInputs()
        {
            bool isContentValid = true;
            var type = (ResourceValueType)typeBox.SelectedIndex;
            if (type is ResourceValueType.Path)
                isContentValid = !string.IsNullOrWhiteSpace(pathBox.Text);
            else if (type is ResourceValueType.EmbeddedData)
            {
                isContentValid = sourceBox.SelectedIndex is 0 ?
                    (_fileToEmbed is not null || _sourceBuffer is not null) :
                    !string.IsNullOrEmpty(editorTextBox?.Text);
            }
            primaryButton.IsEnabled = isContentValid;
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

        private void EditorTextBox_TextChanged(object sender, TextChangedEventArgs e) => ValidateInputs();

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
            var newType = (ResourceValueType)typeBox.SelectedIndex;
            if (newType is ResourceValueType.EmbeddedData)
            {
                // 切到“嵌入数据”时默认用文本源（UTF-8），这样可以直接嵌入当前文本内容，不必先选文件。
                if (sourceBox.SelectedIndex < 0 || sourceBox.SelectedIndex == 0)
                {
                    _isInitializing = true;
                    sourceBox.SelectedIndex = 1;
                    _isInitializing = false;
                }
            }

            LoadControls(newType);
            ValidateInputs();
        }

        private void sourceBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_isInitializing)
                return;
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
            _sourceBuffer = null;
            _sourceChangedOnce = true;
            ValidateInputs();
        }

        private void pathBox_TextChanged(object sender, TextChangedEventArgs e) => ValidateInputs();

        private async void browseButton_Click(object sender, RoutedEventArgs e)
        {
            var picker = new FileOpenPicker();
            picker.FileTypeFilter.Add("*");
            picker.CommitButtonText = L("Picker.Commit.Load");
            picker.Initialize();
            _fileToEmbed = await picker.PickSingleFileAsync();
            if (_fileToEmbed is not null)
                browseButton.Content = _fileToEmbed.Name;
            _sourceBuffer = null;
            ValidateInputs();
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

        [DynamicWindowsRuntimeCast(typeof(Qualifier))]
        private async void deleteQualifierButton_Click(object sender, RoutedEventArgs e)
        {
            var idx = qualifiersList.SelectedIndex;
            if (idx < 0 || idx >= Qualifiers.Count)
                return;
            var q = Qualifiers[idx];

            var dlg = new ContentDialog
            {
                Title = L("Panel.Candidate.DeleteQualifier.Title"),
                Content = string.Format(L("Panel.Candidate.DeleteQualifier.Body"), q.Format()),
                PrimaryButtonText = L("Panel.Candidate.DeleteQualifier.Button"),
                CloseButtonText = L("Common.Button.Cancel"),
                DefaultButton = ContentDialogButton.Primary,
                XamlRoot = Root.XamlRoot
            };
            var r = await dlg.ShowAsync();
            if (r == ContentDialogResult.Primary)
            {
                Qualifiers.RemoveAt(idx);
                RefreshQualifiersList();
            }
        }

        private void addQualifierButton_Click(object sender, RoutedEventArgs e)
        {
            _qualifierGridLoaded = false;
            _isCreatingNewQualifier = true;
            candidateGrid.Visibility = Visibility.Collapsed;
            qualifierGrid.Visibility = Visibility.Visible;
            titleText.Text = L("Panel.Candidate.AddQualifier");
            primaryButton.Content = L("Panel.Candidate.Button.Add");
            secondaryButton.Content = L("Panel.Candidate.Button.Back");
            // qualifierGrid 第一次加载后 Loaded 事件不会再次触发，这里直接标记为已就绪。
            _qualifierGridLoaded = true;
            ValidateQualifierInputs();
        }

        private void qualifiersList_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            deleteQualifierButton.IsEnabled = qualifiersList.SelectedIndex >= 0;
        }

        private void ValidateQualifierInputs()
        {
            string text;
            primaryButton.IsEnabled =
                                     attributeBox.SelectedIndex >= 0 &&
                                     operatorBox.SelectedIndex >= 0 &&
                                     !string.IsNullOrWhiteSpace(text = valueBox.Text) &&
                                     int.TryParse(priorityBox.Text, out var priority) &&
                                     double.TryParse(fallbackBox.Text, out var fallback) &&
                                     !Qualifiers.Any(x => x.Matches(
                                         (QualifierAttribute)attributeBox.SelectedIndex,
                                         (QualifierOperator)operatorBox.SelectedIndex,
                                         text,
                                         priority,
                                         fallback));
        }

        private void qualifierBox_TextChanged(object sender, TextChangedEventArgs e) => ValidateQualifierInputs();

        private void qualifierComboBox_TextChanged(object sender, SelectionChangedEventArgs e) => ValidateQualifierInputs();

        private void qualifierGrid_Loaded(object sender, RoutedEventArgs e)
        {
            _qualifierGridLoaded = true;
            ValidateQualifierInputs();
        }
    }
}
