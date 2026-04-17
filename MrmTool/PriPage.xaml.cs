using Common;
using MrmLib;
using MrmTool.Common;
using MrmTool.Dialogs;
using MrmTool.Models;
using MrmTool.Scintilla;
using MrmTool.SVG;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Net;
using System.Numerics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;
using System.IO;
using Windows.ApplicationModel.DataTransfer;
using Windows.Foundation;
using Windows.Storage;
using Windows.Storage.Pickers;
using Windows.Storage.Streams;
using Windows.UI.Core;
using Windows.UI.Composition;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Controls.Primitives;
using Windows.UI.Xaml.Hosting;
using Windows.UI.Xaml.Input;
using Windows.UI.Xaml.Media;
using Windows.UI.Xaml.Media.Animation;
using Windows.UI.Xaml.Media.Imaging;
using Windows.UI.Xaml.Navigation;
using CommunityToolkit.WinUI;
using WinRT;
using WinUIEditor;
using UnicodeEncoding = Windows.Storage.Streams.UnicodeEncoding;

namespace MrmTool
{
    /// <summary>
    /// An empty page that can be used on its own or navigated to within a Frame.
    /// </summary>
    public sealed partial class PriPage : Page
    {
        private static string L(string key) => LocalizationService.GetString(key);
        private PriFile? _pri;
        private StorageFile? _currentFile;
        private StorageFolder? _rootFolder;
        private ResourceItem? _selectedResource;
        private CandidateItem? _selectedCandidate;
        private bool _isDirty;

        private bool _useWebViewForSvg = false;

        public ObservableCollection<ResourceItem> ResourceItems { get; } = [];

        private MenuFlyout? _editorMenu;
        private MenuFlyout? _valueTextBoxContextMenu;
        private MenuFlyout? _treeSearchBoxContextMenu;
        private Windows.UI.Xaml.DispatcherTimer? _valueEditCommitTimer;
        private string? _pendingValueText;
        private Windows.UI.Xaml.DispatcherTimer? _textBoxCommitTimer;
        private string? _lastBatchLanguageValue;

        private string? _treeSearchCycleQuery;
        private ResourceItem? _treeSearchCycleScopeFolder;
        private ResourceItem? _treeSearchPinnedScopeFolder;
        private int _treeSearchCycleIndex = -1;
        private bool _manualDragActive;
        private int _manualDragStartCursorX;
        private int _manualDragStartCursorY;
        private int _manualDragStartWindowLeft;
        private int _manualDragStartWindowTop;

        public PriPage()
        {
            InitializeComponent();
            _valueTextBoxContextMenu = CreateValueTextBoxContextMenu();
            valueTextBox.ContextFlyout = _valueTextBoxContextMenu;
            _treeSearchBoxContextMenu = CreateTreeSearchBoxContextMenu();
            treeSearchBox.ContextFlyout = _treeSearchBoxContextMenu;
            UpdateLanguageMenuState();
            treeView.Loaded += (_, _) => EnsureTreeViewScrollBarAlwaysVisible();
            Loaded += (_, _) =>
            {
                UpdateWindowButtons();
                UpdateEmptyPriUi();
                // Capture top bar drag even if child controls mark events as handled.
                CaptionBarRoot.AddHandler(UIElement.PointerPressedEvent, new PointerEventHandler(CaptionBar_PointerPressed), true);
            };
            CaptionBarRoot.PointerMoved += CaptionBarRoot_PointerMoved;
            CaptionBarRoot.PointerReleased += CaptionBarRoot_PointerReleased;
            CaptionBarRoot.PointerCanceled += CaptionBarRoot_PointerCanceled;
        }

        [DynamicWindowsRuntimeCast(typeof(PriFile))]
        [DynamicWindowsRuntimeCast(typeof(StorageFile))]
        protected override void OnNavigatedTo(NavigationEventArgs e)
        {
            base.OnNavigatedTo(e);

            if (e.Parameter is (PriFile pri, StorageFile file))
            {
                LoadPri(pri);
                _currentFile = file;
                UpdateCurrentFilePathText();
            }

            Program.ConfirmCloseAsync = ConfirmCloseAsync;
            EnsureTreeViewScrollBarAlwaysVisible();
            UpdateEmptyPriUi();
        }

        /// <summary>未加载 PRI 时显示左侧大按钮占位；加载后显示搜索与资源树。</summary>
        private void UpdateEmptyPriUi()
        {
            bool empty = _pri is null;
            emptyPriPlaceholder.Visibility = empty ? Visibility.Visible : Visibility.Collapsed;
            leftTreeHeaderRow.Visibility = empty ? Visibility.Collapsed : Visibility.Visible;
            treeView.Visibility = empty ? Visibility.Collapsed : Visibility.Visible;

            var hasPri = !empty;
            SaveMenuFlyoutItem.IsEnabled = hasPri && _currentFile is not null;
            SaveAsMenuFlyoutItem.IsEnabled = hasPri;
            NewResourceMenuFlyoutItem.IsEnabled = hasPri;
            BatchImportMenuFlyoutItem.IsEnabled = hasPri;
            BatchExportMenuFlyoutItem.IsEnabled = hasPri;
        }

        private void EnsureTreeViewScrollBarAlwaysVisible()
        {
            if (treeView is not Microsoft.UI.Xaml.Controls.TreeView mux)
                return;

            void ApplyOverrides()
            {
                try
                {
                    if (mux.ListControl is not FrameworkElement list)
                        return;

                    ScrollViewer.SetVerticalScrollBarVisibility(list, ScrollBarVisibility.Visible);
                    ScrollViewer.SetHorizontalScrollBarVisibility(list, ScrollBarVisibility.Disabled);
                    ScrollViewer.SetIsVerticalRailEnabled(list, false);

                    var sv = list.FindDescendant<ScrollViewer>();
                    if (sv is null)
                        return;

                    sv.VerticalScrollBarVisibility = ScrollBarVisibility.Visible;
                    sv.HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled;
                    sv.IsVerticalRailEnabled = false;

                    var vbar = sv.FindDescendant<ScrollBar>(b => b.Orientation == Orientation.Vertical);
                    if (vbar is null)
                        return;

                    vbar.Visibility = Visibility.Visible;
                    vbar.Opacity = 1d;
                    vbar.IndicatorMode = ScrollingIndicatorMode.MouseIndicator;
                }
                catch
                {
                }
            }

            ApplyOverrides();
            _ = Dispatcher.RunAsync(Windows.UI.Core.CoreDispatcherPriority.Low, ApplyOverrides);
        }

        private async Task<bool> ConfirmCloseAsync()
        {
            if (!_isDirty)
                return true;

            var r = await ShowInPageMessageYesNoCancelAsync(L("Dialog.Title.ExitPrompt"), L("Dialog.Body.ExitPrompt"));

            if (r == NativeUi.IDYES)
            {
                if (_currentFile is null)
                    return true;

                await SavePri(_currentFile);
                return true;
            }

            if (r == NativeUi.IDNO)
                return true;

            return false;
        }

        private ResourceItem GetOrAddResourceItem(string name)
        {
            string[] split = name.SplitIntoResourceNames();

            ResourceItem? currentParent = null;
            foreach (var item in split)
            {
                ObservableCollection<ResourceItem> currentList = currentParent?.Children ?? ResourceItems;
                currentParent = currentList.FirstOrDefault(i => i.Name.Equals(item, StringComparison.Ordinal));
                if (currentParent is null)
                {
                    currentParent = new ResourceItem(item, currentList);
                    currentList.Add(currentParent);
                }
            }

            return currentParent!;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void Clear()
        {
            ResourceItems.Clear();
            // TODO: do we need to do any other cleanup here?
        }

        private void LoadPri(PriFile pri)
        {
            Clear();

            _pri = pri;

            foreach (var candidate in pri.ResourceCandidates)
            {
                var item = GetOrAddResourceItem(candidate.ResourceName);
                item.Candidates.Add(candidate);
            }

            SortResourceEntriesRecursively(ResourceItems);
        }

        private async Task TryLoadPri(StorageFile file)
        {
            try
            {
                LoadPri(await PriFile.LoadAsync(file));
                _currentFile = file;
                _rootFolder = null;
                UpdateCurrentFilePathText();
                UpdateEmptyPriUi();
            }
            catch (Exception ex)
            {
                CrashLogger.ShowErrorDialog(L("Dialog.Title.LoadFailed"), ex, "PriPage.TryLoadPri");
            }
        }

        private async void Open_Click(object sender, RoutedEventArgs e)
        {
            if (_isDirty)
            {
                var action = await ShowInPageMessageYesNoCancelAsync(L("Dialog.Title.OpenPrompt"), L("Dialog.Body.OpenPrompt"));
                if (action == NativeUi.IDCANCEL)
                    return;
                if (action == NativeUi.IDYES && _currentFile is not null)
                    await SavePri(_currentFile);
            }

            try
            {
                FileOpenPicker picker = new();
                picker.FileTypeFilter.Add(".pri");
                picker.CommitButtonText = L("Picker.Commit.Load");
                picker.Initialize();
                await Dispatcher.RunAsync(Windows.UI.Core.CoreDispatcherPriority.Normal, () => { });
                var file = await picker.PickSingleFileAsync();
                if (file is not null)
                    await TryLoadPri(file);
            }
            catch (Exception ex)
            {
                CrashLogger.ShowErrorDialog(L("Dialog.Title.OpenFailed"), ex, "PriPage.Open_Click");
            }
        }

        private async Task SavePri(StorageFile file)
        {
            try
            {
                using var stream = await file.OpenAsync(FileAccessMode.ReadWrite);
                stream.Size = 0;

                await _pri!.WriteAsync(stream);
                _currentFile = file;
                UpdateCurrentFilePathText();
                _isDirty = false;
            }
            catch (Exception ex)
            {
                CrashLogger.ShowErrorDialog(L("Dialog.Title.SaveFailed"), ex, "PriPage.SavePri");
            }
        }

        private async void Save_Click(object sender, RoutedEventArgs e)
        {
            if (_pri is null || _currentFile is null)
                return;
            await SavePri(_currentFile);
        }

        private async void SaveAs_Click(object sender, RoutedEventArgs e)
        {
            if (_pri is null)
                return;
            FileSavePicker picker = new();
            picker.FileTypeChoices.Add("PRI File", new List<string>() { ".pri" });
            picker.CommitButtonText = L("Picker.Commit.Save");
            picker.Initialize();

            if (await picker.PickSaveFileAsync() is { } file)
            {
                await SavePri(file);
            }
        }

        [DynamicWindowsRuntimeCast(typeof(MenuFlyoutItem))]
        private async void AddResource_Click(object sender, RoutedEventArgs e)
        {
            var parent = sender is MenuFlyoutItem item &&
                         item.DataContext is ResourceItem resourceItem ?
                resourceItem.Name :
                         (ResourceItem)treeView.SelectedItem is ResourceItem resItem && resItem.IsFolder ?
                resItem.Name : null;

            try
            {
                if (await ShowNewResourceEditorAsync(parent) is { } candidate)
                {
                    var newItem = GetOrAddResourceItem(candidate.Candidate.ResourceName);
                    newItem.Candidates.Add(candidate);
                    _pri!.ResourceCandidates.Add(candidate.Candidate);
                    SortResourceEntriesRecursively(ResourceItems);
                    _isDirty = true;
                }
            }
            catch (Exception ex)
            {
                CrashLogger.ShowErrorDialog(L("Dialog.Title.CreateResourceFailed"), ex, "AddResource_Click");
            }
        }

        private void AttachPageModalChild(UIElement child)
        {
            pageModalHost.Children.Clear();
            try
            {
                pageModalHost.Children.Add(child);
            }
            catch (Exception ex)
            {
                CrashLogger.LogModalStep("AttachPageModalChild.Children.Add", ex);
                throw;
            }
        }

        private void SafeUnloadObjectForModal(string debugName, DependencyObject? obj)
        {
            if (obj is null)
                return;
            try
            {
                UnloadObject(obj);
            }
            catch (Exception ex)
            {
                CrashLogger.LogModalStep($"UnloadObject({debugName})", ex);
            }
        }

        private void ClearPageModalChild()
        {
            pageModalHost.Children.Clear();
        }

        private async Task<CandidateItem?> ShowNewResourceEditorAsync(string? parent)
        {
            if (_pri is null)
                return null;

            CrashLogger.LogModalStep($"ShowNewResourceEditorAsync: before new NewResourcePanel parent={parent ?? "(null)"}");
            var panel = new NewResourcePanel(_pri, parent);
            CrashLogger.LogModalStep("ShowNewResourceEditorAsync: after new NewResourcePanel");
            UnloadHwndBackedPreviewHostsForPageModal();
            CrashLogger.LogModalStep("ShowNewResourceEditorAsync: before AttachPageModalChild");
            AttachPageModalChild(panel.Root);
            CrashLogger.LogModalStep("ShowNewResourceEditorAsync: after AttachPageModalChild");
            pageModalOverlay.Visibility = Visibility.Visible;
            try
            {
                return await panel.ResultTask;
            }
            finally
            {
                pageModalOverlay.Visibility = Visibility.Collapsed;
                ClearPageModalChild();
                await RestorePreviewAfterPageModalAsync();
            }
        }

        private async Task<CandidateItem?> ShowCandidateEditorAsync(ResourceItem resourceItem, CandidateItem? candidate)
        {
            CrashLogger.LogModalStep("ShowCandidateEditorAsync: before new CandidateEditorPanel");
            var panel = new CandidateEditorPanel(resourceItem, candidate);
            CrashLogger.LogModalStep("ShowCandidateEditorAsync: after new CandidateEditorPanel");
            UnloadHwndBackedPreviewHostsForPageModal();
            CrashLogger.LogModalStep("ShowCandidateEditorAsync: before AttachPageModalChild");
            AttachPageModalChild(panel.Root);
            CrashLogger.LogModalStep("ShowCandidateEditorAsync: after AttachPageModalChild");
            pageModalOverlay.Visibility = Visibility.Visible;
            try
            {
                return await panel.ResultTask;
            }
            finally
            {
                pageModalOverlay.Visibility = Visibility.Collapsed;
                ClearPageModalChild();
                await RestorePreviewAfterPageModalAsync();
            }
        }

        private async Task<bool> ShowRenameResourceModalAsync(ResourceItem resourceItem, bool simpleRename)
        {
            CrashLogger.LogModalStep($"ShowRenameResourceModalAsync: before new RenamePanel simpleRename={simpleRename} name={resourceItem.Name}");
            var panel = new RenamePanel(resourceItem, simpleRename);
            CrashLogger.LogModalStep("ShowRenameResourceModalAsync: after new RenamePanel");
            UnloadHwndBackedPreviewHostsForPageModal();
            CrashLogger.LogModalStep("ShowRenameResourceModalAsync: before AttachPageModalChild");
            AttachPageModalChild(panel.Root);
            CrashLogger.LogModalStep("ShowRenameResourceModalAsync: after AttachPageModalChild");
            pageModalOverlay.Visibility = Visibility.Visible;
            try
            {
                return await panel.ResultTask;
            }
            finally
            {
                pageModalOverlay.Visibility = Visibility.Collapsed;
                ClearPageModalChild();
                await RestorePreviewAfterPageModalAsync();
            }
        }

        /// <summary>
        /// CodeEditor / WebView / 图像等预览使用独立 HWND，会叠在全页 XAML 模态层之上，导致仅见遮罩、不见面板。
        /// 打开模态前卸载，关闭后通过 <see cref="RestorePreviewAfterPageModalAsync"/> 恢复。
        /// </summary>
        private void UnloadHwndBackedPreviewHostsForPageModal()
        {
            // XamlMarkupHelper.UnloadObject 在托管 CoreWindow 下对部分 HWND 控件可能 E_NOINTERFACE；失败时忽略以免阻断模态。
            SafeUnloadObjectForModal(nameof(webView), webView);
            SafeUnloadObjectForModal(nameof(valueTextEditor), valueTextEditor);
            SafeUnloadObjectForModal(nameof(imagePreviewerContainer), imagePreviewerContainer);
            SafeUnloadObjectForModal(nameof(svgPreviewerContainer), svgPreviewerContainer);
        }

        private async Task RestorePreviewAfterPageModalAsync()
        {
            if (_selectedCandidate is not null)
                await DisplayCandidate(_selectedCandidate);
            else
                UnloadAllPreviewElements();
        }

        [DynamicWindowsRuntimeCast(typeof(MenuFlyoutItem))]
        private async void RemoveResources_Click(object sender, RoutedEventArgs e)
        {
            if (_pri is null)
                return;

            var resourceItem = sender is MenuFlyoutItem item && item.DataContext is ResourceItem ctx
                ? ctx
                : treeView.SelectedItem as ResourceItem;

            if (resourceItem is null)
                return;

            if (!await ShowInPageMessageYesNoAsync(
                    L("Dialog.Title.DeleteConfirm"),
                    string.Format(L("Dialog.Body.DeleteResource"), resourceItem.DisplayName)))
                return;

            if (resourceItem == _selectedResource)
            {
                _selectedResource = null;
                _selectedCandidate = null;
                RemoveResourcesItem.IsEnabled = false;
                UnloadAllPreviewElements();
            }

            resourceItem.Delete(_pri);
            _isDirty = true;
        }

        private async Task PickRootFolder()
        {
            FolderPicker picker = new();
            picker.FileTypeFilter.Add("*");
            picker.CommitButtonText = L("Picker.Commit.SelectRoot");
            picker.Initialize();

            if (await picker.PickSingleFolderAsync() is { } folder)
            {
                _rootFolder = folder;
            }
        }

        private async void SetRootFolder_Click(object sender, RoutedEventArgs e)
        {
            await PickRootFolder();

            if (_rootFolder is not null && candidatesList.SelectedItem is CandidateItem item && item.Candidate.ValueType is ResourceValueType.Path)
            {
                await DisplayCandidate(item);
            }
        }

        private async void EmbedPathResources_Click(object sender, RoutedEventArgs e)
        {
            if (_rootFolder is null)
            {
                await PickRootFolder();

                if (_rootFolder is null)
                {
                    await ShowInPageMessageOkAsync(L("Dialog.Error.Generic"), L("Dialog.Error.SelectRootBeforeEmbed"), error: true);
                    return;
                }
            }

            await _pri!.ReplacePathCandidatesWithEmbeddedDataAsync(_rootFolder);
        }

        private void Exit_Click(object sender, RoutedEventArgs e)
        {
            Program.Exit();
        }

        private void MinimizeWindow_Click(object sender, RoutedEventArgs e)
        {
            Program.Minimize();
        }

        private void ToggleMaximizeWindow_Click(object sender, RoutedEventArgs e)
        {
            Program.ToggleMaximize();
            UpdateWindowButtons();
        }

        private void CloseWindow_Click(object sender, RoutedEventArgs e)
        {
            Program.Exit();
        }

        private void CaptionBar_PointerPressed(object sender, PointerRoutedEventArgs e)
        {
            if (e.Handled)
            {
                return;
            }

            var point = e.GetCurrentPoint((UIElement)sender);
            if (!point.Properties.IsLeftButtonPressed)
                return;

            if (IsInteractiveTitleBarElement(e.OriginalSource as DependencyObject))
            {
                return;
            }

            StartManualDrag(sender as UIElement, e);
            e.Handled = true;
        }

        private void CaptionBarBlankDrag_PointerPressed(object sender, PointerRoutedEventArgs e)
        {
            var point = e.GetCurrentPoint((UIElement)sender);
            if (!point.Properties.IsLeftButtonPressed)
                return;

            StartManualDrag(sender as UIElement, e);
            e.Handled = true;
        }

        private void StartManualDrag(UIElement? source, PointerRoutedEventArgs e)
        {
            if (!Program.TryGetCursorScreenPosition(out int cursorX, out int cursorY))
                return;
            if (!Program.TryGetWindowTopLeft(out int windowLeft, out int windowTop))
                return;
            bool wasMaximized = Program.IsMaximized();

            if (wasMaximized)
                return;

            _manualDragActive = true;
            _manualDragStartCursorX = cursorX;
            _manualDragStartCursorY = cursorY;
            _manualDragStartWindowLeft = windowLeft;
            _manualDragStartWindowTop = windowTop;

            if (source is not null)
                source.CapturePointer(e.Pointer);
        }

        private void StopManualDrag()
        {
            if (!_manualDragActive)
                return;
            _manualDragActive = false;
        }

        private void CaptionBarRoot_PointerMoved(object sender, PointerRoutedEventArgs e)
        {
            if (!_manualDragActive)
                return;

            var point = e.GetCurrentPoint((UIElement)sender);
            if (!point.Properties.IsLeftButtonPressed)
            {
                StopManualDrag();
                return;
            }

            if (!Program.TryGetCursorScreenPosition(out int cursorX, out int cursorY))
                return;

            int dx = cursorX - _manualDragStartCursorX;
            int dy = cursorY - _manualDragStartCursorY;

            int targetX = _manualDragStartWindowLeft + dx;
            int targetY = _manualDragStartWindowTop + dy;
            Program.MoveWindowToScreen(targetX, targetY);
            e.Handled = true;
        }

        private void CaptionBarRoot_PointerReleased(object sender, PointerRoutedEventArgs e)
        {
            StopManualDrag();
        }

        private void CaptionBarRoot_PointerCanceled(object sender, PointerRoutedEventArgs e)
        {
            StopManualDrag();
        }

        private bool IsInteractiveTitleBarElement(DependencyObject? source)
        {
            bool insideTopMenuBar = false;
            bool hitMenuItem = false;

            for (var node = source; node is not null; node = VisualTreeHelper.GetParent(node))
            {
                if (ReferenceEquals(node, TopMenuBar))
                {
                    insideTopMenuBar = true;
                }

                if (node is Microsoft.UI.Xaml.Controls.MenuBarItem)
                {
                    hitMenuItem = true;
                }

                if (node is ButtonBase ||
                    node is TextBox ||
                    node is ComboBox ||
                    node is ToggleSwitch)
                {
                    if (!insideTopMenuBar || hitMenuItem)
                        return true;
                }
            }

            if (insideTopMenuBar && hitMenuItem)
                return true;

            return false;
        }

        private void UpdateWindowButtons()
        {
            maximizeRestoreIcon.Glyph = Program.IsMaximized() ? "\uE923" : "\uE922";
        }

        private void UpdateCurrentFilePathText()
        {
            if (_currentFile is null)
            {
                CurrentFilePathTextBlock.Text = L("Status.NoFileOpened");
                ToolTipService.SetToolTip(CurrentFilePathTextBlock, null);
                return;
            }

            CurrentFilePathTextBlock.Text = _currentFile.Path;
            ToolTipService.SetToolTip(CurrentFilePathTextBlock, _currentFile.Path);
        }

        private static void SetCaptionButtonVisual(Button button, bool pointerOver, bool pressed)
        {
            bool isClose = button.Name == "closeButton";
            if (isClose)
            {
                // 悬停：稍深的浅红底；按下与悬停相同（不显式加深）
                var hoverBg = Windows.UI.Color.FromArgb(255, 245, 198, 204);
                bool hover = pointerOver || pressed;
                button.Background = new SolidColorBrush(hover ? hoverBg : Windows.UI.Color.FromArgb(0, 0, 0, 0));
                button.Foreground = new SolidColorBrush(hover ?
                    Windows.UI.Color.FromArgb(255, 180, 30, 45) :
                    Windows.UI.Color.FromArgb(255, 224, 108, 117));
                return;
            }

            button.Background = new SolidColorBrush(pressed ? Windows.UI.Color.FromArgb(255, 196, 196, 196) :
                pointerOver ? Windows.UI.Color.FromArgb(255, 214, 214, 214) :
                Windows.UI.Color.FromArgb(0, 0, 0, 0));
            button.Foreground = new SolidColorBrush(pointerOver || pressed ?
                Windows.UI.Color.FromArgb(255, 0, 0, 0) :
                Windows.UI.Color.FromArgb(255, 43, 43, 43));
        }

        private void CaptionButton_PointerEntered(object sender, PointerRoutedEventArgs e)
        {
            if (sender is Button button)
                SetCaptionButtonVisual(button, pointerOver: true, pressed: false);
        }

        private void CaptionButton_PointerExited(object sender, PointerRoutedEventArgs e)
        {
            if (sender is Button button)
                SetCaptionButtonVisual(button, pointerOver: false, pressed: false);
        }

        private void CaptionButton_PointerPressed(object sender, PointerRoutedEventArgs e)
        {
            if (sender is Button button)
                SetCaptionButtonVisual(button, pointerOver: true, pressed: true);
        }

        private void CaptionButton_PointerReleased(object sender, PointerRoutedEventArgs e)
        {
            if (sender is Button button)
                SetCaptionButtonVisual(button, pointerOver: true, pressed: false);
        }

        private void Grid_DragOver(object sender, DragEventArgs e)
        {
            var view = e.DataView;
            if (view.Contains(StandardDataFormats.StorageItems))
            {
                var path = view.GetFirstStorageItemPathUnsafe();
                if (path is null || Path.GetExtension(path).ToLowerInvariant() is ".pri")
                {
                    e.AcceptedOperation = DataPackageOperation.Copy;
                    e.DragUIOverride.Caption = L("Drag.DropLoadPri");
                    e.Handled = true;
                }
                else
                {
                    e.AcceptedOperation = DataPackageOperation.None;
                }
            }
        }

        [DynamicWindowsRuntimeCast(typeof(FrameworkElement))]
        private async void Grid_Drop(object sender, DragEventArgs e)
        {
            if (e.DataView.Contains(StandardDataFormats.StorageItems))
            {
                var items = await e.DataView.GetStorageItemsAsync();
                if (items.Count > 0 && items[0] is StorageFile file && file.Name.ToLowerInvariant().EndsWith(".pri"))
                {
                    await TryLoadPri(file);
                    e.Handled = true;
                }
            }
        }

        private void treeView_SelectionChanged(Microsoft.UI.Xaml.Controls.TreeView sender, Microsoft.UI.Xaml.Controls.TreeViewSelectionChangedEventArgs args)
        {
            if (args.AddedItems.Count is 1 &&
                args.AddedItems[0] is ResourceItem item &&
                (item.Type is not ResourceType.Folder || item.Candidates.Count > 0))
            {
                _selectedResource = item;
                candidatesList.ItemsSource = item.Candidates;
                RemoveResourcesItem.IsEnabled = true;
                _selectedCandidate = null;

                if (item.Candidates.Count == 1)
                {
                    candidatesList.SelectedIndex = 0;
                }
            }
        }

        private void UnloadOtherPreviewElements(CandidateItem item)
        {
            UnloadObject(invalidRootPathContainer);
            UnloadObject(failedToOpenFileContainer);
            UnloadObject(xbfFallbackContainer);

            if (_selectedResource?.Type.IsPreviewedAsText is not true)
                UnloadObject(valueTextEditor);

            if (_selectedResource?.Type is not ResourceType.Image)
                UnloadObject(imagePreviewerContainer);

            if (_selectedResource?.Type is ResourceType.Svg)
            {
                if (_useWebViewForSvg)
                {
                    UnloadObject(svgPreviewerContainer);
                }
                else
                {
                    UnloadObject(webView);
                }
            }
            else
            {
                UnloadObject(svgPreviewerContainer);
                UnloadObject(webView);
            }

            if (!(item.ValueType is ResourceValueType.EmbeddedData && _selectedResource?.Type.IsPreviewable is not true))
                UnloadObject(exportContainer);

            if (!(item.ValueType is ResourceValueType.Path && _selectedResource?.Type.IsPreviewable is not true))
                UnloadObject(openFolderContainer);
        }

        private void UnloadAllPreviewElements()
        {
            UnloadObject(invalidRootPathContainer);
            UnloadObject(failedToOpenFileContainer);
            UnloadObject(xbfFallbackContainer);
            UnloadObject(valueTextEditor);
            UnloadObject(imagePreviewerContainer);
            UnloadObject(svgPreviewerContainer);
            UnloadObject(webView);
            UnloadObject(exportContainer);
            UnloadObject(openFolderContainer);
        }

        private void UnloadNonErrorPreviewElements()
        {
            UnloadObject(valueTextEditor);
            UnloadObject(imagePreviewerContainer);
            UnloadObject(svgPreviewerContainer);
            UnloadObject(webView);
            UnloadObject(exportContainer);
            UnloadObject(openFolderContainer);
        }

        private async Task DisplayCandidate(CandidateItem item)
        {
            UnloadOtherPreviewElements(item);

            var candidate = item.Candidate;
            if (candidate.ValueType is ResourceValueType.Path)
            {
                if (await TryResolvePathCandidateAsync(candidate.StringValue) is StorageFile file)
                {
                    await DisplayPathCandidate(file);
                    return;
                }

                UnloadNonErrorPreviewElements();
                FindName(nameof(invalidRootPathContainer));
            }
            else if (candidate.ValueType is ResourceValueType.EmbeddedData)
            {
                var dataValue = candidate.DataValueReference;
                using (RandomAccessStreamOverBuffer stream = new(dataValue))
                {
                    if (await DisplayBinaryCandidate(stream, _selectedResource!.Type))
                        return;
                }

                FindName(nameof(exportContainer));
                fileSizeLabel.Text = $"File Size: {dataValue.Length} bytes";
            }
            else
            {
                DisplayStringCandidate(candidate.StringValue);
            }
        }

        private void DisplayStringCandidate(string str)
        {
            // Use a stable TextBox for editable string resources to avoid WinUIEditor random crashes.
            UnloadObject(valueTextEditor);
            FindName(nameof(valueTextBox));

            valueTextBox.TextChanged -= ValueTextBox_TextChanged;
            valueTextBox.Text = str;
            valueTextBox.TextChanged += ValueTextBox_TextChanged;
            valueTextBox.Visibility = Visibility.Visible;
        }

        private async Task<bool> DisplayBinaryCandidate(IRandomAccessStream stream, ResourceType type)
        {
            try
            {
                if (type is ResourceType.Image)
                {
                    BitmapImage image = new();
                    await image.SetSourceAsync(stream);
                    FindName(nameof(imagePreviewerContainer));
                    imagePreviewer.Opacity = 0;
                    imagePreviewer.Source = image;

                    imagePreviewer.Stretch = Stretch.None;
                    imagePreviewer.MaxWidth = double.PositiveInfinity;
                    imagePreviewer.MaxHeight = double.PositiveInfinity;

                    imagePreviewerContainer.UpdateLayout();
                    imagePreviewerContainer.ChangeView(null, null, 1f, true);

                    var imageWidth = imagePreviewer.ActualWidth;
                    var imageHeight = imagePreviewer.ActualHeight;
                    var containerWidth = imagePreviewerContainer.ActualWidth;
                    var containerHeight = imagePreviewerContainer.ActualHeight;

                    if (imageWidth > containerWidth ||
                        imageHeight > containerHeight)
                    {
                        var ratio = Math.Min(containerWidth / imageWidth, containerHeight / imageHeight);
                        if (ratio < 0.1d)
                        {
                            imagePreviewer.MaxWidth = containerWidth / 0.1d;
                            imagePreviewer.MaxHeight = containerHeight / 0.1d;
                            imagePreviewer.Stretch = Stretch.Uniform;
                            imagePreviewerContainer.UpdateLayout();

                            ratio = 0.1d;
                        }

                        if (imagePreviewerContainer.ZoomFactor is not 1f)
                        {
                            await imagePreviewerContainer.WaitForZoomFactorChangeAsync();
                        }

                        imagePreviewerContainer.ChangeView(null, null, (float)ratio, true);
                    }

                    imagePreviewer.Opacity = 1;
                    return true;
                }
                else if (type is ResourceType.Xbf)
                {
                    try
                    {
                        // TEMPORARY: we are temporary using XbfAnalyzer to decompile XBF files for now
                        // until we implement our own XBF decompiler/recompiler based on WinUI 3' native
                        // XBF parser and its "WidgetSpinner" XBF decompiler, since the native parser support
                        // XBF v1 and is architectured in a way that allows us to build a recompiler on top easily.

                        var reader = new XbfAnalyzer.Xbf.XbfReader(stream.AsStream());
                        DisplayStringCandidate(reader.RootObject.ToString());
                    }
                    catch (Exception ex)
                    {
                        UnloadNonErrorPreviewElements();
                        FindName(nameof(xbfFallbackContainer));

                        xbfFileNameRun.Text = _selectedResource is not null ?
                            _selectedResource.DisplayName :
                            "the XBF file";

                        failedXbfExceptionMessageRun.Text = $"{ex.GetType().Name} (0x{ex.HResult:X8}) -> {ex.Message}";
                        xbfFallbackContainer.Visibility = Visibility.Visible;
                    }

                    return true;
                }
                else if (type.IsText)
                {
                    if (stream is RandomAccessStreamOverBuffer rasob)
                    {
                        unsafe
                        {
#pragma warning disable CS9123 // The '&' operator should not be used on parameters or local variables in async methods.
                            byte* ptr = default;
                            rasob.BufferByteAccess->Buffer(&ptr);
#pragma warning restore CS9123 // The '&' operator should not be used on parameters or local variables in async methods.

                            if (ptr is not null)
                            {
                                DisplayStringCandidate(Encoding.UTF8.GetString(ptr, (int)rasob.Size));
                                return true;
                            }
                        }
                    }
                    else
                    {
                        var size = (uint)stream.Size;
                        using var buffer = new NativeBuffer(size);
                        await stream.ReadAsync(buffer, size, InputStreamOptions.None);

                        unsafe
                        {
                            DisplayStringCandidate(Encoding.UTF8.GetString(buffer.Buffer, (int)size));
                            return true;
                        }
                    }
                }
                else if (type is ResourceType.Svg)
                {
                    bool succeeded = false;

                    if (!_useWebViewForSvg)
                    {
                        if (Features.IsCompositionRadialGradientBrushAvailable)
                        {
                            var size = (uint)stream.Size;
                            using var buffer = new NativeBuffer(size + 1);
                            await stream.ReadAsync(buffer, size, InputStreamOptions.None);

                            unsafe
                            {
                                var nativeBuffer = buffer.Buffer;
                                nativeBuffer[size] = 0;

                                var parse = NanoSVG.NanoSVG.nsvgParse(nativeBuffer, (byte*)Unsafe.AsPointer(in MemoryMarshal.GetReference("px"u8)), 96);

                                if (parse != null)
                                {
                                    if (Features.IsCompositionRadialGradientBrushAvailable)
                                    {
                                        Compositor compositor = Window.Current.Compositor;

                                        ShapeVisual visual = compositor.CreateShapeVisual();
                                        visual.Shapes.Add(compositor.CreateShapeFromNSVGImage(parse));
                                        visual.RelativeSizeAdjustment = Vector2.One;

                                        FindName(nameof(svgPreviewerContainer));

                                        svgPreviewer.Width = parse->width;
                                        svgPreviewer.Height = parse->height;
                                        ElementCompositionPreview.SetElementChildVisual(svgPreviewer, visual);

                                        succeeded = true;
                                    }
                                }

                                NanoSVG.NanoSVG.nsvgDelete(parse);
                            }
                        }
                        else
                        {
                            SvgImageSource source = new()
                            {
                                RasterizePixelWidth = 1024,
                                RasterizePixelHeight = 1024
                            };

                            if (await source.SetSourceAsync(stream) is SvgImageSourceLoadStatus.Success)
                            {
                                Viewbox viewbox = new()
                                {
                                    Stretch = Stretch.Uniform,
                                    Width = Math.Min(512, PreviewContainer.ActualWidth - 20),
                                    Height = Math.Min(512, PreviewContainer.ActualHeight - 20),
                                    Child = new Image()
                                    {
                                        Source = source,
                                        Width = 1024,
                                        Height = 1024,
                                        Stretch = Stretch.None
                                    }
                                };

                                FindName(nameof(svgPreviewerContainer));
                                svgPreviewer.Content = viewbox;
                                svgPreviewer.VerticalAlignment = VerticalAlignment.Center;
                                svgPreviewer.HorizontalAlignment = HorizontalAlignment.Center;

                                succeeded = true;
                            }
                        }
                    }

                    if (!succeeded && Program.IsWebViewAvailable)
                    {
                        UnloadObject(svgPreviewerContainer);

                        var size = (uint)stream.Size;
                        using var buffer = new NativeBuffer(size);

                        stream.Seek(0);
                        await stream.ReadAsync(buffer, size, InputStreamOptions.None);

                        unsafe
                        {
                            var svg = Encoding.UTF8.GetString(buffer.Buffer, (int)size);
                            var html = @$"
                            <html>
                                <head>
                                <meta charset=""UTF-16"">
                                <style>
                                    html, body {{
                                    width: 100%;
                                    height: 100%;
                                    margin: 0;
                                    }}
                                </style>
                                </head>
                                <body style=""display:flex;justify-content:center;align-items:center;background-color:transparent;"">
                                <div>{svg}</div>
                                </body>
                            </html>";

                            FindName(nameof(webView));
                            webView.NavigateToString(html);

                            succeeded = true;
                        }
                    }

                    return succeeded;
                }
            } catch { }

            return false;
        }

        [DynamicWindowsRuntimeCast(typeof(StorageFile))]
        [DynamicWindowsRuntimeCast(typeof(StorageFolder))]
        private async Task<StorageFile?> TryResolvePathCandidateAsync(string fileName)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(fileName))
                    return null;

                // 某些 PRI 可能用占位符/无效值表示“无法解析路径”，这里直接降级为未找到。
                if (string.Equals(fileName, "UNABLE_TO_MASK_PATH", StringComparison.Ordinal))
                    return null;

                var root = _rootFolder ?? await _currentFile?.GetParentAsync();
                if (root is null)
                    return null;

                // TryGetItemAsync 在不同宿主/WinRT 实现下可能抛 ArgumentException（例如传入了不可解析的项名）。
                // 这里将其视为“未找到”，避免编辑候选后恢复预览导致崩溃/弹窗。
                if (await root.TryGetItemAsync(fileName) is StorageFile cFile)
                {
                    return cFile;
                }

                if (_currentFile?.DisplayName is { } dn &&
                    await root.TryGetItemAsync(dn) is StorageFolder folder)
                {
                    return await folder.TryGetItemAsync(fileName) as StorageFile;
                }
            }
            catch (Exception)
            {
            }

            return null;
        }

        private async Task DisplayPathCandidate(StorageFile file)
        {
            bool result = false;
            IRandomAccessStream stream;

            if (_selectedResource?.Type.IsPreviewable is true)
            {
                try
                {
                    stream = await file.OpenAsync(FileAccessMode.Read, StorageOpenOptions.AllowReadersAndWriters);
                }
                catch (Exception ex)
                {
                    UnloadNonErrorPreviewElements();
                    FindName(nameof(failedToOpenFileContainer));

                    failedFileNameRun.Text = file.Path;
                    failedExceptionMessageRun.Text = $"{ex.GetType().Name} (0x{ex.HResult:X8}) -> {ex.Message}";
                    failedToOpenFileContainer.Visibility = Visibility.Visible;
                    return;
                }

                result = await DisplayBinaryCandidate(stream, _selectedResource.Type);
                stream.Dispose();
            }

            if (!result)
            {
                FindName(nameof(openFolderContainer));
                openFolderContainer.Tag = file.Path;
            }
        }

        private async void candidatesList_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (e.AddedItems.Count is 1 && e.AddedItems[0] is CandidateItem item)
            {
                _selectedCandidate = item;
                await DisplayCandidate(item);
            }
            else
            {
                _selectedCandidate = null;
                UnloadAllPreviewElements();
            }
        }

        [DynamicWindowsRuntimeCast(typeof(MenuFlyoutItem))]
        private async void ExportButton_Click(object sender, RoutedEventArgs e)
        {
            var candidateItem = sender is MenuFlyoutItem item && item.DataContext is CandidateItem ctxItem
                ? ctxItem
                : candidatesList.SelectedItem as CandidateItem;

            if (candidateItem is not null)
            {
                await ExportCandidateItemAsync(candidateItem);
            }
        }

        [DynamicWindowsRuntimeCast(typeof(MenuFlyoutItem))]
        private async void ExportCurrentSelectedFromTree_Click(object sender, RoutedEventArgs e)
        {
            var treeItem = sender is MenuFlyoutItem mi && mi.DataContext is ResourceItem ctx
                ? ctx
                : treeView.SelectedItem as ResourceItem;

            if (treeItem is null || treeItem.IsFolder)
                return;

            CandidateItem? target = null;
            if (_selectedResource == treeItem && _selectedCandidate is not null)
                target = _selectedCandidate;
            else if (treeItem.Candidates.Count == 1)
                target = treeItem.Candidates[0];

            if (target is null)
            {
                await ShowInPageMessageOkAsync(L("Dialog.Error.CannotExport"), L("Dialog.Error.ExportSelectCandidate"));
                return;
            }

            await ExportCandidateItemAsync(target);
        }

        private async Task ExportCandidateItemAsync(CandidateItem candidateItem)
        {
            var candidate = candidateItem.Candidate;
            string fileName = candidate.ResourceName.GetDisplayName();
            string extension = Path.GetExtension(fileName);

            FileSavePicker picker = new();
            picker.Initialize();
            picker.CommitButtonText = L("Picker.Commit.Save");
            picker.SuggestedFileName = candidate.ValueType is ResourceValueType.String ? $"{fileName}_structured" : fileName;

            if (candidate.ValueType is ResourceValueType.String)
            {
                picker.FileTypeChoices.Add(L("Picker.FileType.Text"), new string[] { ".txt" });
            }
            else if (!string.IsNullOrEmpty(extension))
            {
                picker.FileTypeChoices.Add($"{extension[1..].ToUpperInvariant()} file", new string[] { extension });
            }

            picker.FileTypeChoices.Add(L("Picker.FileType.All"), new string[] { "." });

            if (await picker.PickSaveFileAsync() is not { } file)
                return;

            if (candidate.ValueType is ResourceValueType.EmbeddedData)
            {
                await FileIO.WriteBufferAsync(file, candidate.DataValueReference);
            }
            else if (candidate.ValueType is ResourceValueType.String)
            {
                var line = BuildStructuredExportLine(candidateItem);
                await FileIO.WriteTextAsync(file, line + Environment.NewLine, UnicodeEncoding.Utf8);
            }
            else
            {
                await FileIO.WriteTextAsync(file, candidate.StringValue, UnicodeEncoding.Utf8);
            }
        }

        private void SystemTheme_Click(object sender, RoutedEventArgs e)
        {
            PreviewContainer.RequestedTheme = ElementTheme.Default;
        }

        private void LightTheme_Click(object sender, RoutedEventArgs e)
        {
            PreviewContainer.RequestedTheme = ElementTheme.Light;
        }

        private void DarkTheme_Click(object sender, RoutedEventArgs e)
        {
            PreviewContainer.RequestedTheme = ElementTheme.Dark;
        }

        private async void TryAgain_Click(object sender, RoutedEventArgs e)
        {
            if (candidatesList.SelectedItem is CandidateItem item)
            {
                await DisplayCandidate(item);
            }
        }

        [DynamicWindowsRuntimeCast(typeof(MenuFlyoutItem))]
        private async void OpenFolder_Click(object sender, RoutedEventArgs e)
        {
            var path = sender is MenuFlyoutItem item &&
                       item.DataContext is CandidateItem candidateItem &&
                       await TryResolvePathCandidateAsync(candidateItem.StringValue) is { } file ?
                file.Path : openFolderContainer?.Tag as string;

            if (path is not null)
            {
                NativeUtils.ShowFileInExplorer(path);
            }
        }

        private async void Notice_Click(object sender, RoutedEventArgs e)
        {
            var text = TryLoadLocalizedNoticeText() ?? NativeUi.TryGetEmbeddedNoticeText() ?? L("Dialog.Error.FailedToLoadNotice");
            await ShowInPageMessageOkAsync(L("Dialog.Title.ThirdPartyNotice"), text);
        }

        private static string? TryLoadLocalizedNoticeText()
        {
            try
            {
                var lang = LocalizationService.CurrentLanguage;
                var fileName = lang.StartsWith("zh", StringComparison.OrdinalIgnoreCase) ? "NOTICECN.txt" : "NOTICEEN.txt";

                // 1) Preferred: repository root (user-specified location)
                // 2) Fallbacks: executable directory and its parents
                foreach (var dir in EnumerateNoticeCandidateDirs())
                {
                    var path = Path.Combine(dir, fileName);
                    if (!File.Exists(path))
                        continue;
                    return File.ReadAllText(path);
                }
            }
            catch { }

            return null;
        }

        private static IEnumerable<string> EnumerateNoticeCandidateDirs()
        {
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            var baseDir = AppContext.BaseDirectory;
            if (!string.IsNullOrWhiteSpace(baseDir))
            {
                var current = new DirectoryInfo(baseDir);
                for (var i = 0; current is not null && i < 8; i++)
                {
                    if (seen.Add(current.FullName))
                        yield return current.FullName;
                    current = current.Parent;
                }
            }

            var cwd = Environment.CurrentDirectory;
            if (!string.IsNullOrWhiteSpace(cwd) && seen.Add(cwd))
                yield return cwd;
        }

        private async void Usage_Click(object sender, RoutedEventArgs e)
        {
            await ShowInPageMessageOkAsync(L("Dialog.Title.Usage"), L("Usage.Body"));
        }

        private async Task RunSimpleMessageModalAsync(SimpleMessagePanel panel)
        {
            AttachPageModalChild(panel.Root);
            pageModalOverlay.Visibility = Visibility.Visible;
            try
            {
                await panel.ResultTask;
            }
            finally
            {
                pageModalOverlay.Visibility = Visibility.Collapsed;
                ClearPageModalChild();
            }
        }

        private async Task ShowInPageMessageOkAsync(string title, string body, bool error = false)
        {
            var panel = SimpleMessagePanel.CreateOk(title, body, error);
            await RunSimpleMessageModalAsync(panel);
        }

        private async Task<bool> ShowInPageMessageYesNoAsync(string title, string body)
        {
            var panel = SimpleMessagePanel.CreateConfirmCancel(title, body);
            await RunSimpleMessageModalAsync(panel);
            return await panel.ResultTask == NativeUi.IDYES;
        }

        private async Task<int> ShowInPageMessageYesNoCancelAsync(string title, string body)
        {
            var panel = SimpleMessagePanel.CreateSaveCancelExit(title, body);
            await RunSimpleMessageModalAsync(panel);
            return await panel.ResultTask;
        }

        private async Task<(bool Cancelled, string? Language)> ShowBatchLanguagePickerAsync(string title, string confirmText)
        {
            var panel = new BatchLanguagePanel(title, confirmText, GetDistinctLanguageValues(), _lastBatchLanguageValue);
            AttachPageModalChild(panel.Root);
            pageModalOverlay.Visibility = Visibility.Visible;
            try
            {
                var result = await panel.ResultTask;
                if (result == string.Empty)
                    return (true, null);
                _lastBatchLanguageValue = result;
                return (false, result);
            }
            finally
            {
                pageModalOverlay.Visibility = Visibility.Collapsed;
                ClearPageModalChild();
            }
        }

        /// <summary>在已展开的 MUX 节点树上按 Content 深度优先查找（折叠分支下可能不存在子 TreeViewNode）。</summary>
        private static Microsoft.UI.Xaml.Controls.TreeViewNode? TryFindTreeNodeMuxFast(
            Microsoft.UI.Xaml.Controls.TreeView mux,
            ResourceItem item)
        {
            foreach (var root in mux.RootNodes)
            {
                var n = FindTreeNodeInSubtree(root, item);
                if (n is not null)
                    return n;
            }

            return null;
        }

        private static bool TryGetResourcePath(ResourceItem target, ObservableCollection<ResourceItem> roots, out List<ResourceItem> path)
        {
            var cur = new List<ResourceItem>();
            bool Dfs(ResourceItem n)
            {
                cur.Add(n);
                if (ReferenceEquals(n, target))
                    return true;
                foreach (var c in n.Children)
                {
                    if (Dfs(c))
                        return true;
                }

                cur.RemoveAt(cur.Count - 1);
                return false;
            }

            foreach (var r in roots)
            {
                cur.Clear();
                if (Dfs(r))
                {
                    path = [.. cur];
                    return true;
                }
            }

            path = [];
            return false;
        }

        /// <summary>
        /// 父节点折叠时 MUX 可能尚未创建子级 TreeViewNode，先沿数据路径逐级展开再按引用匹配节点。
        /// </summary>
        private async Task<Microsoft.UI.Xaml.Controls.TreeViewNode?> FindTreeNodeForResourceItemAsync(
            Microsoft.UI.Xaml.Controls.TreeView mux,
            ResourceItem item)
        {
            var fast = TryFindTreeNodeMuxFast(mux, item);
            if (fast is not null)
                return fast;

            if (!TryGetResourcePath(item, ResourceItems, out var ancestry) || ancestry.Count == 0)
                return null;

            Microsoft.UI.Xaml.Controls.TreeViewNode? tvNode = null;
            for (var i = 0; i < ancestry.Count; i++)
            {
                var seg = ancestry[i];
                if (i == 0)
                {
                    tvNode = null;
                    foreach (var rn in mux.RootNodes)
                    {
                        if (rn.Content is ResourceItem ri && ReferenceEquals(ri, seg))
                        {
                            tvNode = rn;
                            break;
                        }
                    }
                }
                else
                {
                    if (tvNode is null)
                        return null;
                    tvNode.IsExpanded = true;
                    Microsoft.UI.Xaml.Controls.TreeViewNode? next = null;
                    for (var attempt = 0; attempt < 12 && next is null; attempt++)
                    {
                        mux.UpdateLayout();
                        await Task.Yield();
                        await Dispatcher.RunAsync(Windows.UI.Core.CoreDispatcherPriority.Normal, () => { });
                        foreach (var ch in tvNode.Children)
                        {
                            if (ch.Content is ResourceItem ri && ReferenceEquals(ri, seg))
                            {
                                next = ch;
                                break;
                            }
                        }
                    }

                    tvNode = next;
                }

                if (tvNode is null)
                    return null;
            }

            return tvNode;
        }

        private static Microsoft.UI.Xaml.Controls.TreeViewNode? FindTreeNodeInSubtree(
            Microsoft.UI.Xaml.Controls.TreeViewNode node,
            ResourceItem item)
        {
            if (node.Content is ResourceItem ri)
            {
                if (ReferenceEquals(ri, item))
                    return node;
                // 托管树节点 Content 可能与 DFS 拿到的实例引用不一致时，用完整资源名对齐。
                if (string.Equals(ri.Name, item.Name, StringComparison.Ordinal))
                    return node;
            }

            foreach (var ch in node.Children)
            {
                var r = FindTreeNodeInSubtree(ch, item);
                if (r is not null)
                    return r;
            }

            return null;
        }

        private static void ExpandTreeNodeAncestors(Microsoft.UI.Xaml.Controls.TreeViewNode node)
        {
            for (var p = node.Parent; p is not null; p = p.Parent)
                p.IsExpanded = true;
        }

        [DynamicWindowsRuntimeCast(typeof(MenuFlyoutItem))]
        private void CopyResourceName_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not MenuFlyoutItem item || item.DataContext is not ResourceItem res)
                return;

            var data = new DataPackage();
            data.SetText(res.DisplayName);
            Clipboard.SetContent(data);
        }

        private void TreeSearchBox_KeyDown(object sender, Windows.UI.Xaml.Input.KeyRoutedEventArgs e)
        {
            if (e.Key == Windows.System.VirtualKey.Enter)
            {
                _ = PerformTreeSearchAsync(treeSearchBox.Text);
                e.Handled = true;
            }
        }

        private void TreeSearchButton_Click(object sender, RoutedEventArgs e)
        {
            _ = PerformTreeSearchAsync(treeSearchBox.Text);
        }

        private void TreeSearchClear_Click(object sender, RoutedEventArgs e)
        {
            treeSearchBox.Text = string.Empty;
            treeView.SelectedItem = null;
            _treeSearchCycleQuery = null;
            _treeSearchCycleScopeFolder = null;
            _treeSearchPinnedScopeFolder = null;
            _treeSearchCycleIndex = -1;
        }

        /// <summary>未选中或选中非文件夹：全局；选中文件夹：仅该节点子树（深度优先顺序）。</summary>
        private ResourceItem? GetTreeSearchScopeFolder()
        {
            if (treeView.SelectedItem is ResourceItem sel && sel.IsFolder)
                return sel;
            return null;
        }

        private static bool ResourceItemMatchesTreeQuery(ResourceItem it, string query)
        {
            if (it.Name.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                it.DisplayName.Contains(query, StringComparison.OrdinalIgnoreCase))
                return true;

            foreach (var cand in it.Candidates)
            {
                if (cand.ValueType is ResourceValueType.String or ResourceValueType.Path)
                {
                    if (cand.StringValue.Contains(query, StringComparison.OrdinalIgnoreCase))
                        return true;
                }
            }

            return false;
        }

        /// <summary>深度优先：先根再子；scope 为文件夹时包含该文件夹自身。</summary>
        private void CollectTreeSearchMatches(string query, ResourceItem? scopeFolder, List<ResourceItem> results)
        {
            void Dfs(ResourceItem node)
            {
                if (ResourceItemMatchesTreeQuery(node, query))
                    results.Add(node);
                foreach (var ch in node.Children)
                    Dfs(ch);
            }

            if (scopeFolder is not null)
            {
                Dfs(scopeFolder);
                return;
            }

            foreach (var root in ResourceItems)
                Dfs(root);
        }

        private async Task PerformTreeSearchAsync(string? raw)
        {
            var query = (raw ?? string.Empty).Trim();
            if (query.Length == 0)
                return;

            if (treeView is not Microsoft.UI.Xaml.Controls.TreeView mux || mux.ListControl is not { } list)
                return;

            var scopeFolder = GetTreeSearchScopeFolder();
            var isQueryChanged = !string.Equals(_treeSearchCycleQuery, query, StringComparison.OrdinalIgnoreCase);
            // scopeFolder 可能会因为“选中搜索结果（非文件夹）”变成 null；这种变化不应重置/清空固定作用域。
            var shouldResetByScope = scopeFolder is not null &&
                                     !ReferenceEquals(_treeSearchCycleScopeFolder, scopeFolder);
            if (isQueryChanged || shouldResetByScope)
            {
                _treeSearchCycleIndex = -1;
                _treeSearchPinnedScopeFolder = scopeFolder;
            }

            _treeSearchCycleQuery = query;
            _treeSearchCycleScopeFolder = scopeFolder;

            var matches = new List<ResourceItem>();
            var effectiveScope = _treeSearchPinnedScopeFolder;
            CollectTreeSearchMatches(query, effectiveScope, matches);

            if (matches.Count == 0)
            {
                var where = effectiveScope is null ? L("Tree.Scope.All") : string.Format(L("Tree.Scope.Folder"), effectiveScope.DisplayName);
                await ShowInPageMessageOkAsync(L("Dialog.Title.NotFound"), string.Format(L("Dialog.Body.TreeSearchNotFoundScope"), where, query));
                return;
            }

            _treeSearchCycleIndex = (_treeSearchCycleIndex + 1) % matches.Count;
            var match = matches[_treeSearchCycleIndex];

            var targetNode = await FindTreeNodeForResourceItemAsync(mux, match);
            if (targetNode is null)
            {
                await ShowInPageMessageOkAsync(L("Dialog.Title.NotFound"), string.Format(L("Dialog.Body.TreeSearchNodeNotFound"), match.DisplayName));
                return;
            }

            ExpandTreeNodeAncestors(targetNode);
            if (targetNode.Children.Count > 0)
                targetNode.IsExpanded = true;

            for (var pass = 0; pass < 8; pass++)
            {
                mux.UpdateLayout();
                await Task.Yield();
                await Dispatcher.RunAsync(Windows.UI.Core.CoreDispatcherPriority.Normal, () => { });
            }

            mux.SelectedItem = match;
            mux.SelectedNode = targetNode;

            for (var pass = 0; pass < 10; pass++)
            {
                mux.UpdateLayout();
                list.ScrollIntoView(targetNode);
                if (mux.ContainerFromNode(targetNode) is Microsoft.UI.Xaml.Controls.TreeViewItem tvi)
                {
                    tvi.IsSelected = true;
                    tvi.StartBringIntoView(new BringIntoViewOptions { AnimationDesired = false, VerticalAlignmentRatio = 0.5 });
                    break;
                }

                await Task.Yield();
                await Dispatcher.RunAsync(Windows.UI.Core.CoreDispatcherPriority.Normal, () => { });
            }
        }

        private async void BatchImport_Click(object sender, RoutedEventArgs e)
        {
            if (_pri is null)
                return;

            var importOptions = await ShowBatchLanguagePickerAsync(L("Batch.Import.Title"), L("Common.Button.Next"));
            if (importOptions.Cancelled)
                return;
            var forcedLanguage = importOptions.Language;

            FileOpenPicker picker = new();
            picker.FileTypeFilter.Add(".txt");
            picker.FileTypeFilter.Add(".tsv");
            picker.FileTypeFilter.Add(".csv");
            picker.FileTypeFilter.Add("*");
            picker.CommitButtonText = L("Picker.Commit.Import");
            picker.Initialize();

            if (await picker.PickSingleFileAsync() is not { } file)
                return;

            string content;
            try
            {
                content = await FileIO.ReadTextAsync(file);
            }
            catch (Exception ex)
            {
                CrashLogger.ShowErrorDialog(L("Dialog.Title.ReadFailed"), ex, "PriPage.BatchImport_Click");
                return;
            }

            int replaced = 0, failed = 0, total = 0;
            int skippedNoMatch = 0, skippedNoMatchName = 0, skippedNoMatchLanguage = 0;
            int skippedAmbiguous = 0, skippedInvalid = 0, skippedByLanguage = 0;
            var failureDetails = new List<string>();

            var allStringCandidates = EnumerateAllResources(ResourceItems)
                .SelectMany(res => res.Candidates)
                .Where(c => c.ValueType is ResourceValueType.String)
                .ToList();

            foreach (var lineRaw in content.Split(new[] { "\r\n", "\n" }, StringSplitOptions.None))
            {
                var line = lineRaw.Trim();
                if (line.Length == 0 || line.StartsWith('#'))
                    continue;

                total++;

                if (!TryParseBatchImportLine(line, forcedLanguage, out var entry))
                {
                    skippedInvalid++;
                    failed++;
                    failureDetails.Add($"[{LocalizeBatchImportFailureReason("INVALID_FORMAT")}] {lineRaw}");
                    continue;
                }

                if (!string.IsNullOrWhiteSpace(forcedLanguage) &&
                    entry.LanguageValue is { Length: > 0 } entryLanguage &&
                    !string.Equals(entryLanguage, forcedLanguage, StringComparison.OrdinalIgnoreCase))
                {
                    skippedByLanguage++;
                    failureDetails.Add($"[{LocalizeBatchImportFailureReason("SKIPPED_BY_LANGUAGE")}] {lineRaw}");
                    continue;
                }

                var matches = FindBatchImportMatches(entry, allStringCandidates);

                if (matches.Count == 0)
                {
                    skippedNoMatch++;
                    failed++;
                    var noMatchReason = GetNoMatchReason(entry, allStringCandidates);
                    if (string.Equals(noMatchReason, "NO_MATCH_NAME", StringComparison.Ordinal))
                        skippedNoMatchName++;
                    else if (string.Equals(noMatchReason, "NO_MATCH_LANGUAGE", StringComparison.Ordinal))
                        skippedNoMatchLanguage++;
                    failureDetails.Add($"[{LocalizeBatchImportFailureReason(noMatchReason)}] {lineRaw}");
                    continue;
                }

                if (matches.Count > 1)
                {
                    skippedAmbiguous++;
                    failed++;
                    failureDetails.Add($"[{LocalizeBatchImportFailureReason("SKIPPED_AMBIGUOUS")}] {lineRaw}");
                    continue;
                }

                var target = matches[0];
                if (!string.Equals(target.StringValue, entry.Translation, StringComparison.Ordinal))
                {
                    target.StringValue = entry.Translation;
                    _isDirty = true;
                }

                replaced++;
            }

            string? failureReportPath = null;
            var totalSkippedOrFailed = failed + skippedByLanguage;
            if (totalSkippedOrFailed > 0 && failureDetails.Count > 0)
            {
                failureReportPath = TryWriteBatchImportFailureReport(
                    total,
                    replaced,
                    skippedByLanguage,
                    skippedNoMatch,
                    skippedAmbiguous,
                    skippedInvalid,
                    failureDetails);
            }

            var skippedNoMatchOther = skippedNoMatch - skippedNoMatchName - skippedNoMatchLanguage;
            var completionMessage = new StringBuilder()
                .Append(L("Batch.Import.Summary.Total")).Append("：").Append(total)
                .Append("\r\n").Append(L("Batch.Import.Summary.Replaced")).Append("：").Append(replaced)
                .Append("\r\n").Append(L("Batch.Import.Summary.SkippedByLanguage")).Append("：").Append(skippedByLanguage)
                .Append("\r\n").Append(L("Batch.Import.Summary.NoMatchName")).Append("：").Append(skippedNoMatchName)
                .Append("\r\n").Append(L("Batch.Import.Summary.NoMatchLanguage")).Append("：").Append(skippedNoMatchLanguage);
            if (skippedNoMatchOther > 0)
                completionMessage.Append("\r\n").Append(L("Batch.Import.Summary.NoMatchOther")).Append("：").Append(skippedNoMatchOther);
            completionMessage
                .Append("\r\n").Append(L("Batch.Import.Summary.Ambiguous")).Append("：").Append(skippedAmbiguous)
                .Append("\r\n").Append(L("Batch.Import.Summary.Invalid")).Append("：").Append(skippedInvalid)
                .Append("\r\n").Append(L("Batch.Import.Summary.TotalSkipped")).Append("：").Append(totalSkippedOrFailed);
            if (!string.IsNullOrWhiteSpace(failureReportPath))
                completionMessage.Append("\r\n").Append(L("Batch.Import.Summary.FailureFile")).Append("：").Append(failureReportPath);
            await ShowInPageMessageOkAsync(L("Dialog.Title.ImportDone"), completionMessage.ToString());
        }

        [DynamicWindowsRuntimeCast(typeof(MenuFlyoutItem))]
        private async void BatchExport_Click(object sender, RoutedEventArgs e)
        {
            if (_pri is null)
                return;

            var exportOptions = await ShowBatchLanguagePickerAsync(L("Batch.Export.Title"), L("Common.Button.Next"));
            if (exportOptions.Cancelled)
                return;
            var forcedLanguage = exportOptions.Language;

            var scope = sender is MenuFlyoutItem item && item.DataContext is ResourceItem ctx
                ? ctx
                : treeView.SelectedItem as ResourceItem;

            FileSavePicker picker = new();
            picker.Initialize();
            picker.CommitButtonText = L("Picker.Commit.Save");
            picker.FileTypeChoices.Add(L("Picker.FileType.Text"), new[] { ".txt" });
            picker.FileTypeChoices.Add(L("Picker.FileType.All"), new[] { "." });
            picker.SuggestedFileName = scope?.DisplayName is { Length: > 0 } n
                ? $"{n}_batch_export"
                : "all_resources_batch_export";

            if (await picker.PickSaveFileAsync() is not { } file)
                return;

            var candidates = EnumerateBatchExportCandidates(scope, forcedLanguage).ToList();
            if (candidates.Count == 0)
            {
                await ShowInPageMessageOkAsync(L("Dialog.Title.ExportDone"), L("Dialog.Body.Export.NoCandidates"));
                return;
            }

            var sb = new StringBuilder();
            foreach (var cand in candidates)
            {
                sb.AppendLine(BuildStructuredExportLine(cand));
            }

            try
            {
                await FileIO.WriteTextAsync(file, sb.ToString(), UnicodeEncoding.Utf8);
                await ShowInPageMessageOkAsync(
                    L("Dialog.Title.ExportDone"),
                    string.Format(L("Dialog.Body.Export.Completed"), candidates.Count));
            }
            catch (Exception ex)
            {
                CrashLogger.ShowErrorDialog(L("Dialog.Title.ExportFailed"), ex, "PriPage.BatchExport_Click");
            }
        }

        private async void LanguageChinese_Click(object sender, RoutedEventArgs e)
        {
            await SwitchLanguageAsync("zh-Hans");
        }

        private async void LanguageEnglish_Click(object sender, RoutedEventArgs e)
        {
            await SwitchLanguageAsync("en-US");
        }

        private void UpdateLanguageMenuState()
        {
            var lang = LocalizationService.CurrentLanguage;
            LanguageChineseMenuItem.IsChecked = string.Equals(lang, "zh-Hans", StringComparison.OrdinalIgnoreCase);
            LanguageEnglishMenuItem.IsChecked = string.Equals(lang, "en-US", StringComparison.OrdinalIgnoreCase);
        }

        private async Task SwitchLanguageAsync(string languageTag)
        {
            if (string.Equals(LocalizationService.CurrentLanguage, languageTag, StringComparison.OrdinalIgnoreCase))
            {
                UpdateLanguageMenuState();
                return;
            }

            var restartNow = await ShowInPageMessageYesNoAsync(
                L("Language.Switch.RequiresRestart.Title"),
                L("Language.Switch.RequiresRestart.Body"));
            if (restartNow)
            {
                LocalizationService.SaveLanguagePreference(languageTag);
                try
                {
                    Windows.Globalization.ApplicationLanguages.PrimaryLanguageOverride = languageTag;
                }
                catch
                {
                    // Ignore in unpackaged mode; we persist to file instead.
                }
                Program.Restart();
            }
            else
            {
                UpdateLanguageMenuState();
            }
        }

        private static IEnumerable<ResourceItem> EnumerateAllResources(IEnumerable<ResourceItem> roots)
        {
            foreach (var r in roots)
            {
                yield return r;
                foreach (var c in EnumerateAllResources(r.Children))
                    yield return c;
            }
        }

        private IEnumerable<CandidateItem> EnumerateBatchExportCandidates(ResourceItem? scope, string? forcedLanguage)
        {
            IEnumerable<ResourceItem> resources = scope is null
                ? EnumerateAllResources(ResourceItems)
                : (scope.IsFolder ? EnumerateAllResources([scope]) : [scope]);
            foreach (var res in resources)
            {
                foreach (var cand in res.Candidates)
                {
                    if (cand.ValueType is not ResourceValueType.String)
                        continue;

                    if (!string.IsNullOrWhiteSpace(forcedLanguage))
                    {
                        var language = GetCandidateLanguageValue(cand);
                        if (!string.Equals(language, forcedLanguage, StringComparison.OrdinalIgnoreCase))
                            continue;
                    }

                    yield return cand;
                }
            }
        }

        private readonly record struct BatchImportEntry(
            string? ResourceName,
            string? LanguageValue,
            string Translation,
            string? LegacyLeft);

        private static bool TryParseBatchImportLine(string line, string? forcedLanguage, out BatchImportEntry entry)
        {
            entry = default;

            // 推荐格式：name="..." Language="..." <Value>...</Value>
            if (TryGetQuotedAttributeValue(line, "name", out var name) &&
                (TryGetQuotedAttributeValue(line, "Language", out var language) ||
                 TryGetQuotedAttributeValue(line, "value", out language)) &&
                TryGetXmlElementValue(line, "Value", out var text))
            {
                entry = new BatchImportEntry(
                    NormalizeResourceName(WebUtility.HtmlDecode(name)),
                    string.IsNullOrWhiteSpace(language) ? forcedLanguage : WebUtility.HtmlDecode(language.Trim()),
                    WebUtility.HtmlDecode(text),
                    null);
                return true;
            }

            // 兼容格式：name<TAB>lang<TAB>translation
            var tabParts = line.Split('\t');
            if (tabParts.Length >= 3)
            {
                var resourceName = NormalizeResourceName(tabParts[0].Trim());
                var languageValue = WebUtility.HtmlDecode(tabParts[1].Trim());
                var translation = WebUtility.HtmlDecode(string.Join("\t", tabParts.Skip(2)));
                if (!string.IsNullOrWhiteSpace(resourceName) &&
                    !string.IsNullOrWhiteSpace(languageValue))
                {
                    entry = new BatchImportEntry(resourceName, languageValue, translation, null);
                    if (!string.IsNullOrWhiteSpace(forcedLanguage) && string.IsNullOrWhiteSpace(entry.LanguageValue))
                        entry = entry with { LanguageValue = forcedLanguage };
                    return true;
                }
            }

            // 旧格式：left=translation，left 可能是资源名，也可能是旧文案
            string[] parts = line.Contains('\t') ? line.Split('\t', 2) : line.Split('=', 2);
            if (parts.Length == 2)
            {
                var left = parts[0].Trim();
                var translation = WebUtility.HtmlDecode(parts[1]);
                if (!string.IsNullOrWhiteSpace(left))
                {
                    entry = new BatchImportEntry(
                        NormalizeResourceName(left),
                        forcedLanguage,
                        translation,
                        left);
                    return true;
                }
            }

            return false;
        }

        private static List<CandidateItem> FindBatchImportMatches(BatchImportEntry entry, List<CandidateItem> allStringCandidates)
        {
            if (entry.ResourceName is { Length: > 0 } resourceName)
            {
                var byName = allStringCandidates
                    .Where(c => string.Equals(c.Candidate.ResourceName, resourceName, StringComparison.OrdinalIgnoreCase))
                    .ToList();

                if (entry.LanguageValue is { Length: > 0 } languageValue)
                {
                    return byName
                        .Where(c => string.Equals(GetCandidateLanguageValue(c), languageValue, StringComparison.OrdinalIgnoreCase))
                        .ToList();
                }

                if (byName.Count == 1)
                    return byName;
            }

            // 旧格式兜底：按旧文案匹配，但必须唯一，避免误替换。
            if (entry.LegacyLeft is { Length: > 0 } legacyLeft)
            {
                var bySourceText = allStringCandidates
                    .Where(c => string.Equals(c.StringValue, legacyLeft, StringComparison.Ordinal))
                    .ToList();

                if (bySourceText.Count == 1)
                    return bySourceText;
            }

            return [];
        }

        private static string GetNoMatchReason(BatchImportEntry entry, List<CandidateItem> allStringCandidates)
        {
            if (entry.ResourceName is { Length: > 0 } resourceName)
            {
                var byName = allStringCandidates
                    .Where(c => string.Equals(c.Candidate.ResourceName, resourceName, StringComparison.OrdinalIgnoreCase))
                    .ToList();

                if (byName.Count == 0)
                    return "NO_MATCH_NAME";

                if (entry.LanguageValue is { Length: > 0 } languageValue)
                {
                    var byLanguage = byName
                        .Where(c => string.Equals(GetCandidateLanguageValue(c), languageValue, StringComparison.OrdinalIgnoreCase))
                        .ToList();
                    if (byLanguage.Count == 0)
                        return "NO_MATCH_LANGUAGE";
                }
            }

            return "NO_MATCH";
        }

        private static string? GetCandidateLanguageValue(CandidateItem candidate)
        {
            foreach (var qualifier in candidate.CandidateQualifiers)
            {
                if (qualifier.Attribute is not QualifierAttribute.Language)
                    continue;

                if (qualifier.Operator is QualifierOperator.Match or QualifierOperator.Equal)
                    return qualifier.Value?.Trim();
            }

            return null;
        }

        private static string NormalizeResourceName(string name)
            => name.Replace('\\', '/').Trim('/');

        private static bool TryGetQuotedAttributeValue(string input, string attributeName, out string value)
        {
            value = string.Empty;
            var token = $"{attributeName}=\"";
            var start = input.IndexOf(token, StringComparison.OrdinalIgnoreCase);
            if (start < 0)
                return false;

            start += token.Length;
            var end = input.IndexOf('"', start);
            if (end <= start)
                return false;

            value = input[start..end];
            return true;
        }

        private static bool TryGetXmlElementValue(string input, string elementName, out string value)
        {
            value = string.Empty;
            var openTag = $"<{elementName}>";
            var closeTag = $"</{elementName}>";

            var start = input.IndexOf(openTag, StringComparison.OrdinalIgnoreCase);
            if (start < 0)
                return false;

            start += openTag.Length;
            var end = input.IndexOf(closeTag, start, StringComparison.OrdinalIgnoreCase);
            if (end < start)
                return false;

            value = input[start..end];
            return true;
        }

        private static string EscapeForExportAttribute(string value)
            => value.Replace("&", "&amp;").Replace("\"", "&quot;");

        private static string EscapeForExportValue(string value)
            => value.Replace("&", "&amp;")
                .Replace("<", "&lt;")
                .Replace(">", "&gt;")
                .Replace("\r\n", "&#10;")
                .Replace("\n", "&#10;")
                .Replace("\r", "&#10;");

        private static string BuildStructuredExportLine(CandidateItem candidate)
        {
            var name = NormalizeResourceName(candidate.Candidate.ResourceName);
            var language = GetCandidateLanguageValue(candidate) ?? string.Empty;
            var value = candidate.StringValue ?? string.Empty;

            return $"name=\"{EscapeForExportAttribute(name)}\" Language=\"{EscapeForExportAttribute(language)}\" <Value>{EscapeForExportValue(value)}</Value>";
        }

        private IReadOnlyList<string> GetDistinctLanguageValues()
        {
            var sourceCandidates = _pri?.ResourceCandidates?.AsEnumerable()
                ?? EnumerateAllResources(ResourceItems).SelectMany(r => r.Candidates.Select(c => c.Candidate));

            return sourceCandidates
                .Select(GetLanguageValueFromCandidate)
                .Where(v => !string.IsNullOrWhiteSpace(v))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(v => v, StringComparer.OrdinalIgnoreCase)
                .ToList()!;
        }

        private static string? GetLanguageValueFromCandidate(ResourceCandidate candidate)
        {
            foreach (var qualifier in candidate.Qualifiers)
            {
                if (qualifier.Attribute is not QualifierAttribute.Language)
                    continue;
                if (qualifier.Operator is QualifierOperator.Match or QualifierOperator.Equal)
                    return qualifier.Value?.Trim();
            }

            return null;
        }

        private static string? TryWriteBatchImportFailureReport(
            int total,
            int replaced,
            int skippedByLanguage,
            int skippedNoMatch,
            int skippedAmbiguous,
            int skippedInvalid,
            List<string> failureDetails)
        {
            try
            {
                var baseDir = AppContext.BaseDirectory;
                var fileName = $"batch-import-failures-{DateTime.Now:yyyyMMdd-HHmmss}.txt";
                var path = Path.Combine(baseDir, fileName);
                var sb = new StringBuilder();
                sb.AppendLine(LocalizationService.GetString("Batch.Import.Report.Title"));
                sb.AppendLine($"{LocalizationService.GetString("Batch.Import.Report.Time")}: {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
                sb.AppendLine($"{LocalizationService.GetString("Batch.Import.Summary.Total")}: {total}");
                sb.AppendLine($"{LocalizationService.GetString("Batch.Import.Summary.Replaced")}: {replaced}");
                sb.AppendLine($"{LocalizationService.GetString("Batch.Import.Summary.SkippedByLanguage")}: {skippedByLanguage}");
                sb.AppendLine($"{LocalizationService.GetString("Batch.Import.Summary.NoMatch")}: {skippedNoMatch}");
                sb.AppendLine($"{LocalizationService.GetString("Batch.Import.Summary.Ambiguous")}: {skippedAmbiguous}");
                sb.AppendLine($"{LocalizationService.GetString("Batch.Import.Summary.Invalid")}: {skippedInvalid}");
                sb.AppendLine($"{LocalizationService.GetString("Batch.Import.Summary.TotalSkipped")}: {skippedByLanguage + skippedNoMatch + skippedAmbiguous + skippedInvalid}");
                sb.AppendLine(new string('-', 60));
                foreach (var line in failureDetails)
                    sb.AppendLine(line);

                File.WriteAllText(path, sb.ToString(), Encoding.UTF8);
                return path;
            }
            catch
            {
                return null;
            }
        }

        private static string LocalizeBatchImportFailureReason(string reasonCode) => reasonCode switch
        {
            "INVALID_FORMAT" => LocalizationService.GetString("Batch.Import.Fail.Invalid"),
            "SKIPPED_BY_LANGUAGE" => LocalizationService.GetString("Batch.Import.Fail.ByLanguage"),
            "SKIPPED_AMBIGUOUS" => LocalizationService.GetString("Batch.Import.Fail.Ambiguous"),
            "NO_MATCH_NAME" => LocalizationService.GetString("Batch.Import.Fail.NoMatchName"),
            "NO_MATCH_LANGUAGE" => LocalizationService.GetString("Batch.Import.Fail.NoMatchLanguage"),
            "NO_MATCH" => LocalizationService.GetString("Batch.Import.Fail.NoMatch"),
            _ => reasonCode
        };

        private static void SortResourceEntriesRecursively(ObservableCollection<ResourceItem> items)
        {
            for (var i = 0; i < items.Count; i++)
                SortResourceEntriesRecursively(items[i].Children);

            var nonFolderSlots = new List<int>();
            var nonFolders = new List<ResourceItem>();
            for (var i = 0; i < items.Count; i++)
            {
                var it = items[i];
                if (it.IsFolder)
                    continue;

                nonFolderSlots.Add(i);
                nonFolders.Add(it);
            }

            if (nonFolders.Count <= 1)
                return;

            nonFolders.Sort((a, b) =>
            {
                var cmp = string.Compare(a.DisplayName, b.DisplayName, StringComparison.OrdinalIgnoreCase);
                if (cmp != 0)
                    return cmp;
                return string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase);
            });

            for (var i = 0; i < nonFolderSlots.Count; i++)
            {
                var slot = nonFolderSlots[i];
                if (!ReferenceEquals(items[slot], nonFolders[i]))
                    items[slot] = nonFolders[i];
            }
        }

        private unsafe void SyntaxHighlightingApplied(object sender, ElementTheme e)
        {
            valueTextEditor.HandleSyntaxHighlightingApplied(e);
        }

        [DynamicWindowsRuntimeCast(typeof(MenuFlyoutItem))]
        private async void DeleteCandiate_Click(object sender, RoutedEventArgs e)
        {
            if (_pri is not null &&
                _selectedResource is not null &&
                sender is MenuFlyoutItem item &&
                item.DataContext is CandidateItem candidateItem)
            {
                if (!await ShowInPageMessageYesNoAsync(L("Dialog.Title.DeleteConfirm"), L("Dialog.Body.DeleteCandidate")))
                    return;

                _selectedResource.Candidates.Remove(candidateItem);
                _pri.ResourceCandidates.Remove(candidateItem.Candidate);
                _isDirty = true;
            }
        }

        [DynamicWindowsRuntimeCast(typeof(MenuFlyoutItem))]
        private async void EmbedPathCandidate_Click(object sender, RoutedEventArgs e)
        {
            if (sender is MenuFlyoutItem item &&
                item.DataContext is CandidateItem candidateItem
                && await TryResolvePathCandidateAsync(candidateItem.StringValue) is { } file)
            {
                try
                {
                    using var stream = await file.OpenAsync(FileAccessMode.Read, StorageOpenOptions.AllowReadersAndWriters);
                    var buffer = new Windows.Storage.Streams.Buffer((uint)stream.Size) { Length = (uint)stream.Size };
                    
                    await stream.ReadAsync(buffer, (uint)stream.Size, InputStreamOptions.None);
                    candidateItem.DataValueBuffer = buffer;
                    _isDirty = true;
                }
                catch { }
            }
        }

        [DynamicWindowsRuntimeCast(typeof(MenuFlyoutItem))]
        private async void SimpleRename_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not MenuFlyoutItem item || item.DataContext is not ResourceItem resourceItem)
                return;
            try
            {
                await ShowRenameResourceModalAsync(resourceItem, true);
            }
            catch (Exception ex)
            {
                CrashLogger.ShowErrorDialog(L("Dialog.Title.RenameFailed"), ex, "SimpleRename_Click");
            }
        }

        [DynamicWindowsRuntimeCast(typeof(MenuFlyoutItem))]
        private async void FullRename_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not MenuFlyoutItem item || item.DataContext is not ResourceItem resourceItem)
                return;
            try
            {
                if (!await ShowRenameResourceModalAsync(resourceItem, false))
                    return;

                resourceItem.Parent.Remove(resourceItem);
                var parent = resourceItem.Name.GetParentName() is { } parentName ?
                    GetOrAddResourceItem(parentName).Children :
                    ResourceItems;

                parent.Add(resourceItem);
                SortResourceEntriesRecursively(ResourceItems);
            }
            catch (Exception ex)
            {
                CrashLogger.ShowErrorDialog(L("Dialog.Title.RenameFailed"), ex, "FullRename_Click");
            }
        }

        [DynamicWindowsRuntimeCast(typeof(MenuFlyoutItem))]
        private async void CreateOrModifyCandidate_Click(object sender, RoutedEventArgs e)
        {
            if (_selectedResource is not null && _pri is not null)
            {
                if (sender is MenuFlyoutItem item &&
                    item.DataContext is CandidateItem candidateItem)
                {
                    try
                    {
                        if (await ShowCandidateEditorAsync(_selectedResource, candidateItem) is not null)
                        {
                            _isDirty = true;
                            await DisplayCandidate(candidateItem);
                        }
                    }
                    catch (Exception ex)
                    {
                        CrashLogger.ShowErrorDialog(L("Dialog.Title.ModifyFailed"), ex, "CreateOrModifyCandidate_Click(modify)");
                    }
                }
                else
                {
                    try
                    {
                        if (await ShowCandidateEditorAsync(_selectedResource, null) is { } candidate)
                        {
                            _selectedResource.Candidates.Add(candidate);
                            _pri.ResourceCandidates.Add(candidate);
                            _isDirty = true;
                        }
                    }
                    catch (Exception ex)
                    {
                        CrashLogger.ShowErrorDialog(L("Dialog.Title.CreateFailed"), ex, "CreateOrModifyCandidate_Click(create)");
                    }
                }
            }
        }

        [DynamicWindowsRuntimeCast(typeof(ToggleMenuFlyoutItem))]
        private async void UseWebViewForSvg_Click(object sender, RoutedEventArgs e)
        {
            _useWebViewForSvg = ((ToggleMenuFlyoutItem)sender).IsChecked;

            if (_selectedResource?.Type is ResourceType.Svg &&
                candidatesList.SelectedItem is CandidateItem item)
            {
                await DisplayCandidate(item);
            }
        }

        private void ValueTextBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (_selectedCandidate is null || _selectedCandidate.ValueType is not ResourceValueType.String)
                return;

            _textBoxCommitTimer ??= new Windows.UI.Xaml.DispatcherTimer
            {
                Interval = TimeSpan.FromMilliseconds(250)
            };
            _textBoxCommitTimer.Tick -= TextBoxCommitTimer_Tick;
            _textBoxCommitTimer.Tick += TextBoxCommitTimer_Tick;
            _textBoxCommitTimer.Stop();
            _textBoxCommitTimer.Start();
        }

        private void ValueTextBox_RightTapped(object sender, RightTappedRoutedEventArgs e)
        {
            e.Handled = true;
            _valueTextBoxContextMenu ??= CreateValueTextBoxContextMenu();
            var opts = new FlyoutShowOptions { ShowMode = FlyoutShowMode.Standard };
            try
            {
                opts.Position = e.GetPosition(valueTextBox);
            }
            catch { }
            _valueTextBoxContextMenu.ShowAt(valueTextBox, opts);
        }

        private MenuFlyout CreateValueTextBoxContextMenu()
        {
            var flyout = new MenuFlyout();
            flyout.AreOpenCloseAnimationsEnabled = false;
            if (Resources.TryGetValue("FastMenuFlyoutPresenterStyle", out var st) && st is Style presenterStyle)
                flyout.MenuFlyoutPresenterStyle = presenterStyle;

            flyout.Opened += (_, _) =>
            {
                _ = Dispatcher.RunAsync(Windows.UI.Core.CoreDispatcherPriority.Normal, () =>
                {
                    if (flyout.XamlRoot?.Content is not FrameworkElement root)
                        return;
                    if (root.FindDescendant<MenuFlyoutPresenter>() is not { } presenter)
                        return;
                    presenter.Transitions = new TransitionCollection();
                    StripTransitionsDeep(presenter, 12);
                });
            };

            void Add(string text, RoutedEventHandler click)
            {
                var mi = new MenuFlyoutItem { Text = text };
                mi.Click += click;
                flyout.Items.Add(mi);
            }

            Add(L("Editor.Cut"), ValueTextBoxProofingCut_Click);
            Add(L("Editor.Copy"), ValueTextBoxProofingCopy_Click);
            Add(L("Editor.Paste"), ValueTextBoxProofingPaste_Click);
            Add(L("Editor.SelectAll"), ValueTextBoxProofingSelectAll_Click);
            return flyout;
        }

        private static void StripTransitionsDeep(DependencyObject node, int depth)
        {
            if (depth <= 0 || node is null)
                return;
            if (node is UIElement ue)
                ue.Transitions = new TransitionCollection();
            if (node is Panel panel)
                panel.ChildrenTransitions = new TransitionCollection();
            var n = VisualTreeHelper.GetChildrenCount(node);
            for (var i = 0; i < n; i++)
                StripTransitionsDeep(VisualTreeHelper.GetChild(node, i), depth - 1);
        }

        private void ValueTextBoxProofingCut_Click(object sender, RoutedEventArgs e)
        {
            if (valueTextBox.SelectionLength == 0)
                return;
            var pkg = new DataPackage();
            pkg.SetText(valueTextBox.SelectedText);
            Clipboard.SetContent(pkg);
            var i = valueTextBox.SelectionStart;
            valueTextBox.Text = valueTextBox.Text.Remove(i, valueTextBox.SelectionLength);
            valueTextBox.SelectionStart = i;
            valueTextBox.SelectionLength = 0;
        }

        private void ValueTextBoxProofingCopy_Click(object sender, RoutedEventArgs e)
        {
            if (valueTextBox.SelectionLength == 0)
                return;
            var pkg = new DataPackage();
            pkg.SetText(valueTextBox.SelectedText);
            Clipboard.SetContent(pkg);
        }

        private async void ValueTextBoxProofingPaste_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var content = Clipboard.GetContent();
                if (!content.Contains(StandardDataFormats.Text))
                    return;
                var t = await content.GetTextAsync();
                if (string.IsNullOrEmpty(t))
                    return;
                var i = valueTextBox.SelectionStart;
                var len = valueTextBox.SelectionLength;
                valueTextBox.Text = valueTextBox.Text.Remove(i, len).Insert(i, t);
                valueTextBox.SelectionStart = i + t.Length;
                valueTextBox.SelectionLength = 0;
            }
            catch { }
        }

        private void ValueTextBoxProofingSelectAll_Click(object sender, RoutedEventArgs e) =>
            valueTextBox.SelectAll();

        private void TreeSearchBox_RightTapped(object sender, RightTappedRoutedEventArgs e)
        {
            _treeSearchBoxContextMenu ??= CreateTreeSearchBoxContextMenu();
            var opts = new FlyoutShowOptions();
            try
            {
                opts.Position = e.GetPosition(treeSearchBox);
            }
            catch { }
            _treeSearchBoxContextMenu.ShowAt(treeSearchBox, opts);
            e.Handled = true;
        }

        private MenuFlyout CreateTreeSearchBoxContextMenu()
        {
            var menu = new MenuFlyout();

            MenuFlyoutItem Item(string text, Symbol symbol, Action action)
            {
                var it = new MenuFlyoutItem
                {
                    Text = text,
                    Icon = new SymbolIcon(symbol),
                };
                it.Click += (_, __) =>
                {
                    try { action(); } catch { }
                };
                return it;
            }

            var undo = Item(L("Editor.Undo"), Symbol.Undo, () =>
            {
                if (treeSearchBox.CanUndo)
                    treeSearchBox.Undo();
            });
            var cut = Item(L("Editor.Cut"), Symbol.Cut, TreeSearchBoxCut);
            var copy = Item(L("Editor.Copy"), Symbol.Copy, TreeSearchBoxCopy);
            var paste = Item(L("Editor.Paste"), Symbol.Paste, TreeSearchBoxPaste);
            var selectAll = Item(L("Editor.SelectAll"), Symbol.SelectAll, () => treeSearchBox.SelectAll());

            menu.Items.Add(undo);
            menu.Items.Add(new MenuFlyoutSeparator());
            menu.Items.Add(cut);
            menu.Items.Add(copy);
            menu.Items.Add(paste);
            menu.Items.Add(new MenuFlyoutSeparator());
            menu.Items.Add(selectAll);

            menu.Opening += (_, __) =>
            {
                undo.IsEnabled = treeSearchBox.CanUndo;
                var hasSelection = treeSearchBox.SelectionLength > 0;
                cut.IsEnabled = hasSelection;
                copy.IsEnabled = hasSelection;
                paste.IsEnabled = true;
                selectAll.IsEnabled = !string.IsNullOrEmpty(treeSearchBox.Text);
            };

            return menu;
        }

        private void TreeSearchBoxCut()
        {
            if (treeSearchBox.SelectionLength == 0)
                return;
            var pkg = new DataPackage();
            pkg.SetText(treeSearchBox.SelectedText);
            Clipboard.SetContent(pkg);
            var i = treeSearchBox.SelectionStart;
            treeSearchBox.Text = treeSearchBox.Text.Remove(i, treeSearchBox.SelectionLength);
            treeSearchBox.SelectionStart = i;
            treeSearchBox.SelectionLength = 0;
        }

        private void TreeSearchBoxCopy()
        {
            if (treeSearchBox.SelectionLength == 0)
                return;
            var pkg = new DataPackage();
            pkg.SetText(treeSearchBox.SelectedText);
            Clipboard.SetContent(pkg);
        }

        private async void TreeSearchBoxPaste()
        {
            try
            {
                var content = Clipboard.GetContent();
                if (!content.Contains(StandardDataFormats.Text))
                    return;
                var t = await content.GetTextAsync();
                if (string.IsNullOrEmpty(t))
                    return;
                var i = treeSearchBox.SelectionStart;
                var len = treeSearchBox.SelectionLength;
                treeSearchBox.Text = treeSearchBox.Text.Remove(i, len).Insert(i, t);
                treeSearchBox.SelectionStart = i + t.Length;
                treeSearchBox.SelectionLength = 0;
            }
            catch { }
        }

        private void TextBoxCommitTimer_Tick(object sender, object e)
        {
            _textBoxCommitTimer?.Stop();

            if (_selectedCandidate is null || _selectedCandidate.ValueType is not ResourceValueType.String)
                return;

            var text = valueTextBox.Text ?? string.Empty;
            if (!string.Equals(_selectedCandidate.StringValue, text, StringComparison.Ordinal))
            {
                try
                {
                    _selectedCandidate.SetValue(ResourceValueType.String, text);
                    _isDirty = true;
                }
                catch { }
            }
        }

        private void ValueTextEditor_RightTapped(object sender, RightTappedRoutedEventArgs e)
        {
            if (valueTextEditor?.Editor is null)
                return;

            e.Handled = true;

            _editorMenu ??= EditorContextMenuHelper.CreateChineseEditorMenu(valueTextEditor.Editor);
            EditorContextMenuHelper.ShowAtPointer(_editorMenu, valueTextEditor, e);
        }
    }
}
