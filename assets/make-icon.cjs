/**
 * 把 LobeHub 的 DeepSeek 品牌标记(@lobehub/icons 里 DeepSeek 组件用的同一份 path)
 * 栅格化成多尺寸 Windows .ico,供 exe 图标使用。
 *
 * 为什么不用 SVG 直接当图标:Windows exe 图标必须是 .ico(内含多个位图尺寸)。
 * 这里用仓库 .pnpm 里已有的 sharp(libvips + librsvg)渲染,不额外装任何东西。
 *
 * 用法:  node make-icon.cjs
 * 产物:  ../app.ico           多尺寸图标(16…256),直接给 <ApplicationIcon>
 *        preview-256.png      预览图,用于肉眼确认
 */
const fs = require('node:fs')
const path = require('node:path')

const sharp = require('./resolve-sharp.cjs')()

/** LobeHub / DeepSeek 品牌蓝。 */
const BRAND = '#4D6BFE'

/** 与 @lobehub/icons 一致的 24×24 标记路径。 */
const MARK = 'M23.748 4.482c-.254-.124-.364.113-.512.234-.051.039-.094.09-.137.136-.372.397-.806.657-1.373.626-.829-.046-1.537.214-2.163.848-.133-.782-.575-1.248-1.247-1.548-.352-.156-.708-.311-.955-.65-.172-.241-.219-.51-.305-.774-.055-.16-.11-.323-.293-.35-.2-.031-.278.136-.356.276-.313.572-.434 1.202-.422 1.84.027 1.436.633 2.58 1.838 3.393.137.093.172.187.129.323-.082.28-.18.552-.266.833-.055.179-.137.217-.329.14a5.526 5.526 0 01-1.736-1.18c-.857-.828-1.631-1.742-2.597-2.458a11.365 11.365 0 00-.689-.471c-.985-.957.13-1.743.388-1.836.27-.098.093-.432-.779-.428-.872.004-1.67.295-2.687.684a3.055 3.055 0 01-.465.137 9.597 9.597 0 00-2.883-.102c-1.885.21-3.39 1.102-4.497 2.623C.082 8.606-.231 10.684.152 12.85c.403 2.284 1.569 4.175 3.36 5.653 1.858 1.533 3.997 2.284 6.438 2.14 1.482-.085 3.133-.284 4.994-1.86.47.234.962.327 1.78.397.63.059 1.236-.03 1.705-.128.735-.156.684-.837.419-.961-2.155-1.004-1.682-.595-2.113-.926 1.096-1.296 2.746-2.642 3.392-7.003.05-.347.007-.565 0-.845-.004-.17.035-.237.23-.256a4.173 4.173 0 001.545-.475c1.396-.763 1.96-2.015 2.093-3.517.02-.23-.004-.467-.247-.588zM11.581 18c-2.089-1.642-3.102-2.183-3.52-2.16-.392.024-.321.471-.235.763.09.288.207.486.371.739.114.167.192.416-.113.603-.673.416-1.842-.14-1.897-.167-1.361-.802-2.5-1.86-3.301-3.307-.774-1.393-1.224-2.887-1.298-4.482-.02-.386.093-.522.477-.592a4.696 4.696 0 011.529-.039c2.132.312 3.946 1.265 5.468 2.774.868.86 1.525 1.887 2.202 2.891.72 1.066 1.494 2.082 2.48 2.914.348.292.625.514.891.677-.802.09-2.14.11-3.054-.614zm1-6.44a.306.306 0 01.415-.287.302.302 0 01.2.288.306.306 0 01-.31.307.303.303 0 01-.304-.308zm3.11 1.596c-.2.081-.399.151-.59.16a1.245 1.245 0 01-.798-.254c-.274-.23-.47-.358-.552-.758a1.73 1.73 0 01.016-.588c.07-.327-.008-.537-.239-.727-.187-.156-.426-.199-.688-.199a.559.559 0 01-.254-.078c-.11-.054-.2-.19-.114-.358.028-.054.16-.186.192-.21.356-.202.767-.136 1.146.016.352.144.618.408 1.001.782.391.451.462.576.685.914.176.265.336.537.445.848.067.195-.019.354-.25.452z'

/** Windows 会为这些尺寸各取一张;256 是 Vista 以后的大图标。 */
const SIZES = [16, 20, 24, 32, 40, 48, 64, 128, 256]

/**
 * 按目标像素尺寸渲染一帧。
 * 直接把 width/height 写进 SVG,让 librsvg 以该尺寸栅格化 —— 比先渲染再缩放清晰得多。
 */
async function render(size) {
  const svg = `<svg xmlns="http://www.w3.org/2000/svg" width="${size}" height="${size}" viewBox="0 0 24 24" fill="${BRAND}"><path fill-rule="evenodd" d="${MARK}"/></svg>`
  const buf = await sharp(Buffer.from(svg), { density: 72 }).png({ compressionLevel: 9 }).toBuffer()
  const meta = await sharp(buf).metadata()
  if (meta.width !== size || meta.height !== size) {
    // 兜底:某些 librsvg 版本忽略 width/height,此时再显式缩放。
    return await sharp(buf).resize(size, size, { fit: 'contain' }).png({ compressionLevel: 9 }).toBuffer()
  }
  return buf
}

/** 组装多尺寸 ICO(PNG 载荷,Vista 以后支持)。 */
function packIco(frames) {
  const header = Buffer.alloc(6)
  header.writeUInt16LE(0, 0) // reserved
  header.writeUInt16LE(1, 2) // type = icon
  header.writeUInt16LE(frames.length, 4)

  const directory = Buffer.alloc(16 * frames.length)
  let offset = header.length + directory.length

  frames.forEach((frame, index) => {
    const at = index * 16
    const dim = frame.size >= 256 ? 0 : frame.size // 256 记作 0
    directory.writeUInt8(dim, at)
    directory.writeUInt8(dim, at + 1)
    directory.writeUInt8(0, at + 2) // 调色板数(真彩为 0)
    directory.writeUInt8(0, at + 3) // reserved
    directory.writeUInt16LE(1, at + 4) // planes
    directory.writeUInt16LE(32, at + 6) // 位深
    directory.writeUInt32LE(frame.buffer.length, at + 8)
    directory.writeUInt32LE(offset, at + 12)
    offset += frame.buffer.length
  })

  return Buffer.concat([header, directory, ...frames.map(f => f.buffer)])
}

async function main() {
  const here = __dirname
  const frames = []
  for (const size of SIZES) {
    frames.push({ size, buffer: await render(size) })
    console.log(`rendered ${size}x${size} (${frames[frames.length - 1].buffer.length} bytes)`)
  }

  const ico = packIco(frames)
  const icoPath = path.join(here, '..', 'app.ico')
  fs.writeFileSync(icoPath, ico)
  console.log(`\nwrote ${icoPath} — ${ico.length} bytes, ${frames.length} sizes`)

  const preview = path.join(here, 'preview-256.png')
  fs.writeFileSync(preview, frames[frames.length - 1].buffer)
  console.log(`wrote ${preview}`)

  // 界面(WPF 窗口)里显示的 logo:比任何显示尺寸都大一档,缩放后依然锐利。
  // 用位图而不是把 SVG path 交给 WPF 解析,是因为这份 path 里有 "1.436.633"
  // 这种 SVG 允许、但 WPF 迷你路径语言不一定接受的紧邻小数写法。
  const logoSize = 512
  const logoSvg = `<svg xmlns="http://www.w3.org/2000/svg" width="${logoSize}" height="${logoSize}" viewBox="0 0 24 24" fill="${BRAND}"><path fill-rule="evenodd" d="${MARK}"/></svg>`
  const logo = await sharp(Buffer.from(logoSvg), { density: 72 }).png({ compressionLevel: 9 }).toBuffer()
  const logoPath = path.join(here, 'logo.png')
  fs.mkdirSync(path.dirname(logoPath), { recursive: true })
  fs.writeFileSync(logoPath, logo)
  console.log(`wrote ${logoPath} — ${logoSize}x${logoSize}, ${logo.length} bytes`)
}

main().catch(error => {
  console.error(error)
  process.exit(1)
})
