# TaskbarLyrics Plugin - 项目目录理解

## 项目概述
TaskbarLyricsPlugin 是一个基于 C#/.NET 8 和 WPF 的 Windows 任务栏歌词显示应用程序。该应用程序在 Windows 任务栏上显示当前播放歌曲的歌词，并支持逐字高亮、翻译显示和多种自定义选项。

## 技术架构
- **.NET 8** (target framework: net8.0-windows)
- **WPF** (Windows Presentation Foundation) - 用户界面框架
- **Windows Forms** - 用于系统托盘图标和全屏检测定时器
- **Newtonsoft.Json** - JSON 序列化和反序列化
- **Win32 API** - 通过 P/Invoke 调用原生 Windows API 进行窗口管理

## 构建和开发命令

```bash
# 构建项目
dotnet build

# 运行项目（开发模式）
dotnet run

# 发布为自包含的可执行文件（包含 .NET 运行时）
dotnet publish -c Release -r win-x64 --self-contained

# 发布为依赖运行时的版本（需要目标机器安装 .NET 8 运行时）
dotnet publish -c Release -r win-x64 --self-contained false

# 监控调试日志（需要先启动应用程序）
./watchlog.bat
```

## 目录结构分析

### 根目录文件
- **TaskbarLyrics.csproj** - 项目文件，定义了依赖项和构建配置
- **App.xaml / App.xaml.cs** - 应用程序入口点，负责系统托盘管理、全局异常处理和配置初始化
- **MainWindow.xaml / MainWindow.xaml.cs** - 主窗口，负责歌词显示、播放控制和用户交互
- **SettingsWindow.xaml / SettingsWindow.xaml.cs** - 设置配置窗口
- **AboutWindow.xaml / AboutWindow.xaml.cs** - 关于信息窗口
- **packages.config** - NuGet 包配置
- **watchlog.bat** - 用于监控调试日志的批处理文件

### 核心类文件
- **LyricsApiService.cs** - API服务层，与本地歌词API服务器（端口35374）通信
- **ConfigManager.cs** - 配置管理器，处理用户设置的保存和加载
- **LyricsRenderer.cs** - 歌词渲染引擎，解析LRC格式歌词并实现逐字动画效果
- **TaskbarMonitor.cs** - Windows API封装，管理任务栏位置检测和窗口样式设置
- **FullScreenDetector.cs** - 全屏检测器，检测全屏应用并自动隐藏歌词
- **Logger.cs** - 日志系统，支持多级别日志和重复消息过滤

### Models 文件夹
包含API响应和数据模型：
- **ConfigResponse.cs** - 配置响应模型（实际上是LyricsConfig的定义）
- **LyricsResponse.cs** - 歌词API响应模型
- **NowPlayingResponse.cs** - 播放状态响应模型
- **WordTiming.cs** - 歌词行和词语时间模型

### 关键功能模块

#### 1. 窗口管理 (TaskbarMonitor)
- 获取Windows任务栏位置和大小
- 设置窗口为透明、置顶、鼠标穿透等特殊样式
- 处理DPI缩放，确保在不同显示器上正确显示

#### 2. 歌词渲染系统 (LyricsRenderer)
- 解析LRC格式歌词（支持时间戳）
- 生成逐字同步的时间轴
- 实现平滑的渐变高亮动画
- 支持双语显示（原文+翻译）
- 过滤非歌词内容（制作人信息等）

#### 3. API通信 (LyricsApiService)
- 与本地API服务器（localhost:35374）通信
- 提供歌词获取、播放控制等功能
- 错误处理和备用API端点

#### 4. 配置管理 (ConfigManager)
- JSON配置文件的读写
- 用户设置的持久化
- 配置位置：%AppData%/TaskbarLyrics/config.json

#### 5. 定时器系统
- _nowPlayingTimer (50ms) - 高频更新播放位置，确保歌词同步精确
- _smoothUpdateTimer (32ms) - 管理歌词的平滑显示和动画效果
- _positionTimer (2s) - 同步窗口位置，跟随任务栏变化
- _restoreTimer (100ms) - 维护窗口置顶和可见状态
- _mouseLeaveTimer (300ms) - 处理鼠标离开事件

## 数据流程

### 1. 歌词获取流程
- UpdateNowPlaying 检测歌曲变化
- 歌曲变化时调用 UpdateLyrics
- 从API获取歌词文本
- LyricsRenderer 解析LRC歌词
- 过滤非歌词内容
- 生成逐字时间轴

### 2. 显示流程
- SmoothUpdateLyrics 定期更新当前显示
- UpdateCurrentLyricsLine 选择正确的歌词行
- CreateDualLineLyricsVisual 创建视觉元素
- 实现渐变高亮动画

### 3. 窗口管理流程
- 初始化时嵌入任务栏
- 定时同步位置跟随任务栏
- 全屏时自动隐藏
- 始终保持置顶状态

## API依赖
项目依赖于本地运行的歌词API服务器（端口35374）：
- 不再使用配置API，配置完全本地化
- 主要API端点：
  - `/api/lyric` - 获取内置歌词
  - `/api/lyricfile` - 获取歌词文件
  - `/api/now-playing` - 获取播放状态
  - `/api/play-pause` - 播放控制

## 架构和核心组件

### 主要架构模式
- **MVVM-like 架构**：虽然不是严格的 MVVM，但分离了 UI (XAML) 和逻辑 (C#)
- **事件驱动模型**：使用 DispatcherTimer 进行定时更新和动画
- **API 客户端模式**：通过 HTTP API 与外部歌词服务通信

### 核心组件

1. **App.xaml.cs** - 应用程序入口点
   - 系统托盘图标管理
   - 全局异常处理
   - 配置管理初始化
   - 右键菜单（字体、颜色、对齐、翻译设置）

2. **MainWindow.xaml.cs** - 主窗口和歌词显示
   - 定时器管理（位置同步、动画、播放控制）
   - 歌词渲染和同步
   - 播放控制（播放/暂停、上一首、下一首）
   - 鼠标交互处理

3. **LyricsApiService.cs** - API 服务层
   - 与本地歌词 API 服务器通信（端口 35374）
   - 获取歌词、当前播放状态和控制播放
   - 错误处理和备用 API 端点

4. **LyricsRenderer.cs** - 歌词渲染引擎
   - 解析带时间戳的歌词（LRC 格式）
   - 逐字动画和高亮效果
   - 双行显示（原文+翻译）
   - CJK 字符支持
   - 平滑动画和渐变效果

5. **TaskbarMonitor.cs** - Windows API 封装
   - 任务栏位置检测和窗口定位
   - 窗口样式设置（透明、置顶、穿透等）
   - Win32 API 调用封装

6. **ConfigManager.cs** - 配置管理
   - JSON 配置文件读写
   - 用户偏好设置持久化
   - 配置位置：%AppData%/TaskbarLyrics/config.json

7. **Logger.cs** - 日志系统
   - 支持多级别日志（Error, Info, Debug）
   - 自动过滤重复日志消息（5秒内）
   - 日志文件位置：%AppData%/TaskbarLyrics/debug.log
   - 可通过 watchlog.bat 实时监控日志

8. **FullScreenDetector.cs** - 全屏检测
   - 检测应用程序是否处于全屏模式
   - 全屏时自动隐藏歌词显示
   - 使用 Windows Forms 定时器定期检查

9. **Models/** - 数据模型
   - `LyricsResponse.cs` - API 响应模型
   - `NowPlayingResponse.cs` - 播放状态模型
   - `ConfigResponse.cs` - 配置响应模型
   - `WordTiming.cs` - 词语时间同步模型

### 特色功能

1. **逐字同步动画**：使用精确的时间戳实现词语级别的同步高亮
2. **多语言支持**：特别优化了 CJK 字符的渲染和动画
3. **任务栏集成**：通过 Win32 API 实现真正的任务栏嵌入
4. **全屏自适应**：检测全屏应用并自动隐藏歌词
5. **实时控制**：可以通过 API 控制音乐播放
6. **高度可定制**：字体、颜色、对齐方式、翻译等均可自定义
7. **智能日志**：支持多级别日志和重复消息过滤
8. **歌词过滤**：使用正则表达式过滤非歌词内容（如制作人信息）
9. **歌曲标题显示**：自动在歌词列表第一项显示当前歌曲标题
10. **动态歌词更新**：歌曲变化时自动触发歌词更新，不使用定时轮询

## 开发注意事项

1. **窗口管理**：应用程序使用特殊的窗口样式来嵌入任务栏，需要小心处理窗口状态
2. **定时器管理**：多个 DispatcherTimer 需要在窗口关闭时正确清理
3. **API 依赖**：依赖于本地运行的歌词 API 服务器（localhost:35374）
4. **DPI 缩放**：需要考虑不同 DPI 设置下的显示效果
5. **内存管理**：歌词渲染缓存需要定期清理以避免内存泄漏
6. **日志级别**：生产环境默认关闭日志，开发时可通过配置启用
7. **全屏检测**：使用 Windows Forms 定时器，每秒检查一次全屏状态
8. **歌词过滤性能**：过滤逻辑在歌词解析阶段执行，避免运行时性能问题
9. **正则表达式配置**：用户可自定义过滤规则，默认规则过滤包含冒号的非歌词行

## 架构设计要点

### 歌词处理流程
1. **歌词获取**：`LyricsApiService` 从本地 API 服务器获取歌词文本
2. **歌词解析**：`LyricsRenderer.ParseLyrics` 解析 LRC 格式歌词
3. **歌词过滤**：在解析阶段应用 `ShouldFilterLyricsText` 过滤非歌词内容
4. **标题插入**：将歌曲标题作为独立行插入到歌词列表开头
5. **渲染显示**：`MainWindow` 使用渲染后的歌词列表进行显示

### 歌词过滤实现
- **配置位置**：`ConfigResponse.EnableLyricsFilter` 和 `ConfigResponse.LyricsFilterRegex`
- **过滤时机**：在 `ParseLyricsLineGroup` 中解析后立即过滤
- **性能优化**：过滤只在解析时执行一次，避免运行时重复计算
- **默认规则**：过滤制作人信息、许可声明等非歌词内容

### 歌曲标题管理
- **标题跟踪**：`MainWindow._lastSongTitle` 跟踪当前播放歌曲
- **更新时机**：在 `UpdateNowPlaying` 中检测歌曲变化
- **插入位置**：作为歌词列表的第一项（StartTime = 0）

### 窗口 XAML 文件
- `MainWindow.xaml` - 主歌词显示窗口
- `SettingsWindow.xaml` - 设置配置窗口
- `AboutWindow.xaml` - 关于信息窗口
- `App.xaml` - 应用程序资源和启动配置

## 重要更新说明

### 歌词更新机制优化
**修改内容**：
- 移除了原有的 `_updateTimer` 定时器（800ms间隔）
- 改为在 `UpdateNowPlaying` 中检测到歌曲变化时才触发 `UpdateLyrics`
- 减少了不必要的API调用，提高了性能

**优势**：
- 歌词更新更加精确，只在歌曲切换时执行
- 避免了定时器轮询导致的资源浪费
- 提高了响应速度，歌曲切换时歌词立即更新

### 定时器调整
现有的定时器配置：
- **保留的定时器**：
  - `_nowPlayingTimer` (50ms) - 高频更新播放位置
  - `_smoothUpdateTimer` (32ms) - 歌词动画和显示更新
  - `_positionTimer` (2s) - 窗口位置同步
  - `_restoreTimer` (100ms) - 窗口状态维护
  - `_mouseLeaveTimer` (300ms) - 鼠标离开处理

- **移除的定时器**：
  - `_updateTimer` - 原本用于定期检查歌词变化

### 配置管理说明
- **本地化设计**：配置完全依赖本地文件系统，不再从API获取
- **配置位置**：`%AppData%/TaskbarLyrics/config.json`
- **实时生效**：设置窗口中的更改会立即保存和应用

## 构建和部署

### 构建命令
```bash
# 构建项目
dotnet build

# 运行项目（开发模式）
dotnet run

# 发布为自包含的可执行文件（包含 .NET 运行时）
dotnet publish -c Release -r win-x64 --self-contained

# 发布为依赖运行时的版本（需要目标机器安装 .NET 8 运行时）
dotnet publish -c Release -r win-x64 --self-contained false
```

### 依赖包
- Newtonsoft.Json 13.0.3 - JSON序列化

### 输出
- 单个可执行文件
- 依赖.NET 8运行时
- Windows平台专用