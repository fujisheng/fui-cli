---
name: fui-cli
description: 使用 FUI CLI 与 UnityCli 进行 FUI 运行态检查、交互诊断、ViewModel/元素状态修改，以及 Web 原型到 UGUI/FUI prefab 的正式工具链工作流
license: MIT
compatibility: opencode
metadata:
  audience: developers
  workflow: unity-ui, fui, runtime-inspection, diagnostics, web-to-ugui-prefab
  aliases: fui cli, fui workflow, fui runtime, fui diagnostics, fui-cli, web to ugui, web prefab
  tags: unity, fui, mvvm, unitycli, diagnostics, runtime, prefab, ugui
  concepts: ViewBinding, Binding, Command, ViewModel, UIManager, WebVisualUi, UGUIPrefab
  related: unitycli
  tools: unitycli.exe, imagegen
---

## 这个技能是干什么的

`fui-cli` 指导代理在 **FUI + fui.cli + UnityCli** 体系下完成 UI 工作，覆盖两大场景：

- **Web → Prefab**：从 Web 原型提取布局 JSON，通过正式工具生成 UGUI/FUI prefab
- **运行时验证**：PlayMode 下检查视图、诊断绑定、执行交互、修改 ViewModel 状态

## 硬约束

**不要用代码绕过工具链去修改 Unity 资源。** 当前允许的资源写入入口只有 `ui.web_to_ugui_prefab`。

**不要用代码创作 UI 位图资源。** 需要生成 `design-master.png`、按钮底图、面板、图标、装饰等真实美术图片时，必须使用 Codex 的 `imagegen` 图片生成能力。代码只能用于布局提取、截图、裁剪参考图、透明度后处理、拼合预览、报告生成和 Unity 导入设置；禁止用 HTML/CSS/canvas/SVG/Python/Unity 代码绘制最终美术资源。

**不要把确认稿或设计图当整屏主视觉 Sprite。** `design-master.png` 和用户确认稿只能作为参考图，禁止作为 prefab 的整屏背景图来显示完整 UI，也禁止在其上叠透明按钮/协议区/点击热区。prefab 必须由 `asset-manifest.json` 中的独立资源图拼装；拼不准就重新生成或修正对应资源图。

**默认直接采用 imagegen 修补后的独立资源。** 资源拆分流程必须先用代码从 `design-master.png` 裁出 `Source Crop`、生成 `cutout`、`repair_mask`、`edit_target` 和报告；随后由 Codex `imagegen` 修复遮挡、文字、缺失和破边区域。需要透明的修补资源必须要求 imagegen 输出到纯色 chroma-key 背景（默认 `#ff00ff`），再由代码扣色生成 alpha。修补后的完整资源可以作为 `assets_png` 中的 Production Asset，代码只允许做尺寸对齐、alpha/透明边处理、chroma key、预览和校验。`Source Crop` 是修复参考和抠图基底，不再默认要求把 donor 局部回贴到原图；只有在需要严格锁定可见像素、AI 明显跑偏或用户明确要求时，才使用 Source-First Composition 作为回退策略。

**裁图边界必须以设计图确认为准。** HTML / `visual-ui.json` 的元素 rect 只表示运行时布局、语义和交互热区，不能直接作为最终资源裁切框或资源尺寸。进入 `Source Crop` 裁切前，必须用 `bbox-review.html` 把 `design-master.png` 作为 1:1 背景，叠加 `html_rect` 与可调整的 `design_visual_bbox`；执行者调整到完整包含描边、阴影、发光、圆角、外扩装饰和透明边缘，并让用户确认后，才允许把 `design_visual_bbox` / `source_crop_bbox` 写入 `layer_plan.json` 并进入后续 imagegen 流程。

**不要污染原始 `visual-ui.json`。** `visual-ui.json` 是固定提取脚本从 HTML DOM 生成的布局基准，只能作为 `html_rect` 和节点语义来源；禁止写入设计图 bbox、新资源尺寸或新 Sprite 路径。bbox review 必须使用原始 `visual-ui.json` 加已确认 bbox 的 `layer_plan.json`。如果确认 bbox 后确实需要改变 prefab 中 Image 的 rect 或 Sprite，必须生成派生的 `<ViewName>.visual-ui.recut.json` 给 `ui.web_to_ugui_prefab` 使用。

**bbox 改变后必须重新生成资源链路。** 一旦 `design_visual_bbox` / `source_crop_bbox` 调整，旧 `Source Crop`、mask、`edit_target`、imagegen 输出、`alphaSource` 和 `asset-manifest.json.size` 都视为过期。复用旧 AI 输出再缩放只能用于临时验证问题，不能作为最终交付资源；最终必须基于新的 `Source Crop` 重新跑 imagegen 修复和后处理。

## 文档索引

按需跳文档，不要一次加载全部：

| 场景 | 文档 |
|------|------|
| ViewModel / Presenter 怎么写 | `@references/authoring-model.md` |
| Web 原型设计规范 | `@references/web-prototype-design.md` |
| Web 原型生成 Prefab 完整流程 | `@references/web-to-ugui-prefab.md` |
| 从设计图生成 UI 精灵资源 | `@references/asset-generation-workflow.md` |
| 运行时接入与 PlayMode 验证 | `@references/runtime-bootstrap.md` |
| 验证流程与排障指南 | `@references/verification-and-troubleshooting.md` |
