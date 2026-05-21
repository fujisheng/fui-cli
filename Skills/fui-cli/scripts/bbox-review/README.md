# bbox-review

设计图资源裁切框确认工具。它把 `design-master.png` 作为 1:1 背景，把 `visual-ui.json` 的元素框叠在图上，生成可拖拽调整的 `design_visual_bbox`。

## 用法

```powershell
node Packages\fui-cli\Skills\fui-cli\scripts\bbox-review\bbox-review.mjs `
  --view LoginView `
  --design FUI-CLI\LoginView\design-master.png `
  --visual FUI-CLI\LoginView\LoginView.visual-ui.json
```

项目内常用路径：

```powershell
node Packages\fui-cli\Skills\fui-cli\scripts\bbox-review\bbox-review.mjs `
  --view LoginView `
  --design FUI-CLI\LoginView\design-master.png `
  --visual FUI-CLI\LoginView\LoginView.visual-ui.json `
  --layer-plan FUI-CLI\LoginView\layer_plan.json
```

输出：

- `FUI-CLI/<ViewName>/bbox-review-data.json`
- `FUI-CLI/<ViewName>/previews/<ViewName>.bbox-review.html`

## 规则

- `html_rect` 只表示布局、热区和初始搜索区域。
- `design_visual_bbox` 表示设计图上的真实美术边界。
- `source_crop_bbox` 默认等于确认后的 `design_visual_bbox`。
- 后续 `sources/` 裁切、`asset-manifest.json.size` 和资源拼装对齐都必须基于确认后的 `design_visual_bbox`。
- 按钮、头像框、面板、发光和阴影允许 `design_visual_bbox` 大于 `html_rect`。
- `visual-ui.json` 必须保持为原始 DOM 提取产物，禁止写入确认后的设计 bbox、新资源尺寸或新 Sprite 路径。
- 复查 bbox 时必须使用“原始 `visual-ui.json` + 已确认 bbox 的 `layer_plan.json`”。不要把 `<ViewName>.visual-ui.recut.json` 传给本工具，否则蓝色 `html_rect` 会被设计 bbox 覆盖，无法发现 HTML 布局与设计图的差异。
- 如果确认后的 bbox 需要改变 prefab 中 Image 的 rect 或 Sprite 路径，应另行生成 `<ViewName>.visual-ui.recut.json`，只给 `ui.web_to_ugui_prefab` 使用。

## 确认流程

1. 打开生成的 `bbox-review.html`。
2. 逐个检查蓝色 `html_rect` 和橙色 `design_visual_bbox`。
3. 拖动或输入数值调整橙框，确保完整包含描边、阴影、发光、圆角、装饰和透明边缘。
4. 对关键资源生成局部截图或 contact sheet 给用户确认。
5. 用户确认后，把导出的 JSON 合并进 `layer_plan.json`。
6. 只有确认后的框可以进入 `Source Crop -> imagegen -> assets_png`。
7. 如果某个框后来被修正，必须重新裁 `Source Crop`，并基于新的裁图重新生成 mask、`edit_target`、imagegen 修复图和 `asset-manifest.json`；拉伸旧 AI 图只允许作为临时验证。

## Unity 交付注意事项

- 资源复制到 `Assets/Resources/UI/<ViewName>/` 后必须刷新 Unity AssetDatabase，否则 `ui.web_to_ugui_prefab` dry-run 可能报 `sprite_not_found`。
- `ui.web_to_ugui_prefab` 必须在 Edit Mode 运行；如果返回 `wrong_mode`，先停止 Play Mode，再重新 dry-run。
