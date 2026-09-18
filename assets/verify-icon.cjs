/**
 * 无图预览的校验:解析 app.ico,逐尺寸统计不透明覆盖率、填充色和外接框,
 * 用来确认标记真的渲染出来了(而不是空白或实心方块)。
 *
 * 用法:  node verify-icon.cjs [icoPath]
 */
const fs = require('node:fs')
const path = require('node:path')

const sharp = require('./resolve-sharp.cjs')()

const BRAND = { r: 0x4d, g: 0x6b, b: 0xfe }

function readFrames(file) {
  const buf = fs.readFileSync(file)

  // 也接受单张 PNG:从 exe 里抽出来的图标会先转成 PNG 再走这里校验。
  if (buf[0] === 0x89 && buf.toString('latin1', 1, 4) === 'PNG') {
    return [{ width: null, height: null, bytes: buf.length, offset: 0, data: buf }]
  }

  const type = buf.readUInt16LE(2)
  const count = buf.readUInt16LE(4)
  if (type !== 1) throw new Error(`not an icon (type=${type})`)

  const frames = []
  for (let i = 0; i < count; i++) {
    const at = 6 + i * 16
    const width = buf.readUInt8(at) || 256
    const height = buf.readUInt8(at + 1) || 256
    const bytes = buf.readUInt32LE(at + 8)
    const offset = buf.readUInt32LE(at + 12)
    frames.push({ width, height, bytes, offset, data: buf.subarray(offset, offset + bytes) })
  }
  return frames
}

async function analyse(frame) {
  const isPng = frame.data[0] === 0x89 && frame.data.toString('latin1', 1, 4) === 'PNG'
  if (!isPng) return { ...frame, format: 'BMP', opaque: null }

  const { data, info } = await sharp(frame.data).ensureAlpha().raw().toBuffer({ resolveWithObject: true })
  const { width, height, channels } = info

  let opaque = 0
  let brandish = 0
  let minX = width
  let minY = height
  let maxX = -1
  let maxY = -1
  let sumR = 0
  let sumG = 0
  let sumB = 0

  for (let y = 0; y < height; y++) {
    for (let x = 0; x < width; x++) {
      const at = (y * width + x) * channels
      const a = data[at + 3]
      if (a < 24) continue // 半透明边缘不计入覆盖率,避免抗锯齿噪声
      const r = data[at]
      const g = data[at + 1]
      const b = data[at + 2]
      opaque++
      sumR += r
      sumG += g
      sumB += b
      if (Math.abs(r - BRAND.r) < 40 && Math.abs(g - BRAND.g) < 40 && Math.abs(b - BRAND.b) < 40) brandish++
      if (x < minX) minX = x
      if (y < minY) minY = y
      if (x > maxX) maxX = x
      if (y > maxY) maxY = y
    }
  }

  return {
    ...frame,
    width,
    height,
    format: 'PNG',
    opaque,
    coverage: opaque / (width * height),
    brandShare: opaque === 0 ? 0 : brandish / opaque,
    mean: opaque === 0 ? null : [Math.round(sumR / opaque), Math.round(sumG / opaque), Math.round(sumB / opaque)],
    bbox: opaque === 0 ? null : [minX, minY, maxX, maxY],
  }
}

async function main() {
  const file = process.argv[2] ?? path.join(__dirname, '..', 'app.ico')
  const frames = readFrames(file)
  console.log(`${file}: ${frames.length} frame(s)\n`)

  let allGood = true
  const analysed = []

  for (const frame of frames) {
    const r = await analyse(frame)
    if (r.format === 'BMP') {
      console.log(`${String(r.width).padStart(3)}x${String(r.height).padEnd(3)} BMP  payload ${r.bytes} bytes`)
      continue
    }
    analysed.push(r)

    const coverage = (r.coverage * 100).toFixed(1)
    const brand = (r.brandShare * 100).toFixed(1)
    const w = r.bbox[2] - r.bbox[0] + 1
    const h = r.bbox[3] - r.bbox[1] + 1
    r.aspect = w / h
    r.inkHeight = h

    // 判定标准来自这枚标记本身的几何:横满宽、纵向墨迹约占 70%、轮廓复杂但非实心。
    const sane = r.coverage > 0.05 && r.coverage < 0.9
      && r.brandShare > 0.85
      && r.bbox[0] <= 1 && r.bbox[2] >= r.width - 2      // 横向铺满
      && h / r.height > 0.55 && h / r.height < 0.85      // 纵向约七成
      && w / r.width > 0.95                              // 横向确实用满
    if (!sane) allGood = false

    console.log(
      `${String(r.width).padStart(3)}x${String(r.height).padEnd(3)} PNG  ${String(r.bytes).padStart(5)}B  `
      + `coverage=${coverage.padStart(5)}%  brand=${brand.padStart(5)}%  `
      + `mean=rgb(${r.mean.join(',')})  bbox=[${r.bbox.join(',')}]  aspect=${r.aspect.toFixed(3)}  ${sane ? 'OK' : '<<< SUSPECT'}`,
    )
  }

  if (analysed.length === 0) {
    console.log('\nno PNG frames to analyse')
    process.exit(1)
  }

  // 每个尺寸都该是同一枚标记。容差按像素取整缩放:小尺寸下 1 像素就是 5% 的长宽比,
  // 所以用「允许约 4 像素的纵向取整误差」而不是固定百分比。渲染歪了会远远超出这个量级。
  const aspects = analysed.map(r => r.aspect)
  const sorted = [...aspects].sort((a, b) => a - b)
  const median = sorted[Math.floor(sorted.length / 2)]

  if (analysed.length > 1) {
    console.log(`\nbbox aspect: ${aspects.map(a => a.toFixed(3)).join(' ')}  (median ${median.toFixed(3)})`)
    for (const r of analysed) {
      const allowed = 4.0 / r.inkHeight
      const deviation = Math.abs(r.aspect - median)
      const ok = deviation <= allowed
      if (!ok) allGood = false
      console.log(
        `  ${String(r.width).padStart(3)}px  deviation=${deviation.toFixed(3)}  allowed=${allowed.toFixed(3)}  ${ok ? 'OK' : '<<< SUSPECT'}`,
      )
    }
  }

  console.log(allGood
    ? '\nall frames are the same correctly rendered brand mark on transparency'
    : '\nSOMETHING IS OFF — inspect the frames above')
  process.exit(allGood ? 0 : 1)
}

main().catch(error => {
  console.error(error)
  process.exit(1)
})
