# process-ui-assets

对 imagegen 已生成的独立位图执行确定性后处理。默认保留原生 Alpha 与画布边距；不创作最终美术，不修改 prefab。

## 流程与边界

整体设计 → bbox 确认 → Source Crop / 修复输入 → imagegen 独立资源 → 原图验收 → 按需后处理 → 输出验收 → Web 拼装 → Unity。

透明资源优先原生 Alpha，原始输出保存在 `ai_alpha_sources/` 或等价目录；只有显式扣色兜底才使用 `ai_chroma_sources/`。不得把整页设计图作为最终 UI。bbox 改变后必须从新 Source Crop 重新生成，不得用缩放旧输出冒充最终资源。

脚本只检查像素与声明的处理参数。棋盘格伪影、浅色/深色背景边缘、小尺寸辨识度、pivot、placement offset、九宫格 border 及 Unity 实际效果仍需视觉验收。

## 使用

在 Unity 项目根目录运行（`--python` 可指定包含 Pillow 的 Python）：

```sh
node Packages/fui-cli/Skills/fui-cli/scripts/process-ui-assets/process-ui-assets.mjs \
  --manifest FUI-CLI/LoginView/asset-manifest.json --python python3
```

- `--asset <id>`：按资源筛选，可重复。
- `--dry-run`：只向 stdout 输出报告，不写资源或报告文件。
- `--report <path>`：非 dry-run 时指定报告位置。
- 默认后处理报告为 `asset-processing-report.json`；不覆盖保存 prompt、来源与重试记录的 `asset-generation-log.json`。

manifest 和报告必须位于项目根 `FUI-CLI/` 下。中间路径相对 manifest；仅 `FUI-CLI/...` 与 `Assets/...` 可用项目根相对路径。禁止绝对路径、盘符、UNC 或 `..`。用户确认资源后才配置 Unity 目标路径并执行正式复制。

## 原生透明资源

```json
{
  "assets": [{
    "id": "login_button",
    "sourceCrop": "sources/login_button.png",
    "repairedAsset": "ai_alpha_sources/login_button.ai.png",
    "file": "assets_png/login_button.png",
    "size": { "width": 772, "height": 124 },
    "transparent": true,
    "alphaSourceKind": "native",
    "alphaMode": "keep",
    "fit": "contain",
    "generationMode": "direct_repaired_asset",
    "aiEditScope": "direct_repaired_asset"
  }]
}
```

`size` 来自已确认的资源尺寸，不是 HTML 热区。示例采用 contain；只有明确需要拉伸时才选 stretch。省略 size 不缩放。确认后可添加 `path: "Assets/Resources/UI/LoginView/login_button.png"` 交付。

| 字段 | 语义 |
| --- | --- |
| `transparent` | 是否要求真实透明，布尔值，默认 false；不是“生成成功”的标记 |
| `alphaSourceKind` | 本次实际输入为 native / chroma / opaque；透明要求默认 native，其他默认 opaque；显式 chroma 模式默认 chroma |
| `alphaMode` | 默认 keep；显式 chroma 来源默认 chroma-soft；裁边必须显式 trim |
| `fit` | stretch（默认）、contain、cover、none；keep 不会禁用显式尺寸缩放 |
| `padding` | 仅显式 trim 后补透明边距；使用前确认布局和九宫格边框 |
| `alphaThreshold` | 可见 bbox / 裁边阈值，默认 8；不用于放宽真实透明验收 |

透明要求与 opaque 来源冲突时拒收；native 与 chroma 处理冲突时拒收。不根据目录、`aiChromaSource` 名称或残留的 chroma 配置自动扣色。

输入优先级：`alphaSource` → `aiAlphaSource` → `repairedAsset` → `aiChromaSource` → `source` → `rawPath` → `input`。选择第一个已声明来源；不存在则失败，不静默改用其他原图或旧产物。只有未声明来源时才依次寻找 `assets_raw/<输出名>`、`tempPath`、`path`。处理已扣色的 `alphaSource` 时声明 native，扣色历史仍保存在生成日志中。

原图在 contain/padding 前检查，避免新增留白掩盖伪透明输入。native 原图和要求透明的输出必须存在可见像素且不是全不透明；全透明图片一律拒收。RGBA 全 Alpha=255 与 RGB 一样不满足透明要求；半透明辉光不要求存在 Alpha=255 的像素。透明输出只允许 PNG / WebP。

默认不扣色、不裁边、不收缩、不羽化、不 despill。裁边后若丢失全部透明像素，应保留原画布或按设计补边距，而非关闭验收。失败不覆盖目标资源。

## 显式扣色兜底

原生透明失败时先重新生成或编辑；确需扣色时记录原因并声明实际来源：

```json
{
  "id": "fallback_icon",
  "repairedAsset": "ai_chroma_sources/icon.png",
  "file": "assets_png/icon.png",
  "transparent": true,
  "alphaSourceKind": "chroma",
  "alphaMode": "chroma-soft",
  "chroma": {
    "keyColor": "#ff00ff",
    "transparentThreshold": 48,
    "opaqueThreshold": 120,
    "maxResidueRatio": 0.001
  }
}
```

- 必须指定 `keyColor` 或显式 `autoKey: "corners" / "border"`，默认不自动采样。
- 需要裁边时选 `chroma-soft-trim` 或 `chroma-trim`。
- `edgeContract`、`edgeFeather`、`despill` 默认关闭，只能在 soft chroma 模式显式开启。
- `maxResidueRatio` 控制可见像素的残留比例，超限失败；原生透明分支不运行该检查。
- 原生透明图即使放在 chroma 命名的目录中，也不会自动扣色。

旧清单依赖隐式扣色/trim 的，需明确填写来源和处理方式；旧 chroma 配置不能与 native 输入混用。已扣色的 Alpha 源应移除本次不需要的 chroma 参数。

## Python 单图工具

```sh
python3 Packages/fui-cli/Skills/fui-cli/scripts/process-ui-assets/postprocess_asset.py \
  --source FUI-CLI/LoginView/ai_alpha_sources/button.png \
  --output FUI-CLI/LoginView/assets_png/button.png \
  --alpha-source-kind native --alpha-required --alpha-mode keep
```

单图 `--dry-run` 不写图片；显式 `--report` 可保存诊断 JSON。批处理的 dry-run 更严格，不写任何文件。报告包含 sourceMode、实际输入/输出尺寸、Alpha 极值、透明/半透明/不透明像素数、Alpha bbox、处理模式及验收结果。它不证明图片没有棋盘格或边缘污染。

## 回归测试

不需要 Unity，使用包含 Pillow 的 Python 和 Node.js；测试位图是临时诊断夹具，不是最终美术：

```sh
python3 -B -m unittest discover \
  -s Packages/fui-cli/Skills/fui-cli/scripts/process-ui-assets/tests -v
```
