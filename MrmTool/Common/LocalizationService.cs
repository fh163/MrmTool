using System;
using System.Collections.Generic;
using System.IO;
using Windows.Globalization;
using Windows.Storage;
using Windows.System.UserProfile;

namespace MrmTool.Common
{
    internal static class LocalizationService
    {
        private const string LanguageSettingKey = "AppLanguageOverride";
        private static readonly object _gate = new();
        private static string? _cachedStartupLanguage;

        private static string SettingsFilePath
        {
            get
            {
                var dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "MrmTool");
                Directory.CreateDirectory(dir);
                return Path.Combine(dir, "language.txt");
            }
        }

        private static readonly Dictionary<string, string> ZhHans = new(StringComparer.Ordinal)
        {
            ["App.Error.ProgramException"] = "程序异常",
            ["Common.Button.Ok"] = "确定",
            ["Common.Button.Confirm"] = "确认",
            ["Common.Button.Cancel"] = "取消",
            ["Common.Button.Exit"] = "退出",
            ["Common.Button.Close"] = "关闭",
            ["Menu.File"] = "文件",
            ["Menu.Edit"] = "编辑",
            ["Menu.View"] = "视图",
            ["Menu.Language"] = "语言",
            ["Menu.Help"] = "帮助",
            ["Menu.Language.Chinese"] = "简体中文",
            ["Menu.Language.English"] = "English",
            ["Status.NoFileOpened"] = "未打开文件",
            ["Dialog.Title.Usage"] = "使用说明",
            ["Dialog.Title.ThirdPartyNotice"] = "第三方声明",
            ["Dialog.Error.FailedToLoadNotice"] = "（无法加载第三方声明）",
            ["Dialog.Title.ImportDone"] = "导入完成",
            ["Dialog.Title.ExportDone"] = "导出完成",
            ["Dialog.Body.Export.Completed"] = "已导出 {0} 条字符串候选。",
            ["Dialog.Body.Export.NoCandidates"] = "未找到可导出的字符串资源。",
            ["Dialog.Title.NotFound"] = "未找到",
            ["Dialog.Title.DeleteConfirm"] = "确认删除",
            ["Dialog.Body.DeleteResource"] = "确定要删除资源「{0}」吗？\r\n此操作将删除该节点及其所有子节点/候选，且不可撤销。",
            ["Dialog.Title.ExportFailed"] = "导出失败",
            ["Dialog.Title.LoadFailed"] = "加载失败",
            ["Dialog.Title.OpenPrompt"] = "打开提示",
            ["Dialog.Title.ExitPrompt"] = "退出提示",
            ["Dialog.Body.ExitPrompt"] = "当前打开的 PRI 文件已修改，是否保存？\r\n\r\n「确认」保存并关闭\r\n「取消」返回继续编辑\r\n「退出」不保存并关闭",
            ["Dialog.Body.OpenPrompt"] = "当前 PRI 文件已修改，打开新文件前是否先保存？\r\n\r\n「确认」保存并继续打开\r\n「取消」停止打开\r\n「退出」不保存直接打开",
            ["Dialog.Title.SaveFailed"] = "保存失败",
            ["Dialog.Title.OpenFailed"] = "打开失败",
            ["Dialog.Title.ReadFailed"] = "读取失败",
            ["Dialog.Title.CreateResourceFailed"] = "新建资源失败",
            ["Dialog.Title.RenameFailed"] = "重命名失败",
            ["Dialog.Title.ModifyFailed"] = "修改失败",
            ["Dialog.Title.CreateFailed"] = "新建失败",
            ["Dialog.Error.SelectRootBeforeEmbed"] = "请先选择 PRI 根目录，才能将路径资源嵌入到 PRI 文件。",
            ["Dialog.Error.Generic"] = "错误",
            ["Dialog.Error.CannotExport"] = "无法导出",
            ["Dialog.Error.ExportSelectCandidate"] = "该资源包含多条候选，请先在右上列表选中一条后再导出。",
            ["Dialog.Body.DeleteCandidate"] = "确定要删除该项吗？",
            ["Dialog.Body.TreeSearchNotFoundScope"] = "在{0}没有在资源名、显示名或字符串/路径候选内容中找到「{1}」。",
            ["Dialog.Body.TreeSearchNodeNotFound"] = "无法在树中定位资源「{0}」（数据与树控件未对齐）。请尝试清除搜索后再试。",
            ["Language.Switch.RequiresRestart.Title"] = "语言切换",
            ["Language.Switch.RequiresRestart.Body"] = "语言将在重启应用后生效，是否立即重启？",
            ["Usage.Body"] =
@"- 通过“文件 → 打开...”加载 .pri 文件
- 左侧资源树选择资源，右侧上方为候选列表，右下方为预览/编辑区
- 字符串资源可在右下方直接编辑（右键菜单已汉化）
- 批量导入翻译：在“编辑 → 批量导入翻译...”选择文本文件
- 批量导出翻译：在“编辑 → 批量导出翻译...”或左侧资源右键“导出(批量)”

批量导入格式（每行一条）：
- 推荐：name=""Resources/InputGroup_InstalledApps"" Language=""EN"" <Value>Installed apps</Value>
- 也支持：资源名\t语言值\t译文
- 兼容旧格式：资源名=译文（仅唯一匹配时替换）

支持的文件类型：
- .txt / .tsv / .csv（按纯文本逐行解析；其他扩展名也可选）",
            ["Panel.BatchLanguage.All"] = "（全部语言）",
            ["Panel.BatchLanguage.Desc"] = "Language 限定符值（可选）：\r\n- 选择具体值：仅处理该语言\r\n- 选择“全部语言”：处理所有语言",
            ["Panel.Candidate.New"] = "新建候选",
            ["Panel.Candidate.Edit"] = "修改候选",
            ["Panel.Candidate.AddQualifier"] = "添加限定符",
            ["Panel.Candidate.Button.Create"] = "创建",
            ["Panel.Candidate.Button.Save"] = "保存修改",
            ["Panel.Candidate.Button.Add"] = "添加",
            ["Panel.Candidate.Button.Back"] = "返回",
            ["Panel.Candidate.Button.Delete"] = "删除",
            ["Panel.Candidate.Button.New"] = "新增",
            ["Panel.Candidate.Button.ImportFile"] = "从文件导入",
            ["Panel.Candidate.Button.SelectFile"] = "选择文件...",
            ["Panel.Candidate.Button.EmbeddedDataLoaded"] = "（已嵌入数据）",
            ["Panel.Candidate.Label.Type"] = "类型",
            ["Panel.Candidate.Label.Content"] = "内容",
            ["Panel.Candidate.Label.FilePath"] = "文件路径",
            ["Panel.Candidate.Label.DataSource"] = "数据来源",
            ["Panel.Candidate.Label.Qualifier"] = "限定符",
            ["Panel.Candidate.Label.Attribute"] = "属性",
            ["Panel.Candidate.Label.Operator"] = "运算符",
            ["Panel.Candidate.Label.Value"] = "值",
            ["Panel.Candidate.Label.Priority"] = "优先级",
            ["Panel.Candidate.Label.Fallback"] = "回退分数",
            ["Panel.Candidate.Type.String"] = "字符串",
            ["Panel.Candidate.Type.Path"] = "文件路径",
            ["Panel.Candidate.Type.EmbeddedData"] = "嵌入数据",
            ["Panel.Candidate.Source.File"] = "文件",
            ["Panel.Candidate.Source.TextUtf8"] = "文本(UTF-8)",
            ["Panel.Candidate.Source.TextUtf16"] = "文本(UTF-16LE)",
            ["Panel.Candidate.Error.AddQualifierFailed"] = "添加限定符失败：{0}",
            ["Panel.Candidate.Error.DuplicateQualifierCandidate"] = "该资源已存在相同限定符的候选。",
            ["Panel.Candidate.DeleteQualifier.Title"] = "确认删除",
            ["Panel.Candidate.DeleteQualifier.Body"] = "确定要删除限定符「{0}」吗？",
            ["Panel.Candidate.DeleteQualifier.Button"] = "删除",
            ["Qualifier.Attribute.Language"] = "语言",
            ["Qualifier.Attribute.Contrast"] = "对比度",
            ["Qualifier.Attribute.Scale"] = "缩放",
            ["Qualifier.Attribute.HomeRegion"] = "主区域",
            ["Qualifier.Attribute.TargetSize"] = "目标大小",
            ["Qualifier.Attribute.LayoutDirection"] = "布局方向",
            ["Qualifier.Attribute.Theme"] = "主题",
            ["Qualifier.Attribute.AlternateForm"] = "替代形式",
            ["Qualifier.Attribute.DXFeatureLevel"] = "DirectX 功能级别",
            ["Qualifier.Attribute.Configuration"] = "配置",
            ["Qualifier.Attribute.DeviceFamily"] = "设备系列",
            ["Qualifier.Operator.False"] = "假",
            ["Qualifier.Operator.True"] = "真",
            ["Qualifier.Operator.AttributeDefined"] = "属性已定义",
            ["Qualifier.Operator.AttributeUndefined"] = "属性未定义",
            ["Qualifier.Operator.NotEqual"] = "不等于",
            ["Qualifier.Operator.NoMatch"] = "不匹配",
            ["Qualifier.Operator.Less"] = "小于",
            ["Qualifier.Operator.LessOrEqual"] = "小于等于",
            ["Qualifier.Operator.Greater"] = "大于",
            ["Qualifier.Operator.GreaterOrEqual"] = "大于等于",
            ["Qualifier.Operator.Match"] = "匹配",
            ["Qualifier.Operator.Equal"] = "等于",
            ["Qualifier.Format.Priority"] = "，优先级 = {0}",
            ["Qualifier.Format.FallbackScore"] = "，回退分数 = {0}",
            ["Panel.Rename.Title"] = "重命名资源",
            ["Panel.Rename.Label.Name"] = "名称",
            ["Panel.Rename.Placeholder"] = "请输入新名称",
            ["Panel.Rename.Button.Rename"] = "重命名",
            ["Panel.NewResource.Title"] = "新建资源",
            ["Panel.NewResource.Button.Create"] = "创建",
            ["Panel.NewResource.Button.Cancel"] = "取消",
            ["Panel.NewResource.Error.DuplicateName"] = "已存在同名资源。",
            ["Panel.NewResource.Error.SelectEmbedFile"] = "请选择要嵌入的文件。",
            ["Panel.NewResource.Label.Name"] = "名称",
            ["Panel.NewResource.Label.Type"] = "类型",
            ["Model.Candidate.Type.Unknown"] = "未知",
            ["Model.Candidate.Qualifiers.None"] = "（无）",
            ["Editor.Undo"] = "撤销",
            ["Editor.Redo"] = "重做",
            ["Editor.Cut"] = "剪切",
            ["Editor.Copy"] = "复制",
            ["Editor.Paste"] = "粘贴",
            ["Editor.Delete"] = "删除",
            ["Editor.SelectAll"] = "全选",
            ["Tree.NewResource"] = "新建资源",
            ["Tree.CopyResourceName"] = "复制资源名",
            ["Tree.BatchExport"] = "导出(批量)",
            ["Tree.SimpleRename"] = "简单重命名",
            ["Tree.FullRename"] = "完整重命名",
            ["Tree.Scope.All"] = "资源树",
            ["Tree.Scope.Folder"] = "文件夹「{0}」内",
            ["Common.Export"] = "导出",
            ["Common.Delete"] = "删除",
            ["Common.Edit"] = "修改",
            ["Common.Clear"] = "清除",
            ["Common.Retry"] = "重试",
            ["Menu.File.Open"] = "打开...",
            ["Picker.Commit.Load"] = "加载",
            ["Picker.Commit.Import"] = "导入",
            ["Picker.Commit.SelectRoot"] = "选择 PRI 根目录",
            ["Picker.Commit.Save"] = "保存",
            ["Picker.FileType.Text"] = "文本文件",
            ["Picker.FileType.All"] = "所有文件",
            ["Drag.DropLoadPri"] = "松开以加载 PRI 文件",
            ["Common.Button.Next"] = "下一步",
            ["Batch.Import.Title"] = "批量导入",
            ["Batch.Export.Title"] = "批量导出",
            ["Batch.Import.Summary.Total"] = "处理行数",
            ["Batch.Import.Summary.Replaced"] = "成功替换",
            ["Batch.Import.Summary.SkippedByLanguage"] = "按语言筛选跳过",
            ["Batch.Import.Summary.NoMatchName"] = "未匹配:name",
            ["Batch.Import.Summary.NoMatchLanguage"] = "未匹配:Language",
            ["Batch.Import.Summary.NoMatch"] = "未匹配",
            ["Batch.Import.Summary.NoMatchOther"] = "未匹配:其他",
            ["Batch.Import.Summary.Ambiguous"] = "歧义跳过",
            ["Batch.Import.Summary.Invalid"] = "格式无效",
            ["Batch.Import.Summary.TotalSkipped"] = "总失败/跳过",
            ["Batch.Import.Summary.FailureFile"] = "失败明细文件",
            ["Batch.Import.Report.Title"] = "批量导入失败/跳过明细",
            ["Batch.Import.Report.Time"] = "时间",
            ["Batch.Import.Fail.Invalid"] = "格式无效",
            ["Batch.Import.Fail.ByLanguage"] = "按语言筛选跳过",
            ["Batch.Import.Fail.Ambiguous"] = "歧义跳过",
            ["Batch.Import.Fail.NoMatchName"] = "未匹配:name",
            ["Batch.Import.Fail.NoMatchLanguage"] = "未匹配:Language",
            ["Batch.Import.Fail.NoMatch"] = "未匹配",
            ["Menu.File.Save"] = "保存",
            ["Menu.File.SaveAs"] = "另存为...",
            ["Menu.File.Exit"] = "退出",
            ["Menu.Edit.NewResource"] = "新建资源",
            ["Menu.Edit.DeleteSelected"] = "删除选中资源",
            ["Menu.Edit.BatchImport"] = "批量导入翻译...",
            ["Menu.Edit.BatchExport"] = "批量导出翻译...",
            ["Menu.Edit.SetRootFolder"] = "设置根目录...",
            ["Menu.Edit.SetRootFolderShort"] = "设置根目录",
            ["Menu.Edit.EmbedPaths"] = "将路径资源嵌入到PRI",
            ["Menu.View.UseWebViewSvg"] = "使用WebView预览SVG",
            ["Menu.View.PreviewTheme"] = "预览区主题",
            ["Menu.View.Theme.System"] = "跟随系统",
            ["Menu.View.Theme.Light"] = "浅色",
            ["Menu.View.Theme.Dark"] = "深色",
            ["Menu.Help.Usage"] = "使用说明",
            ["Menu.Help.Notice"] = "第三方声明",
            ["TitleBar.Minimize"] = "最小化",
            ["TitleBar.MaxRestore"] = "最大化/还原",
            ["TitleBar.Close"] = "关闭",
            ["Search.Placeholder"] = "搜索资源...",
            ["Search.Button"] = "搜索",
            ["Empty.NoPriOpened"] = "尚未打开 PRI 文件",
            ["Empty.OpenPriFile"] = "打开 PRI 文件",
            ["Candidates.Type"] = "类型",
            ["Candidates.Qualifier"] = "限定符",
            ["Candidate.ShowInExplorer"] = "在资源管理器中显示",
            ["Candidate.TryEmbedToPri"] = "尝试嵌入到PRI",
            ["Candidate.Add"] = "添加候选",
            ["Preview.InvalidRoot"] = "当前根目录不包含该文件。你可以在下面指定新的根目录。",
            ["Preview.ExportToDisk"] = "导出到磁盘",
            ["Preview.FailedOpen.Prefix"] = "无法打开",
            ["Preview.FailedOpen.Suffix"] = "，请确认文件有效、可访问，并且未被其他应用占用。",
            ["Preview.FailedOpen.ExceptionLabel"] = "异常：",
            ["Preview.FailedXbf.Prefix"] = "无法反编译",
            ["Preview.FailedXbf.Suffix"] = "。",
            ["Preview.FailedXbf.ExceptionLabel"] = "异常：",
            ["Crash.Error.Prefix"] = "发生异常：",
            ["Crash.Error.LogPath"] = "日志：",
        };

        private static readonly Dictionary<string, string> EnUs = new(StringComparer.Ordinal)
        {
            ["App.Error.ProgramException"] = "Application Error",
            ["Common.Button.Ok"] = "OK",
            ["Common.Button.Confirm"] = "Confirm",
            ["Common.Button.Cancel"] = "Cancel",
            ["Common.Button.Exit"] = "Exit",
            ["Common.Button.Close"] = "Close",
            ["Menu.File"] = "File",
            ["Menu.Edit"] = "Edit",
            ["Menu.View"] = "View",
            ["Menu.Language"] = "Language",
            ["Menu.Help"] = "Help",
            ["Menu.Language.Chinese"] = "Simplified Chinese",
            ["Menu.Language.English"] = "English",
            ["Status.NoFileOpened"] = "No file opened",
            ["Dialog.Title.Usage"] = "Usage",
            ["Dialog.Title.ThirdPartyNotice"] = "Third-Party Notices",
            ["Dialog.Error.FailedToLoadNotice"] = "(Failed to load third-party notices)",
            ["Dialog.Title.ImportDone"] = "Import Complete",
            ["Dialog.Title.ExportDone"] = "Export Complete",
            ["Dialog.Body.Export.Completed"] = "Exported {0} string candidates.",
            ["Dialog.Body.Export.NoCandidates"] = "No exportable string resources found.",
            ["Dialog.Title.NotFound"] = "Not Found",
            ["Dialog.Title.DeleteConfirm"] = "Confirm Delete",
            ["Dialog.Body.DeleteResource"] = "Are you sure you want to delete resource \"{0}\"?\r\nThis will remove the node and all its children/candidates, and cannot be undone.",
            ["Dialog.Title.ExportFailed"] = "Export Failed",
            ["Dialog.Title.LoadFailed"] = "Load Failed",
            ["Dialog.Title.OpenPrompt"] = "Open Prompt",
            ["Dialog.Title.ExitPrompt"] = "Exit Prompt",
            ["Dialog.Body.ExitPrompt"] = "The currently opened PRI file has unsaved changes. Save before closing?\r\n\r\n[Confirm] Save and close\r\n[Cancel] Return and continue editing\r\n[Exit] Close without saving",
            ["Dialog.Body.OpenPrompt"] = "The current PRI file has unsaved changes. Save before opening a new file?\r\n\r\n[Confirm] Save and continue opening\r\n[Cancel] Stop opening\r\n[Exit] Open without saving",
            ["Dialog.Title.SaveFailed"] = "Save Failed",
            ["Dialog.Title.OpenFailed"] = "Open Failed",
            ["Dialog.Title.ReadFailed"] = "Read Failed",
            ["Dialog.Title.CreateResourceFailed"] = "Create Resource Failed",
            ["Dialog.Title.RenameFailed"] = "Rename Failed",
            ["Dialog.Title.ModifyFailed"] = "Modify Failed",
            ["Dialog.Title.CreateFailed"] = "Create Failed",
            ["Dialog.Error.SelectRootBeforeEmbed"] = "Please select the PRI root folder before embedding path resources into the PRI file.",
            ["Dialog.Error.Generic"] = "Error",
            ["Dialog.Error.CannotExport"] = "Cannot Export",
            ["Dialog.Error.ExportSelectCandidate"] = "This resource has multiple candidates. Please select one in the top-right list before exporting.",
            ["Dialog.Body.DeleteCandidate"] = "Are you sure you want to delete this item?",
            ["Dialog.Body.TreeSearchNotFoundScope"] = "No match for \"{1}\" was found in resource name, display name, or string/path candidate content within {0}.",
            ["Dialog.Body.TreeSearchNodeNotFound"] = "Unable to locate resource \"{0}\" in the tree (tree/data out of sync). Try clearing search and trying again.",
            ["Language.Switch.RequiresRestart.Title"] = "Language Switch",
            ["Language.Switch.RequiresRestart.Body"] = "Language changes take effect after restart. Restart now?",
            ["Usage.Body"] =
@"- Load a .pri file via File -> Open...
- Select resources in the left tree; candidates appear in the top-right list, and preview/editor appears in the bottom-right pane
- String resources can be edited directly in the bottom-right editor (context menu is localized)
- Batch import translations via Edit -> Batch Import Translations... and choose a text file
- Batch export translations via Edit -> Batch Export Translations... or the left resource tree context menu (Export Batch)

Batch import formats (one entry per line):
- Recommended: name=""Resources/InputGroup_InstalledApps"" Language=""EN"" <Value>Installed apps</Value>
- Also supported: ResourceName<TAB>LanguageValue<TAB>Translation
- Legacy compatible: ResourceName=Translation (replaced only when uniquely matched)

Supported file types:
- .txt / .tsv / .csv (parsed line-by-line as plain text; other extensions can also be selected)",
            ["Panel.BatchLanguage.All"] = "(All languages)",
            ["Panel.BatchLanguage.Desc"] = "Language qualifier value (optional):\r\n- Select a specific value: process only that language\r\n- Select \"All languages\": process all languages",
            ["Panel.Candidate.New"] = "New Candidate",
            ["Panel.Candidate.Edit"] = "Edit Candidate",
            ["Panel.Candidate.AddQualifier"] = "Add Qualifier",
            ["Panel.Candidate.Button.Create"] = "Create",
            ["Panel.Candidate.Button.Save"] = "Save",
            ["Panel.Candidate.Button.Add"] = "Add",
            ["Panel.Candidate.Button.Back"] = "Back",
            ["Panel.Candidate.Button.Delete"] = "Delete",
            ["Panel.Candidate.Button.New"] = "New",
            ["Panel.Candidate.Button.ImportFile"] = "Import from File",
            ["Panel.Candidate.Button.SelectFile"] = "Select File...",
            ["Panel.Candidate.Button.EmbeddedDataLoaded"] = "(Embedded data loaded)",
            ["Panel.Candidate.Label.Type"] = "Type",
            ["Panel.Candidate.Label.Content"] = "Content",
            ["Panel.Candidate.Label.FilePath"] = "File Path",
            ["Panel.Candidate.Label.DataSource"] = "Data Source",
            ["Panel.Candidate.Label.Qualifier"] = "Qualifiers",
            ["Panel.Candidate.Label.Attribute"] = "Attribute",
            ["Panel.Candidate.Label.Operator"] = "Operator",
            ["Panel.Candidate.Label.Value"] = "Value",
            ["Panel.Candidate.Label.Priority"] = "Priority",
            ["Panel.Candidate.Label.Fallback"] = "Fallback Score",
            ["Panel.Candidate.Type.String"] = "String",
            ["Panel.Candidate.Type.Path"] = "File Path",
            ["Panel.Candidate.Type.EmbeddedData"] = "Embedded Data",
            ["Panel.Candidate.Source.File"] = "File",
            ["Panel.Candidate.Source.TextUtf8"] = "Text (UTF-8)",
            ["Panel.Candidate.Source.TextUtf16"] = "Text (UTF-16LE)",
            ["Panel.Candidate.Error.AddQualifierFailed"] = "Failed to add qualifier: {0}",
            ["Panel.Candidate.Error.DuplicateQualifierCandidate"] = "A candidate with the same qualifiers already exists for this resource.",
            ["Panel.Candidate.DeleteQualifier.Title"] = "Confirm Delete",
            ["Panel.Candidate.DeleteQualifier.Body"] = "Are you sure you want to delete qualifier \"{0}\"?",
            ["Panel.Candidate.DeleteQualifier.Button"] = "Delete",
            ["Qualifier.Attribute.Language"] = "Language",
            ["Qualifier.Attribute.Contrast"] = "Contrast",
            ["Qualifier.Attribute.Scale"] = "Scale",
            ["Qualifier.Attribute.HomeRegion"] = "Home Region",
            ["Qualifier.Attribute.TargetSize"] = "Target Size",
            ["Qualifier.Attribute.LayoutDirection"] = "Layout Direction",
            ["Qualifier.Attribute.Theme"] = "Theme",
            ["Qualifier.Attribute.AlternateForm"] = "Alternate Form",
            ["Qualifier.Attribute.DXFeatureLevel"] = "DirectX Feature Level",
            ["Qualifier.Attribute.Configuration"] = "Configuration",
            ["Qualifier.Attribute.DeviceFamily"] = "Device Family",
            ["Qualifier.Operator.False"] = "False",
            ["Qualifier.Operator.True"] = "True",
            ["Qualifier.Operator.AttributeDefined"] = "Attribute Defined",
            ["Qualifier.Operator.AttributeUndefined"] = "Attribute Undefined",
            ["Qualifier.Operator.NotEqual"] = "Not Equal",
            ["Qualifier.Operator.NoMatch"] = "No Match",
            ["Qualifier.Operator.Less"] = "Less Than",
            ["Qualifier.Operator.LessOrEqual"] = "Less Or Equal",
            ["Qualifier.Operator.Greater"] = "Greater Than",
            ["Qualifier.Operator.GreaterOrEqual"] = "Greater Or Equal",
            ["Qualifier.Operator.Match"] = "Match",
            ["Qualifier.Operator.Equal"] = "Equal",
            ["Qualifier.Format.Priority"] = ", priority = {0}",
            ["Qualifier.Format.FallbackScore"] = ", fallback score = {0}",
            ["Panel.Rename.Title"] = "Rename Resource",
            ["Panel.Rename.Label.Name"] = "Name",
            ["Panel.Rename.Placeholder"] = "Enter a new name",
            ["Panel.Rename.Button.Rename"] = "Rename",
            ["Panel.NewResource.Title"] = "New Resource",
            ["Panel.NewResource.Button.Create"] = "Create",
            ["Panel.NewResource.Button.Cancel"] = "Cancel",
            ["Panel.NewResource.Error.DuplicateName"] = "A resource with the same name already exists.",
            ["Panel.NewResource.Error.SelectEmbedFile"] = "Please choose a file to embed.",
            ["Panel.NewResource.Label.Name"] = "Name",
            ["Panel.NewResource.Label.Type"] = "Type",
            ["Model.Candidate.Type.Unknown"] = "Unknown",
            ["Model.Candidate.Qualifiers.None"] = "(None)",
            ["Editor.Undo"] = "Undo",
            ["Editor.Redo"] = "Redo",
            ["Editor.Cut"] = "Cut",
            ["Editor.Copy"] = "Copy",
            ["Editor.Paste"] = "Paste",
            ["Editor.Delete"] = "Delete",
            ["Editor.SelectAll"] = "Select All",
            ["Tree.NewResource"] = "New Resource",
            ["Tree.CopyResourceName"] = "Copy Resource Name",
            ["Tree.BatchExport"] = "Batch Export",
            ["Tree.SimpleRename"] = "Simple Rename",
            ["Tree.FullRename"] = "Full Rename",
            ["Tree.Scope.All"] = "the resource tree",
            ["Tree.Scope.Folder"] = "folder \"{0}\"",
            ["Common.Export"] = "Export",
            ["Common.Delete"] = "Delete",
            ["Common.Edit"] = "Edit",
            ["Common.Clear"] = "Clear",
            ["Common.Retry"] = "Retry",
            ["Menu.File.Open"] = "Open...",
            ["Picker.Commit.Load"] = "Load",
            ["Picker.Commit.Import"] = "Import",
            ["Picker.Commit.SelectRoot"] = "Select PRI Root Folder",
            ["Picker.Commit.Save"] = "Save",
            ["Picker.FileType.Text"] = "Text files",
            ["Picker.FileType.All"] = "All files",
            ["Drag.DropLoadPri"] = "Drop to load PRI file",
            ["Common.Button.Next"] = "Next",
            ["Batch.Import.Title"] = "Batch Import",
            ["Batch.Export.Title"] = "Batch Export",
            ["Batch.Import.Summary.Total"] = "Processed Lines",
            ["Batch.Import.Summary.Replaced"] = "Replaced",
            ["Batch.Import.Summary.SkippedByLanguage"] = "Skipped by Language Filter",
            ["Batch.Import.Summary.NoMatchName"] = "No Match:name",
            ["Batch.Import.Summary.NoMatchLanguage"] = "No Match:Language",
            ["Batch.Import.Summary.NoMatch"] = "No Match",
            ["Batch.Import.Summary.NoMatchOther"] = "No Match:Other",
            ["Batch.Import.Summary.Ambiguous"] = "Skipped Ambiguous",
            ["Batch.Import.Summary.Invalid"] = "Invalid Format",
            ["Batch.Import.Summary.TotalSkipped"] = "Total Failed/Skipped",
            ["Batch.Import.Summary.FailureFile"] = "Failure Details File",
            ["Batch.Import.Report.Title"] = "Batch Import Failure/Skip Details",
            ["Batch.Import.Report.Time"] = "Time",
            ["Batch.Import.Fail.Invalid"] = "Invalid Format",
            ["Batch.Import.Fail.ByLanguage"] = "Skipped by Language Filter",
            ["Batch.Import.Fail.Ambiguous"] = "Skipped Ambiguous",
            ["Batch.Import.Fail.NoMatchName"] = "No Match:name",
            ["Batch.Import.Fail.NoMatchLanguage"] = "No Match:Language",
            ["Batch.Import.Fail.NoMatch"] = "No Match",
            ["Menu.File.Save"] = "Save",
            ["Menu.File.SaveAs"] = "Save As...",
            ["Menu.File.Exit"] = "Exit",
            ["Menu.Edit.NewResource"] = "New Resource",
            ["Menu.Edit.DeleteSelected"] = "Delete Selected Resource",
            ["Menu.Edit.BatchImport"] = "Batch Import Translations...",
            ["Menu.Edit.BatchExport"] = "Batch Export Translations...",
            ["Menu.Edit.SetRootFolder"] = "Set Root Folder...",
            ["Menu.Edit.SetRootFolderShort"] = "Set Root Folder",
            ["Menu.Edit.EmbedPaths"] = "Embed Path Resources into PRI",
            ["Menu.View.UseWebViewSvg"] = "Use WebView for SVG Preview",
            ["Menu.View.PreviewTheme"] = "Preview Theme",
            ["Menu.View.Theme.System"] = "Follow System",
            ["Menu.View.Theme.Light"] = "Light",
            ["Menu.View.Theme.Dark"] = "Dark",
            ["Menu.Help.Usage"] = "Usage",
            ["Menu.Help.Notice"] = "Third-Party Notices",
            ["TitleBar.Minimize"] = "Minimize",
            ["TitleBar.MaxRestore"] = "Maximize/Restore",
            ["TitleBar.Close"] = "Close",
            ["Search.Placeholder"] = "Search resources...",
            ["Search.Button"] = "Search",
            ["Empty.NoPriOpened"] = "No PRI file opened",
            ["Empty.OpenPriFile"] = "Open PRI File",
            ["Candidates.Type"] = "Type",
            ["Candidates.Qualifier"] = "Qualifier",
            ["Candidate.ShowInExplorer"] = "Show in File Explorer",
            ["Candidate.TryEmbedToPri"] = "Try Embedding to PRI",
            ["Candidate.Add"] = "Add Candidate",
            ["Preview.InvalidRoot"] = "The current root folder does not contain this file. You can set a new root folder below.",
            ["Preview.ExportToDisk"] = "Export to Disk",
            ["Preview.FailedOpen.Prefix"] = "Unable to open",
            ["Preview.FailedOpen.Suffix"] = ". Please confirm the file is valid, accessible, and not occupied by another application.",
            ["Preview.FailedOpen.ExceptionLabel"] = "Exception:",
            ["Preview.FailedXbf.Prefix"] = "Unable to decompile",
            ["Preview.FailedXbf.Suffix"] = ".",
            ["Preview.FailedXbf.ExceptionLabel"] = "Exception:",
            ["Crash.Error.Prefix"] = "Exception: ",
            ["Crash.Error.LogPath"] = "Log: ",
        };

        internal static string CurrentLanguage
        {
            get
            {
                try
                {
                    return NormalizeLanguage(ApplicationLanguages.PrimaryLanguageOverride);
                }
                catch
                {
                    return _cachedStartupLanguage ?? ResolveStartupLanguage();
                }
            }
        }

        internal static string GetString(string key)
        {
            var useEn = CurrentLanguage.StartsWith("en", StringComparison.OrdinalIgnoreCase);
            var selected = useEn ? EnUs : ZhHans;
            if (selected.TryGetValue(key, out var value))
            {
                return value;
            }

            // Fallback to the other language before returning key.
            var fallback = useEn ? ZhHans : EnUs;
            return fallback.TryGetValue(key, out var fallbackValue) ? fallbackValue : key;
        }

        internal static string ResolveStartupLanguage()
        {
            var preferred = GetSavedLanguage();
            if (preferred is not null)
            {
                _cachedStartupLanguage = preferred;
                return preferred;
            }

            try
            {
                foreach (var lang in GlobalizationPreferences.Languages)
                {
                    var normalized = NormalizeLanguage(lang);
                    if (normalized is "zh-Hans" or "en-US")
                    {
                        _cachedStartupLanguage = normalized;
                        return normalized;
                    }
                }
            }
            catch
            {
            }

            _cachedStartupLanguage = "zh-Hans";
            return "zh-Hans";
        }

        internal static void SaveLanguagePreference(string languageTag)
        {
            var normalized = NormalizeLanguage(languageTag);
            _cachedStartupLanguage = normalized;

            // Prefer file-based storage for unpackaged mode compatibility.
            TrySaveLanguageToFile(normalized);

            // Best-effort: if packaged/localsettings is available, also write it.
            try
            {
                ApplicationData.Current.LocalSettings.Values[LanguageSettingKey] = normalized;
            }
            catch { }
        }

        private static string? GetSavedLanguage()
        {
            var fromFile = TryLoadLanguageFromFile();
            if (fromFile is not null)
                return fromFile;

            try
            {
                var value = ApplicationData.Current.LocalSettings.Values[LanguageSettingKey] as string;
                if (string.IsNullOrWhiteSpace(value))
                {
                    return null;
                }

                var normalized = NormalizeLanguage(value);
                return normalized is "zh-Hans" or "en-US" ? normalized : null;
            }
            catch
            {
                return null;
            }
        }

        private static void TrySaveLanguageToFile(string normalizedLanguage)
        {
            try
            {
                lock (_gate)
                {
                    File.WriteAllText(SettingsFilePath, normalizedLanguage);
                }
            }
            catch
            {
            }
        }

        private static string? TryLoadLanguageFromFile()
        {
            try
            {
                lock (_gate)
                {
                    var path = SettingsFilePath;
                    if (!File.Exists(path))
                        return null;

                    var text = File.ReadAllText(path)?.Trim();
                    if (string.IsNullOrWhiteSpace(text))
                        return null;

                    var normalized = NormalizeLanguage(text);
                    return normalized is "zh-Hans" or "en-US" ? normalized : null;
                }
            }
            catch
            {
                return null;
            }
        }

        private static string NormalizeLanguage(string? languageTag)
        {
            if (string.IsNullOrWhiteSpace(languageTag))
            {
                return "zh-Hans";
            }

            if (languageTag.StartsWith("en", StringComparison.OrdinalIgnoreCase))
            {
                return "en-US";
            }

            if (languageTag.StartsWith("zh", StringComparison.OrdinalIgnoreCase))
            {
                return "zh-Hans";
            }

            return "zh-Hans";
        }
    }
}
