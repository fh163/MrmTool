using Common;
using MrmLib;
using MrmTool.Common;
using MrmTool.Dialogs;
using MrmTool.Models;
using MrmTool.Scintilla;
using System.Collections.ObjectModel;
using System.Numerics;
using Windows.Storage.Pickers;
using Windows.UI.Composition;
using Windows.ApplicationModel.DataTransfer;
using Windows.Storage;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
namespace MrmTool
{
public sealed partial class PriPage : Page
{
private PriFile? _pri;
private StorageFile? _currentFile;
private StorageFolder? _rootFolder;
private ResourceItem? _selectedResource;
private bool _useWebViewForSvg = false;
public ObservableCollection<ResourceItem> ResourceItems { get; } = [];
public PriPage()
{
InitializeComponent();
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
}
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
}
[DynamicWindowsRuntimeCast(typeof(ControlTemplate))]
private async Task TryLoadPri(StorageFile file)
{
try
{
LoadPri(await PriFile.LoadAsync(file));
_currentFile = file;
_rootFolder = null;
}
catch (Exception ex)
{
ContentDialog dialog = new()
{
Title = "错误",
Content = $"加载选中的PRI文件失败。\r\n异常：{ex.GetType().Name} (0x{ex.HResult:X8})\r\n异常信息：{ex.Message}\r\n堆栈跟踪：\r\n\r\n{ex.StackTrace}",
CloseButtonText = "确定",
DefaultButton = ContentDialogButton.Close,
Template = (ControlTemplate)Program.Application.Resources["ScrollableContentDialogTemplate"]
};
await dialog.ShowAsync();
}
}
private async void Open_Click(object sender, RoutedEventArgs e)
{
FileOpenPicker picker = new();
picker.FileTypeFilter.Add(".pri");
picker.CommitButtonText = "加载";
picker.Initialize();
if (await picker.PickSingleFileAsync() is { } file)
{
await TryLoadPri(file);
}
}
[DynamicWindowsRuntimeCast(typeof(ControlTemplate))]
private async Task SavePri(StorageFile file)
{
try
{
using var stream = await file.OpenAsync(FileAccessMode.ReadWrite);
stream.Size = 0;
await _pri!.WriteAsync(stream);
_currentFile = file;
}
catch (Exception ex)
{
ContentDialog dialog = new()
{
Title = "错误",
Content = $"保存PRI文件失败。\r\n异常：{ex.GetType().Name} (0x{ex.HResult:X8})\r\n异常信息：{ex.Message}\r\n堆栈跟踪：\r\n\r\n{ex.StackTrace}",
CloseButtonText = "确定",
DefaultButton = ContentDialogButton.Close,
Template = (ControlTemplate)Program.Application.Resources["ScrollableContentDialogTemplate"]
};
await dialog.ShowAsync();
}
}
private async void Save_Click(object sender, RoutedEventArgs e)
{
await SavePri(_currentFile!);
}
private async void SaveAs_Click(object sender, RoutedEventArgs e)
{
FileSavePicker picker = new();
picker.FileTypeChoices.Add("PRI文件", new List<string>() { ".pri" });
picker.Initialize();
if (await picker.PickSaveFileAsync() is { } file)
{
await SavePri(file);
}
}
private async void AddResource_Click(object sender, RoutedEventArgs e)
{
var parent = sender is MenuFlyoutItem item &&
item.DataContext is ResourceItem resourceItem ?
resourceItem.Name :
(ResourceItem)treeView.SelectedItem is ResourceItem resItem && resItem.IsFolder ?
resItem.Name : null;
var dialog = new NewResourceDialog(_pri!, parent);
            
try
{
if (await dialog.ShowAsync() is { } candidate)
{
var newItem = GetOrAddResourceItem(candidate.Candidate.ResourceName);
newItem.Candidates.Add(candidate);
_pri.ResourceCandidates.Add(candidate.Candidate);
}
}
catch (Exception ex)
{
ContentDialog errorDialog = new()
{
Title = "错误",
Content = $"创建资源失败。\r\n异常：{ex.GetType().Name} (0x{ex.HResult:X8})\r\n异常信息：{ex.Message}\r\n堆栈跟踪：\r\n\r\n{ex.StackTrace}",
CloseButtonText = "确定",
DefaultButton = ContentDialogButton.Close,
Template = (ControlTemplate)Program.Application.Resources["ScrollableContentDialogTemplate"]
};
await errorDialog.ShowAsync();
}
}
private void RemoveResources_Click(object sender, RoutedEventArgs e)
{
if (_pri is not null &&
sender is MenuFlyoutItem item &&
item.DataContext is ResourceItem resourceItem)
{
if (resourceItem == _selectedResource)
{
_selectedResource = null;
RemoveResourcesItem.IsEnabled = false;
UnloadAllPreviewElements();
}
resourceItem.Delete(_pri);
}
}
private async Task PickRootFolder()
{
FolderPicker picker = new();
picker.FileTypeFilter.Add("*");
picker.CommitButtonText = "选择PRI根文件夹";
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
ContentDialog dialog = new()
{
Title = "错误",
Content = "请先选择PRI根文件夹，才能将路径资源嵌入到PRI文件中。",
CloseButtonText = "确定",
DefaultButton = ContentDialogButton.Close,
};
await dialog.ShowAsync();
return;
}
}
await _pri!.ReplacePathCandidatesWithEmbeddedDataAsync(_rootFolder);
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
e.DragUIOverride.Caption = "拖放以加载PRI文件";
e.Handled = true;
}
else
{
e.AcceptedOperation = DataPackageOperation.None;
}
}
}
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
if (_selectedResource?.Type is not ResourceType.Svg)
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
[DynamicWindowsRuntimeCast(typeof(StorageFile))]
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
fileSizeLabel.Text = $"文件大小：{dataValue.Length} 字节";
}
else
{
DisplayStringCandidate(candidate.StringValue);
}
}
private void DisplayStringCandidate(string str)
{
FindName(nameof(valueTextEditor));
var editor = valueTextEditor.Editor;
editor.ReadOnly = false;
editor.WrapMode = Wrap.Word;
editor.CaretStyle = CaretStyle.Invisible;
editor.SetText(str);
editor.ReadOnly = true;
valueTextEditor.ApplyDefaultsToDocument();
if (_selectedResource is not null)
valueTextEditor.HighlightingLanguage = _selectedResource.Type is ResourceType.Xaml or ResourceType.Xbf ?
"xml" :
_selectedResource.DisplayName.GetExtensionAfterPeriod().ToScintillaLanguage();
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
imagePreviewer.Source = image;
imagePreviewer.Stretch = Stretch.None;
imagePreviewer.MaxWidth = double.PositiveInfinity;
imagePreviewer.MaxHeight = double.PositiveInfinity;
imagePreviewerContainer.UpdateLayout();
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
}
else
{
imagePreviewer.Stretch = Stretch.Uniform;
}
imagePreviewerContainer.UpdateLayout();
imagePreviewerContainer.ChangeView(null, null, (float)ratio, true);
}
return true;
}
catch
{
// Ignore
}
// TEMPORARY: we are temporary using XbfAnalyzer to decompile XBF files for now
if (type is ResourceType.Xbf)
{
try
{
stream.Seek(0);
var reader = new XbfAnalyzer.Xbf.XbfReader(stream.AsStream());
DisplayStringCandidate(reader.RootObject.ToString());
return true;
}
catch
{
// Ignore, fallback
}
}
if (type.IsText)
{
unsafe
{
if (stream is RandomAccessStreamOverBuffer rasob)
{
byte* ptr = default;
rasob.BufferByteAccess->Buffer(&ptr);
#pragma warning restore CS9123 // The '&' operator should not be used on parameters or local variables in async methods.
if (ptr is not null)
{
DisplayStringCandidate(Encoding.UTF8.GetString(ptr, (int)rasob.Size));
return true;
}
}
var size = (uint)stream.Size;
using var buffer = new NativeBuffer(size);
await stream.ReadAsync(buffer, size, InputStreamOptions.None);
unsafe
{
DisplayStringCandidate(Encoding.UTF8.GetString(buffer.Buffer, (int)size));
return true;
}
}
else if (type is ResourceType.Svg)
{
if (!_useWebViewForSvg)
{
try
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
Compositor compositor = Window.Current.Compositor;
ShapeVisual visual = compositor.CreateShapeVisual();
visual.Shapes.Add(compositor.CreateShapeFromNSVGImage(parse));
visual.RelativeSizeAdjustment = Vector2.One;
FindName(nameof(svgPreviewerContainer));
svgPreviewer.Width = parse->width;
svgPreviewer.Height = parse->height;
ElementCompositionPreview.SetElementChildVisual(svgPreviewer, visual);
NanoSVG.NanoSVG.nsvgDelete(parse);
return true;
}
}
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
return true;
}
}
else
{
stream.Seek(0);
UnloadObject(svgPreviewerContainer);
var size = (uint)stream.Size;
using var buffer = new NativeBuffer(size);
stream.Seek(0);
await stream.ReadAsync(buffer, size, InputStreamOptions.None);
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
webView.NavigateToString(html);
FindName(nameof(webView));
return true;
}
}
return false;
}
private async Task<StorageFile?> TryResolvePathCandidateAsync(string path)
{
if (File.Exists(path))
{
return await StorageFile.GetFileFromPathAsync(path);
}
if (_rootFolder != null)
{
try
{
var file = await _rootFolder.GetFileAsync(path);
return file;
}
catch
{
// Ignore
}
}
return null;
}
private async Task DisplayPathCandidate(StorageFile file)
{
try
{
if (file.FileType.ToLowerInvariant() is ".svg")
{
if (_useWebViewForSvg)
{
webView.NavigateToLocalFile(file);
UnloadNonErrorPreviewElements();
FindName(nameof(webView));
}
else
{
using var stream = await file.OpenReadAsync();
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
Compositor compositor = Window.Current.Compositor;
ShapeVisual visual = compositor.CreateShapeVisual();
visual.Shapes.Add(compositor.CreateShapeFromNSVGImage(parse));
visual.RelativeSizeAdjustment = Vector2.One;
FindName(nameof(svgPreviewerContainer));
svgPreviewer.Width = parse->width;
svgPreviewer.Height = parse->height;
ElementCompositionPreview.SetElementChildVisual(svgPreviewer, visual);
NanoSVG.NanoSVG.nsvgDelete(parse);
return;
}
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
}
}
}
else if (file.FileType.ToLowerInvariant() is ".png" or ".jpg" or ".jpeg" or ".bmp" or ".gif")
{
BitmapImage image = new BitmapImage();
await image.SetSourceAsync(await file.OpenReadAsync());
imagePreviewer.Source = image;
imagePreviewer.Stretch = Stretch.None;
imagePreviewer.MaxWidth = double.PositiveInfinity;
imagePreviewer.MaxHeight = double.PositiveInfinity;
imagePreviewerContainer.UpdateLayout();
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
}
else
{
imagePreviewer.Stretch = Stretch.Uniform;
}
imagePreviewerContainer.UpdateLayout();
imagePreviewerContainer.ChangeView(null, null, (float)ratio, true);
UnloadNonErrorPreviewElements();
FindName(nameof(imagePreviewerContainer));
}
else
{
using var stream = await file.OpenReadAsync();
if (await DisplayBinaryCandidate(stream, _selectedResource!.Type))
return;
                
stream.Seek(0);
using var reader = new StreamReader(stream.AsStream(), Encoding.UTF8);
string text = await reader.ReadToEndAsync();
DisplayStringCandidate(text);
FindName(nameof(valueTextEditor));
}
}
catch (Exception ex)
{
UnloadNonErrorPreviewElements();
FindName(nameof(failedToOpenFileContainer));
failedFileNameRun.Text = file.Name;
failedExceptionMessageRun.Text = ex.Message;
}
}
private void UnloadObject(UIElement element)
{
if (element != null) element.Visibility = Visibility.Collapsed;
}
private void candidatesList_SelectionChanged(object sender, SelectionChangedEventArgs e)
{
if (candidatesList.SelectedItem is CandidateItem item)
{
_ = DisplayCandidate(item);
}
}
private void ExportButton_Click(object sender, RoutedEventArgs e)
{
if (candidatesList.SelectedItem is CandidateItem item)
{
_ = ExportCandidate(item);
}
}
[DynamicWindowsRuntimeCast(typeof(ControlTemplate))]
private async Task ExportCandidate(CandidateItem item)
{
FileSavePicker picker = new();
picker.SuggestedFileName = _selectedResource!.DisplayName;
picker.FileTypeChoices.Add("所有文件", new List<string>() { "*" });
picker.Initialize();
if (await picker.PickSaveFileAsync() is { } file)
{
try
{
if (item.Candidate.ValueType is ResourceValueType.Path)
{
if (await TryResolvePathCandidateAsync(item.Candidate.StringValue) is StorageFile srcFile)
{
await srcFile.CopyAndReplaceAsync(file);
}
}
else
{
using var stream = await file.OpenAsync(FileAccessMode.ReadWrite);
stream.Size = 0;
var writer = new DataWriter(stream);
writer.WriteBytes(item.Candidate.DataValueReference.ToArray());
await writer.StoreAsync();
await writer.FlushAsync();
}
}
catch (Exception ex)
{
ContentDialog dialog = new()
{
Title = "错误",
Content = $"导出资源失败：{ex.Message}",
CloseButtonText = "确定"
};
await dialog.ShowAsync();
}
}
}
}
private void OpenFolder_Click(object sender, RoutedEventArgs e)
{
if (candidatesList.SelectedItem is CandidateItem item)
{
_ = OpenCandidateFolder(item);
}
}
private async Task OpenCandidateFolder(CandidateItem item)
{
if (item.Candidate.ValueType is ResourceValueType.Path)
{
if (await TryResolvePathCandidateAsync(item.Candidate.StringValue) is StorageFile file)
{
await Windows.System.Launcher.LaunchFolderAsync(await file.GetParentAsync());
}
}
}
private void EmbedPathCandidate_Click(object sender, RoutedEventArgs e)
{
if (candidatesList.SelectedItem is CandidateItem item)
{
_ = EmbedCandidate(item);
}
}
private async Task EmbedCandidate(CandidateItem item)
{
if (_rootFolder is null)
{
await PickRootFolder();
}
if (_rootFolder != null)
{
await _pri!.EmbedPathCandidateAsync(item.Candidate, _rootFolder);
RefreshResourceList_Click(null, null);
}
}
private void CreateOrModifyCandidate_Click(object sender, TappedRoutedEventArgs e)
{
_ = CreateOrModifyCandidate();
}
private void CreateOrModifyCandidate_Click(object sender, RoutedEventArgs e)
{
_ = CreateOrModifyCandidate();
}
[DynamicWindowsRuntimeCast(typeof(ControlTemplate))]
private async Task CreateOrModifyCandidate()
{
var parent = _selectedResource;
CandidateItem? item = candidatesList.SelectedItem as CandidateItem;
var dialog = new CreateOrModifyCandidateDialog(_selectedResource, item);
if (await dialog.ShowAsync() is { } newItem)
{
if (item != null)
{
// Modify existing
_pri.ResourceCandidates.Remove(item.Candidate);
parent!.Candidates.Remove(item);
}
// Add new
_pri.ResourceCandidates.Add(newItem.Candidate);
parent!.Candidates.Add(newItem);
candidatesList.ItemsSource = parent.Candidates;
await DisplayCandidate(newItem);
}
}
private void DeleteCandiate_Click(object sender, RoutedEventArgs e)
{
if (candidatesList.SelectedItem is CandidateItem item)
{
_pri!.ResourceCandidates.Remove(item.Candidate);
_selectedResource!.Candidates.Remove(item);
candidatesList.ItemsSource = _selectedResource.Candidates;
}
}
private void UseWebViewForSvg_Click(object sender, RoutedEventArgs e)
{
_useWebViewForSvg = ((ToggleMenuFlyoutItem)sender).IsChecked;
if (candidatesList.SelectedItem is CandidateItem item)
{
_ = DisplayCandidate(item);
}
}
private void SystemTheme_Click(object sender, RoutedEventArgs e)
{
Program.Application.RequestedTheme = ElementTheme.Default;
}
private void LightTheme_Click(object sender, RoutedEventArgs e)
{
Program.Application.RequestedTheme = ElementTheme.Light;
}
private void DarkTheme_Click(object sender, RoutedEventArgs e)
{
Program.Application.RequestedTheme = ElementTheme.Dark;
}
private void SimpleRename_Click(object sender, RoutedEventArgs e)
{
if (sender is MenuFlyoutItem item && item.DataContext is ResourceItem resourceItem)
{
_ = RenameResource(resourceItem, false);
}
}
private void FullRename_Click(object sender, RoutedEventArgs e)
{
if (sender is MenuFlyoutItem item && item.DataContext is ResourceItem resourceItem)
{
_ = RenameResource(resourceItem, true);
}
}
[DynamicWindowsRuntimeCast(typeof(ControlTemplate))]
private async Task RenameResource(ResourceItem resourceItem, bool fullRename)
{
ContentDialog dialog = new()
{
Title = fullRename ? "完整重命名" : "简单重命名",
PrimaryButtonText = "确定",
CloseButtonText = "取消"
};
TextBox textBox = new() { PlaceholderText = "新名称" };
dialog.Content = textBox;
if (await dialog.ShowAsync() == ContentDialogResult.Primary)
{
string newName = textBox.Text;
if (!string.IsNullOrEmpty(newName))
{
resourceItem.Rename(_pri!, newName, fullRename);
RefreshResourceList_Click(null, null);
}
}
}
private void Notice_Click(object sender, RoutedEventArgs e)
{
_ = new NoticeDialog().ShowAsync();
}
private void TryAgain_Click(object sender, RoutedEventArgs e)
{
if (candidatesList.SelectedItem is CandidateItem item)
{
_ = DisplayCandidate(item);
}
}

// ======================================
// 👇 只加了这些新的方法，其他的都是你原来的代码！
// ======================================

// TreeView 汉化功能菜单的后台方法
private void PriMenuFlyout_Opening(object sender, object e)
{
    bool hasPri = _pri != null;
    miExportPri.IsEnabled = hasPri;
    miImportLang.IsEnabled = hasPri;
    miRebuildPri.IsEnabled = hasPri;
    miRefresh.IsEnabled = hasPri;
}

[DynamicWindowsRuntimeCast(typeof(ControlTemplate))]
private async void ExportPriFile_Click(object sender, RoutedEventArgs e)
{
    try
    {
        FileSavePicker picker = new();
        picker.FileTypeChoices.Add("PRI 文件", new List<string>() { ".pri" });
        picker.SuggestedFileName = "resources_backup";
        picker.Initialize();
        
        if (await picker.PickSaveFileAsync() is { } file && _pri != null)
        {
            await PriHelper.ExportPriFileAsync(_pri, file);
            
            ContentDialog dialog = new()
            {
                Title = "成功",
                Content = "导出 PRI 文件成功",
                CloseButtonText = "确定"
            };
            await dialog.ShowAsync();
        }
    }
    catch (Exception ex)
    {
        ContentDialog dialog = new()
        {
            Title = "错误",
            Content = $"导出失败：{ex.Message}",
            CloseButtonText = "确定",
            Template = (ControlTemplate)Program.Application.Resources["ScrollableContentDialogTemplate"]
        };
        await dialog.ShowAsync();
    }
}

[DynamicWindowsRuntimeCast(typeof(ControlTemplate))]
private async void ImportChineseLang_Click(object sender, RoutedEventArgs e)
{
    try
    {
        FileOpenPicker picker = new();
        picker.FileTypeFilter.Add(".pri");
        picker.CommitButtonText = "导入";
        picker.Initialize();
        
        if (await picker.PickSingleFileAsync() is { } file && _pri != null)
        {
            await PriHelper.ImportChineseLocalizationAsync(_pri, file);
            
            ContentDialog dialog = new()
            {
                Title = "成功",
                Content = "中文汉化包导入完成",
                CloseButtonText = "确定"
            };
            await dialog.ShowAsync();
            
            RefreshResourceList_Click(null, null);
        }
    }
    catch (Exception ex)
    {
        ContentDialog dialog = new()
        {
            Title = "错误",
            Content = $"导入失败：{ex.Message}",
            CloseButtonText = "确定",
            Template = (ControlTemplate)Program.Application.Resources["ScrollableContentDialogTemplate"]
        };
        await dialog.ShowAsync();
    }
}

[DynamicWindowsRuntimeCast(typeof(ControlTemplate))]
private async void RebuildPri_Click(object sender, RoutedEventArgs e)
{
    try
    {
        if (_pri != null)
        {
            await PriHelper.RebuildPriWithChineseAsync(_pri);
            
            ContentDialog dialog = new()
            {
                Title = "成功",
                Content = "PRI 重新生成并应用中文成功",
                CloseButtonText = "确定"
            };
            await dialog.ShowAsync();
        }
    }
    catch (Exception ex)
    {
        ContentDialog dialog = new()
        {
            Title = "错误",
            Content = $"生成失败：{ex.Message}",
            CloseButtonText = "确定",
            Template = (ControlTemplate)Program.Application.Resources["ScrollableContentDialogTemplate"]
        };
        await dialog.ShowAsync();
    }
}

private void RefreshResourceList_Click(object sender, RoutedEventArgs e)
{
    if (_pri != null)
    {
        LoadPri(_pri);
    }
}

// 文本编辑器中文菜单的后台方法
private void EditorMenuFlyout_Opening(object sender, object e)
{
    if (valueTextEditor?.Editor != null)
    {
        var editor = valueTextEditor.Editor;
        miUndo.IsEnabled = editor.CanUndo;
        miRedo.IsEnabled = editor.CanRedo;
        bool hasSelection = editor.SelectionLength > 0;
        miCut.IsEnabled = hasSelection;
        miCopy.IsEnabled = hasSelection;
        miDelete.IsEnabled = hasSelection;
        miPaste.IsEnabled = Clipboard.HasContent();
        miSelectAll.IsEnabled = editor.TextLength > 0;
    }
}

private void Undo_Click(object sender, RoutedEventArgs e)
{
    valueTextEditor.Editor.Undo();
}

private void Redo_Click(object sender, RoutedEventArgs e)
{
    valueTextEditor.Editor.Redo();
}

private void Cut_Click(object sender, RoutedEventArgs e)
{
    valueTextEditor.Editor.Cut();
}

private void Copy_Click(object sender, RoutedEventArgs e)
{
    valueTextEditor.Editor.Copy();
}

private void Paste_Click(object sender, RoutedEventArgs e)
{
    valueTextEditor.Editor.Paste();
}

private void Delete_Click(object sender, RoutedEventArgs e)
{
    valueTextEditor.Editor.Delete();
}

private void SelectAll_Click(object sender, RoutedEventArgs e)
{
    valueTextEditor.Editor.SelectAll();
}
}
}
