# 熔岩翻译助手 (.NET 10)

基于 **.NET 10 + WPF** 的桌面翻译工具，由原 Python/PySide6 版本重构而来。

## 功能

- **百度翻译**：通过 [百度翻译开放平台](https://fanyi-api.baidu.com/) 的 App ID 与密钥调用官方 API
- **AI 翻译**：支持任意 **OpenAI 兼容** 接口（可配置多个），例如 OpenAI、DeepSeek、智谱 GLM、Ollama 等
- 系统托盘、全局快捷键 **Alt+Space**（显示/隐藏窗口）
- **开机自启动**：在设置中开启后，登录 Windows 时自动在托盘运行（不弹出主窗口）
- 剪贴板快速翻译、原文/译文交换、复制结果

## 运行

需要 [.NET 10 SDK](https://dotnet.microsoft.com/download)。

```bash
# 在仓库根目录
dotnet run --project LavaTranslator/LavaTranslator.csproj
```

或双击 `run.bat`。

发布单文件：

```bash
dotnet publish LavaTranslator/LavaTranslator.csproj -c Release -r win-x64 --self-contained
```

## 配置

配置文件路径：`%USERPROFILE%\.lava_translator\config.json`

也可参考仓库中的 `appsettings.example.json`。

### 百度翻译

在 **设置 → 百度翻译** 中填写 App ID 与 Secret Key。

### AI 翻译（多个）

在 **设置 → AI 翻译** 中可添加多条配置，每条包含：

| 字段 | 说明 |
|------|------|
| 显示名称 | 菜单中显示的引擎名称 |
| API Key | 接口密钥 |
| Base URL | 兼容端点根地址（如 `https://api.openai.com/v1/`） |
| 模型 | 如 `gpt-4o-mini`、`glm-4-flash`、`deepseek-chat` |
| Temperature / Max Tokens | 生成参数 |

从旧版 Python 配置迁移：若存在 `glm` 节点且未配置新的 `openAiProviders`，首次启动会自动迁移为一条「GLM翻译」配置。

## 项目结构

```
agent-translate/
├── LavaTranslator.slnx
├── LavaTranslator/              # WPF 主程序
│   ├── Models/
│   ├── Services/
│   ├── Infrastructure/
│   └── Views/
├── Translate/                   # 旧版 Python 代码（参考）
├── appsettings.example.json
└── run.bat
```
