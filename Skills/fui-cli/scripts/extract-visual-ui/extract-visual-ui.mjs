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

const readOptionalPositiveIntegerArg = (name, description) => {
  const rawValue = readArg(name).trim();
  if (!rawValue) {
    return 0;
  }

  const value = Number.parseInt(rawValue, 10);
  if (!Number.isFinite(value) || value <= 0 || `${value}` !== rawValue) {
    fail(`${name} 必须是大于 0 的整数，当前值：${rawValue}。`);
  }

  return value;
};

const showHelp = () => {
  console.log(`FUI WebToUgui visual-ui 提取器

用法：
   node Packages/fui-cli/Skills/fui-cli/scripts/extract-visual-ui/extract-visual-ui.mjs \\
    --input Temp/WebToUgui/MobaHomeView/MobaHomeView.html \\
    --view MobaHomeView

必填：
  --input   Web 原型 HTML 路径，项目相对路径或绝对路径
  --view    View 名称，用于 JSON viewName 和默认输出目录

HTML 必填：
  原型 HTML 必须声明设计分辨率，推荐二选一：
  <meta name="fui-design-resolution" content="1170x2532">
  <div data-design-width="1170" data-design-height="2532">

可选：
  --width   兼容旧原型的设计宽度；如果 HTML 已声明，必须与 HTML 一致
  --height  兼容旧原型的设计高度；如果 HTML 已声明，必须与 HTML 一致
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
const cliResolution = readCliResolution();
const headed = hasFlag('--headed');
const outputJson = resolveProjectPath(readArg('--json') || `Temp/WebToUgui/${viewName}/${viewName}.visual-ui.json`);
const outputScreenshot = resolveProjectPath(readArg('--png') || `Temp/WebToUgui/${viewName}/${viewName}.web.png`);

await ensureFileExists(inputHtml, `Web 原型 HTML 不存在: ${toProjectRelative(inputHtml)}。`);

const browser = await chromium.launch({ headless: !headed });
const page = await browser.newPage({ deviceScaleFactor: 1 });

await page.goto(pathToFileURL(inputHtml).href);
await page.waitForLoadState('load');

const htmlResolution = await page.evaluate((currentViewName) => {
  const parsePositiveInteger = (value) => {
    const normalized = `${value || ''}`.trim();
    const parsed = Number.parseInt(normalized, 10);
    return Number.isFinite(parsed) && parsed > 0 && `${parsed}` === normalized ? parsed : 0;
  };

  const parseResolutionText = (value, source) => {
    const match = `${value || ''}`.trim().match(/^(\d+)\s*(?:x|X|×|\*)\s*(\d+)$/);
    if (!match) {
      return null;
    }

    return {
      width: parsePositiveInteger(match[1]),
      height: parsePositiveInteger(match[2]),
      source
    };
  };

  const parseDataResolution = (element, source) => {
    if (!element) {
      return null;
    }

    const width = parsePositiveInteger(element.getAttribute('data-design-width'));
    const height = parsePositiveInteger(element.getAttribute('data-design-height'));
    return width > 0 && height > 0 ? { width, height, source } : null;
  };

  const parseViewportResolution = () => {
    const content = document.querySelector('meta[name="viewport"]')?.getAttribute('content') || '';
    const parts = Object.fromEntries(content.split(',')
      .map((part) => part.trim().split('=').map((value) => value.trim()))
      .filter((part) => part.length === 2));
    const width = parsePositiveInteger(parts.width);
    const height = parsePositiveInteger(parts.height);
    return width > 0 && height > 0 ? { width, height, source: 'meta[name="viewport"]' } : null;
  };

  const rootByView = Array.from(document.querySelectorAll('[data-ui-id]'))
    .find((element) => element.getAttribute('data-ui-id') === currentViewName);
  const firstDataHolder = document.querySelector('[data-design-width][data-design-height]');
  const candidates = [
    parseResolutionText(document.querySelector('meta[name="fui-design-resolution"]')?.getAttribute('content'), 'meta[name="fui-design-resolution"]'),
    parseDataResolution(document.documentElement, 'html[data-design-*]'),
    parseDataResolution(document.body, 'body[data-design-*]'),
    parseDataResolution(rootByView, `[data-ui-id="${currentViewName}"][data-design-*]`),
    parseDataResolution(firstDataHolder, '[data-design-width][data-design-height]'),
    parseViewportResolution()
  ];

  return candidates.find(Boolean) || null;
}, viewName);

const viewport = resolveViewport(htmlResolution, cliResolution);
await page.setViewportSize(viewport);
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
      case 'panel':
        return 'Container';
      case 'scrollview':
        return 'ScrollView';
      case 'listview':
      case 'grid':
        return 'ListView';
      case 'template':
        return 'Template';
      case 'slider':
        return 'SliderElement';
      case 'dropdown':
        return 'DropdownElement';
      case 'scrollbar':
        return 'ScrollbarElement';
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
          sprite: element.dataset.uiSprite || '',
          imageType: element.dataset.imageType || 'simple',
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
          alignment: style.textAlign || 'left',
          overflow: element.dataset.textOverflow || '',
          truncate: element.dataset.textTruncate || '',
          bestFit: element.dataset.textBestFit || ''
        },
        list: {
          layout: element.dataset.listLayout || '',
          binding: element.dataset.listBinding || '',
          itemView: element.dataset.itemView || '',
          rowView: element.dataset.rowView || '',
          scrollDirection: element.dataset.scrollDirection || '',
          scrollMovement: element.dataset.scrollMovement || '',
          scrollInertia: element.dataset.scrollInertia || '',
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
        slider: {
          minValue: readNumberData('sliderMinValue', 0),
          maxValue: readNumberData('sliderMaxValue', 1),
          value: readNumberData('sliderValue', 0),
          direction: element.dataset.sliderDirection || 'leftToRight',
          wholeNumbers: element.dataset.sliderWholeNumbers || 'false'
        },
        dropdown: {
          options: element.dataset.dropdownOptions || '',
          value: readIntegerData('dropdownValue', 0)
        },
        scrollbar: {
          direction: element.dataset.scrollbarDirection || 'vertical',
          size: readNumberData('scrollbarSize', 60),
          value: readNumberData('scrollbarValue', 0)
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

function readCliResolution() {
  const width = readOptionalPositiveIntegerArg('--width', '设计分辨率宽度');
  const height = readOptionalPositiveIntegerArg('--height', '设计分辨率高度');
  if ((width > 0 && height <= 0) || (width <= 0 && height > 0)) {
    fail('--width 与 --height 必须同时提供。');
  }

  return width > 0 && height > 0 ? { width, height, source: 'command line' } : null;
}

function resolveViewport(htmlResolution, cliResolution) {
  if (htmlResolution && cliResolution) {
    if (htmlResolution.width !== cliResolution.width || htmlResolution.height !== cliResolution.height) {
      fail(`HTML 设计分辨率 ${htmlResolution.width}x${htmlResolution.height}（${htmlResolution.source}）与命令行 ${cliResolution.width}x${cliResolution.height} 不一致。`);
    }

    return { width: htmlResolution.width, height: htmlResolution.height };
  }

  if (htmlResolution) {
    return { width: htmlResolution.width, height: htmlResolution.height };
  }

  if (cliResolution) {
    console.warn('HTML 未声明设计分辨率，当前仅使用 --width/--height 兼容旧原型。请在 HTML 中补充 fui-design-resolution 或 data-design-width/data-design-height。');
    return { width: cliResolution.width, height: cliResolution.height };
  }

  fail('HTML 未声明设计分辨率。请添加 <meta name="fui-design-resolution" content="1170x2532"> 或 data-design-width/data-design-height。');
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
