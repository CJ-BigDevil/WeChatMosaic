# WeChatMosaic / 马赛克工具

一个轻量的 Windows 桌面隐私遮挡小工具：在你用微信聊天时，用可自定义的遮挡块盖住不想被旁人看到的区域（会话列表、聊天内容、联系人等）。**微信在前台时自动遮挡，切走自动收起**，几乎零性能开销。

> 纯本地运行，不联网、不收集任何数据。

---

## ✨ 功能特性

- **多块自定义遮挡**：想遮几处加几处，鼠标拖动摆放、拖角调大小
- **4 种遮挡样式**：
  - **毛玻璃**（推荐）：自动贴近背后页面颜色 + 磨砂噪点，隐约见影、读不清字、不突兀
  - **模糊**：深色底 + 噪点
  - **马赛克**：棋盘格块
  - **纯色**：自定义颜色
- **不透明度滑块**：右键拖动实时调节，所见即所得
- **🤖 跟随微信**：自动检测微信是否在最前台——正在用时遮挡出现，切到别的窗口/最小化时自动收起（300ms 响应）
- **⌨ 自定义全局热键**：一键显示/隐藏全部遮挡，按键组合可自由设置（默认 `Shift + |`）
- **🔒 锁定穿透**：摆好后锁定，鼠标可穿透遮挡块正常操作微信
- **🚀 开机自启**：可选，写注册表 `HKCU\...\Run`（无需管理员权限）
- **托盘驻留**：关闭窗口缩到托盘，不退出；遮挡与热键持续生效
- **💾 配置自动保存**：区域位置、样式、热键、开关状态，下次启动自动恢复

---

## 🚀 快速开始

### 直接使用
1. 下载 / 编译得到 `WeChatMosaic.exe`
2. 双击运行（独立单文件，无需安装，.NET Framework 4.x 系统自带）
3. 首次运行自带两块示例遮挡，拖到微信要遮的位置即可

### 基本操作
| 操作 | 方法 |
|------|------|
| 移动遮挡块 | 鼠标按住拖动（编辑模式下） |
| 调整大小 | 拖动右下角黄色角标 |
| 换样式 / 调透明度 / 删除 | 在遮挡块上**右键** |
| 锁定位置 | 主窗口「🔒 锁定位置」（鼠标可穿透） |
| 一键遮挡/还原 | 按热键（默认 `Shift + \|`） |
| 退出程序 | 右键托盘图标 → 退出 |

---

## 🤖 跟随微信（自动遮挡）

开启后（默认开）：

- 微信窗口**在最前台、正在使用** → 遮挡自动出现
- **切到其它窗口**或**微信最小化到托盘** → 遮挡自动收起
- 每 300ms 检测一次前台窗口，响应迅速
- 手动按一次热键会临时转为手动控制；再点「跟随微信」按钮恢复自动

> 通过 `GetForegroundWindow` + 进程名识别（兼容老版 `WeChat` 与新版 `Weixin` 进程）。

---

## 🛠 从源码编译

本项目**不依赖任何 NuGet 包 / SDK**，直接用 Windows 系统自带的 .NET Framework 编译器（`csc.exe`）即可编译：

```bat
set CSC=C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe
set WPF=C:\Windows\Microsoft.NET\Framework64\v4.0.30319\WPF

%CSC% -nologo -target:winexe -platform:x64 -out:WeChatMosaic.exe ^
  -r:"%WPF%\PresentationFramework.dll" ^
  -r:"%WPF%\PresentationCore.dll" ^
  -r:"%WPF%\WindowsBase.dll" ^
  -r:System.dll -r:System.Core.dll -r:System.Xaml.dll ^
  -r:System.Windows.Forms.dll -r:System.Drawing.dll ^
  -utf8output -codepage:65001 ^
  MosaicApp.cs
```

编译产出单个 `WeChatMosaic.exe`，即可直接运行。

> ⚠️ 源码需保存为 **UTF-8 带 BOM**，否则老编译器对中文字符串会乱码。
> 也可以用任何支持 C# 的方式（如 Visual Studio / `dotnet`）编译，只是系统 csc 最省事。

---

## 📁 文件说明

| 文件 | 说明 |
|------|------|
| `MosaicApp.cs` | 全部源码（单文件，WPF） |
| `WeChatMosaic.exe` | 编译产物，可直接运行 |
| `mosaic-config.json` | 运行后自动生成的配置文件（区域/样式/热键等） |

---

## ⚙️ 技术要点

- **WPF 置顶透明窗口**：每个遮挡块是一个 `WindowStyle=None + AllowsTransparency + Topmost` 的窗口
- **拖动**：用 WPF 内置 `DragMove()`（屏幕坐标，不抖动）
- **毛玻璃取色**：`GetDC` / `GetPixel` 采样背后屏幕颜色 → 提亮 + 半透明 + 程序化磨砂噪点
- **全局热键**：`RegisterHotKey` / `WndProc` 拦截 `WM_HOTKEY`
- **跟随微信**：`DispatcherTimer` 每 300ms 用 `GetForegroundWindow` 判断前台进程
- **托盘**：`System.Windows.Forms.NotifyIcon`
- **自启**：注册表 `HKCU\Software\Microsoft\Windows\CurrentVersion\Run`
- **配置**：手写极简 JSON 读写，零外部依赖

---

## ⚠️ 注意事项 / 已知限制

- 仅支持 **Windows**（WPF + Win32 API）
- 「跟随微信」按前台进程名识别；若微信的某些子窗口（如独立小程序/朋友圈窗口）是不同进程，可能不被识别
- 毛玻璃为**程序化磨砂效果**，非 Win11 系统级实时亚克力模糊（那样既耗性能又可能透出文字，不利于遮挡）

---

## 📄 License

MIT License — 随意使用、修改、分发。

---

## 🙏 免责

本工具仅用于**保护个人屏幕隐私**的正当用途（如开放式办公区防止旁人窥屏）。请勿用于任何违反公司信息安全政策或法律法规的场景。使用者需自行遵守所在组织的相关规定。
