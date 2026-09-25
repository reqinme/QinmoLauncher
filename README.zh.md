<div align="center">

# QinmoLauncher

**基于 Trident 核心的 Windows 桌面 Minecraft 启动器。**

[![License: MIT](https://img.shields.io/badge/License-MIT-yellow.svg?style=for-the-badge)](https://opensource.org/licenses/MIT)
[![.NET 10](https://img.shields.io/badge/.NET-10-5C2D91?style=for-the-badge)](https://dotnet.microsoft.com/)
[![Avalonia](https://img.shields.io/badge/Avalonia-12-3355FF?style=for-the-badge)](https://avaloniaui.net/)

[English](README.md) • [发布版本](https://github.com/reqinme/QinmoLauncher/releases) • [报告问题](https://github.com/reqinme/QinmoLauncher/issues)

</div>

---

## 这是什么

QinmoLauncher 是一个**仅面向 Windows** 的桌面启动器：支持全部 Minecraft 版本及配套的模组加载器，支持微软账号与离线账号，每个实例拥有独立的、互不干扰的工作区。

它是 [Polymerium](https://github.com/d3ara1n/Polymerium) 的改名分支。界面、主题与引擎都来自 Polymerium 的工作；本分支保留这套基础，只改动下面列出的几件事。署名见 [NOTICE](NOTICE)。

引擎是 **Trident**（[submodules/Trident.Net](submodules/Trident.Net)）——一套声明式的实例工具链，同时也驱动独立的 `trident` 命令行与 MCP 服务器。启动器是这套引擎之上的一层外壳：它调用同一批管理器、读取同一份 `profile.json`、写出同一套磁盘布局。

## 本分支改了什么

- **无需开发者模式即可部署。** Trident 把实例的 `build/` 目录表达为指向共享缓存的链接，而创建符号链接需要普通 Windows 用户并不持有的特权，过去会直接导致部署失败。本分支回退到硬链接与 junction，因此常规安装即可正常工作。
- **下载源可配置。** 可选的镜像源并自动回退到原始源，并行度与请求超时也可调。
- **崩溃上报走 GitHub Issue。** 不接入任何第三方崩溃服务，也没有遥测。所谓上报就是一个预填好的 Issue 链接，由你决定是否打开，绝不是后台悄悄上传。
- **中文界面真的显示中文。** 启动时会拿语言设置去匹配实际提供的资源集，`zh-CN` 的 Windows 不会再静默回退成英文。
- **实例数据待在自己的目录里**，不再与其他 Trident 前端共用的引擎级目录混在一起。见 [数据位置](#数据位置)。
- **部署失败会说清原因。** 一批操作失败时会逐项给出原因，而不是只报一个数量。

## 环境要求

- Windows 10 或 11，x64
- 不需要开发者模式，不需要符号链接特权，不需要管理员权限

## 安装

发布版本由 CI 在推送 `v*` 标签时构建。在第一个正式发布之前，请从源码构建：

```powershell
git clone --recurse-submodules https://github.com/reqinme/QinmoLauncher.git
cd QinmoLauncher
dotnet restore
dotnet build "Polymerium.slnx"
```

然后运行：

```powershell
.\src\Polymerium.Avalonia\bin\Debug\net10.0\QinmoLauncher.exe
```

.NET 10 SDK 版本由 `global.json` 锁定（当前为 `10.0.401`）。

## 数据位置

本启动器自己的东西全部集中在一个目录里，并且**刻意与其他 Trident 前端共用的引擎级目录分开**：

| 内容 | 位置 |
|---|---|
| 数据根目录 | `%LOCALAPPDATA%\QinmoLauncher\Trident` |
| 实例 | `<根>\instances\<标识>\` |
| 共享缓存（资源、库、运行时、包） | `<根>\cache\` |
| 设置、账号、HTTP 缓存、崩溃报告 | `<根>\.qinmolauncher\` |
| 随附命令行使用的账号 | `<根>\.trident.cli\` |

如果外部显式设置了 `TRIDENT_HOME`，它会覆盖上表的根目录，以上内容随之整体迁移到该路径下。手动运行随附的 `trident` 命令行时需要注意：**命令行不会套用启动器的默认值**，所以要指向同一个根目录，否则它报告的是引擎级实例库：

```powershell
$env:TRIDENT_HOME = "$env:LOCALAPPDATA\QinmoLauncher\Trident"
```

## 架构

Trident 核心之上的一层薄 Avalonia 外壳。外壳负责 MVVM 的页面/对话框/模态/吐司体验、主题、本地持久化与自动更新；它**不**重新实现实例管理、部署、仓库、账号或导入导出——那些属于核心。

实例是**声明式**的。`profile.json` 声明这个实例应该是什​​么样——游戏版本、加载器、包、规则——分阶段的部署流水线再把它变成可运行目录：

```
instances/<标识>/
  profile.json    声明
  import/         整合包源文件（真实复制）
  persist/        重新部署后依然保留的本地数据
  build/          可运行目录，链接进 cache/ 与 persist/
  snapshots/      快照
```

`build/` 里大多是链接而非副本，这让实例的创建与重建都很轻。由于声明是唯一真相，实例随时可以从 `profile.json` 重建。

### 项目结构

```
src/Polymerium.Avalonia/    桌面外壳
submodules/Trident.Net/     引擎：Abstractions <- Pref <- Core <- Cli
```

## 平台支持

**仅 Windows x64。** 本分支只在 Windows 上构建与测试。上游项目同时提供 Linux 与 macOS 构建，本分支不提供。

## 隐私

没有遥测，没有统计，没有第三方崩溃上报。

当启动器捕获到未处理错误时，它可以整理一份报告。**除非你自己打开那个预填好的 GitHub Issue**，否则什么都不会被发送；而且发送前内容完全由你过目。

## 许可

MIT，见 [LICENSE.txt](LICENSE.txt)。

本项目派生自 Polymerium（Copyright (c) d3ara1n），并以子模块形式使用 Trident.Net。两者均为 MIT 许可，所需的署名在 [NOTICE](NOTICE) 中。
