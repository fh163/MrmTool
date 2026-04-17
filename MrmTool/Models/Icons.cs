using Windows.UI.Xaml.Media.Imaging;
using System.IO;

namespace MrmTool.Models
{
    internal static class Icons
    {
        private static BitmapImage CreateIcon(string fileName, bool large)
        {
            var image = new BitmapImage();
            if (!large)
            {
                image.DecodePixelWidth = 28;
                image.DecodePixelType = DecodePixelType.Logical;
            }

            var localPath = Path.Combine(AppContext.BaseDirectory, "Assets", "Icons", fileName);
            if (File.Exists(localPath))
            {
                image.UriSource = new Uri(localPath, UriKind.Absolute);
                return image;
            }

            image.UriSource = new Uri($"ms-appx:///Assets/Icons/{fileName}");
            return image;
        }

        static internal Lazy<BitmapImage> Unknown { get; } = new(() => CreateIcon("generic.png", large: false));
        
        static internal Lazy<BitmapImage> UnknownLarge { get; } = new(() => CreateIcon("generic.png", large: true));

        static internal Lazy<BitmapImage> Folder { get; } = new(() => CreateIcon("folder.png", large: false));
        
        static internal Lazy<BitmapImage> FolderLarge { get; } = new(() => CreateIcon("folder.png", large: true));

        static internal Lazy<BitmapImage> Text { get; } = new(() => CreateIcon("text.png", large: false));
        
        static internal Lazy<BitmapImage> TextLarge { get; } = new(() => CreateIcon("text.png", large: true));

        static internal Lazy<BitmapImage> Image { get; } = new(() => CreateIcon("image.png", large: false));
        
        static internal Lazy<BitmapImage> ImageLarge { get; } = new(() => CreateIcon("image.png", large: true));

        static internal Lazy<BitmapImage> Audio { get; } = new(() => CreateIcon("audio.png", large: false));
        
        static internal Lazy<BitmapImage> AudioLarge { get; } = new(() => CreateIcon("audio.png", large: true));

        static internal Lazy<BitmapImage> Video { get; } = new(() => CreateIcon("video.png", large: false));
        
        static internal Lazy<BitmapImage> VideoLarge { get; } = new(() => CreateIcon("video.png", large: true));

        static internal Lazy<BitmapImage> Xaml { get; } = new(() => CreateIcon("xaml.png", large: false));
        
        static internal Lazy<BitmapImage> XamlLarge { get; } = new(() => CreateIcon("xaml.png", large: true));

        static internal Lazy<BitmapImage> Font { get; } = new(() => CreateIcon("font.png", large: false));
        
        static internal Lazy<BitmapImage> FontLarge { get; } = new(() => CreateIcon("font.png", large: true));
    }
}
