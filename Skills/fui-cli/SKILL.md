---
name: fui-cli
description: 使用 FUI CLI 与 UnityCli 进行 FUI 运行态检查、交互诊断、ViewModel/元素状态修改，以及 Web 原型到 UGUI/FUI prefab 的正式工具链工作流
license: MIT
metadata:
  clients: codex, opencode
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

**默认直接采用 imagegen 修补后的独立资源，透明资源优先原生 Alpha。** 资源拆分先从 `design-master.png` 裁出 `Source Crop` 并准备修复所需 mask、`edit_target` 和报告，再用 Codex `imagegen` 清理遮挡、文字、缺失和破边。需要透明时明确要求真实透明 PNG，而不是棋盘格图片；默认保留原始 Alpha 和画布边距，不自动扣色、裁边、收缩、羽化或 despill。透明失败时优先重新生成或编辑，必要时显式声明 chroma 兜底并记录原因。脚本必须检查实际原图与最终输出，不能把 RGBA 格式或新增透明留白当成验收通过。详细契约见 `references/asset-generation-workflow.md` 第 9.6 节及后处理脚本 README。通过校验的独立资源可以直接作为 `assets_png` 中的 Production Asset；Source-First Composition 仅用于严格锁像素、AI 跑偏或用户明确要求的回退场景。

**裁图边界必须以设计图确认为准。** HTML / `visual-ui.json` 的元素 rect 只表示运行时布局、语义和交互热区，不能直接作为最终资源裁切框或资源尺寸。进入 `Source Crop` 裁切前，必须用 `bbox-review.html` 把 `design-master.png` 作为 1:1 背景，叠加 `html_rect` 与可调整的 `design_visual_bbox`；执行者调整到完整包含描边、阴影、发光、圆角、外扩装饰和透明边缘，并让用户确认后，才允许把 `design_visual_bbox` / `source_crop_bbox` 写入 `layer_plan.json` 并进入后续 imagegen 流程。

**不要污染原始 `visual-ui.json`。** `visual-ui.json` 是固定提取脚本从 HTML DOM 生成的布局基准，只能作为 `html_rect` 和节点语义来源；禁止写入设计图 bbox、新资源尺寸或新 Sprite 路径。bbox review 必须使用原始 `visual-ui.json` 加已确认 bbox 的 `layer_plan.json`。如果确认 bbox 后确实需要改变 prefab 中 Image 的 rect 或 Sprite，必须生成派生的 `<ViewName>.visual-ui.recut.json` 给 `ui.web_to_ugui_prefab` 使用。

**bbox 改变后必须重新生成资源链路。** 一旦 `design_visual_bbox` / `source_crop_bbox` 调整，旧 `Source Crop`、mask、`edit_target`、imagegen 输出、`alphaSource` 和 `asset-manifest.json.size` 都视为过期。复用旧 AI 输出再缩放只能用于临时验证问题，不能作为最终交付资源；最终必须基于新的 `Source Crop` 重新跑 imagegen 修复和后处理。

## 文档索引

本技能的维护源是 `Packages/fui-cli/Skills/fui-cli/`；`.agents/skills/fui-cli/` 和 `.opencode/skills/fui-cli/` 是安装副本。修改流程时同步副本并校验文件哈希，不在副本中另行维护一套规则。安装可通过 `FUI/Install Skill` 完成。

按需跳文档，不要一次加载全部：

| 场景 | 文档 |
|------|------|
| ViewModel / Presenter 怎么写 | `@references/authoring-model.md` |
| Web 原型设计规范 | `@references/web-prototype-design.md` |
| Web 原型生成 Prefab 完整流程 | `@references/web-to-ugui-prefab.md` |
| 从设计图生成 UI 精灵资源 | `@references/asset-generation-workflow.md` |
| 运行时接入与 PlayMode 验证 | `@references/runtime-bootstrap.md` |
| 验证流程与排障指南 | `@references/verification-and-troubleshooting.md` |

## 生成前结构检查

Web → Prefab 必须遵守 `references/semantic-structure.md`：保留控件部件父子关系，装饰显式标记，九宫格导出单个语义 Image。结构验证失败先修 HTML，再重新提取，不能跳过检查或手改 JSON。

基础控件必须按语义结构规范的 UGUI 控件表声明部件。Slider、Toggle、InputField、Dropdown、ScrollView、Scrollbar 缺少必要角色时必须在生成前失败；禁止按名字猜测部件、自动补出重复结构或用业务脚本弥补错误层级。基础控件修改后执行 Node 结构测试与 Unity `FUI/Validate UGUI Control Structure`，确认正式 HTML 提取/预检也通过。

## 布局、列表与适配

Web → Prefab 同时遵守 `references/layout-contract.md`。固定项声明 LayoutGroup，动态记录使用 Item 模板与集合绑定；可适配区域声明锚点、安全区与尺寸归属。不能以父子结构检查或单尺寸截图代替列表和多比例 Unity 验证。
