using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using Windows.Storage;

namespace Elgato.WaveLink.Helpers
{
    public static class PriHelper
    {
        private static readonly string PriPath = Path.Combine(ApplicationData.Current.LocalFolder.Path, "resources.pri");

        public static async Task ExportDefaultPriFile(StorageFile file)
        {
            if (File.Exists(PriPath))
            {
                using var readStream = File.OpenRead(PriPath);
                using var writeStream = await file.OpenStreamForWriteAsync();
                await readStream.CopyToAsync(writeStream);
            }
        }

        public static async Task ImportChineseLocalization(StorageFile langFile)
        {
            // 覆盖汉化逻辑，直接替换中文资源
            var target = await StorageFile.GetFileFromPathAsync(PriPath);
            await langFile.CopyAndReplaceAsync(target);
        }

        public static async Task RebuildPriWithChineseResources()
        {
            // 调用 MakePri 生成中文 PRI
            await Task.Run(() => MakePriHelper.BuildChinesePri());
        }

        public static List<string> LoadResourceList()
        {
            return new List<string> { "中文菜单资源", "本地化字符串", "PRI 配置项" };
        }
    }
}