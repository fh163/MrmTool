using WinRT;
using MrmLib;
using MrmTool.Common;
using Windows.Storage;
using Windows.Storage.Pickers;
using Windows.ApplicationModel.DataTransfer;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using MrmTool.Dialogs;

namespace MrmTool
{
    /// <summary>
    /// An empty page that can be used on its own or navigated to within a Frame.
    /// </summary>
    public sealed partial class MainPage : Page
    {
        public MainPage()
        {
            this.InitializeComponent();
        }

        private async void OnOpenFileClicked(object sender, RoutedEventArgs e)
        {
            FileOpenPicker picker = new();
            picker.FileTypeFilter.Add(".pri");
            picker.CommitButtonText = "加载";
            picker.Initialize();

            if (await picker.PickSingleFileAsync() is { } file)
            {
                await LoadPri(file);
            }
        }

        private void MainGrid_DragOver(object sender, DragEventArgs e)
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

        [DynamicWindowsRuntimeCast(typeof(StorageFile))]
        private async void MainGrid_Drop(object sender, DragEventArgs e)
        {
            if (e.DataView.Contains(StandardDataFormats.StorageItems))
            {
                var items = await e.DataView.GetStorageItemsAsync();
                if (items.Count > 0 && items[0] is StorageFile file && file.Name.ToLowerInvariant().EndsWith(".pri"))
                {
                    await LoadPri(file);
                    e.Handled = true;
                }
            }
        }

        [DynamicWindowsRuntimeCast(typeof(ControlTemplate))]
        private async Task LoadPri(StorageFile file)
        {
            try
            {
                var pri = await PriFile.LoadAsync(file);
                Frame.Navigate(typeof(PriPage), (pri, file));
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

        private async void NoticeButtonClick(object sender, RoutedEventArgs e)
        {
            var dialog = new NoticeDialog();
            await dialog.ShowAsync();
        }
    }
}
