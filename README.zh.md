# DeepSeek Harness QuickOpen

[English](README.md) | 中文

DeepSeek Harness QuickOpen 是给 [DeepSeek Harness](https://github.com/deepseek-ai/deepseek-harness) 用的 Windows 一键启动器。

双击一个图标:启动器先自检环境,再把本地 `dsh web` 服务起起来,并用默认浏览器直接打开 Web UI。
不用终端、不用敲 `pnpm dsh web`。

> 非官方第三方工具,与 DeepSeek 官方没有隶属、背书或支持关系。

## 状态

一个小而专的工具:约 1.5k 行 C#,目标框架 `net9.0-windows`,发布为单个可执行文件。
在 Windows 11 x64 + Node.js 24 + .NET 9 上验证。

它只负责**把 DeepSeek Harness 起起来**,本身不实现 harness 的任何功能。
harness 的启动方式变了,需要跟着改的就是这个启动器。

## 运行

### 用发布好的程序

针对从源码运行的DSH
从 [Releases](../../releases) 下载 `StartDSH.exe`或者`StartDSH-portable.exe`,放到任意可写目录,双击即可。
建议使用**StartDSH-portable.exe**

| 文件 | 目标机器需要 | 体积 |
|---|---|---|
| `StartDSH.exe` | .NET 9 桌面运行时 | 约 300 KB |
| `StartDSH-portable.exe` | 什么都不用装 —— 运行时已打包 | 约 65 MB |

两者都是 win-x64。不确定装哪个就先用小的:缺运行时时 Windows 会直接提示,
那时再换成零依赖版。

### 从源码构建

需要 .NET 9 SDK。

```cmd
build.cmd                  :: 框架依赖单文件,约 300 KB
build.cmd /selfcontained   :: 零依赖单文件,约 65 MB
```

产物都是 `dist\StartDSH.exe`。**正在运行的启动器也能就地更新** —— 构建会把运行中的镜像改名挪开、
再把新版本放到位,所以重新构建不会打断正在跑的服务。

## 其余运行方式

启动器与路径无关:唯一随机器变的是"`deepseek-harness` 检出在哪",而那是**配置**,不是编译进去的东西。

如果默认位置不存在、也没人指定过路径,启动器会在**首次启动时弹出目录选择框**,校验后把
`dsh-launcher.json` 写在 exe 同目录,然后继续启动。想"只配置、先不启动":

```cmd
StartDSH.exe --setup                                    :: 选目录 + 写配置 + 建桌面快捷方式
StartDSH.exe --setup --repo "D:\code\deepseek-harness"   :: 不弹选择框
```

目标机器的前提:有一份带构建产物的 `deepseek-harness` 检出
(`apps\cli\lib\bin.js`、`apps\web\dist\index.html`),以及 Node.js `^22.19.0 || >=24.0.0`。

## 参数

| 参数 | 作用 |
|---|---|
| `--port <N>` | 监听端口(默认 `3080`;`0` 让系统随机分配) |
| `--host <地址>` | 绑定地址(默认 `127.0.0.1`) |
| `--workspace <目录>` | 起始工作目录,即会话的 workspace root |
| `--repo <目录>` | deepseek-harness 检出目录 |
| `--mode <auto\|built\|source>` | 用哪个 CLI 入口(`source` 必须从仓库根启动) |
| `--proxy <地址>` | 让 harness 走这个出站代理 |
| `--keep-env-proxy` | 沿袭环境里的 `HTTP_PROXY` / `HTTPS_PROXY` |
| `--print-env` | 打印子进程的环境与命令行后退出(诊断) |
| `--setup`、`--no-shortcut` | 换机器时的一次性配置 |
| `--no-browser` | 不自动打开浏览器 |
| `--install`、`--rebuild` | 启动前先跑 `pnpm install` / `pnpm run build` |
| `--console` | 用纯控制台界面代替窗口 |
| `--console-window` | 额外开一个最小化的日志控制台 |
| `-h`、`--help` | 帮助 |

把文件夹拖到 exe 上,该文件夹就成为本次会话的工作目录。

配置优先级:命令行 > 环境变量(`DSH_REPO`、`DSH_WORKSPACE`、`DSH_PORT`、`DSH_HOST`、`DSH_PROXY`)
> exe 同目录的 `dsh-launcher.json` > 内置默认。


## 日志

```
%LOCALAPPDATA%\DeepSeekHarness\launcher.log          每次运行的日志:自检、harness 输出、失败原因
%LOCALAPPDATA%\DeepSeekHarness\launcher-error.log    仅启动期崩溃
```
## 声明
作者非专业人员，本项目由AI独立完成
