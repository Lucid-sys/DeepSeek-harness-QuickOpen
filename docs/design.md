# 设计说明

这份文档记录启动器里那些"看起来可以更简单、但实际不能"的决定。
每一条都是在真实机器上撞到问题之后才定下来的,附上当时观察到的现象。

---

## 为什么用已构建入口 `apps/cli/lib/bin.js`,而不是 `pnpm dsh web`

源码执行模式(`apps/cli/src/bin.ts` + `tsx`)靠 **tsx 从当前工作目录发现仓库的 `tsconfig.json`
paths** 来解析 `@deepseek-ai/*` 包。也就是说它必须 `cwd = 仓库根`。

实测:从别的工作目录启动源码模式,会以模块解析错误失败
(`@deepseek-ai/cordis does not provide an export named 'FiberState'`)。

这条约束的代价是:工作区被锁死成仓库根,`--workspace` 和"把文件夹拖到 exe 上"就都没意义了。
已构建入口与 cwd 无关,所以默认走它。要跑源码用 `--mode source`,此时启动器会明确提示
工作目录被改用仓库根。

---

## 为什么不直接用 harness 自己的自动开浏览器

DSH 的 Web 服务在**没有 token、没有 cookie** 时对 `GET /` 返回一个极简的 **401**,页面里没有二次登录入口。
认证方式是:启动时打印的那行地址里带一次性 token,访问它换取一个 30 天的 cookie。

所以启动器传 `--no-open`,自己解析 harness 打印的就绪行:

```
dsh web: http://127.0.0.1:3080/?token=xxx (LAN: http://192.168.1.5:3080/?token=xxx)
```

拿到带 token 的地址再打开浏览器 —— 这才是一步免登录直达。
harness 源码里也明确写了:这行 URL 和浏览器交接就是给 supervisor 用的就绪信号。

---

## 为什么是"最小化日志窗口"曾经存在、现在又去掉了

最初保留了一个 `AllocConsole` 出来、再 `ShowWindow(hwnd, SW_SHOWMINNOACTIVE)` 不激活最小化的控制台,
用来承载与旧版一致的运行日志。后来去掉了:

1. 它的价值只是"日志事后能查",而这由写文件(`launcher.log`)做得更好,还不带任何窗口;
2. 控制台本身有个真实故障 —— **被鼠标点中进入标记/选择模式后,任何输出都会阻塞进程**,
   旧版就那样卡死过(窗口标题会变成"选择 …")。留着它等于留着一个能冻住服务的机关。

`--console-window` 仍可临时要回来;`--console` 则是接回调用终端的纯文本模式。

---

## 为什么关窗口/退出不会留下孤儿进程

启动器给 harness 子进程建了一个 Windows **作业对象**并设置 `KILL_ON_JOB_CLOSE`。
无论启动器是正常退出、Ctrl+C 还是被任务管理器强杀,内核都会连带干掉整棵子进程树。

实测:强杀启动器后,node 子进程一起消失,端口零残留。

另外,点「停止服务」后窗口会自己关闭 —— 判断依据是**子进程真的退出了**
(`Completed` 是在 `WaitForExitAsync` 之后才发出的),不是"发出停止请求"就算数。

---

## 端口探测为什么给 5 秒超时

这台机器上连接一个**空闲**回环端口大约要 2.0–2.1 秒才返回拒绝(有安全软件/WFP 在延迟 RST)。
超时设短了(第一版是 1500ms)会把空闲端口误判成"被占用"。真正在监听的端口几十毫秒就接上。

顺带一提,"已有实例在跑"是靠 `GET /` 返回 **401** 这个指纹认出来的 ——
这既能把"已有 DSH"和"别的程序占了端口"区分开,也避免了一件真会出问题的事:
DSH 同一 `DSH_HOME` 只支持单实例(它自己会警告 `workspaces.json 已被另一个实例修改`)。

---

## 代理为什么默认摘掉

DeepSeek Harness 自带 `packages/util/http-proxy`:它从启动环境读 `HTTP_PROXY` / `HTTPS_PROXY` /
`ALL_PROXY`,装进自己进程的全局 dispatcher(Node 内置 fetch 默认不认这些变量),并给子进程
设置 `NODE_USE_ENV_PROXY=1`。

后果是:一台因为别的原因配了代理(比如 Clash 之类)的机器上,**harness 的全部出站流量,
包括模型 API 调用,都会走那个代理**,而且没有任何提示。

启动器因此默认把这一组变量从子进程环境里摘掉,即直连。**不改动机器上的任何设置**,
只是不往下传。`--proxy` / `--keep-env-proxy` 可显式改变这个行为,`--print-env` 可当场核对。

---

## 发布出去的 exe 里为什么不会有源码路径

`DebugType=embedded` 会把 PDB 打进 exe,而 PDB 记录着编译时的**绝对源文件路径**。
这个 exe 是要分发的,所以 csproj 里加了:

```xml
<PathMap>$(MSBuildProjectDirectory)=/src</PathMap>
```

效果:崩溃日志里的堆栈显示 `/src/Program.cs`,而不是构建者的主目录。
实测成品 exe 里 `C:\Users`、`dsh-launcher\` 这类字符串出现 **0** 次。

---

## 界面里的若干硬约束

- **`InvariantGlobalization` 必须为 false**。第一版为控制台场景设了 `true`,结果 WPF 一渲染就崩:
  它的字体缓存会构造 `CultureInfo("en")`,invariant 模式下直接抛 `CultureNotFoundException`。
  这个坑是靠崩溃日志定位的 —— 那也是 `launcher-error.log` 存在的理由。
- **界面资源必须用 pack URI**。写成相对路径 `Source="assets/logo.png"` 时 WPF **静默失败**:
  窗口照常显示、图就是不出来,既不抛异常也不写日志。规范写法是
  `pack://application:,,,/assets/logo.png`。这个坑是靠数像素发现的(全图品牌蓝的外接框落在
  日志区而不是头部),所以顺手给 `Image.ImageFailed` 加了日志。
- **进度条的"扫动"需要 `ClipToBounds`**。等待期间那个效果是靠平移指示条实现的,
  轨道容器不裁剪的话,指示条会平移出轨道、画到卡片外面去。
- **发布时交换正在运行的 exe**。运行中的镜像不能被覆盖,但可以被**重命名**。
  注意 `Move-Item` / `move` 默认**不覆盖**已有文件,而且 `catch` 里 `$_` 是错误记录而不是文件 ——
  第一版交换脚本正是踩了这两点,静默地什么都没做还报告成功。所以改用**带时间戳的唯一名字**。

---

## 关于量化验收

有几个结论不是"看起来对"就算数的,而是写了脚本来数:

- `assets/verify-icon.cjs` —— 逐帧统计图标的覆盖率、填充色、外接框、跨尺寸长宽比一致性;
- `assets/verify-exe-icon.ps1` —— 用 Win32 资源 API 从 exe 里读出图标组,确认尺寸真的嵌进去了;
- `assets/check-theme.cjs` —— 界面截图的三项判定:浅色主题(平均亮度/亮像素占比)、
  品牌标记可见(logo 区域品牌蓝占比)、进度条无越界(品牌蓝像素的最右 x)。
  这套常量和阈值按截图宽度等比缩放,因为截图有可能是物理像素。

这些脚本需要 [sharp](https://sharp.pixelplumbing.com/),定位方式见 `assets/resolve-sharp.cjs`。
