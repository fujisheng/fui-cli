# 验证与排障

## Web→prefab 验证顺序

1. 确认设计分辨率来自原型 HTML 的显式声明，并与项目约定一致。
2. 确认 Web 原型只给 UI 节点加 `data-ui-id`。
3. 使用固定脚本 `Packages/fui-cli/Skills/fui-cli/scripts/extract-visual-ui/extract-visual-ui.mjs` 生成 `visual-ui.json`，禁止手写。
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

不能自行选择 `1920x1080`、`1080x1920` 等默认值，也不能从浏览器窗口或截图尺寸反推。

处理：先在 HTML 中补充 `<meta name="fui-design-resolution" content="1170x2532">` 或根容器 `data-design-width` / `data-design-height`。项目 `AGENTS.md` 只作为校验基线；如果 HTML 与项目约定冲突，先修正 HTML 后重新提取。

### 中间文件放错位置

Web 原型、截图和 `visual-ui.json` 必须放在项目根目录 `Temp/WebToUgui/<ViewName>/`。

不要放到 `Assets/`、`Packages/` 或 demo 输出目录。

### `border_radius_not_supported`

这是已知 warning。默认 UGUI `Image` 不精确还原 Web `border-radius`，prefab 仍可正常生成。

### 调用了未注册的工具

只调用当前源码实际注册的 FUI CLI 工具。不要凭旧经验调用未列出的旧 prefab/schema 工具。当前可用工具以 `ui.web_to_ugui_prefab` 和 `ui_*` 系列运

行态工具为准。

## 验收标准

一次完整的 fui-cli 工作流，至少应满足：

**Web→prefab 任务：**
- [ ] 设计分辨率来自原型 HTML，且与项目约定一致
- [ ] Web 原型明确区分视觉参考层和可提取 UI 层
- [ ] 中间 Web/截图/JSON 位于 `Temp/WebToUgui/`
- [ ] `visual-ui.json` 由固定提取脚本生成，没有手写或人工编辑
- [ ] 生成 prefab 前执行过 dry-run，且 `issues` 为空
- [ ] 生成的 prefab 不包含背景、角色插画、云、山体、草地等纯装饰节点

**运行时验证：**
- [ ] 目标视图能在 PlayMode 中被找到
- [ ] 关键元素、绑定和 ViewModel 状态可检查
- [ ] 关键交互可以被自动化调用
- [ ] `console` 没有隐藏错误
