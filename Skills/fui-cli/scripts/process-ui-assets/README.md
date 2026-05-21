# process-ui-assets

通用 UI 位图后处理工具，配合 `asset-manifest.json` 使用。

## 标准流程

当前资源流程默认采用：

```text
design-master.png
  -> bbox-review.html       # 在设计图上确认真实资源 bbox
  -> sources/               # 按确认后的 source_crop_bbox 裁切参考图
  -> imagegen               # 修复为完整独立 UI sprite
  -> ai_chroma_sources/     # 纯色 chroma-key 背景修复源
  -> ai_alpha_sources/      # 扣色、despill、alpha 清理后的中间源
  -> assets_png/            # 正式游戏可用资源
  -> Assets/Resources/UI/<ViewName>/
```

`Source Crop` 只作为修复参考和校验输入，且必须来自用户确认后的 `design_visual_bbox` / `source_crop_bbox`。通过校验的 imagegen 完整修复资源可以用 `generationMode: "direct_repaired_asset"` 直接进入 `assets_png/`；`source_first_patch_only` 只作为 AI 跑偏、strict 锁像素或用户明确要求时的回退路线。

如果 `design_visual_bbox` / `source_crop_bbox` 被修正，必须重新裁出 `Source Crop` 并重新生成 imagegen 修复图、`alphaSource` 和 `asset-manifest.json.size`。本工具可以把旧修复图缩放到新尺寸用于临时验证，但这种结果不能作为最终交付资源。

## 边界

- 只处理 `imagegen` 已生成的独立 PNG。
- 可以做透明处理、裁透明边、缩放、复制和报告。
- 不创建最终美术。
- 不负责把旧 AI 输出升级为新 bbox 的最终资源；bbox 改变后的最终美术必须重新 imagegen。
- 不把 `design-master.png` 当整屏 UI。
- 不直接修改 Unity prefab，prefab 仍由 `ui.web_to_ugui_prefab` 生成。

## 用法

```powershell
node Packages\fui-cli\Skills\fui-cli\scripts\process-ui-assets\process-ui-assets.mjs `
  --manifest FUI-CLI\LoginView\asset-manifest.json
```

只验证不写文件：

```powershell
node Packages\fui-cli\Skills\fui-cli\scripts\process-ui-assets\process-ui-assets.mjs `
  --manifest FUI-CLI\LoginView\asset-manifest.json `
  --dry-run
```

处理单个资源：

```powershell
node Packages\fui-cli\Skills\fui-cli\scripts\process-ui-assets\process-ui-assets.mjs `
  --manifest FUI-CLI\LoginView\asset-manifest.json `
  --asset WechatLoginButton
```

## manifest 字段

兼容当前 `asset-manifest.json`：

`asset-manifest.json` 和报告文件必须位于项目根目录 `FUI-CLI/` 下。`file`、`sourceCrop`、`repairedAsset`、`alphaSource` 等中间产物路径推荐写成相对 `asset-manifest.json` 所在目录的路径，例如 `assets_png/button.png`；`path` 这类 Unity 交付路径仍写项目根相对路径，例如 `Assets/Resources/UI/<ViewName>/button.png`。manifest 路径禁止使用绝对路径、盘符、UNC 路径或 `..` 逃逸；项目根相对路径只允许 `FUI-CLI/...` 和 `Assets/...` 两类。

```json
{
  "assets": [
    {
      "id": "login_wechat_button",
      "file": "assets_png/login_wechat_button_772x124.png",
      "path": "Assets/Resources/UI/Login/Elements/login_wechat_button_772x124.png",
      "sourceCrop": "sources/login_wechat_button.source.png",
      "repairedAsset": "ai_chroma_sources/login_wechat_button.ai.png",
      "alphaSource": "ai_alpha_sources/login_wechat_button.alpha.png",
      "usedBy": ["WechatLoginButton"],
      "size": { "width": 772, "height": 124 },
      "transparent": true,
      "fit": "stretch",
      "generationMode": "direct_repaired_asset",
      "aiEditScope": "direct_repaired_asset",
      "compositionPolicy": "direct_repaired_asset",
      "chroma": {
        "keyColor": "#ff00ff",
        "autoKey": "border",
        "transparentThreshold": 48,
        "opaqueThreshold": 120,
        "edgeContract": 1,
        "edgeFeather": 1,
        "despill": true,
        "maxChromaResidueRatio": 0.001
      }
    }
  ]
}
```

输入图查找顺序：

1. `alphaSource`
2. `aiAlphaSource`
3. `repairedAsset`
4. `aiChromaSource`
5. `source`
6. `rawPath`
7. `input`
8. `assets_raw/<输出文件名>`
9. `tempPath`
10. `path`

`alphaMode` 可显式配置；未配置时按来源推断：

- `transparent: false` -> `keep`
- `alphaSource` / `aiAlphaSource` -> `trim`
- `aiChromaSource` 或带 `chroma` 配置的 `repairedAsset` -> `chroma-soft-trim`
- 其他 `repairedAsset` -> `trim`
- 其他透明资源 -> `trim`

`chroma` 支持字段：

- `keyColor`：固定 key color，例如 `#ff00ff`。
- `autoKey`：`corners`、`border` 或 `none`。
- `transparentThreshold` / `opaqueThreshold`：扣色透明和不透明阈值。
- `edgeContract` / `edgeFeather`：alpha 边缘收缩和羽化。
- `despill`：清理边缘 key color 污染。
- `maxChromaResidueRatio`：允许残留 key color 像素占比，超过则失败。

输出顺序：

1. 写入 `file` 或 `tempPath`
2. 如果 `path` 不同，再复制到 `path`

## Python 单图工具

```powershell
python Packages\fui-cli\Skills\fui-cli\scripts\process-ui-assets\postprocess_asset.py `
  --source FUI-CLI\LoginView\assets_raw\button.png `
  --output FUI-CLI\LoginView\assets\button.png `
  --width 772 `
  --height 124 `
  --fit stretch `
  --alpha-mode chroma-soft-trim `
  --chroma-key-color "#ff00ff" `
  --chroma-auto-key border `
  --transparent-threshold 48 `
  --opaque-threshold 120 `
  --edge-contract 1 `
  --edge-feather 1 `
  --despill `
  --max-chroma-residue-ratio 0.001
```
