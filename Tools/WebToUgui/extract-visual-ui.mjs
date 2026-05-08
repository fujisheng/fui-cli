import { chromium } from '@playwright/test';
import { statSync } from 'node:fs';
import { access, mkdir, writeFile } from 'node:fs/promises';
import path from 'node:path';
import { fileURLToPath, pathToFileURL } from 'node:url';

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

const readPositiveIntegerArg = (name, description) => {
  const rawValue = readRequiredArg(name, description);
  const value = Number.parseInt(rawValue, 10);
  if (!Number.isFinite(value) || value <= 0 || `${value}` !== rawValue) {
    fail(`${name} 必须是大于 0 的整数，当前值：${rawValue}。`);
  }

  return value;
};

const showHelp = () => {
  console.log(`FUI WebToUgui visual-ui 提取器

用法：
  node Packages/com.fujisheng.fui.cli/Tools/WebToUgui/extract-visual-ui.mjs \\
    --input Temp/WebToUgui/MobaHomeView/MobaHomeView.html \\
    --view MobaHomeView \\
    --width 1920 \\
    --height 1080

必填：
  --input   Web 原型 HTML 路径，项目相对路径或绝对路径
  --view    View 名称，用于 JSON viewName 和默认输出目录
  --width   设计分辨率宽度；必须由用户或项目约定提供，不能默认猜测
  --height  设计分辨率高度；必须由用户或项目约定提供，不能默认猜测

可选：
  --json    输出 JSON 路径；默认 Temp/WebToUgui/<ViewName>/<ViewName>.visual-ui.json
  --png     输出截图路径；默认 Temp/WebToUgui/<ViewName>/<ViewName>.web.png
  --headed  显示浏览器窗口
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

const inputHtml = resolveProjectPath(readRequiredArg('--input', 'Web 原型 HTML 路径'));
const viewName = readRequiredArg('--view', 'View 名称');
const viewport = {
  width: readPositiveIntegerArg('--width', '设计分辨率宽度'),
  height: readPositiveIntegerArg('--height', '设计分辨率高度')
};
const headed = hasFlag('--headed');
const outputJson = resolveProjectPath(readArg('--json') || `Temp/WebToUgui/${viewName}/${viewName}.visual-ui.json`);
const outputScreenshot = resolveProjectPath(readArg('--png') || `Temp/WebToUgui/${viewName}/${viewName}.web.png`);

await ensureFileExists(inputHtml, `Web 原型 HTML 不存在: ${toProjectRelative(inputHtml)}。`);

const browser = await chromium.launch({ headless: !headed });
const page = await browser.newPage({ viewport, deviceScaleFactor: 1 });

await page.goto(pathToFileURL(inputHtml).href);
await page.waitForLoadState('load');

const plan = await page.evaluate(({ width, height, viewName }) => {
  const toNumber = (value) => {
    const parsed = Number.parseFloat(value);
    return Number.isFinite(parsed) ? parsed : 0;
  };

  const clamp = (value) => Math.round(value * 1000) / 1000;

  const rgbToHex = (value) => {
    const match = value.match(/rgba?\(([^)]+)\)/i);
    if (!match) {
      return value || '';
    }

    const parts = match[1].split(',').map((part) => Number.parseFloat(part.trim()));
    const [r, g, b] = parts;
    if (![r, g, b].every(Number.isFinite)) {
      return value || '';
    }

    const toHex = (component) => Math.max(0, Math.min(255, Math.round(component))).toString(16).padStart(2, '0').toUpperCase();
    return `#${toHex(r)}${toHex(g)}${toHex(b)}`;
  };

  const alphaFromColor = (value, opacity) => {
    const match = value.match(/rgba\(([^)]+)\)/i);
    if (!match) {
      return toNumber(opacity) || 1;
    }

    const parts = match[1].split(',').map((part) => Number.parseFloat(part.trim()));
    return Number.isFinite(parts[3]) ? parts[3] : (toNumber(opacity) || 1);
  };

  const directText = (element) => Array.from(element.childNodes)
    .filter((node) => node.nodeType === Node.TEXT_NODE)
    .map((node) => node.textContent.trim())
    .filter(Boolean)
    .join(' ');

  const mapElement = (webType) => {
    switch ((webType || '').toLowerCase()) {
      case 'button':
        return 'ButtonElement';
      case 'input':
        return 'InputFieldElement';
      case 'toggle':
        return 'ToggleElement';
      case 'text':
        return 'TextElement';
      case 'container':
        return 'Container';
      case 'scrollview':
        return 'ScrollView';
      case 'listview':
      case 'grid':
        return 'ListView';
      case 'template':
        return 'Template';
      case 'image':
      default:
        return 'ImageElement';
    }
  };

  const elements = Array.from(document.querySelectorAll('[data-ui-id]'));
  const records = elements.map((element) => {
    const rect = element.getBoundingClientRect();
    const style = window.getComputedStyle(element);
    const backgroundColor = style.backgroundColor === 'rgba(0, 0, 0, 0)' ? '' : style.backgroundColor;
    const color = style.color === 'rgba(0, 0, 0, 0)' ? '' : style.color;
    const text = directText(element);
    const webType = element.dataset.uiType || 'Image';
    const readNumberData = (name, fallback = 0) => {
      const value = Number.parseFloat(element.dataset[name] || '');
      return Number.isFinite(value) ? clamp(value) : fallback;
    };
    const readIntegerData = (name, fallback = 0) => {
      const value = Number.parseInt(element.dataset[name] || '', 10);
      return Number.isFinite(value) ? value : fallback;
    };

    return {
      element,
      node: {
        id: element.dataset.uiId,
        name: element.dataset.uiId,
        webType,
        element: mapElement(webType),
        rect: {
          x: clamp(rect.left),
          y: clamp(rect.top),
          width: clamp(rect.width),
          height: clamp(rect.height)
        },
        style: {
          color: rgbToHex(backgroundColor),
          textColor: rgbToHex(color),
          alpha: clamp(alphaFromColor(backgroundColor, style.opacity)),
          opacity: clamp(toNumber(style.opacity) || 1),
          borderRadius: clamp(toNumber(style.borderTopLeftRadius)),
          contentWidth: clamp(element.scrollWidth || rect.width),
          contentHeight: clamp(element.scrollHeight || rect.height)
        },
        text: {
          content: text,
          fontSize: clamp(toNumber(style.fontSize)),
          fontWeight: style.fontWeight,
          color: rgbToHex(color),
          alignment: style.textAlign || 'left'
        },
        list: {
          layout: element.dataset.listLayout || '',
          binding: element.dataset.listBinding || '',
          itemView: element.dataset.itemView || '',
          rowView: element.dataset.rowView || '',
          scrollDirection: element.dataset.scrollDirection || '',
          gridConstraint: element.dataset.gridConstraint || '',
          gridCount: readIntegerData('gridCount', 0),
          cellWidth: readNumberData('cellWidth', 0),
          cellHeight: readNumberData('cellHeight', 0),
          spacingX: readNumberData('spacingX', toNumber(style.columnGap) || toNumber(style.gap) || 0),
          spacingY: readNumberData('spacingY', toNumber(style.rowGap) || toNumber(style.gap) || 0),
          paddingLeft: readNumberData('paddingLeft', toNumber(style.paddingLeft) || 0),
          paddingRight: readNumberData('paddingRight', toNumber(style.paddingRight) || 0),
          paddingTop: readNumberData('paddingTop', toNumber(style.paddingTop) || 0),
          paddingBottom: readNumberData('paddingBottom', toNumber(style.paddingBottom) || 0)
        },
        template: {
          kind: element.dataset.templateKind || '',
          view: element.dataset.templateView || element.dataset.uiId || ''
        },
        children: []
      }
    };
  });

  const byElement = new Map(records.map((record) => [record.element, record]));
  const roots = [];

  for (const record of records) {
    const parent = record.element.parentElement?.closest('[data-ui-id]');
    if (parent && byElement.has(parent)) {
      byElement.get(parent).node.children.push(record.node);
    } else {
      roots.push(record.node);
    }
  }

  return {
    schemaVersion: 'web-to-ugui-1.0',
    viewName,
    referenceResolution: { width, height },
    coordinateSystem: 'top-left pixels',
    nodes: roots
  };
}, { ...viewport, viewName });

await mkdir(path.dirname(outputJson), { recursive: true });
await mkdir(path.dirname(outputScreenshot), { recursive: true });
await writeFile(outputJson, JSON.stringify(plan, null, 2), 'utf8');
await page.screenshot({ path: outputScreenshot, fullPage: false });
await browser.close();

console.log(`Wrote ${toProjectRelative(outputJson)}`);
console.log(`Wrote ${toProjectRelative(outputScreenshot)}`);

function resolveProjectPath(value) {
  return path.isAbsolute(value) ? path.normalize(value) : path.resolve(projectRoot, value);
}

function toProjectRelative(value) {
  return path.relative(projectRoot, value).replaceAll('\\', '/');
}

async function ensureFileExists(filePath, message) {
  try {
    await access(filePath);
  } catch {
    fail(message);
  }
}

function findProjectRoot(startPath) {
  let current = path.resolve(startPath);
  while (true) {
    if (hasUnityProjectMarkers(current)) {
      return current;
    }

    const parent = path.dirname(current);
    if (parent === current) {
      return '';
    }

    current = parent;
  }
}

function hasUnityProjectMarkers(directory) {
  return directoryExists(path.join(directory, 'Assets'))
    && directoryExists(path.join(directory, 'Packages'))
    && directoryExists(path.join(directory, 'ProjectSettings'));
}

function directoryExists(directory) {
  try {
    return statSync(directory).isDirectory();
  } catch {
    return false;
  }
}

function fail(message) {
  console.error(message);
  process.exit(1);
}
