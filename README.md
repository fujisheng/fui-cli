# FUI CLI

[![Unity](https://img.shields.io/badge/Unity-2019.4+-black.svg?style=flat&logo=unity)](https://unity.com/)
[![License](https://img.shields.io/badge/License-MIT-green.svg)](LICENSE)

> FUI CLI 是面向 [FUI](https://github.com/fujisheng/FUI) 框架的 Unity Editor CLI 扩展工具集，构建于 [UnityCli](https://github.com/fujisheng/unitycli) 之上，提供 Web 原型到 UGUI Prefab 的正式工具链、运行态诊断、交互操作和 ViewModel 状态修改能力。

---

## 特色

- **Web → UGUI Prefab 正式工具链**
  - 通过 Web 原型（HTML/CSS）提取布局 JSON，一键生成 FUI/UGUI Prefab
  - 自动区分视觉参考层（背景、角色、特效）与可提取 UI 层
  - 支持 dry-run 预检，确保生成质量

- **运行态诊断与检查**
  - 在 PlayMode 中列出已打开的视图、检查元素结构
  - 诊断绑定关系、布局问题和文本异常
  - 读取 ViewModel 属性状态，排查绑定失效根因

- **运行态交互与修改**
  - 模拟点击、输入文本、滑动、Toggle、Slider、Dropdown 等操作
  - 通过 Selector 精准定位列表子项（`view → element → itemIndex → child`）
  - 运行时修改 ViewModel 属性或元素 BindableProperty，即时验证效果

- **Editor-only，不侵入 Player**
  - 所有工具仅在 Editor 下编译和运行
  - 不影响实际打包产物，零运行时开销

---

## 依赖

| 依赖 | 版本 | 说明 |
|------|------|------|
| [Unity](https://unity.com/) | 2019.4+ | 基础引擎版本 |
| [FUI](https://github.com/fujisheng/FUI) | main | FUI 核心框架（MVVM + UGUI） |
| [UnityCli](https://github.com/fujisheng/unitycli) | main | Named Pipe 桥接层，提供 CLI ↔ Editor 通信 |
| `com.unity.nuget.mono-cecil` | 1.11.5 | Unity 内置 NuGet 包，用于 IL 处理 |
| [Node.js](https://nodejs.org/) | 18+ | Web 原型提取脚本运行时 |

---

## 使用方法

### 1. 安装

通过 Unity Package Manager 添加以下依赖：

```json
{
  "dependencies": {
    "com.fujisheng.fui.cli": "https://github.com/fujisheng/fui-cli.git#main"
  }
}
```

### 2. Web 原型生成 Prefab

#### 编写 Web 原型

在 `Temp/WebToUgui/<ViewName>/` 下创建 HTML 原型，给真实 UI 节点添加 `data-ui-id` 和 `data-ui-type`：

```html
<!-- 视觉参考层（不提取） -->
<div class="background"></div>
<div class="character"></div>

<!-- 可提取 UI 层（生成 prefab） -->
<button data-ui-id="LoginButton" data-ui-type="Button">登录</button>
<input  data-ui-id="UsernameInput" data-ui-type="InputField" />
<div    data-ui-id="TitleText" data-ui-type="Text">欢迎</div>
```

#### 提取 visual-ui JSON

```bash
node Packages/com.fujisheng.fui.cli/Tools/WebToUgui/extract-visual-ui.mjs \
  --input Temp/WebToUgui/LoginView/LoginView.html \
  --view LoginView \
  --width 1170 \
  --height 2532
```

#### dry-run 预检

```bash
'{"args":{"json_file":"Temp/WebToUgui/LoginView/LoginView.visual-ui.json","prefab_path":"Assets/Resources/UI/Prefabs/LoginView.prefab","dry_run":true}}' |
  Library/UnityCliBridge/unitycli.exe invoke --tool ui.web_to_ugui_prefab --stdin
```

#### 正式生成

```bash
'{"args":{"json_file":"Temp/WebToUgui/LoginView/LoginView.visual-ui.json","prefab_path":"Assets/Resources/UI/Prefabs/LoginView.prefab","dry_run":false}}' |
  Library/UnityCliBridge/unitycli.exe invoke --tool ui.web_to_ugui_prefab --stdin
```

### 3. 运行态诊断

```bash
# 列出已打开的视图
Library/UnityCliBridge/unitycli.exe invoke --tool ui_list_open_views --stdin

# 诊断指定视图的绑定
'{"args":{"viewName":"BagView"}}' |
  Library/UnityCliBridge/unitycli.exe invoke --tool ui_diagnose_bindings --stdin

# 读取 ViewModel 状态
'{"args":{"viewName":"BagItemView"}}' |
  Library/UnityCliBridge/unitycli.exe invoke --tool ui_get_viewmodel_state --stdin

# 检查具体元素
'{"args":{"selector":{"view":"BagView","element":"ItemGrid","itemIndex":3,"child":"ItemName"}}}' |
  Library/UnityCliBridge/unitycli.exe invoke --tool ui_inspect_element --stdin
```

### 4. 运行态交互

```bash
# 点击按钮
'{"args":{"selector":{"view":"HomeView","element":"BagButton"}}}' |
  Library/UnityCliBridge/unitycli.exe invoke --tool ui_click_element --stdin

# 输入文本
'{"args":{"selector":{"view":"LoginView","element":"UsernameInput"},"text":"admin"}}' |
  Library/UnityCliBridge/unitycli.exe invoke --tool ui_input_text --stdin

# 修改 ViewModel 属性
'{"args":{"viewName":"BagView","propertyName":"SelectedTabIndex","propertyValue":1}}' |
  Library/UnityCliBridge/unitycli.exe invoke --tool ui_modify_viewmodel --stdin
```

---

## 架构

```
┌─────────────────────────────────────────────────────────────┐
│                        外部 CLI 进程                         │
│  (PowerShell / Terminal / CI 脚本 / AI Agent)               │
└──────────────────────┬──────────────────────────────────────┘
                       │ Named Pipe (JSON-RPC)
                       ▼
┌─────────────────────────────────────────────────────────────┐
│                    UnityCli Bridge 层                        │
│  ┌─────────────┐  ┌──────────────┐  ┌─────────────────────┐ │
│  │ UnityCli    │  │ 注册表       │  │ 调度器              │ │
│  │ Server      │→│ Registry     │→│ Dispatcher          │ │
│  │ (Named Pipe)│  │ (Tool扫描)   │  │ (主线程调度)        │ │
│  └─────────────┘  └──────────────┘  └─────────────────────┘ │
└──────────────────────┬──────────────────────────────────────┘
                       │ C# 反射调用
                       ▼
┌─────────────────────────────────────────────────────────────┐
│                      FUI CLI 工具层                          │
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
└──────────────────────┬──────────────────────────────────────┘
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

### 核心组件

| 组件 | 路径 | 说明 |
|------|------|------|
| `UIInspectorTool` | `Editor/UIInspectorTool.cs` | 视图/元素/绑定检查与诊断 |
| `UIInteractionTool` | `Editor/UIInteractionTool.cs` | 运行态交互（点击、输入、滑动等） |
| `UIRuntimeTool` | `Editor/UIRuntimeTool.cs` | ViewModel 和元素属性修改 |
| `WebVisualUiPrefabTool` | `Editor/WebVisualUiPrefabTool.cs` | Web 原型到 UGUI Prefab 生成 |
| `FuiElementSelectorResolver` | `Editor/FuiElementSelectorResolver.cs` | 元素选择器解析（支持列表子项） |
| `extract-visual-ui.mjs` | `Tools/WebToUgui/extract-visual-ui.mjs` | Playwright 驱动的 Web DOM 提取脚本 |

### 通信流程

1. **CLI 发送请求** → UnityCli Bridge Server（Named Pipe）
2. **Server 路由** → 根据 `tool` 名称分发到对应 `IUnityCliTool`
3. **Dispatcher 调度** → 确保在主线程执行 Unity API 操作
4. **工具执行** → 调用 FUI 运行时 API 或生成 Prefab
5. **返回结果** → JSON 结构化响应，包含成功/失败状态和数据

---

## 工具清单

### PlayMode 工具

| 工具名 | 功能 |
|--------|------|
| `ui_list_open_views` | 列出当前所有已启用的 FUI 视图 |
| `ui_inspect_view` | 检查指定视图的元素结构 |
| `ui_inspect_element` | 检查具体元素的属性和状态 |
| `ui_get_viewmodel_state` | 读取 ViewModel 的所有绑定属性值 |
| `ui_diagnose_bindings` | 诊断视图的绑定关系问题 |
| `ui_diagnose_layout` | 诊断布局问题 |
| `ui_diagnose_text` | 诊断文本显示问题 |
| `ui_click_element` | 模拟点击元素 |
| `ui_input_text` | 在 InputField 中输入文本 |
| `ui_modify_element` | 修改元素的 BindableProperty |
| `ui_modify_viewmodel` | 修改 ViewModel 的公共属性 |
| `ui_scroll_to` | 滚动到指定位置 |

### EditMode 工具

| 工具名 | 功能 |
|--------|------|
| `ui.web_to_ugui_prefab` | 从 visual-ui JSON 生成 UGUI/FUI Prefab |

---

## 目录结构

```
Packages/com.fujisheng.fui.cli/
├── Editor/                          # Editor 工具实现
│   ├── UIInspectorTool.cs           # 视图/绑定诊断
│   ├── UIInteractionTool.cs         # 运行态交互
│   ├── UIRuntimeTool.cs             # VM/元素修改
│   ├── UIElementInspectorTool.cs    # 元素详细检查
│   ├── UIElementModifierTool.cs     # 元素属性修改
│   ├── AtomicActionTool.cs          # 原子操作组合
│   ├── InteractionExtensionTool.cs  # 扩展交互
│   ├── WebVisualUiPrefabTool.cs     # Web → Prefab 生成
│   ├── FuiElementSelectorResolver.cs # 选择器解析
│   ├── UnityCliMigrationUtilities.cs # 工具基类与辅助
│   └── FUI.Cli.asmdef               # Editor-only 程序集定义
├── Tools/
│   └── WebToUgui/
│       ├── extract-visual-ui.mjs    # Playwright 提取脚本
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

## 许可证

[MIT](LICENSE)
