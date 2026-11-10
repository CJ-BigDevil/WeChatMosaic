# 本地发布流程

本文用于在 Windows 本地编译、提交并发布 **WeChatMosaic** 正式版本。示例以 `v1.1.3` 为例，实际发布时替换为新的版本号。

> **只改文档、README 或不准备发版时：正常提交并推送 `main` 即可，不要打版本标签，也不要创建 Release。**

## 1. 发布前检查

1. 确认要发布的代码改动已完成，必要文档已同步更新。使用说明应对照当前界面核对：例如当前按钮是「鼠标穿透」，不要沿用旧版「锁定位置」等文案；行为不确定的地方不要写成已验证功能。
2. 确认 `MosaicApp.cs` 为 **UTF-8 带 BOM**，且只使用 **C# 5** 语法。
3. 检查工作区：

   ```bash
   git status --short
   git diff --check
   git diff
   git ls-files --others --exclude-standard
   ```

4. 不要把以下本地文件提交或上传：
   - `mosaic-config.json`：个人遮挡位置、热键等配置；
   - `local-backups/`：个人配置和发布文件的本地备份；
   - `WeChatMosaic-icon-test.exe`：试用版；
   - 候选图标、预览图、截图及临时绘图脚本；
   - 发布压缩包、临时编译脚本和说明草稿。

   `.exe`、个人配置和本地备份已被 `.gitignore` 排除，但仍须人工检查暂存清单。

5. 确认当前分支为 `main`，并检查远端是否已有新提交：

   ```bash
   git branch --show-current
   git fetch origin
   git status --short --branch
   ```

   若远端有他人新提交，先正常拉取、检查并合并；不要强推覆盖远端历史。

## 2. 编译与验证

### 编译前

- 若 `WeChatMosaic.exe` 正在运行，直接覆盖会报“文件正在使用”。**先关闭正在运行的本项目程序，或把验证版输出为另一个临时文件名**。
- 不要通过改名后的试用 exe 同时运行两个实例：它们可能共用同一份配置。
- 不要删除、移动或重置用户的 `mosaic-config.json`。若因功能变更必须处理配置，先备份并明确告知用户。

### 编译

在项目根目录用 **CMD 或 `.cmd` 文件**执行（`^` 是 CMD 的续行符）：

```bat
set CSC=C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe
set WPF=C:\Windows\Microsoft.NET\Framework64\v4.0.30319\WPF

%CSC% -nologo -target:winexe -platform:x64 -out:WeChatMosaic.exe ^
  -win32icon:assets\app.ico ^
  -r:"%WPF%\PresentationFramework.dll" ^
  -r:"%WPF%\PresentationCore.dll" ^
  -r:"%WPF%\WindowsBase.dll" ^
  -r:System.dll -r:System.Core.dll -r:System.Xaml.dll ^
  -r:System.Windows.Forms.dll -r:System.Drawing.dll ^
  -utf8output -codepage:65001 ^
  MosaicApp.cs
```

> `-win32icon` 将图标作为 **Win32 资源**嵌入 exe。不要用 `csc -resource` 代替 WPF 的打包方式；本项目的窗口和托盘直接从 exe 的 Win32 图标资源加载，不依赖 `pack://` URI 或旁置 ico 文件。

### 验证

- 编译无错误，启动后无“找不到资源”等异常；
- 主窗口中文标题正常，exe、任务栏和托盘显示预期图标；
- 对本次涉及的功能做对应验证；
- 若使用现有配置，确认遮挡区域、热键等没有被意外改动；
- 若只是编译临时验证版，验证后删除临时 exe 和脚本。

## 3. 提交版本代码和标签

示例版本号为 `v1.1.3`；提交信息按实际功能修改，不要照抄示例内容。

```bash
# 明确列出要提交的文件，避免把个人文件和临时产物带入。
git add -- MosaicApp.cs README.md AGENTS.md assets/app.ico

# 必须检查暂存清单，再提交。
git diff --cached --check
git status --short
git diff --cached --stat

git commit -m "v1.1.3: 本次版本的简要说明"
git tag -a v1.1.3 -m "v1.1.3: 本次版本的简要说明"
git push origin main v1.1.3
```

> **推送标签不等于创建 GitHub Release。** 标签指向源码版本；Release 是 GitHub 的发布页面和下载入口，需要下一步单独创建。

## 4. 制作发布压缩包

发布包**只放**：

- `WeChatMosaic.exe`
- 简短、最新的 `使用说明.txt`（可选但建议）

不要放入个人配置、ico、候选图、备份或编译脚本。图标已嵌入 exe，无需附带。

### 用 PowerShell 打包

先在项目根目录准备好发布用说明文件，确认内容准确后执行。可从 `docs/使用说明.txt` 复制最新模板到根目录（`Copy-Item -LiteralPath 'docs\使用说明.txt' -Destination '使用说明.txt'` 会覆盖根目录同名文件，覆盖前先检查并备份已有文件），再按当前版本修订。

打包前先确认目标压缩包尚不存在；若已存在，先检查它是不是需要保留的历史发布包，确认后再替换，不要直接覆盖不明文件：

```powershell
Compress-Archive -LiteralPath 'WeChatMosaic.exe','使用说明.txt' `
  -DestinationPath 'WeChatMosaic-v1.1.3.zip' -Force

Add-Type -AssemblyName System.IO.Compression.FileSystem
$zip = [IO.Compression.ZipFile]::OpenRead((Resolve-Path 'WeChatMosaic-v1.1.3.zip'))
$zip.Entries | Select-Object FullName,Length
$zip.Dispose()
Get-FileHash -Algorithm SHA256 'WeChatMosaic-v1.1.3.zip'
```

检查清单中**只能有预期的 exe 和说明文件**；若发现其他文件，删除错误压缩包后重新打包，不要直接上传。

## 5. 登录 GitHub CLI 并创建 Release

### 一次性准备

安装 [GitHub CLI](https://cli.github.com/)：

```bash
winget install --id GitHub.cli
```

安装后若当前终端找不到 `gh`，可使用完整路径：

```bash
"/c/Program Files/GitHub CLI/gh.exe" --version
```

以下命令以 `gh` 为例；找不到命令时统一替换为上述完整路径。

### 登录

```bash
gh auth login
```

选择 **GitHub.com → HTTPS → Login with a web browser**。终端会显示一次性**设备授权码**，在浏览器打开 `https://github.com/login/device`，输入该码并授权。

- 设备授权码不是短信 / Authenticator 的二次验证码。
- 登录命令会等待浏览器授权；不要反复启动多个登录进程。
- 若系统使用代理而 `gh` 网络超时，可只为本次命令指定已有代理（端口以本机实际配置为准）：

  ```bash
  HTTPS_PROXY=http://127.0.0.1:7897 gh auth login --hostname github.com --git-protocol https --web
  ```

- 登录成功后检查：

  ```bash
  gh auth status
  gh release view v1.1.3 --repo CJ-BigDevil/WeChatMosaic
  ```

  目标版本尚不存在时才继续创建；不要覆盖已有版本的附件。

### 创建 Release 并上传附件

以下示例在 Bash 中执行；`gh release create` 已包含附件上传，无需再读取或导出 GitHub 凭据：

```bash
gh release create v1.1.3 \
  "WeChatMosaic-v1.1.3.zip#WeChatMosaic v1.1.3 Windows x64" \
  --repo CJ-BigDevil/WeChatMosaic \
  --title "WeChatMosaic v1.1.3" \
  --notes-file RELEASE_NOTES.md
```

`RELEASE_NOTES.md` 是临时发布说明文件，建议包含：新增功能、修复、使用方式、升级配置注意事项。不要把个人配置、内部路径、令牌或不必要的调试信息写进去。

若网络需要代理，给 `gh release create` 同样设置 `HTTPS_PROXY` 环境变量即可。

## 6. 发布后核对与清理

```bash
gh release view v1.1.3 --repo CJ-BigDevil/WeChatMosaic \
  --json url,isDraft,isPrerelease,tagName,name,assets
```

确认：

- `isDraft` 和 `isPrerelease` 符合发布意图（正式发布应为 `false`）；
- 页面版本、标签和标题正确；
- 附件状态为 `uploaded`，大小和 SHA256 与本地压缩包一致；
- Release 页面和下载链接可访问；
- 未上传个人配置或无关文件。

将本地压缩包、发布说明草稿等移到已忽略的 `local-backups/`，或确认不再需要后删除。最后检查：

```bash
git status --short --branch
```

普通文档更新只需 `git add` 指定文档、`git commit`、`git push origin main`，**无需重复本流程**。
