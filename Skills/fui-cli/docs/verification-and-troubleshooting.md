# 验证与排障

## Web→prefab 验证顺序

1. 确认设计分辨率来自用户或项目约定；未知时先询问。
2. 确认 Web 原型只给 UI 节点加 `data-ui-id`。
3. 使用固定脚本 `Packages/com.fujisheng.fui.cli/Tools/WebToUgui/extract-visual-ui.mjs` 生成 `visual-ui.json`，禁止手写。
4. 将 Web、截图、`visual-ui.json` 放到 `Temp/WebToUgui/<ViewName>/`。
5. 调用 `ui.web_to_ugui_prefab` 且 `dry_run=true`。
6. 检查 `issues`、`warnings`、`hierarchy`。
7. 按项目资源结构确定 prefab 路径后调用 `dry_run=false`。
8. 检查 prefab 分辨率、关键节点和 `console`。

## 常见故障

### visual-ui.json 被手写或手改

这是不允许的。`visual-ui.json` 必须由固定提取脚本从 Web DOM 生成。

处理：删除手写 JSON，修正 Web HTML/CSS 或提取脚本，然后重新运行固定提取流程。

### Web 背景或角色被生成进 prefab

原因通常是这些 DOM 节点带了 `data-ui-id`。

处理：

1. 从背景图、角色插画、云、山体、草地、粒子等纯视觉节点上移除 `data-ui-id`。
2. 重新提取 `visual-ui.json`。
3. 用 `dry_run=true` 确认 `hierarchy` 只包含 UI 节点。

### 设计分辨率不明确

不能自行选择 `1920x1080`、`1080x1920` 等默认值。

处理：先检查用户输入和项目 `AGENTS.md`。仍无明确约定时，询问用户确认设计分辨率后再生成。

### 中间文件放错位置

Web 原型、截图和 `visual-ui.json` 必须放在项目根目录 `Temp/WebToUgui/<ViewName>/`。

不要放到 `Assets/`、`Packages/` 或 demo 输出目录。

### `border_radius_not_supported`

这是已知 warning。默认 UGUI `Image` 不精确还原 Web `border-radius`，prefab 仍可正常生成。
