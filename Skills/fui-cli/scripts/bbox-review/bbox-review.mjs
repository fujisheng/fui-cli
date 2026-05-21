#!/usr/bin/env node
import { mkdir, readFile, writeFile } from 'node:fs/promises';
import { existsSync, readFileSync } from 'node:fs';
import path from 'node:path';
import { fileURLToPath } from 'node:url';

const __filename = fileURLToPath(import.meta.url);
const __dirname = path.dirname(__filename);
const args = process.argv.slice(2);

const hasFlag = (name) => args.includes(name);

const readArg = (name) => {
  const index = args.indexOf(name);
  if (index < 0 || index + 1 >= args.length) {
    return '';
  }

  return args[index + 1];
};

const readRequiredArg = (name, description) => {
  const value = readArg(name).trim();
  if (!value) {
    fail(`缺少必填参数 ${name}：${description}。`);
  }

  return value;
};

const fail = (message) => {
  console.error(message);
  process.exit(1);
};

const showHelp = () => {
  console.log(`FUI 设计图 bbox review 生成器

用法：
  node Packages/fui-cli/Skills/fui-cli/scripts/bbox-review/bbox-review.mjs \\
    --view LoginView \\
    --design FUI-CLI/LoginView/design-master.png \\
    --visual FUI-CLI/LoginView/LoginView.visual-ui.json

必填：
  --view     View 名称
  --design   用户确认的 design-master.png
  --visual   extract-visual-ui 生成的 visual-ui.json

可选：
  --layer-plan <path>  已有 layer_plan.json；存在时优先读取其中的 bbox 字段
  --data <path>        输出 bbox-review-data.json；默认 FUI-CLI/<ViewName>/bbox-review-data.json
  --html <path>        输出 bbox-review.html；默认 FUI-CLI/<ViewName>/previews/<ViewName>.bbox-review.html
  --padding <px>       初始 design_visual_bbox 相对 html_rect 外扩像素；默认 48
  --include-root       包含接近整屏的根节点
  --asset <id>         只输出匹配 id/name 的节点，可重复
  --help               显示帮助

原则：
  html_rect 只作为布局/热区参考。裁切必须以设计图上确认后的 design_visual_bbox 为准。
`);
};

if (hasFlag('--help') || hasFlag('-h')) {
  showHelp();
  process.exit(0);
}

const projectRoot = findProjectRoot(process.cwd()) || findProjectRoot(__dirname);
if (!projectRoot) {
  fail('无法定位 Unity 项目根目录。请在包含 Assets/Packages/ProjectSettings 的项目内执行。');
}

const viewName = readRequiredArg('--view', 'View 名称');
const designPath = resolveProjectPath(readRequiredArg('--design', 'design-master.png 路径'));
const visualPath = resolveProjectPath(readRequiredArg('--visual', 'visual-ui.json 路径'));
const layerPlanPath = resolveProjectPath(readArg('--layer-plan'));
const padding = readNonNegativeIntegerArg('--padding', 48);
const includeRoot = hasFlag('--include-root');
const filters = readRepeatedArg('--asset').map((value) => value.toLowerCase());
const defaultDir = path.join(projectRoot, 'FUI-CLI', viewName);
const dataPath = resolveProjectPath(readArg('--data') || path.join(defaultDir, 'bbox-review-data.json'));
const htmlPath = resolveProjectPath(readArg('--html') || path.join(defaultDir, 'previews', `${viewName}.bbox-review.html`));

await ensureFileExists(designPath, `design-master.png 不存在：${toProjectRelative(designPath)}。`);
await ensureFileExists(visualPath, `visual-ui.json 不存在：${toProjectRelative(visualPath)}。`);

const visual = JSON.parse(await readFile(visualPath, 'utf8'));
const canvas = readCanvasSize(visual, designPath);
const layerPlan = layerPlanPath && existsSync(layerPlanPath)
  ? JSON.parse(await readFile(layerPlanPath, 'utf8'))
  : null;

const candidates = buildCandidates(visual, layerPlan, canvas, padding, includeRoot, filters);
if (candidates.length === 0) {
  fail('没有可 review 的元素。请检查 visual-ui.json、--asset 过滤条件或 --include-root。');
}

const reviewData = {
  schemaVersion: 'fui-bbox-review-1.0',
  viewName,
  designMaster: toProjectRelative(designPath),
  visualUi: toProjectRelative(visualPath),
  layerPlan: layerPlanPath ? toProjectRelative(layerPlanPath) : '',
  canvas,
  coordinateSystem: 'top-left pixels',
  rule: 'html_rect is layout/hit reference only; source crops MUST use confirmed design_visual_bbox.',
  items: candidates
};

await mkdir(path.dirname(dataPath), { recursive: true });
await writeFile(dataPath, `${JSON.stringify(reviewData, null, 2)}\n`, 'utf8');

await mkdir(path.dirname(htmlPath), { recursive: true });
await writeFile(htmlPath, buildHtml(reviewData, designPath, dataPath, htmlPath), 'utf8');

console.log(JSON.stringify({
  ok: true,
  viewName,
  itemCount: candidates.length,
  data: toProjectRelative(dataPath),
  html: toProjectRelative(htmlPath)
}, null, 2));

function buildCandidates(visual, layerPlan, canvas, padding, includeRoot, filters) {
  const layerItems = new Map();
  for (const item of Array.isArray(layerPlan?.items) ? layerPlan.items : []) {
    if (item.id) {
      layerItems.set(item.id, item);
    }
  }

  const nodes = [];
  walkNodes(visual.nodes || [], (node) => nodes.push(node));

  const candidates = [];
  for (const node of nodes) {
    const id = `${node.id || node.name || ''}`.trim();
    if (!id || !node.rect) {
      continue;
    }

    const htmlRect = normalizeRect(node.rect);
    if (!htmlRect || htmlRect.width <= 0 || htmlRect.height <= 0) {
      continue;
    }

    if (!includeRoot && isRootLikeRect(htmlRect, canvas)) {
      continue;
    }

    if (!isAssetCandidate(node)) {
      continue;
    }

    if (filters.length > 0 && !filters.some((filter) => `${id} ${node.name || ''}`.toLowerCase().includes(filter))) {
      continue;
    }

    const layerItem = layerItems.get(id) || {};
    const designVisualBBox = normalizeArrayRect(
      layerItem.design_visual_bbox
      || layerItem.source_crop_bbox
      || layerItem.target_bbox
      || layerItem.visible_bbox
    ) || expandRect(htmlRect, padding, canvas);

    const hitRect = normalizeArrayRect(layerItem.hit_rect || layerItem.html_rect) || htmlRect;
    const sourceCropBBox = normalizeArrayRect(layerItem.source_crop_bbox) || designVisualBBox;

    candidates.push({
      id,
      name: node.name || id,
      webType: node.webType || '',
      element: node.element || '',
      html_rect: htmlRect,
      design_visual_bbox: designVisualBBox,
      source_crop_bbox: sourceCropBBox,
      hit_rect: hitRect,
      placement_offset: {
        x: designVisualBBox.x - htmlRect.x,
        y: designVisualBBox.y - htmlRect.y
      },
      text_mode: node.element === 'Text' ? 'runtime_text' : '',
      review_status: 'needs_review',
      notes: layerItem.notes || ''
    });
  }

  return candidates.sort((left, right) => {
    if (left.html_rect.y !== right.html_rect.y) {
      return left.html_rect.y - right.html_rect.y;
    }

    return left.html_rect.x - right.html_rect.x;
  });
}

function walkNodes(nodes, visitor) {
  for (const node of Array.isArray(nodes) ? nodes : []) {
    visitor(node);
    walkNodes(node.children || [], visitor);
  }
}

function isAssetCandidate(node) {
  const webType = `${node.webType || ''}`.toLowerCase();
  const element = `${node.element || ''}`.toLowerCase();
  const sprite = `${node.style?.sprite || ''}`.trim();
  const alpha = Number(node.style?.alpha ?? 1);

  if (sprite) {
    return true;
  }

  if (element === 'text' || webType === 'text') {
    return false;
  }

  if (alpha > 0 && alpha < 0.05 && !sprite) {
    return false;
  }

  return ['image', 'button', 'toggle', 'panel', 'container'].includes(webType)
    || ['imageelement', 'buttonelement', 'toggleelement', 'container'].includes(element);
}

function isRootLikeRect(rect, canvas) {
  return rect.x === 0
    && rect.y === 0
    && rect.width >= canvas.width * 0.95
    && rect.height >= canvas.height * 0.95;
}

function normalizeRect(rect) {
  const x = Math.round(Number(rect.x));
  const y = Math.round(Number(rect.y));
  const width = Math.round(Number(rect.width));
  const height = Math.round(Number(rect.height));
  if (![x, y, width, height].every(Number.isFinite)) {
    return null;
  }

  return { x, y, width, height };
}

function normalizeArrayRect(value) {
  if (!value) {
    return null;
  }

  if (Array.isArray(value) && value.length >= 4) {
    const [x, y, width, height] = value.map((item) => Math.round(Number(item)));
    if ([x, y, width, height].every(Number.isFinite)) {
      return { x, y, width, height };
    }
  }

  return normalizeRect(value);
}

function expandRect(rect, padding, canvas) {
  const x = Math.max(0, rect.x - padding);
  const y = Math.max(0, rect.y - padding);
  const right = Math.min(canvas.width, rect.x + rect.width + padding);
  const bottom = Math.min(canvas.height, rect.y + rect.height + padding);
  return {
    x,
    y,
    width: Math.max(1, right - x),
    height: Math.max(1, bottom - y)
  };
}

function readCanvasSize(visual, designPath) {
  const fromJson = visual.referenceResolution || {};
  const width = Number(fromJson.width || 0);
  const height = Number(fromJson.height || 0);
  if (width > 0 && height > 0) {
    return { width, height };
  }

  return readPngSize(designPath);
}

function readPngSize(filePath) {
  const bytes = requireBuffer(filePath, 24);
  const signature = bytes.subarray(0, 8).toString('hex');
  if (signature !== '89504e470d0a1a0a') {
    fail(`当前只支持 PNG 设计图：${toProjectRelative(filePath)}。`);
  }

  return {
    width: bytes.readUInt32BE(16),
    height: bytes.readUInt32BE(20)
  };
}

function requireBuffer(filePath, length) {
  return readFileSync(filePath).subarray(0, length);
}

function buildHtml(reviewData, designPath, dataPath, htmlPath) {
  const designSrc = toPosix(path.relative(path.dirname(htmlPath), designPath));
  const reviewJson = JSON.stringify(reviewData).replace(/</g, '\\u003c');
  return `<!doctype html>
<html lang="zh-CN">
<head>
  <meta charset="utf-8">
  <meta name="viewport" content="width=device-width, initial-scale=1">
  <title>${escapeHtml(reviewData.viewName)} BBox Review</title>
  <style>
    :root {
      color-scheme: dark;
      font-family: Arial, "Microsoft YaHei", sans-serif;
      background: #171b1d;
      color: #e8ece8;
    }
    body {
      margin: 0;
      display: grid;
      grid-template-columns: minmax(320px, 380px) 1fr;
      min-height: 100vh;
    }
    aside {
      position: sticky;
      top: 0;
      height: 100vh;
      overflow: auto;
      box-sizing: border-box;
      padding: 16px;
      background: #202629;
      border-right: 1px solid #394245;
    }
    main {
      overflow: auto;
      padding: 24px;
    }
    h1 {
      margin: 0 0 12px;
      font-size: 20px;
    }
    .note {
      margin: 0 0 14px;
      color: #b9c4bd;
      line-height: 1.5;
      font-size: 13px;
    }
    .toolbar {
      display: grid;
      grid-template-columns: 1fr 1fr;
      gap: 8px;
      margin: 12px 0;
    }
    button, input, textarea {
      font: inherit;
    }
    button {
      border: 1px solid #526064;
      background: #2f383b;
      color: #eef4ef;
      border-radius: 6px;
      padding: 8px 10px;
      cursor: pointer;
    }
    button:hover {
      background: #394448;
    }
    .item-list {
      display: grid;
      gap: 8px;
    }
    .item {
      text-align: left;
      border-color: #435055;
    }
    .item.active {
      border-color: #ffb84d;
      background: #4a3920;
    }
    .item small {
      display: block;
      color: #b8c0bb;
      margin-top: 3px;
    }
    .stage {
      position: relative;
      width: ${reviewData.canvas.width}px;
      height: ${reviewData.canvas.height}px;
      background: #101314;
      box-shadow: 0 0 0 1px #3b464a;
    }
    .stage > img {
      position: absolute;
      inset: 0;
      width: ${reviewData.canvas.width}px;
      height: ${reviewData.canvas.height}px;
      user-select: none;
      -webkit-user-drag: none;
    }
    .box {
      position: absolute;
      box-sizing: border-box;
      pointer-events: none;
    }
    .html-box {
      border: 2px dashed #35a7ff;
      background: rgba(53, 167, 255, 0.08);
    }
    .visual-box {
      border: 3px solid #ffb84d;
      background: rgba(255, 184, 77, 0.07);
      cursor: move;
      pointer-events: auto;
    }
    .visual-box.active {
      border-color: #ff553d;
      background: rgba(255, 85, 61, 0.1);
    }
    .label {
      position: absolute;
      left: 0;
      top: -24px;
      white-space: nowrap;
      background: rgba(10, 12, 12, 0.82);
      color: #fff6dc;
      padding: 2px 6px;
      border-radius: 4px;
      font-size: 13px;
    }
    .handle {
      position: absolute;
      width: 12px;
      height: 12px;
      right: -7px;
      bottom: -7px;
      border-radius: 50%;
      background: #ff553d;
      border: 2px solid #fff4dd;
      cursor: nwse-resize;
      pointer-events: auto;
    }
    .editor {
      display: grid;
      grid-template-columns: 1fr 1fr;
      gap: 8px;
      margin: 12px 0;
    }
    .editor label {
      display: grid;
      gap: 4px;
      color: #cfd8d2;
      font-size: 12px;
    }
    .editor input {
      width: 100%;
      box-sizing: border-box;
      border: 1px solid #4a5558;
      border-radius: 4px;
      padding: 6px;
      background: #15191b;
      color: #f2f6f3;
    }
    textarea {
      width: 100%;
      min-height: 150px;
      box-sizing: border-box;
      border: 1px solid #4a5558;
      border-radius: 6px;
      padding: 8px;
      background: #15191b;
      color: #e8ece8;
      font-family: Consolas, monospace;
      font-size: 12px;
    }
    .legend {
      display: flex;
      gap: 10px;
      flex-wrap: wrap;
      margin: 8px 0 12px;
      color: #c8d0cb;
      font-size: 12px;
    }
    .swatch {
      display: inline-block;
      width: 18px;
      height: 10px;
      margin-right: 5px;
      border-radius: 2px;
    }
    .blue {
      border: 2px dashed #35a7ff;
    }
    .orange {
      border: 2px solid #ffb84d;
    }
  </style>
</head>
<body>
  <aside>
    <h1>${escapeHtml(reviewData.viewName)} BBox Review</h1>
    <p class="note">蓝框是 HTML 布局/热区参考，橙框是设计图真实美术裁切框。最终 Source Crop 只能使用确认后的橙框。</p>
    <div class="legend">
      <span><i class="swatch blue"></i>html_rect</span>
      <span><i class="swatch orange"></i>design_visual_bbox</span>
    </div>
    <div class="toolbar">
      <button id="copyJson">复制 JSON</button>
      <button id="downloadJson">下载 JSON</button>
      <button id="markReviewed">标记确认</button>
      <button id="resetActive">重置当前</button>
    </div>
    <div class="editor">
      <label>x<input id="boxX" type="number" min="0"></label>
      <label>y<input id="boxY" type="number" min="0"></label>
      <label>width<input id="boxW" type="number" min="1"></label>
      <label>height<input id="boxH" type="number" min="1"></label>
    </div>
    <div id="itemList" class="item-list"></div>
    <p class="note">拖动橙框移动；拖右下角圆点缩放。键盘方向键微调位置，Shift+方向键调整尺寸。</p>
    <textarea id="jsonOut" spellcheck="false"></textarea>
  </aside>
  <main>
    <div id="stage" class="stage">
      <img src="${escapeAttribute(designSrc)}" alt="design-master">
    </div>
  </main>
  <script>
    const review = ${reviewJson};
    const original = structuredClone(review);
    const stage = document.querySelector('#stage');
    const list = document.querySelector('#itemList');
    const jsonOut = document.querySelector('#jsonOut');
    const inputs = {
      x: document.querySelector('#boxX'),
      y: document.querySelector('#boxY'),
      width: document.querySelector('#boxW'),
      height: document.querySelector('#boxH')
    };
    let activeIndex = 0;
    let drag = null;

    function clampRect(rect) {
      rect.x = Math.max(0, Math.min(review.canvas.width - 1, Math.round(Number(rect.x) || 0)));
      rect.y = Math.max(0, Math.min(review.canvas.height - 1, Math.round(Number(rect.y) || 0)));
      rect.width = Math.max(1, Math.round(Number(rect.width) || 1));
      rect.height = Math.max(1, Math.round(Number(rect.height) || 1));
      if (rect.x + rect.width > review.canvas.width) {
        rect.width = review.canvas.width - rect.x;
      }
      if (rect.y + rect.height > review.canvas.height) {
        rect.height = review.canvas.height - rect.y;
      }
    }

    function updateDerived(item) {
      item.source_crop_bbox = { ...item.design_visual_bbox };
      item.placement_offset = {
        x: item.design_visual_bbox.x - item.html_rect.x,
        y: item.design_visual_bbox.y - item.html_rect.y
      };
    }

    function render() {
      stage.querySelectorAll('.box').forEach((node) => node.remove());
      list.textContent = '';
      review.items.forEach((item, index) => {
        const htmlBox = makeBox(item.html_rect, 'html-box');
        stage.appendChild(htmlBox);
        const visualBox = makeBox(item.design_visual_bbox, 'visual-box' + (index === activeIndex ? ' active' : ''));
        visualBox.dataset.index = index;
        visualBox.innerHTML = '<span class="label"></span><span class="handle"></span>';
        visualBox.querySelector('.label').textContent = item.id;
        visualBox.addEventListener('pointerdown', startDrag);
        stage.appendChild(visualBox);

        const button = document.createElement('button');
        button.className = 'item' + (index === activeIndex ? ' active' : '');
        button.innerHTML = '<strong></strong><small></small>';
        button.querySelector('strong').textContent = item.id;
        button.querySelector('small').textContent = rectText(item.design_visual_bbox) + ' | ' + item.review_status;
        button.addEventListener('click', () => select(index));
        list.appendChild(button);
      });
      syncEditor();
      writeJson();
    }

    function makeBox(rect, className) {
      const box = document.createElement('div');
      box.className = 'box ' + className;
      box.style.left = rect.x + 'px';
      box.style.top = rect.y + 'px';
      box.style.width = rect.width + 'px';
      box.style.height = rect.height + 'px';
      return box;
    }

    function rectText(rect) {
      return Math.round(rect.x) + ',' + Math.round(rect.y) + ',' + Math.round(rect.width) + ',' + Math.round(rect.height);
    }

    function select(index) {
      activeIndex = index;
      render();
      const item = review.items[activeIndex];
      const rect = item.design_visual_bbox;
      stage.parentElement.scrollTo({
        left: Math.max(0, rect.x - 240),
        top: Math.max(0, rect.y - 240),
        behavior: 'smooth'
      });
    }

    function syncEditor() {
      const rect = review.items[activeIndex].design_visual_bbox;
      for (const key of Object.keys(inputs)) {
        inputs[key].value = rect[key];
      }
    }

    function applyEditor() {
      const item = review.items[activeIndex];
      item.design_visual_bbox = {
        x: Number(inputs.x.value),
        y: Number(inputs.y.value),
        width: Number(inputs.width.value),
        height: Number(inputs.height.value)
      };
      clampRect(item.design_visual_bbox);
      item.review_status = 'adjusted';
      updateDerived(item);
      render();
    }

    Object.values(inputs).forEach((input) => input.addEventListener('change', applyEditor));

    function startDrag(event) {
      const box = event.currentTarget;
      const index = Number(box.dataset.index);
      activeIndex = index;
      const item = review.items[index];
      const isResize = event.target.classList.contains('handle');
      drag = {
        pointerId: event.pointerId,
        isResize,
        startX: event.clientX,
        startY: event.clientY,
        rect: { ...item.design_visual_bbox }
      };
      box.setPointerCapture(event.pointerId);
      event.preventDefault();
    }

    window.addEventListener('pointermove', (event) => {
      if (!drag) {
        return;
      }
      const dx = Math.round(event.clientX - drag.startX);
      const dy = Math.round(event.clientY - drag.startY);
      const item = review.items[activeIndex];
      const next = { ...drag.rect };
      if (drag.isResize) {
        next.width += dx;
        next.height += dy;
      } else {
        next.x += dx;
        next.y += dy;
      }
      clampRect(next);
      item.design_visual_bbox = next;
      item.review_status = 'adjusted';
      updateDerived(item);
      render();
    });

    window.addEventListener('pointerup', () => {
      drag = null;
    });

    window.addEventListener('keydown', (event) => {
      if (!['ArrowLeft', 'ArrowRight', 'ArrowUp', 'ArrowDown'].includes(event.key)) {
        return;
      }
      const item = review.items[activeIndex];
      const rect = item.design_visual_bbox;
      const step = event.ctrlKey ? 10 : 1;
      const sign = (event.key === 'ArrowLeft' || event.key === 'ArrowUp') ? -step : step;
      if (event.shiftKey) {
        if (event.key === 'ArrowLeft' || event.key === 'ArrowRight') {
          rect.width += sign;
        } else {
          rect.height += sign;
        }
      } else if (event.key === 'ArrowLeft' || event.key === 'ArrowRight') {
        rect.x += sign;
      } else {
        rect.y += sign;
      }
      clampRect(rect);
      item.review_status = 'adjusted';
      updateDerived(item);
      render();
      event.preventDefault();
    });

    document.querySelector('#markReviewed').addEventListener('click', () => {
      review.items[activeIndex].review_status = 'reviewed';
      render();
    });

    document.querySelector('#resetActive').addEventListener('click', () => {
      review.items[activeIndex] = structuredClone(original.items[activeIndex]);
      render();
    });

    document.querySelector('#copyJson').addEventListener('click', async () => {
      writeJson();
      await navigator.clipboard.writeText(jsonOut.value);
    });

    document.querySelector('#downloadJson').addEventListener('click', () => {
      writeJson();
      const blob = new Blob([jsonOut.value], { type: 'application/json' });
      const url = URL.createObjectURL(blob);
      const a = document.createElement('a');
      a.href = url;
      a.download = '${escapeAttribute(path.basename(dataPath))}';
      a.click();
      URL.revokeObjectURL(url);
    });

    function writeJson() {
      jsonOut.value = JSON.stringify(review, null, 2);
    }

    window.fuiExportBboxReview = () => structuredClone(review);
    render();
  </script>
</body>
</html>
`;
}

function readNonNegativeIntegerArg(name, fallback) {
  const raw = readArg(name).trim();
  if (!raw) {
    return fallback;
  }

  const value = Number.parseInt(raw, 10);
  if (!Number.isFinite(value) || value < 0 || `${value}` !== raw) {
    fail(`${name} 必须是非负整数，当前值：${raw}。`);
  }

  return value;
}

function readRepeatedArg(name) {
  const values = [];
  for (let index = 0; index < args.length; index++) {
    if (args[index] === name && index + 1 < args.length) {
      values.push(args[index + 1]);
    }
  }

  return values;
}

async function ensureFileExists(filePath, message) {
  if (!existsSync(filePath)) {
    fail(message);
  }
}

function resolveProjectPath(value) {
  if (!value) {
    return '';
  }

  if (path.isAbsolute(value)) {
    return path.normalize(value);
  }

  return path.resolve(projectRoot, value);
}

function findProjectRoot(start) {
  let current = path.resolve(start);
  while (true) {
    if (
      existsSync(path.join(current, 'Assets'))
      && existsSync(path.join(current, 'Packages'))
      && existsSync(path.join(current, 'ProjectSettings'))
    ) {
      return current;
    }

    const parent = path.dirname(current);
    if (parent === current) {
      return '';
    }

    current = parent;
  }
}

function toProjectRelative(value) {
  if (!value) {
    return '';
  }

  return toPosix(path.relative(projectRoot, value));
}

function toPosix(value) {
  return value.replaceAll(path.sep, '/');
}

function escapeHtml(value) {
  return `${value}`
    .replaceAll('&', '&amp;')
    .replaceAll('<', '&lt;')
    .replaceAll('>', '&gt;')
    .replaceAll('"', '&quot;');
}

function escapeAttribute(value) {
  return escapeHtml(value).replaceAll("'", '&#39;');
}
