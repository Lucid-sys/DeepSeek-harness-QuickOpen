# DeepSeek Harness QuickOpen

English | [中文](README.zh.md)
<p align="center">
<img width="623" height="582" alt="SnowShot_2026-09-19_00-01-16" src="https://github.com/user-attachments/assets/25e7c5e5-8057-4c40-8fb5-6fd86e4e73b1" />
</p>
DeepSeek Harness QuickOpen is a one-click Windows launcher for [DeepSeek Harness](https://github.com/deepseek-ai/deepseek-harness).

Double-click an icon: the launcher self-checks the environment, starts the local `dsh web` server, and opens the default browser straight into the Web UI. No terminal, no `pnpm dsh web`, and no console window.

> Unofficial third-party tool. Not affiliated with, endorsed by, or supported by DeepSeek.
> See [NOTICE.md](NOTICE.md) for brand and trademark details.

## Status

A small, single-purpose tool: roughly 1.5k lines of C# targeting `net9.0-windows`, shipped as one
single-file executable. Tested on Windows 11 x64 with Node.js 24 and .NET 9.

It launches DeepSeek Harness; it does not implement any of it. When the harness changes how it
starts, this launcher is what needs updating.

## Run

### Run from a release

Download `StartDSH.exe` from [Releases](../../releases), drop it in any writable folder, and
double-click it.

| file | needs on the target machine | size |
|---|---|---|
| `StartDSH.exe` | .NET 9 Desktop Runtime | ~300 KB |
| `StartDSH-portable.exe` | nothing extra — the runtime is bundled | ~65 MB |

Both are win-x64. Not sure which one? Start with the small one: if the runtime is missing,
Windows says so, and you switch to the portable build.

### Build from source

Requires the .NET 9 SDK.

```cmd
build.cmd                  :: framework-dependent single file, ~300 KB
build.cmd /selfcontained   :: self-contained single file, ~65 MB
```

Both write `dist\StartDSH.exe`. A running launcher can be updated in place — the build renames the
running image aside and swaps the new one in, so rebuilding never interrupts a running service.

## First run on a new machine

The launcher is path-agnostic: the only machine-specific value is *where the `deepseek-harness`
checkout lives*, and that is configuration, not something compiled in.

If the default location does not exist and no path was given, the launcher **asks for the folder on
first launch**, validates it, writes `dsh-launcher.json` next to itself, and carries on starting.
To configure without starting:

```cmd
StartDSH.exe --setup                                    :: pick folder, write config, create a desktop shortcut
StartDSH.exe --setup --repo "D:\code\deepseek-harness"   :: skip the folder picker
```

Prerequisites on the target machine: a `deepseek-harness` checkout with build artifacts
(`apps\cli\lib\bin.js`, `apps\web\dist\index.html`) and Node.js `^22.19.0 || >=24.0.0`.

## Options

| option | effect |
|---|---|
| `--port <N>` | listen port (default `3080`; `0` lets the OS choose) |
| `--host <addr>` | bind address (default `127.0.0.1`) |
| `--workspace <dir>` | starting working directory, i.e. the session's workspace root |
| `--repo <dir>` | the deepseek-harness checkout |
| `--mode <auto\|built\|source>` | which CLI entry to run (`source` must start from the repo root) |
| `--proxy <url>` | route the harness through this outbound proxy |
| `--keep-env-proxy` | inherit `HTTP_PROXY` / `HTTPS_PROXY` from the environment |
| `--print-env` | print the child process environment and command line, then exit (diagnostic) |
| `--setup`, `--no-shortcut` | one-time configuration on a new machine |
| `--no-browser` | do not open a browser |
| `--install`, `--rebuild` | run `pnpm install` / `pnpm run build` first |
| `--console` | plain console UI instead of the window |
| `--console-window` | also open a minimized log console |
| `-h`, `--help` | usage |

Dropping a folder onto the `.exe` uses it as the session's working directory.

Configuration resolves as: command line > environment (`DSH_REPO`, `DSH_WORKSPACE`, `DSH_PORT`,
`DSH_HOST`, `DSH_PROXY`) > `dsh-launcher.json` next to the exe > built-in defaults.


## Logs

```
%LOCALAPPDATA%\DeepSeekHarness\launcher.log          per-run log: checks, harness output, failures
%LOCALAPPDATA%\DeepSeekHarness\launcher-error.log    startup crashes only
```

## Troubleshooting

| symptom | what to do |
|---|---|
| Double-click does **nothing at all** | Read `launcher-error.log` (above) |
| "needs the .NET Desktop Runtime" | Use `StartDSH-portable.exe` instead |
| "does not look like a deepseek-harness checkout" | Wrong folder: run `StartDSH.exe --setup` again |
| "missing apps/web/dist/index.html" | Run `pnpm run build` in that checkout, or `StartDSH.exe --rebuild` |
| "missing node_modules\.pnpm" | Run `pnpm install` in that checkout, or `StartDSH.exe --install` |
| "Node version not supported" | Needs `^22.19.0 \|\| >=24.0.0` (**23.x is not supported**) |
| "port 3080 is in use" | `StartDSH.exe --port 3099` |
| Copying the exe to another machine | See `install-guide-zh.md` shipped in Releases |

## Documentation

- [PUBLISHING.md](PUBLISHING.md) — how to publish this repository and cut a release.
- [NOTICE.md](NOTICE.md) — brand, icon and trademark notices.
- [docs/design.md](docs/design.md) — why the launcher is built this way (design notes, in Chinese).
- Releases also ship `install-guide-zh.md`, a one-page install guide for the target machine.

## Contributing

Issues and pull requests are welcome. 

## License

[MIT](LICENSE)

Third-party assets and trademarks are disclosed in [NOTICE.md](NOTICE.md).
