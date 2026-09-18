/**
 * 定位 sharp —— 图标生成与校验脚本共用的依赖。
 *
 * 这几个脚本不常跑,所以没有为它们单独装一套依赖。按顺序尝试三种来源:
 *
 *   1. 环境变量 DSH_SHARP   —— 直接给 sharp 包的目录(或包名)
 *   2. 常规 require('sharp') —— 在 assets/ 或项目根 npm install sharp 之后可用
 *   3. 环境变量 DSH_REPO     —— 指向 deepseek-harness 检出,它自带 sharp
 *
 * 找不到就抛一条能照着做的说明,而不是像早先那样把某台机器的绝对路径写死在代码里。
 */
const fs = require('node:fs')
const path = require('node:path')

/** require 一个路径或包名;失败返回 null(而不是抛)。 */
function tryRequire(spec) {
  try {
    return require(spec)
  } catch {
    return null
  }
}

/** 在一个 deepseek-harness 检出里找 sharp(兼容 pnpm 的 .pnpm 布局)。 */
function fromHarness(repo) {
  const direct = path.join(repo, 'node_modules', 'sharp')
  if (fs.existsSync(direct)) {
    const mod = tryRequire(direct)
    if (mod) return mod
  }

  const store = path.join(repo, 'node_modules', '.pnpm')
  if (fs.existsSync(store)) {
    // 目录名形如 sharp@0.35.3 或 sharp@0.35.3_@types+node@22.20.0;取版本最高的一个。
    const candidates = fs.readdirSync(store)
      .filter(name => name.startsWith('sharp@'))
      .sort()
      .reverse()
    for (const name of candidates) {
      const mod = tryRequire(path.join(store, name, 'node_modules', 'sharp'))
      if (mod) return mod
    }
  }

  return null
}

module.exports = function resolveSharp() {
  const explicit = process.env.DSH_SHARP
  if (explicit) {
    const mod = tryRequire(explicit) || tryRequire(path.resolve(explicit))
    if (mod) return mod
    throw new Error(`DSH_SHARP 指向的位置加载不了 sharp:${explicit}`)
  }

  const plain = tryRequire('sharp')
  if (plain) return plain

  const repo = process.env.DSH_REPO
    || (process.argv.find(arg => arg.startsWith('--repo=')) ?? '').slice('--repo='.length)
  if (repo) {
    const mod = fromHarness(repo)
    if (mod) return mod
  }

  throw new Error(
    '找不到 sharp。三种办法任选其一:\n'
    + '  1) 在 assets/ 或项目根执行:npm install sharp\n'
    + '  2) 设环境变量 DSH_SHARP=<sharp 包所在目录>\n'
    + '  3) 设环境变量 DSH_REPO=<deepseek-harness 检出目录>(它自带 sharp)',
  )
}
