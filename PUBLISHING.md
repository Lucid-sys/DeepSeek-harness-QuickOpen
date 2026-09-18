# 如何发布到 GitHub

这份文件夹(`newfold`)就是从零开始可以 `git init` 并推送的一套内容。

---

## 0. 先确认这里有什么

```
newfold\
  .gitignore                 排除构建产物、每台机器的配置、私有记录
  .github\workflows\build.yml  GitHub Actions:推上去自动构建两个 exe
  LICENSE                    MIT(记得把 <把你的名字或 GitHub 用户名写在这里> 改掉)
  NOTICE.md                  第三方素材与商标说明(DeepSeek 标记来自 lobehub/lobe-icons)
  README.md                  项目说明(英文,仓库首页)
  README.zh.md               项目说明(中文)
  docs\design.md             设计说明:那些"看起来能更简单、但不能"的决定
  PUBLISHING.md              本文件
  DshLauncher.csproj         工程文件
  NuGet.Config               清掉机器级 NuGet 回退目录,让构建自足
  build.cmd / build-swap.ps1  构建脚本
  *.cs                       启动器源码
  Wpf\                       界面(WPF,浅色主题)
  assets\                    图标素材与生成/校验脚本
  app.ico                    exe 图标
  release\                   ★ 不进 git:放发布用的 exe 和安装说明
```

**为什么 exe 放在 `release\` 且被 gitignore**:仓库里塞二进制不好 ——
65MB 的自包含版超过 GitHub 对普通文件的建议上限(50MB)。
正确做法是把它作为 **Release 附件**上传(见第 4 节)。`release\` 里已经放好了:

| 文件 | 用途 |
|---|---|
| `release\StartDSH.exe` | 小体积版(约 300KB),作为 Release 附件 |
| `release\StartDSH-portable.exe` | 零依赖单文件(约 65MB),作为 Release 附件 |
| `release\install-guide-zh.md` | 一页安装说明(中文内容,ASCII 文件名),也可以一并附上 |

---

## 1. 发布前自查(重要)

这套内容已经清理过一遍:所有硬编码的个人路径都被去掉了
(`DefaultRepo` 改成 `%USERPROFILE%\deepseek-harness`,图标脚本改成从环境变量/检出里找 sharp),
exe 里的调试符号路径也用 `PathMap` 重写成了 `/src`。

你自己在改完之后,建议每次推送前跑一遍(把下面 `$mine` 换成**你自己的**用户名、
常用的盘符路径等,目的是搜"只有你的机器才会有的字符串"):

```powershell
$mine = @('<你的用户名>', 'C:\Users\<你的用户名>', '<你常用的某个盘符目录>')

Get-ChildItem -Recurse -File |
  Where-Object { $_.FullName -notmatch '\\(bin|obj|release|\.git)\\' } |
  Select-String -Pattern $mine -SimpleMatch |
  Select-Object Path, LineNumber, Line
```

预期输出为空(像本文档里出现的 `<你的用户名>` 这种占位符是正常的)。
仓库当前状态已经按这个标准清理过一遍。

**另外千万不要把下面这些提交上去**:

- `dsh-launcher.json`(每台机器的绝对路径,已 gitignore)
- `%LOCALAPPDATA%\DeepSeekHarness\launcher.log` 里的内容(含 token 地址)
- `交付总结.md`(如果放在同一目录,已 gitignore)

---

## 2. 本地建仓库并提交

```powershell
cd "F:\deepseek harness\newfold"

git init
git add .
git status          # ★ 看清楚要提交哪些文件;release\ 不应该出现在列表里
git commit -m "DeepSeek Harness 一键启动器"
```

`git status` 那一步值得多看一眼:确认没有 `release\`、没有 `dsh-launcher.json`、
没有你不想公开的文件。想反悔就 `git rm --cached <文件>` 再重新提交。

---

## 3. 推到 GitHub

先在网页上建一个**空仓库**(不要勾选 "Add a README" / .gitignore / license,
否则会有冲突):<https://github.com/new>

然后:

```powershell
git branch -M main
git remote add origin https://github.com/<你的用户名>/<仓库名>.git
git push -u origin main
```

第一次推送会弹浏览器让你登录 GitHub(或者用 `gh auth login` / PAT)。
如果装了 [GitHub CLI](https://cli.github.com/),第 3 步可以一步到位:

```powershell
gh repo create <仓库名> --public --source . --remote origin --push
```

---

## 4. 发一个 Release,把 exe 挂上去

网页操作:

1. 仓库页右侧 **Releases** → **Create a new release**
2. **Choose a tag**:输入 `v1.0.0`,选 "Create new tag on publish"
3. **Release title**:`v1.0.0`
4. 把 `release\` 里的文件拖进 "Attach binaries" 区域:
   - `StartDSH.exe`
   - `StartDSH-portable.exe`(65MB,可以只在你需要零依赖版时才挂)
   - `install-guide-zh.md`
5. 写几句说明,发布

> **资源名务必用 ASCII。** GitHub 会清洗 Release 资源名里的非 ASCII 字符:
> `安装说明.md` 上传后会变成 **`default.md`**。所以发布用的安装说明在 `release\` 里
> 叫 `install-guide-zh.md`(内容仍是中文)。
> 顺带一提,`release\` 里那个 65MB 的零依赖版**不进 git**,只作为 Release 附件 ——
> 它超过 GitHub 对普通文件的建议上限(50MB)。

命令行等价做法(需要 `gh`):

```powershell
git tag v1.0.0
git push origin v1.0.0
gh release create v1.0.0 release\StartDSH.exe release\StartDSH-portable.exe release\install-guide-zh.md --title "v1.0.0" --notes "首个公开版本"
```

> Release 附件单文件上限是 2GB,所以 65MB 的零依赖版完全没问题 ——
> 只是别放进 git 历史里。

---

## 5. 之后的日常改动

```powershell
git add -A
git commit -m "说明这次改了什么"
git push
```

想换版本号再发 Release,就是打新的 tag(`v2.1.0`)然后重复第 4 节。

---

## 6. GitHub Actions 会做什么

`.github/workflows/build.yml` 已经在里面了。推送之后,仓库的 **Actions** 标签页会自动:

1. 在 `windows-latest` 上装 .NET 9 SDK;
2. 构建框架依赖版和零依赖版两个 exe;
3. 把它们作为 **Artifacts** 上传(在 workflow 运行页面底部可下载)。

这样别人不用装 .NET SDK 也能拿到可执行文件;也顺便证明源码是可构建的。

> 第一次跑如果失败,最常见的原因是 `release\` 之外少了某个被 csproj 引用的文件
> (比如 `app.ico`、`assets\logo.png`)—— 它们必须在仓库里。

---

## 7. 一句话总结

```powershell
cd "F:\deepseek harness\newfold"
git init && git add . && git status     # 先确认清单
git commit -m "DeepSeek Harness 一键启动器"
git branch -M main
git remote add origin https://github.com/<你的用户名>/<仓库名>.git
git push -u origin main
# 然后到网页上发 Release,把 release\ 里的 exe 拖上去
```
