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
  tools: unitycli.exe
---

## What I do

`fui-cli` 用来指导代理在 **FUI + fui.cli + UnityCli** 体系下完成当前已注册的 UI 工作：

- 在 PlayMode 检查已打开视图、元素、ViewModel 状态和绑定关系
- 诊断绑定、布局和文本问题
- 执行点击、输入、滑动、Toggle、Slider、Dropdown、ScrollRect、Drag 等运行态交互
- 修改 ViewModel 属性或元素 BindableProperty
- 用原子操作组合“执行 + 等待 + 读取结果”
- 从 Web 原型提取布局 JSON，并通过正式工具生成 UGUI/FUI prefab

## Hard constraint

**不要用代码绕过工具链去修改 Unity 资源。**

当前允许的资源写入入口：`ui.web_to_ugui_prefab`。它的 C# 实现位于 `Packages/com.fujisheng.fui.cli/Editor/WebVisualUiPrefabTool.cs`，只能读取固定提取脚本生成的 Web visual-ui JSON，并写入 `Assets/` 下的 `.prefab`。

## Use this skill with low context

按需跳文档，不要一次加载全部：

- ViewModel / Presenter 写法 → `@docs/authoring-model.md`
- Web 原型生成 UGUI/FUI prefab → `@docs/web-to-ugui-prefab.md`
- 运行时接入思路 → `@docs/runtime-bootstrap.md`
- 验证与排障 → `@docs/verification-and-troubleshooting.md`

## High-value rules

1. **ViewModel 用普通公共属性做绑定。** 不要把绑定字段写成 `BindableProperty<T>`。
2. **所有参与 FUI SourceGenerator 的 ViewModel 都必须是 `partial class`。**
3. **不要手写具体 `BindingContext`。** 交给 FUI SourceGenerator 生成。
4. **只调用当前源码实际注册的 FUI CLI 工具。** 不要凭旧经验调用未列出的旧 prefab/schema 工具。
5. **Web 原型只标记 UI 节点。** 背景图、角色插画、云、山体、草地等纯视觉装饰不要加 `data-ui-id`，避免进入 prefab 层级。
6. **设计分辨率未知时必须先问。** 如果用户和项目文档都没有明确分辨率，不要自行选择默认值。
7. **Web→prefab 中间产物统一放 `Temp/WebToUgui/`。** prefab 输出路径按项目资源结构决定，不能固定照搬 demo 路径。
8. **禁止手写 `visual-ui.json`。** 该 JSON 只能由固定脚本 `Packages/com.fujisheng.fui.cli/Tools/WebToUgui/extract-visual-ui.mjs` 从 Web DOM 生成；不符合预期时回改 Web 或提取脚本后重新生成。

## Minimal workflows

### Web 原型生成 prefab

```text
1. 确认设计分辨率：用户/项目约定没有时先询问
2. 没有参考图时，根据功能需求和项目风格生成原创布局
3. 拆分页面：视觉参考层不标记，真实 UI 层才加 data-ui-id
4. 在 Temp/WebToUgui/<ViewName>/ 编写 Web 原型并生成截图
5. 通过固定提取脚本生成 visual-ui JSON 到 Temp/WebToUgui/<ViewName>/，禁止手写 JSON
6. 检查 JSON：分辨率正确、关键 UI 存在、背景/角色装饰不存在
7. 调用 ui.web_to_ugui_prefab dry_run=true 检查 hierarchy/issues
8. 按项目资源结构确定 prefab_path 后 dry_run=false 写入 prefab
9. 检查 prefab 根组件、关键节点、CanvasScaler 和 console
```

### PlayMode 运行态验证

```text
1. 确认 Unity Editor 稳定：ping -> editor.status -> console
2. 进入 PlayMode 并打开目标视图
3. 用 ui_list_open_views / ui_inspect_view / ui_get_bindings 检查运行态结构
4. 用 ui_input_text / ui_click_element / 其他交互工具验证关键行为
5. 用 ui_diagnose_bindings / ui_diagnose_layout / ui_diagnose_text / console 排查问题
```

## EditMode prefab 生成工具

- `ui.web_to_ugui_prefab` - 从 Web 提取的 visual-ui JSON 生成 UGUI/FUI prefab

## Success criteria

一次当前 fui-cli 工作流，至少应满足：

- Web→prefab 任务中，Web 原型明确区分视觉参考层和可提取 UI 层
- 设计分辨率来源明确；没有明确来源时已先询问用户
- 中间 Web/截图/JSON 位于 `Temp/WebToUgui/`
- `visual-ui.json` 由固定提取脚本生成，没有手写或人工编辑
- 生成 prefab 前执行过 dry-run，且 `issues` 为空
- 生成的 prefab 不包含背景、角色插画、云、山体、草地等纯装饰节点
- 目标视图能在 PlayMode 中被找到
- 关键元素、绑定和 ViewModel 状态可检查
- 关键交互可以被自动化调用
- `console` 没有隐藏错误
