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
    --manifest Temp/WebToUgui/LoginView/asset-manifest.json

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

const manifestPath = resolveProjectPath(readArg('--manifest'));
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
const reportPath = resolveProjectPath(readArg('--report') || path.join(manifestDir, 'asset-generation-log.json'));
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
  const tempOutput = resolveAssetPath(asset.tempPath || asset.output || asset.path);
  const finalOutput = resolveAssetPath(asset.path || asset.output || asset.tempPath);

  if (!source) {
    return {
      id,
      ok: false,
      error: 'source_not_found',
      message: '找不到输入图片。请在 asset.source/rawPath/tempPath/path 中提供一个存在的路径。'
    };
  }

  if (!tempOutput) {
    return {
      id,
      ok: false,
      error: 'output_not_found',
      message: '找不到输出路径。请在 asset.tempPath 或 asset.path 中提供路径。'
    };
  }

  const size = asset.size || {};
  const width = Number(asset.width || size.width || 0);
  const height = Number(asset.height || size.height || 0);
  const alphaMode = asset.alphaMode || asset.process?.alphaMode || (asset.transparent ? 'trim' : 'keep');
  const fit = asset.fit || asset.process?.fit || 'stretch';
  const padding = Number(asset.padding || asset.process?.padding || 0);
  const alphaThreshold = Number(asset.alphaThreshold || asset.process?.alphaThreshold || 8);
  const chromaThreshold = Number(asset.chromaThreshold || asset.process?.chromaThreshold || 28);

  const commandArgs = [
    path.join(__dirname, 'postprocess_asset.py'),
    '--source', source,
    '--output', tempOutput,
    '--fit', fit,
    '--alpha-mode', alphaMode,
    '--padding', String(padding),
    '--alpha-threshold', String(alphaThreshold),
    '--chroma-threshold', String(chromaThreshold)
  ];

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
    report: postprocessReport
  };
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

  return path.join(manifestDir, 'assets_raw', path.basename(target));
}

function resolveAssetPath(value) {
  if (!value) {
    return '';
  }

  if (path.isAbsolute(value)) {
    return path.normalize(value);
  }

  return path.resolve(projectRoot, value);
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

  return path.relative(projectRoot, value).replaceAll(path.sep, '/');
}

function lastNonEmptyLine(value) {
  const lines = value.split(/\r?\n/).map((line) => line.trim()).filter(Boolean);
  return lines[lines.length - 1] || '';
}
