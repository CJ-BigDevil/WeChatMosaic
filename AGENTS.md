# AI 助手指南（AGENTS.md）

> 这份文件是给**所有 AI 编程助手**（Claude、GitHub Copilot、Cursor、Windsurf 等）看的通用指南。
> 如果用户不会用、想改功能、或编译遇到问题，**请先读完这份，再一步步教用户操作**。
> 用户可能会把整个项目文件夹丢给你，并说一句"教我用"或"帮我改 XXX"。

---

## 1. 这是个什么项目

一个 **Windows 桌面隐私遮挡工具**：用可自定义的色块盖住微信（或任意窗口）的指定区域，防止旁人窥屏。
- 单文件 WPF 应用，全部逻辑在 `MosaicApp.cs`
- **零第三方依赖**，用 Windows 系统自带的 `csc.exe` 即可编译
- 中文用户，界面和注释都是中文

---

## 2. 项目结构

```
WeChatMosaic/
├── MosaicApp.cs        ← 全部源码（单文件，约 900 行）
├── WeChatMosaic.exe    ← 编译产物（被 .gitignore 忽略）
├── assets/app.ico    ← 应用图标（编译时嵌入 exe，运行时无需携带）
├── mosaic-config.json  ← 运行时自动生成的个人配置（被 .gitignore 忽略）
├── README.md           ← 给人看的说明
├── RELEASING.md        ← 本地编译、打包与 GitHub Release 发布流程
├── CLAUDE.md           ← 本文件（给 AI 看）
├── LICENSE             ← MIT
└── .gitignore
```

`MosaicApp.cs` 里的关键类：
- `Region` — 一个遮挡区域的数据（位置/宽高/样式/透明度/颜色）
- `Config` — 配置的读写（手写极简 JSON，无外部库）
- `MaskWindow` — **单个遮挡块**（一个置顶透明 WPF 窗口），含拖动/调大小/右键菜单/样式渲染
- `MainWindow` — **主控制窗口**（按钮、热键、托盘、跟随微信、开机自启、区域管理）
- `App` — 入口 `Main()`，含**单实例检测**和全局异常捕获

---

## 3. 如何编译（最常问！）

用户没有 Visual Studio 也能编译。用系统自带 csc：

```bat
set CSC=C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe
set WPF=C:\Windows\Microsoft.NET\Framework64\v4.0.30319\WPF

%CSC% -nologo -target:winexe -platform:x64 -out:WeChatMosaic.exe ^
  -win32icon:assets\app.ico ^
  -r:"%WPF%\PresentationFramework.dll" -r:"%WPF%\PresentationCore.dll" ^
  -r:"%WPF%\WindowsBase.dll" -r:System.dll -r:System.Core.dll -r:System.Xaml.dll ^
  -r:System.Windows.Forms.dll -r:System.Drawing.dll ^
  -utf8output -codepage:65001 MosaicApp.cs
```

**编译的两个大坑（务必提醒用户）：**
1. **源码必须是 UTF-8 带 BOM**，否则中文字符串乱码、报错一堆看不懂的符号。
   修复方法（PowerShell）：
   `powershell -NoProfile -Command "$c=[IO.File]::ReadAllText('MosaicApp.cs',[Text.UTF8Encoding]::new($false));[IO.File]::WriteAllText('MosaicApp.cs',$c,[Text.UTF8Encoding]::new($true))"`
2. **这个 csc 是 C# 5 语法**，不支持：属性自动初始化 `public string S {get;set;}="x"`、本地函数 `byte L(byte v){...}`、字符串插值 `$""`、模式匹配等。**改代码时只用 C# 5 及以前的语法**，否则编译报错。

---

## 4. 各功能的实现位置（改功能先找到这里）

| 功能 | 位置（在 `MosaicApp.cs` 中） | 说明 |
|------|------------------------------|------|
| 遮挡块拖动 | `MaskWindow.DragStart` → 用 `DragMove()` | 别改成手算相对坐标，会抖动 |
| 遮挡块调大小 | `MaskWindow.ResizeStart` | 用 `PointToScreen` 屏幕坐标，别用 `GetPosition(win)`（会抖） |
| 样式渲染 | `MaskWindow.ApplyStyle()` | `glass/blur/mosaic/solid` 四个分支 |
| 毛玻璃噪点 | `MaskWindow.MakeFrostCanvas()` | 调噪点密度/大小/透明度都在这里 |
| 毛玻璃取色 | `MaskWindow.SampleColorBehind()` | `GetDC`+`GetPixel` 采样背后颜色 |
| 不透明度 | 右键菜单里的 `Slider`（`OnMouseRightButtonUp`） | 0.3~1.0，实时生效 |
| 全局热键 | `MainWindow.RegisterCurrentHotkey()` + `WndProc` | `RegisterHotKey` 拦截 `WM_HOTKEY` |
| 自定义热键捕获 | `MainWindow.CaptureKey()` | 用 `e.KeyboardDevice.Modifiers` 读修饰键，别用 `Keyboard.IsKeyDown`（Alt 会失效） |
| 跟随微信 | `MainWindow.WeChatTick()` + `IsWeChatWindowVisible()` | 默认 300ms；smart 模式=可见+前台白名单；**目标窗口置顶(Topmost)时保持遮挡**；目标窗口用 `GetTargetMainWindows()` 枚举该进程**所有顶层可见窗口**（含子窗口） |
| 置顶兼容 | `MainWindow.ReTopIfCovered()` + `MaskWindow.BringToTop()` | 微信置顶会压过遮挡（同为 Topmost 后顶者在上）；沿 Z-order(`GW_HWNDPREV`)检测被压则 `SetWindowPos(HWND_TOPMOST)` 重顶 |
| 鼠标穿透 | `MaskWindow.SetClickThrough()` / `ApplyClickThrough()` | `GetWindowLong/SetWindowLong` 加 `WS_EX_TRANSPARENT`；**句柄未创建时要靠 `pendingClickThrough` 在 `OnSourceInitialized` 补应用** |
| DPI 感知 | `App.Main()` 设 PerMonitorV2 + `MaskWindow.GetDpiScale()` | 取色用物理像素，`Left/Top` 是 DIP，必须 `×TransformToDevice` 缩放，否则高 DPI 取色偏 |
| 托盘 | `MainWindow.SetupTray()` | `System.Windows.Forms.NotifyIcon` |
| 开机自启 | `MainWindow.SetAutoStart()` | 写 `HKCU\...\Run`，无需管理员 |
| 配置保存 | `Config.Save()/Load()` | 手写 JSON；加字段记得同步改这两处 |

---

## 5. 常见"帮我改"需求的处理套路

**"想改默认热键"** → 改 `Config.HotkeyVk`（主键 VK 码）和 `Config.HotkeyMods`（修饰键：Ctrl=2 Alt=1 Shift=4 Win=8），然后**删掉旧的 `mosaic-config.json`**（否则旧配置覆盖新默认值）。

**"想改跟随微信的检测逻辑"** → 改 `IsWeChatWindowVisible()`：
**"想改跟随微信的检测逻辑"** → 改 `IsWeChatWindowVisible()`：
- 当前实现是 **微信可见 + 前台窗口属于白名单（WeChat/Weixin/本工具）**
- 想"只要微信可见就遮（不管前台是谁）"→ 删掉后半段白名单判断，直接 `return wechatVisible`
- 想"前台才遮"→ 只保留 `GetForegroundWindow` 进程名判断（但点右键/控制窗会误收起，不推荐）
- 想加别的软件（如 QQ、钉钉）→ 在白名单里加 `"QQ"`、`"DingTalk"` 等

**"想改检测频率"** → `wechatWatcher = new DispatcherTimer { Interval = ... }`

**"毛玻璃太透/太实"** → `ApplyStyle()` 里 `glass.A = (byte)(Math.Max(op, 0.75) * 255)` 调下限；噪点在 `MakeFrostCanvas()` 调密度（`/16` 改小更密）和颗粒大小。

**"想加新样式"** → 在 `ApplyStyle()` 加 `else if (Data.Style=="xxx")` 分支 + 右键菜单加一项 + `UpdateBadge`（如果还显示样式名）。

**"想改成跟随别的窗口"** → 全局把 `WeChat/Weixin` 换成目标进程名即可。

---

## 6. 教用户使用时的话术要点

用户多半不懂技术，**用大白话、给具体步骤**：
1. 双击 exe → 屏幕出现色块 + 一个控制小窗口
2. 拖色块到微信要遮的地方，拖右下角黄点调大小
3. 色块上右键换样式（推荐"毛玻璃"）、拖滑块调透明度
4. 点「🖱 鼠标穿透：开」→ 点击直达微信、正常聊天；要再调就切回"关"
5. 之后基本不用管：微信在用时自动遮，被别的窗口盖住/最小化自动收
6. 想手动开关 → 按热键；想退出 → 右键右下角托盘图标

---

## 7. 故障排查速查

| 现象 | 原因 / 解决 |
|------|------------|
| 双击没反应 | 看任务栏/托盘有没有窗口；可能已在后台运行（单实例会把旧窗口唤到前面） |
| 编译报一堆乱码错 | 源码编码不是 UTF-8 BOM，按第 3 节转码 |
| 编译报"本地函数/字符串插值"错 | 用了 C# 6+ 语法，改成 C# 5 写法 |
| 热键设置了不生效 | 检查是否带了修饰键；若被占用，`RegisterCurrentHotkey()` 现在会弹窗提示换一个 |
| 微信在前台但遮挡没出现 | 目标子窗口进程名不同；在高级设置的「目标进程」里加上对应进程名 |
| **点开公众号文章遮挡不跟过去** | **设计取舍，不是 bug**：文章/网页在微信内置独立浏览器窗口，遮挡故意不覆盖，否则会糊住你正读的文章。见 README 已知限制 |
| 微信置顶后遮挡被压 / 切走消失 | 已在 v1.1.1 修复：置顶时遮挡保持在其上且不消失（`ReTopIfCovered` + Topmost 检测） |
| 高 DPI 下毛玻璃颜色对不上 | 已在 v1.1.1 声明 PerMonitorV2 + 取色按缩放换算；多屏混合 DPI 仍未充分验证 |
| 改了默认配置但没生效 | 旧 `mosaic-config.json` 覆盖了，删掉它再启动 |

---

## 8. 给 AI 的协作原则

- **改代码只用 C# 5 语法**（这是硬约束，csc 版本决定）
- 改完**必须重新编译**并把新 exe 给用户（编译命令见第 3 节）
- 改默认值类配置后，**提醒删旧 `mosaic-config.json`**
- 任何"拖动/调大小抖动"问题，先看是不是又用了相对坐标，改回屏幕坐标 / `DragMove()`
- 保持单文件结构，别轻易拆分（用户就图个简单）
- 发布正式版本先读 **[RELEASING.md](./RELEASING.md)**；普通文档/代码提交不打标签、不创建 Release
- 任何发布包都不得包含个人 `mosaic-config.json`、备份、候选图标或临时脚本
- 用户是中文使用者，**全程用中文回复**
- **别把这些设计取舍当 bug 修**：① 公众号文章/内置浏览器独立窗口不跟随遮挡（故意，否则糊住文章）；② 毛玻璃是程序化磨砂而非实时亚克力（实时模糊耗性能且可能透出文字，违背遮挡目的）；③ 300ms 轮询而非事件钩子（实测 CPU 仅约 3.4%，换事件驱动收益小、复杂度高）。改之前先问用户
