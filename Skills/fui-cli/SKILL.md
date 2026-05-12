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
