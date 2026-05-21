#!/usr/bin/env node
import { copyFile, mkdir, readFile, writeFile } from 'node:fs/promises';
import { existsSync } from 'node:fs';
import path from 'node:path';
import { fileURLToPath } from 'node:url';
import { spawn } from 'node:child_process';

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

const fail = (message) => {
  console.error(message);
  process.exit(1);
};

const showHelp = () => {
  console.log(`FUI UI asset post-process pipeline

Usage:
  node Packages/fui-cli/Skills/fui-cli/scripts/process-ui-assets/process-ui-assets.mjs \\
    --manifest FUI-CLI/LoginView/asset-manifest.json

Options:
  --manifest <path>     Required asset-manifest.json path
  --asset <id>          Optional asset id/name/path filter, repeatable
  --python <command>    Python executable, default: python
  --report <path>       Output report path
  --dry-run             Validate and report without writing files
  --help                Show this help

Rules:
  This tool only post-processes bitmap files created by imagegen.
  It does not create final art, does not use a full-screen design as UI, and does not edit Unity prefabs.
  Use alphaSource/repairedAsset/aiChromaSource to adopt imagegen repaired assets into assets_png.
`);
};

if (hasFlag('--help') || hasFlag('-h')) {
  showHelp();
  process.exit(0);
}

const projectRoot = findProjectRoot(process.cwd()) || findProjectRoot(__dirname);
if (!projectRoot) {
  fail('无法定位 Unity 项目根目录，请在包含 Assets/Packages/ProjectSettings 的项目内执行。');
}

const manifestPath = resolveManifestPath(readArg('--manifest'));
if (!manifestPath) {
  fail('缺少必填参数 --manifest。');
}

if (!existsSync(manifestPath)) {
  fail(`manifest 不存在：${toProjectRelative(manifestPath)}`);
}

const pythonCommand = readArg('--python') || 'python';
const dryRun = hasFlag('--dry-run');
const assetFilters = readRepeatedArg('--asset');
const manifestDir = path.dirname(manifestPath);
const reportPath = resolveReportPath(readArg('--report') || path.join(manifestDir, 'asset-generation-log.json'));
const manifest = JSON.parse(await readFile(manifestPath, 'utf8'));
const assets = Array.isArray(manifest.assets) ? manifest.assets : [];

if (assets.length === 0) {
  fail(`manifest.assets 为空：${toProjectRelative(manifestPath)}`);
}

const selectedAssets = assets
  .filter((asset) => matchesFilters(asset, assetFilters))
  .sort((left, right) => Number(left.priority || 0) - Number(right.priority || 0));

if (selectedAssets.length === 0) {
  fail(`没有匹配 --asset 条件的资源。`);
}

const results = [];
for (const asset of selectedAssets) {
  const result = await processAsset(asset);
  results.push(result);
}

const report = {
  manifest: toProjectRelative(manifestPath),
  dryRun,
  processedCount: results.length,
  ok: results.every((item) => item.ok),
  results
};

await mkdir(path.dirname(reportPath), { recursive: true });
await writeFile(reportPath, `${JSON.stringify(report, null, 2)}\n`, 'utf8');

console.log(JSON.stringify(report, null, 2));
process.exit(report.ok ? 0 : 1);

async function processAsset(asset) {
  const id = resolveAssetId(asset);
  const source = resolveAssetSource(asset);
  const tempOutput = resolveAssetPath(asset.file || asset.tempPath || asset.output || asset.path);
  const finalOutput = resolveAssetPath(asset.path || asset.output || asset.tempPath || asset.file);

  if (!source) {
    return {
      id,
      ok: false,
      error: 'source_not_found',
      message: '找不到输入图片。请在 asset.alphaSource/repairedAsset/aiChromaSource/source/rawPath/tempPath/path 中提供一个存在的路径。'
    };
  }

  if (!tempOutput) {
    return {
      id,
      ok: false,
      error: 'output_not_found',
      message: '找不到输出路径。请在 asset.file、asset.tempPath 或 asset.path 中提供路径。'
    };
  }

  const size = asset.size || {};
  const width = Number(asset.width || size.width || 0);
  const height = Number(asset.height || size.height || 0);
  const alphaMode = resolveAlphaMode(asset);
  const fit = asset.fit || asset.process?.fit || 'stretch';
  const padding = Number(asset.padding || asset.process?.padding || 0);
  const alphaThreshold = Number(asset.alphaThreshold || asset.process?.alphaThreshold || 8);
  const chromaThreshold = Number(asset.chromaThreshold || asset.process?.chromaThreshold || 28);
  const chroma = asset.chroma || asset.process?.chroma || {};
  const chromaKeyColor = asset.chromaKeyColor || chroma.keyColor || '';
  const chromaAutoKey = asset.chromaAutoKey || chroma.autoKey || (asset.aiChromaSource ? 'border' : 'corners');
  const transparentThreshold = Number(asset.transparentThreshold || chroma.transparentThreshold || 18);
  const opaqueThreshold = Number(asset.opaqueThreshold || chroma.opaqueThreshold || 180);
  const edgeContract = Number(asset.edgeContract || chroma.edgeContract || 0);
  const edgeFeather = Number(asset.edgeFeather || chroma.edgeFeather || 0);
  const despill = Boolean(asset.despill || chroma.despill || alphaMode.startsWith('chroma-soft'));
  const maxChromaResidueRatio = Number(
    asset.maxChromaResidueRatio
    ?? chroma.maxResidueRatio
    ?? asset.process?.maxChromaResidueRatio
    ?? -1
  );

  const commandArgs = [
    path.join(__dirname, 'postprocess_asset.py'),
    '--source', source,
    '--output', tempOutput,
    '--fit', fit,
    '--alpha-mode', alphaMode,
    '--padding', String(padding),
    '--alpha-threshold', String(alphaThreshold),
    '--chroma-threshold', String(chromaThreshold),
    '--chroma-auto-key', chromaAutoKey,
    '--transparent-threshold', String(transparentThreshold),
    '--opaque-threshold', String(opaqueThreshold),
    '--edge-contract', String(edgeContract),
    '--edge-feather', String(edgeFeather),
    '--max-chroma-residue-ratio', String(maxChromaResidueRatio)
  ];

  if (chromaKeyColor) {
    commandArgs.push('--chroma-key-color', chromaKeyColor);
  }

  if (despill) {
    commandArgs.push('--despill');
  }

  if (width > 0 && height > 0) {
    commandArgs.push('--width', String(width), '--height', String(height));
  }

  if (dryRun) {
    commandArgs.push('--dry-run');
  }

  const child = await runProcess(pythonCommand, commandArgs);
  if (child.code !== 0) {
    return {
      id,
      ok: false,
      error: 'postprocess_failed',
      source: toProjectRelative(source),
      tempPath: toProjectRelative(tempOutput),
      path: finalOutput ? toProjectRelative(finalOutput) : '',
      stderr: child.stderr.trim(),
      stdout: child.stdout.trim()
    };
  }

  let postprocessReport = {};
  try {
    postprocessReport = JSON.parse(lastNonEmptyLine(child.stdout));
  } catch {
    postprocessReport = { raw: child.stdout.trim() };
  }

  if (!dryRun && finalOutput && path.resolve(finalOutput) !== path.resolve(tempOutput)) {
    await mkdir(path.dirname(finalOutput), { recursive: true });
    await copyFile(tempOutput, finalOutput);
  }

  return {
    id,
    ok: true,
    source: toProjectRelative(source),
    tempPath: toProjectRelative(tempOutput),
    path: finalOutput ? toProjectRelative(finalOutput) : '',
    fit,
    alphaMode,
    generationMode: asset.generationMode || '',
    aiEditScope: asset.aiEditScope || '',
    repairedAsset: asset.repairedAsset ? toProjectRelative(resolveAssetPath(asset.repairedAsset)) : '',
    alphaSource: asset.alphaSource ? toProjectRelative(resolveAssetPath(asset.alphaSource)) : '',
    report: postprocessReport
  };
}

function resolveAlphaMode(asset) {
  if (asset.alphaMode || asset.process?.alphaMode) {
    return asset.alphaMode || asset.process.alphaMode;
  }

  if (!asset.transparent) {
    return 'keep';
  }

  if (asset.alphaSource || asset.aiAlphaSource) {
    return 'trim';
  }

  if (asset.aiChromaSource || isChromaRepairedAsset(asset)) {
    return 'chroma-soft-trim';
  }

  return 'trim';
}

function isChromaRepairedAsset(asset) {
  if (!asset.repairedAsset) {
    return false;
  }

  if (asset.chroma || asset.chromaKeyColor) {
    return true;
  }

  return `${asset.repairedAsset}`.replaceAll('\\', '/').includes('ai_chroma_sources/');
}

function runProcess(command, commandArgs) {
  return new Promise((resolve) => {
    const child = spawn(command, commandArgs, {
      cwd: projectRoot
    });

    let stdout = '';
    let stderr = '';
    child.stdout.on('data', (chunk) => {
      stdout += chunk.toString();
    });
    child.stderr.on('data', (chunk) => {
      stderr += chunk.toString();
    });
    child.on('close', (code) => {
      resolve({ code, stdout, stderr });
    });
  });
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

function matchesFilters(asset, filters) {
  if (filters.length === 0) {
    return true;
  }

  const haystack = [
    resolveAssetId(asset),
    asset.path || '',
    asset.tempPath || '',
    ...(Array.isArray(asset.usedBy) ? asset.usedBy : [])
  ].map((value) => `${value}`.toLowerCase());

  return filters.some((filter) => {
    const normalized = filter.toLowerCase();
    return haystack.some((value) => value.includes(normalized));
  });
}

function resolveAssetId(asset) {
  if (asset.id) {
    return asset.id;
  }

  if (Array.isArray(asset.usedBy) && asset.usedBy.length > 0) {
    return asset.usedBy.join(',');
  }

  return path.basename(asset.path || asset.tempPath || asset.source || 'asset');
}

function resolveAssetSource(asset) {
  const candidates = [
    asset.alphaSource,
    asset.aiAlphaSource,
    asset.repairedAsset,
    asset.aiChromaSource,
    asset.source,
    asset.rawPath,
    asset.input,
    inferRawAssetPath(asset),
    asset.tempPath,
    asset.path
  ];

  for (const candidate of candidates) {
    const resolved = resolveAssetPath(candidate);
    if (resolved && existsSync(resolved)) {
      return resolved;
    }
  }

  return '';
}

function inferRawAssetPath(asset) {
  const target = asset.path || asset.tempPath || '';
  if (!target) {
    return '';
  }

  return path.join('assets_raw', path.basename(target));
}

function resolveAssetPath(value) {
  if (!value) {
    return '';
  }

  const raw = `${value}`.trim();
  if (!raw) {
    return '';
  }

  const normalized = raw.replaceAll('\\', '/');
  rejectUnsafeRelativePath(normalized);

  if (isUnityAssetPath(normalized)) {
    return resolveContainedPath(projectRoot, normalized, path.join(projectRoot, 'Assets'));
  }

  if (isFuiCliPath(normalized)) {
    return resolveContainedPath(projectRoot, normalized, path.join(projectRoot, 'FUI-CLI'));
  }

  return resolveContainedPath(manifestDir, normalized, manifestDir);
}

function rejectUnsafeRelativePath(value) {
  if (value.includes('\0')) {
    fail(`manifest 路径包含非法字符：${value}`);
  }

  if (path.isAbsolute(value) || /^[A-Za-z]:/.test(value) || value.startsWith('//')) {
    fail(`manifest 路径必须使用项目相对路径，禁止绝对路径：${value}`);
  }

  if (value.split('/').some((part) => part === '..')) {
    fail(`manifest 路径禁止使用 .. 逃逸目录：${value}`);
  }
}

function resolveContainedPath(baseDir, relativePath, allowedRoot) {
  const resolved = path.resolve(baseDir, relativePath);
  if (!isPathInside(resolved, allowedRoot)) {
    fail(`manifest 路径越界：${relativePath}`);
  }

  return resolved;
}

function isPathInside(filePath, directory) {
  const relative = path.relative(directory, filePath);
  return relative === '' || (!relative.startsWith('..') && !path.isAbsolute(relative));
}

function isUnityAssetPath(value) {
  return value === 'Assets' || value.startsWith('Assets/');
}

function isFuiCliPath(value) {
  return value === 'FUI-CLI' || value.startsWith('FUI-CLI/');
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

function resolveManifestPath(value) {
  const resolved = resolveProjectPath(value);
  if (resolved && !isPathInside(resolved, path.join(projectRoot, 'FUI-CLI'))) {
    fail(`manifest 必须位于项目根目录 FUI-CLI/ 下：${toProjectRelative(resolved)}`);
  }

  return resolved;
}

function resolveReportPath(value) {
  const resolved = resolveProjectPath(value);
  if (resolved && !isPathInside(resolved, path.join(projectRoot, 'FUI-CLI'))) {
    fail(`报告必须写入项目根目录 FUI-CLI/ 下：${toProjectRelative(resolved)}`);
  }

  return resolved;
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

  return path.relative(projectRoot, value).replaceAll(path.sep, '/');
}

function lastNonEmptyLine(value) {
  const lines = value.split(/\r?\n/).map((line) => line.trim()).filter(Boolean);
  return lines[lines.length - 1] || '';
}
