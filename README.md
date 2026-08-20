# 熔岩翻译助手 (.NET 10 + Avalonia)

基于 **.NET 10 + Avalonia** 的跨平台桌面翻译工具，由原 Python/PySide6 版本重构而来。

## 功能

- **百度翻译**：通过 [百度翻译开放平台](https://fanyi-api.baidu.com/) 的 App ID 与密钥调用官方 API
- 系统托盘、全局快捷键 **Alt+Space**（Windows：剪贴板有内容时自动填入当前翻译平台，否则显示/隐藏窗口）
- **开机自启动**（Windows）：在设置中开启后，登录时自动在托盘运行（不弹出主窗口）
- 剪贴板快速翻译（托盘菜单）、按快捷键自动把剪贴板内容填入当前翻译平台（原生翻译页 + 内嵌网页：有道、搜狗、百度、必应、谷歌、DeepL、腾讯）、原文/译文交换、复制结果
- **网页翻译**：内嵌 WebView 打开有道、搜狗、百度、谷歌、DeepL、必应、腾讯等翻译站（Windows 需 [WebView2](https://developer.microsoft.com/microsoft-edge/webview2/)）

## 运行

需要 [.NET 10 SDK](https://dotnet.microsoft.com/download)。

```bash
# 在仓库根目录
dotnet run --project Translator/LavaTranslator.csproj
```

发布单文件（Windows x64 示例）：

```bash
dotnet publish Translator/LavaTranslator.csproj -c Release -r win-x64 --self-contained
```

也可发布到 macOS / Linux（`osx-x64`、`linux-x64` 等）。非 Windows 平台上全局热键与开机自启会降级（不可用或隐藏）。

## 配置

配置文件路径：`%USERPROFILE%\.lava_translator\config.json`（非 Windows 为 `~/.lava_translator/config.json`）

也可参考仓库中的 `appsettings.example.json`。

### 百度翻译

在 **设置 → 百度翻译** 中填写 App ID 与 Secret Key。

## 项目结构

```
Translator.Net/
├── Translator.slnx
├── Translator/                  # Avalonia 主程序
│   ├── Assets/
│   ├── Themes/
│   ├── Models/
│   ├── Services/
│   └── Infrastructure/
├── appsettings.example.json
└── scripts/
```
