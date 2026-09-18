/**
 * 界面配色的量化校验。
 *
 * 当前模型读不了图,所以"是不是浅色主题""左上角那枚标记到底看得见吗"这类问题
 * 靠像素统计回答,而不是靠肉眼声称。
 *
 * 用法:  node check-theme.cjs <窗口截图.png>
 *
 * 判定:
 *   - 浅色主题   → 内部区域里亮度 > 220 的像素应占多数(> 55%)
 *   - 标记可见   → logo 区域里品牌蓝(#4D6BFE)像素占比落在 8%~60% 之间
 *                  (0% = 没渲染出来;> 70% = 那是一整块蓝色圆底而不是标记)
 */
const fs = require('node:fs')

const sharp = require('./resolve-sharp.cjs')()

/**
 * 参照尺寸:窗口设计尺寸 668x624(WPF 的 DIP)。
 * 截图有可能按物理像素抓(高 DPI 下更大),所以下面的坐标与阈值都按实际宽度等比缩放 ——
 * 早先这套常量是写死的 668,换个缩放比例就会误报"越界"。
 */
const REFERENCE_WIDTH = 668
/** 窗口截图的外圈阴影留白(见 MainWindow.xaml 的 Margin="16")。 */
const REFERENCE_INSET = 20
/** logo 在窗口坐标里的位置:卡片内边距 20,16 + 图片 56x56。 */
const REFERENCE_LOGO = { x: 36, y: 32, w: 56, h: 56 }
/** 卡片内边界约 651;品牌蓝的合法最右位置是进度轨道末端 632,留一点余量到 640。 */
const REFERENCE_OVERFLOW_LIMIT = 640

function luminance(r, g, b) {
  return 0.2126 * r + 0.7152 * g + 0.0722 * b
}

async function main() {
  const file = process.argv[2]
  if (!file) throw new Error('usage: node check-theme.cjs <screenshot.png>')

  const { data, info } = await sharp(fs.readFileSync(file))
    .ensureAlpha()
    .raw()
    .toBuffer({ resolveWithObject: true })
  const { width, height, channels } = info

  const scale = width / REFERENCE_WIDTH
  const INSET = Math.round(REFERENCE_INSET * scale)
  const LOGO = {
    x: Math.round(REFERENCE_LOGO.x * scale),
    y: Math.round(REFERENCE_LOGO.y * scale),
    w: Math.round(REFERENCE_LOGO.w * scale),
    h: Math.round(REFERENCE_LOGO.h * scale),
  }
  const OVERFLOW_LIMIT = Math.round(REFERENCE_OVERFLOW_LIMIT * scale)

  console.log(`${file}: ${width}x${height}(缩放 {scale.toFixed(3)}x)\n`)

  let light = 0
  let total = 0
  let sum = 0
  const histogram = new Map()

  for (let y = INSET; y < height - INSET; y++) {
    for (let x = INSET; x < width - INSET; x++) {
      const at = (y * width + x) * channels
      const lum = luminance(data[at], data[at + 1], data[at + 2])
      total++
      sum += lum
      if (lum > 220) light++
      const bucket = Math.round(lum / 16) * 16
      histogram.set(bucket, (histogram.get(bucket) ?? 0) + 1)
    }
  }

  const meanLum = sum / total
  const lightShare = light / total
  const dominant = [...histogram.entries()].sort((a, b) => b[1] - a[1])[0]

  console.log('== 内部区域亮度 ==')
  console.log(`  平均亮度      ${meanLum.toFixed(1)} / 255`)
  console.log(`  >220 占比     ${(lightShare * 100).toFixed(1)}%`)
  console.log(`  最常见亮度档  ${dominant[0]} (${((dominant[1] / total) * 100).toFixed(1)}%)`)

  // 全图搜品牌蓝:先确认它到底画在哪儿,再看是不是落在预期位置。
  let globalBlue = 0
  let bx0 = width
  let by0 = height
  let bx1 = -1
  let by1 = -1
  for (let y = 0; y < height; y++) {
    for (let x = 0; x < width; x++) {
      const at = (y * width + x) * channels
      const r = data[at]
      const g = data[at + 1]
      const b = data[at + 2]
      if (b - r > 60 && b > 150 && g < b) {
        globalBlue++
        if (x < bx0) bx0 = x
        if (y < by0) by0 = y
        if (x > bx1) bx1 = x
        if (y > by1) by1 = y
      }
    }
  }

  console.log('\n== 全图品牌蓝像素 ==')
  console.log(`  数量          ${globalBlue}`)
  console.log(`  外接框        ${globalBlue === 0 ? '(无)' : `[${bx0},${by0} .. ${bx1},${by1}]`}`)

  // 进度条"扫动"是靠平移指示条实现的;容器若不裁剪,它会画到卡片外面去。
  // 合法位置(按 668 宽计):进度轨道止于 x≈632,按钮止于 x≈536。
  const overflowOk = globalBlue === 0 || bx1 <= OVERFLOW_LIMIT
  console.log(`  最右 x        ${bx1}(允许上限 ${OVERFLOW_LIMIT},按 ${scale.toFixed(2)}x 缩放)`)

  let blue = 0
  let logoPixels = 0
  for (let y = LOGO.y; y < LOGO.y + LOGO.h; y++) {
    for (let x = LOGO.x; x < LOGO.x + LOGO.w; x++) {
      if (x >= width || y >= height) continue
      const at = (y * width + x) * channels
      const r = data[at]
      const g = data[at + 1]
      const b = data[at + 2]
      logoPixels++
      // 品牌蓝 #4D6BFE = rgb(77,107,254):蓝远高于红,且足够饱和
      if (b - r > 60 && b > 150 && g < b) blue++
    }
  }
  const blueShare = logoPixels === 0 ? 0 : blue / logoPixels

  console.log('\n== 预期标记区域 ==')
  console.log(`  框            (${LOGO.x},${LOGO.y}) ${LOGO.w}x${LOGO.h}`)
  console.log(`  品牌蓝占比    ${(blueShare * 100).toFixed(1)}%`)

  const lightOk = lightShare > 0.55 && meanLum > 200
  const markOk = blueShare >= 0.08 && blueShare <= 0.60

  console.log('\n== 判定 ==')
  console.log(`  浅色主题      ${lightOk ? 'OK' : '<<< 不像浅色'}`)
  console.log(`  标记可见      ${markOk ? 'OK' : blueShare === 0 ? '<<< 没渲染出来' : blueShare > 0.6 ? '<<< 像一整块蓝色底' : '<<< 占比异常'}`)
  console.log(`  进度条无越界  ${overflowOk ? 'OK' : `<<< 有元素画到 x=${bx1},超出了卡片`}`)

  process.exit(lightOk && markOk && overflowOk ? 0 : 1)
}

main().catch(error => {
  console.error(error)
  process.exit(1)
})
