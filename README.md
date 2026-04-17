# MrmTool 中英双语版

MrmTool 是一款用于查看和编辑 PRI 文件的工具，支持创建、修改、删除 PRI 资源，也可预览资源内容。

MrmTool 依赖 [MrmLib](https://github.com/ahmed605/MrmLib) 库来处理和修改 PRI 文件。

该工具还内置了 XBF（XAML 二进制格式）反编译器（暂不支持重新编译），目前使用的是修改版 [XbfAnalyzer](https://github.com/chausner/XbfAnalyzer)（基于旧版本提交，非最新版），后续计划替换为基于 WinUI 3 XBF 解析器的全新反编译/重新编译工具。

MrmTool 支持以下 PRI 版本：
- Windows 8（`mrm_pri0`）
- Windows 8.1（`mrm_pri1`）
- Windows Phone 8.1（`mrm_prif`）
- UWP（`mrm_pri2`）
- UWP RS4+（`mrm_pri3`）
- Windows App SDK / WinUI 3（`mrm_pri3`）
- UWP vNext（`mrm_vnxt`）

## 截图
