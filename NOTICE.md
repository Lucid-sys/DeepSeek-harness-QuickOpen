# 第三方素材与商标说明

本项目自己的代码以 [MIT](LICENSE) 发布。它另外用到/提到了下面这些第三方内容。

## DeepSeek 品牌标记(图标与界面 logo)

`app.ico` 与 `assets/logo.png` 里的图形,是 **DeepSeek 的品牌标记**,
取自 [lobehub/lobe-icons](https://github.com/lobehub/lobe-icons) 的
`@lobehub/icons-static-svg`(该图标集以 **MIT** 许可发布),品牌色 `#4D6BFE`。

- 矢量源保留在 `assets/deepseek.svg`,便于核对与替换;
- 重新生成:`cd assets && node make-icon.cjs`(需要 sharp,见 `assets/resolve-sharp.cjs`)。

**商标提醒**:"DeepSeek"、DeepSeek 标记及相关名称是其权利人的商标。
本项目是**非官方**的第三方启动器,与 DeepSeek 官方没有隶属或背书关系。
如果你要在自己的分支里发布,建议保留这段说明。

## 参考的实现

- [@lobehub/icons](https://www.npmjs.com/package/@lobehub/icons) —— 界面配色与图标用法参考。
- [sharp](https://sharp.pixelplumbing.com/)(libvips / librsvg)—— **仅**被 `assets/` 下的
  图标生成与校验脚本使用,不随本项目分发。
