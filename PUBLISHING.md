# 发布说明

项目为 Windows x64 和 Linux x64 分别提供“单文件自包含”和“Native AOT”两种发布模式。

## 单文件自包含发布

此模式将 .NET 运行时和本机图形库打包进一个可执行文件，目标计算机无需安装 .NET。本机库会在程序启动时自动解压。

在 Windows x64 环境中执行：

```powershell
dotnet publish .\AvaloniauiForEasytier\AvaloniauiForEasytier.csproj -p:PublishProfile=Windows-x64-SingleFile
```

在 Linux x64 环境中执行：

```bash
dotnet publish ./AvaloniauiForEasytier/AvaloniauiForEasytier.csproj -p:PublishProfile=Linux-x64-SingleFile
```

## Native AOT 发布

此模式将托管代码提前编译为目标平台的本机代码，并包含所需的 .NET 运行环境。Avalonia/Skia 使用本机图形动态库，因此 AOT 发布目录中可能包含 `libSkiaSharp`、HarfBuzz 等少量旁置文件；分发时需要保留整个发布目录。

在 Windows x64 环境中执行：

```powershell
dotnet publish .\AvaloniauiForEasytier\AvaloniauiForEasytier.csproj -p:PublishProfile=Windows-x64-AOT
```

在 Linux x64 环境中执行：

```bash
dotnet publish ./AvaloniauiForEasytier/AvaloniauiForEasytier.csproj -p:PublishProfile=Linux-x64-AOT
```

## 输出目录

默认输出位置如下：

```text
AvaloniauiForEasytier/bin/Release/net10.0/win-x64/publish/
AvaloniauiForEasytier/bin/Release/net10.0/linux-x64/publish/
```

## EasyTier FFI 原生文件

Windows 发布目录除主程序外，还必须保留以下旁置文件：

```text
easytier_ffi.dll
Packet.dll
wintun.dll
WinDivert64.sys
```

这些文件由 `.easytier-src` 中的 EasyTier 构建产物提供，并由项目文件自动复制到
Windows 输出目录。`easytier_ffi.dll` 使用旁置加载方式，不能只分发单文件 EXE。
Linux 发布需要在 Linux 主机上单独编译 `libeasytier_ffi.so`，并将它放在 Linux
发布目录中。

## 应用日志

应用使用 NLog 统一记录运行日志。普通日志会写入控制台和按日期滚动的文本文件，
只有 `Error` 与 `Fatal` 级别的关键日志会通过 FreeSql 异步写入本地 SQLite。
因此不会将所有运行输出复制到数据库中。

数据目录由 .NET 跨平台 API 决定：

```text
Windows: %LOCALAPPDATA%/AvaloniauiForEasytier/
Linux:   ~/.local/share/AvaloniauiForEasytier/
```

目录中的 `logs/application-YYYY-MM-DD.log` 保存普通日志，
`critical-logs.db` 保存关键日志。SQLite 不可用时，NLog 仍会继续写入控制台和文本文件，
不会阻断应用启动。

## 平台限制

Native AOT 不支持从 Windows 直接交叉编译 Linux 本机程序，也不支持从 Linux 直接交叉编译 Windows 本机程序。Windows 包需在 Windows 构建，Linux 包需在 Linux 构建，可分别使用对应系统的本机环境或持续集成任务。

Linux 目标计算机无需安装 .NET，但仍需具备图形系统以及 Avalonia/Skia 所需的操作系统级动态库。
