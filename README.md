# FUI CLI

[![OpenCode](https://img.shields.io/badge/Built%20for-OpenCode-blue.svg)](https://opencode.ai)
[![Unity](https://img.shields.io/badge/Unity-2022.3+-black.svg?style=flat&logo=unity)](https://unity.com/)
[![License](https://img.shields.io/badge/License-MIT-green.svg)](LICENSE)

> **FUI CLI 是面向 AI Agent 的 Unity UI 自动化工具链。** 构建于 [FUI](https://github.com/fujisheng/FUI) 框架和 [UnityCli](https://github.com/fujisheng/unitycli) 之上，专为 [OpenCode](https://opencode.ai) 等 AI 编程助手设计，实现 **"一句话生成 UI，自动验证调试"** 的闭环工作流。

---

## 这是什么？

FUI CLI 不是给人类开发者手动敲命令的工具。它是 **AI Agent 的操作系统扩展** —— 让 AI 能够：

1. **🎨 自动生成 Prefab** —— 从 Web 原型（HTML/CSS）自动提取布局，生成 UGUI/FUI Prefab
2. **🔍 自动诊断调试** —— 在 PlayMode 中自动检查视图结构、绑定关系、ViewModel 状态
3. **🤖 自动交互验证** —— 模拟点击、输入、滑动等操作，验证 UI 行为是否符合预期

AI Agent 通过 `fui-cli` Skill 调用这些能力，无需人类干预即可完成从设计到验证的完整 UI 开发流程。

---

## 核心能力

### 1. Web → UGUI Prefab 自动生成

AI Agent 根据用户需求生成 Web 原型（HTML/CSS），通过固定提取脚本自动生成 `visual-ui.json`，再调用 `ui.web_to_ugui_prefab` 工具生成 Unity Prefab。

```
用户需求 → AI 生成 Web 原型 → extract-visual-ui.mjs → visual-ui.json → ui.web_to_ugui_prefab → .prefab
```

**AI 工作流示例：**

```text
1. 用户："帮我做一个登录界面，包含用户名输入框、密码输入框和登录按钮"
2. AI 生成 Web 原型到 FUI-CLI/LoginView/LoginView.html
3. AI 调用提取脚本生成 visual-ui.json
4. AI 调用 ui.web_to_ugui_prefab dry_run=true 预检
5. AI 确认无 issues 后，dry_run=false 生成 Prefab
6. AI 自动创建 ViewModel 和 Presenter 代码
```

### 2. 运行态自动诊断

AI Agent 在 PlayMode 中自动检查 UI 运行状态，发现问题立即报告：

- **视图检查** —— 列出所有已打开视图，确认视图是否正确创建
- **绑定诊断** —— 检查 ViewModel 与 UI 元素的绑定关系是否完整
- **布局诊断** —— 检测布局异常（如尺寸为 0、锚点错误等）
- **文本诊断** —— 检查文本显示问题（如空文本、字体缺失等）

### 3. 运行态自动交互

AI Agent 自动执行 UI 交互，验证功能完整性：

- **点击元素** —— 模拟按钮点击，验证导航和事件响应
- **输入文本** —— 在 InputField 中输入内容，验证数据绑定
- **列表操作** —— 支持通过 Selector 定位列表子项（`view → element → itemIndex → child`）
- **修改状态** —— 运行时修改 ViewModel 属性，验证状态变化是否正确反映到 UI

---

## 面向 AI Agent 的架构

```
┌─────────────────────────────────────────────────────────────┐
│                      AI Agent (OpenCode)                     │
│  ┌───────────────────────────────────────────────────────┐  │
│  │                    fui-cli Skill                       │  │
│  │  - Web 原型生成策略                                     │  │
│  │  - 元素选择器解析 (view/element/itemIndex/child)       │  │
│  │  - 诊断与验证工作流                                     │  │
│  │  - 自动化交互脚本                                       │  │
│  └────────────────────┬──────────────────────────────────┘  │
└───────────────────────┼─────────────────────────────────────┘
                        │ JSON-RPC over Named Pipe
                        ▼
┌─────────────────────────────────────────────────────────────┐
│                    UnityCli Bridge 层                        │
│  ┌─────────────┐  ┌──────────────┐  ┌─────────────────────┐ │
│  │ UnityCli    │  │ 工具注册表    │  │ 主线程调度器         │ │
│  │ Server      │→│ Registry     │→│ Dispatcher          │ │
│  │ (Named Pipe)│  │ (Tool扫描)   │  │ (确保线程安全)       │ │
│  └─────────────┘  └──────────────┘  └─────────────────────┘ │
└───────────────────────┬─────────────────────────────────────┘
                        │ C# 反射调用
                        ▼
┌─────────────────────────────────────────────────────────────┐
│                    FUI CLI 工具层                            │
│  ┌──────────┐ ┌──────────┐ ┌──────────┐ ┌───────────────┐  │
│  │ UIInspector│ │ UIInteraction│ │ UIRuntime  │ │ WebVisualUi   │  │
│  │ Tool     │ │ Tool     │ │ Tool     │ │ PrefabTool    │  │
│  │ (视图/绑定 │ │ (点击/输入 │ │ (VM修改   │ │ (Web→Prefab  │  │
│  │  诊断)   │ │  滑动)   │ │  元素修改)│ │  生成)       │  │
│  └──────────┘ └──────────┘ └──────────┘ └───────────────┘  │
│  ┌──────────┐ ┌──────────┐ ┌──────────┐ ┌───────────────┐  │
│  │ Atomic   │ │ Element  │ │ Element  │ │ Selector      │  │
│  │ Action   │ │ Inspector│ │ Modifier │ │ Resolver      │  │
│  │ Tool     │ │ Tool     │ │ Tool     │ │ (选择器解析)  │  │
│  └──────────┘ └──────────┘ └──────────┘ └───────────────┘  │
└───────────────────────┬─────────────────────────────────────┘
                        │
                        ▼
┌─────────────────────────────────────────────────────────────┐
│                     FUI 运行时层                             │
│  ┌──────────┐ ┌──────────┐ ┌──────────┐ ┌───────────────┐  │
│  │ UIEntity │ │ ViewModel│ │ Binding  │ │ UIManager     │  │
│  │ Registry │ │          │ │ Context  │ │               │  │
│  │ (Editor) │ │          │ │          │ │               │  │
│  └──────────┘ └──────────┘ └──────────┘ └───────────────┘  │
└─────────────────────────────────────────────────────────────┘
```

### AI Agent 如何调用

AI Agent 不直接操作 Unity Editor，而是通过 **结构化工具调用** 与 FUI CLI 交互：

```csharp
// AI 生成工具调用请求（JSON）
{
  "tool": "ui.web_to_ugui_prefab",
  "args": {
    "json_file": "FUI-CLI/LoginView/LoginView.visual-ui.json",
    "prefab_path": "Assets/Resources/UI/Prefabs/LoginView.prefab",
    "dry_run": false
  }
}

// Unity Editor 执行后返回结构化结果
{
  "success": true,
  "prefab_path": "Assets/Resources/UI/Prefabs/LoginView.prefab",
  "hierarchy": [...]
}
```

---

## AI Agent 工作流

### 工作流 1：从零生成 UI

```text
用户输入需求
    ↓
AI 从原型 HTML 显式声明确定设计分辨率
    ↓
AI 生成 Web 原型（HTML/CSS）
    ↓
AI 调用 extract-visual-ui.mjs 生成 visual-ui.json
    ↓
AI 调用 ui.web_to_ugui_prefab dry_run=true 预检
    ↓
AI 检查 issues/warnings，修正 Web 原型
    ↓
AI 调用 ui.web_to_ugui_prefab dry_run=false 生成 Prefab
    ↓
AI 生成 ViewModel + Presenter 代码
    ↓
AI 编译验证，确认无 error
    ↓
AI 进入 PlayMode，验证视图可正常打开
    ↓
AI 运行 ui_diagnose_bindings，确认绑定无问题
    ↓
完成，向用户报告结果
```

### 工作流 2：调试现有 UI

```text
用户报告 UI 问题
    ↓
AI 进入 PlayMode
    ↓
AI 调用 ui_list_open_views 确认视图状态
    ↓
AI 调用 ui_diagnose_bindings 定位绑定问题
    ↓
AI 调用 ui_inspect_element 检查具体元素
    ↓
AI 调用 ui_get_viewmodel_state 读取 VM 状态
    ↓
AI 分析根因，提出修复方案
    ↓
AI 执行修复，重新验证
    ↓
完成
```

### 工作流 3：自动化 UI 测试

```text
AI 进入 PlayMode
    ↓
AI 打开目标视图
    ↓
AI 调用 ui_click_element 点击按钮
    ↓
AI 调用 ui_input_text 输入测试数据
    ↓
AI 调用 ui_get_viewmodel_state 验证状态变化
    ↓
AI 调用 ui_diagnose_bindings 确认绑定正常
    ↓
AI 生成测试报告
```

---

## 工具清单（AI Agent 可用）

### PlayMode 诊断工具

| 工具名 | AI 用途 |
|--------|---------|
| `ui_list_open_views` | 确认视图是否正确创建和启用 |
| `ui_inspect_view` | 检查视图元素结构完整性 |
| `ui_inspect_element` | 定位并检查具体元素属性 |
| `ui_get_viewmodel_state` | 读取 VM 状态，验证数据绑定 |
| `ui_diagnose_bindings` | 自动诊断绑定关系问题 |
| `ui_diagnose_layout` | 检测布局异常 |
| `ui_diagnose_text` | 检查文本显示问题 |

### PlayMode 交互工具

| 工具名 | AI 用途 |
|--------|---------|
| `ui_click_element` | 模拟点击，验证按钮响应 |
| `ui_input_text` | 输入测试数据，验证 InputField 绑定 |
| `ui_modify_element` | 修改元素属性，测试运行时变化 |
| `ui_modify_viewmodel` | 修改 VM 属性，验证状态流转 |
| `ui_scroll_to` | 滚动到指定位置，验证 ScrollRect |

### EditMode 生成工具

| 工具名 | AI 用途 |
|--------|---------|
| `ui.web_to_ugui_prefab` | 从 Web 原型生成 UGUI/FUI Prefab |

---

## 安装

### 作为 AI Agent 工具链安装

在 Unity 项目的 `Packages/manifest.json` 中添加：

```json
{
  "dependencies": {
    "com.fujisheng.fui.cli": "https://github.com/fujisheng/fui-cli.git#main"
  }
}
```

### 前置依赖

| 依赖 | 版本 | 说明 |
|------|------|------|
| [Unity](https://unity.com/) | 2022.3+ | FUI SourceGenerator 需要 Unity 2022.3 及以上 |
| [FUI](https://github.com/fujisheng/FUI) | main | FUI 核心框架（MVVM + UGUI） |
| [UnityCli](https://github.com/fujisheng/unitycli) | main | Named Pipe 桥接层，提供 CLI ↔ Editor 通信 |
| [Node.js](https://nodejs.org/) | 18+ | Web 原型提取脚本运行时 |

---

## fui-cli Skill

FUI CLI 为 AI Agent 提供了专门的 **Skill 文档**，位于 `Skills/fui-cli/`：

| 文档 | 内容 |
|------|------|
| `SKILL.md` | Skill 元数据、核心规则、最小工作流 |
| `docs/authoring-model.md` | ViewModel/Presenter 编写规范 |
| `docs/web-to-ugui-prefab.md` | Web 原型 → Prefab 完整流程 |
| `docs/runtime-bootstrap.md` | 运行时接入和自动化前提 |
| `docs/verification-and-troubleshooting.md` | 验证顺序和常见故障处理 |

### AI Agent 核心规则

1. **ViewModel 用普通公共属性做绑定**，不要写成 `BindableProperty<T>`
2. **所有 ViewModel 必须是 `partial class`**，以便 SourceGenerator 生成绑定代码
3. **不要手写具体 `BindingContext`**，交给 FUI SourceGenerator
4. **禁止手写 `visual-ui.json`**，只能由固定提取脚本从 Web DOM 生成
5. **Web 原型只标记 UI 节点**，背景/角色/装饰不加 `data-ui-id`
6. **设计分辨率必须写在原型 HTML 中**，不从浏览器窗口、截图尺寸或默认值推断

---

## 目录结构

```
Packages/com.fujisheng.fui.cli/
├── Editor/                          # Editor 工具实现（AI 可调用的 API）
│   ├── UIInspectorTool.cs           # 视图/绑定诊断
│   ├── UIInteractionTool.cs         # 运行态交互
│   ├── UIRuntimeTool.cs             # VM/元素修改
│   ├── UIElementInspectorTool.cs    # 元素详细检查
│   ├── UIElementModifierTool.cs     # 元素属性修改
│   ├── AtomicActionTool.cs          # 原子操作组合
│   ├── InteractionExtensionTool.cs  # 扩展交互
│   ├── WebVisualUiPrefabTool.cs     # Web → Prefab 生成
│   ├── FuiElementSelectorResolver.cs # 选择器解析（支持列表子项）
│   ├── UnityCliMigrationUtilities.cs # 工具基类与辅助
│   └── FUI.Cli.asmdef               # Editor-only 程序集定义
├── Tools/
│   └── WebToUgui/
│       ├── extract-visual-ui.mjs    # Playwright 驱动的 Web DOM 提取脚本
│       └── package.json             # Node 依赖
├── Skills/
│   └── fui-cli/                     # AI Agent Skill 文档
│       ├── SKILL.md
│       └── docs/
│           ├── authoring-model.md
│           ├── web-to-ugui-prefab.md
│           ├── verification-and-troubleshooting.md
│           └── runtime-bootstrap.md
└── package.json                     # UPM 包配置
```

---

## 为什么需要这个工具？

在传统的 Unity UI 开发中，AI Agent 面临以下难题：

| 问题 | FUI CLI 解决方案 |
|------|-----------------|
| AI 无法直接操作 Unity Editor | 通过 Named Pipe 桥接，AI 调用结构化工具 |
| AI 无法验证生成的 Prefab 是否正确 | PlayMode 自动诊断，检查绑定/布局/文本 |
| AI 无法确认 UI 交互是否生效 | 自动模拟点击/输入，验证事件响应 |
| AI 无法读取运行时状态 | 直接读取 ViewModel 属性，验证数据流转 |
| AI 生成 Web 原型后无法转为 Unity 资源 | 固定提取脚本 + Prefab 生成工具，一键转换 |

**FUI CLI 让 AI Agent 具备了完整的 Unity UI 开发能力**，从设计到验证无需人类干预。

---

## 许可证

[MIT](LICENSE)
