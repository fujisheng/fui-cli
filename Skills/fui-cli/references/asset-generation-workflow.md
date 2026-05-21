# SPEC: FUI 设计图资源化与 Web 拼装校验

Status: Accepted
Version: 2.5
Last Updated: 2026-05-15
Owner: fui-cli workflow

## 1. Intent

本文定义 `fui-cli` 中从设计图生成 Unity UI 图片资源的正式流程。

本流程的目标是把用户确认的完整 UI 设计图拆成游戏可用的独立 PNG 资源，并用这些资源重新拼出 Web 预览，证明后续 prefab 不是整屏图加透明热区。

本流程明确不生成 PSD，也不以 PSD 作为校验对象。

资源生成默认采用“`Source Crop` 作为修复参考 → Codex `imagegen` 生成干净独立资源 → chroma-key/alpha/尺寸后处理 → `assets_png/` 直接采用修复结果”的路线。`Source Crop` 不再默认作为最终像素主体；它用于锁定形状、风格、比例、遮挡关系、mask 和验收对照。

### 1.1 Result

完成后 MUST 得到：

- `assets_png/`：最终游戏可用 PNG 资源。
- `sources/`：从 `design-master.png` 裁出的资源像素基底和参考图。
- `bbox-review-data.json`：在设计图上确认过的资源真实视觉边界。
- `previews/<ViewName>.bbox-review.html`：以 `design-master.png` 为 1:1 背景的 bbox 可视化确认页。
- `debug/*_visible_mask.png` / `debug/*_repair_mask.png`：可见锁定区与允许修复区。
- `ai_chroma_sources/`：Codex `imagegen` 输出的纯色 chroma-key 修复源。
- `ai_alpha_sources/`：从 chroma-key 修复源扣色得到的 alpha 源；若透明资源直接使用 true-alpha 输出，也放在这里。
- `previews/<ViewName>.resource-preview.html`：只引用 `assets_png/` 的 Web 拼装预览。
- `previews/web-composited.png`：浏览器渲染 Web 拼装预览后的截图。
- `previews/web-vs-master-diff.png`：Web 拼装结果与 `design-master.png` 的对比图。
- `previews/coordinate_compare.png`：资源 bbox 与目标位置对比图。
- `previews/web-validation.json`：包含逐资源相似度和 Web 拼装校验的机器可读验证报告。
- `asset-manifest.json`：Unity 资源交付清单。
- `<ViewName>.visual-ui.recut.json`：可选的 prefab 生成派生文件；只在确认后的资源 bbox 需要改变运行时 Image 尺寸或 Sprite 路径时生成，禁止替代原始 `visual-ui.json`。

用户确认 Web 拼装结果后，资源才可以复制到 `Assets/Resources/UI/<ViewName>/` 并交给 `ui.web_to_ugui_prefab`。

### 1.2 Requirement Language

- `MUST`：必须满足，否则产物不合格。
- `SHOULD`：默认应满足；若偏离，必须有明确原因并写入报告。
- `MAY`：可选能力，不影响基础合格性。

## 2. Scope

### 2.1 In Scope

- 单个 FUI/UGUI View 的 UI 图片资源生成。
- 从 Web 原型提取 `visual-ui.json`。
- 生成或接收用户确认的 `design-master.png`。
- 生成资源拆分规划 `layer_plan.json`。
- 生成 `bbox-review.html`，在设计图上确认真实资源 bbox。
- 生成资源拆分确认图 `extraction_plan_overlay.png`。
- 从设计图裁切 `sources/` 参考图。
- 使用 Codex `imagegen` 修复、补绘或生成独立 UI 资源。
- 对 imagegen 输出做 chroma key、alpha 清理、bbox 对齐和尺寸校验。
- 生成只由 `assets_png/` 资源拼出的 Web 预览和验证报告。
- 用户确认后复制资源到 `Assets/Resources/UI/<ViewName>/`。
- 设置 Unity Sprite Importer，并运行 `ui.web_to_ugui_prefab` dry-run 与正式生成。

### 2.2 Out of Scope

- 不生成、导出、回读或校验 PSD。
- 不使用整屏设计图作为 prefab 主视觉。
- 不使用透明点击热区覆盖整屏主视觉来冒充真实 UI。
- 不用 HTML/CSS/canvas/SVG/Python/Unity 代码创作最终美术位图。
- 不自动决定所有资源拆分粒度；`layer_plan.json` 仍需要人工或执行者确认。

## 3. Non-Negotiable Rules

### 3.1 Final Bitmap Art MUST Use imagegen

以下产物 MUST 通过 Codex `imagegen` 创建或改图：

- `design-master.png`
- 背景、面板、卡片底图
- 按钮底图与状态变体
- 图标、装饰、分隔线、高光条
- 被遮挡区域的补绘版本
- 重新生成的失败版本

禁止用以下方式创作最终美术图：

- HTML / CSS / canvas / SVG
- Python / Pillow / ImageMagick / Unity 代码
- 纯色块、渐变、占位矢量冒充真实资源
- imagegen 不可用时自动退回代码绘制

代码只允许做确定性处理：

- 从 HTML 提取 `visual-ui.json`
- 截取 Web 原型布局参考图
- 从 `design-master.png` 裁剪局部参考图
- 对 imagegen 输出做透明度处理、尺寸校验、bbox、拼合预览和 diff 报告
- 复制已确认资源并设置 Unity Sprite Importer

如果当前会话没有可用的 Codex `imagegen`，资源生成流程 MUST 暂停并说明阻塞原因。

### 3.2 Source Crop Is The Repair Reference

从 `design-master.png` 裁出的 `Source Crop` 不是最终资源，而是修复前的主要参考、抠图基底和校验输入。

规则：

- `Source Crop` 的裁切框 MUST 来自已确认的 `design_visual_bbox` / `source_crop_bbox`，不得直接使用 HTML 元素 rect。
- 每个独立图片资源 MUST 先生成 `Source Crop`，即使后续会直接采用 imagegen 修复结果。
- 代码 MUST 生成或确认 `visible_mask`、`repair_mask`、`edit_target`；被遮挡资源还 SHOULD 生成 `occluder_mask` 与 `hole_source`。
- `repair_mask` 用于告诉 imagegen 哪些区域需要清理、补绘或重建；它是修复指令，不再默认意味着最终只取 mask 内像素。
- 默认最终资源 SHOULD 采用 `direct_repaired_asset`：imagegen 输出的完整独立 sprite 经过 alpha、尺寸、bbox 和残留校验后写入 `assets_png/`。
- 如果 `Source Crop` 已能满足独立资源要求，流程 MAY 采用 `source_crop_exact_png` 或 `source_crop_alpha_png`，但必须在 `asset-manifest.json` 记录采用原因。
- 如果 imagegen 输出明显偏离 `Source Crop` 的形状、颜色、材质或文字，产物 MUST fail；应重新生成或回退到 Source-First Composition。

### 3.3 Strategy Routing Is Mandatory

资源生成前 MUST 先按资源语义选择处理策略。策略用于约束 prompt、透明处理和验收标准；它不再禁止所有资源都进入 imagegen 修复，只要求每个资源都记录采用原因和 `ai_edit_scope`。

默认决策表：

| Resource Type                       | Default Strategy                                       | imagegen Role                                                |
| ----------------------------------- | ------------------------------------------------------ | ------------------------------------------------------------ |
| 背景                                | `direct_repaired_asset`                              | 生成不含前景 UI、普通文字和透明热区的独立背景。              |
| 标题 Logo / 艺术字                  | `direct_repaired_asset` 或 `source_crop_alpha_png` | 默认可用 imagegen 清边和重建材质；文字必须逐字验收。         |
| 麻将牌装饰 / 固定装饰               | `direct_repaired_asset` 或 `source_crop_alpha_png` | 可用 imagegen 输出干净透明 sprite；必须保留主体语义。        |
| 登录面板 / 弹窗底板                 | `direct_repaired_asset`                              | 清掉按钮、输入框、文字、头像等运行时内容，输出完整空白底板。 |
| 按钮底图                            | `direct_repaired_asset`                              | 清掉运行时文字；按需求保留或移除图标。                       |
| imagegen 失败或出现棋盘格伪影的资源 | `source_first_patch_only`                            | 回退到原图抠出 + 局部确定性修复，不强行使用 AI 输出。        |
| 普通文本 / 动态图标                 | `runtime_text` / `runtime_icon`                    | 默认不用；正式游戏里由 UI Text/Icon 替换。                   |
| 明确允许重新设计的资源              | `full_redraw_allowed`                                | 用户显式批准时允许更大幅度重绘。                             |

策略规则：

- `direct_repaired_asset` MUST 记录 `sourceCrop`、`repairMask` / `editTarget`、`repairedAsset`、prompt 摘要和采用原因。
- `source_crop_exact_png` 和 `source_crop_alpha_png` MAY 以原图像素为主体，但不得含运行时文字、按钮残影或背景污染。
- `source_first_patch_only` 只作为回退策略，用于 AI 输出局部可用但整体偏离时。
- `full_redraw_allowed` MUST 在 `layer_plan.json` 中显式声明，并写明批准原因；普通 direct repaired asset 不需要默认打开它。
- 如果 imagegen 输出比 `Source Crop` 更偏离原图，MUST 回退到 source-crop 路线。

### 3.4 imagegen Output Is The Default Repaired Asset

默认情况下，imagegen 修复后的完整独立资源可以直接进入 `assets_png/`，前提是经过尺寸、alpha、来源和预览校验。

direct repaired asset 的采用条件：

- 它来自当前资源的 `Source Crop`、`repair_mask` / `editTarget` 和明确 prompt。
- 它是单个独立 sprite，不是整屏 UI 截图，也不包含不属于该资源的运行时文本或交互控件。
- 透明资源必须先输出到纯色 chroma-key 背景或 true-alpha，再做 alpha 清理、despill、bbox 和残留校验。
- 文件已经按目标 bbox 尺寸对齐，并写入 `assets_png/`。
- `asset-manifest.json`、`layer_plan.json` 或 `asset-generation-log.json` 记录 `generationMode: "direct_repaired_asset"` 和 `aiEditScope: "direct_repaired_asset"`。

只有以下情况 SHOULD 使用 Source-First Composition：

- 用户明确要求严格锁定可见像素。
- AI 输出整体偏离，但局部补洞可用。
- 文本、标志或图标必须 1:1 保真，direct 输出无法稳定达标。

### 3.5 Web Layout Is The Runtime Layout Source

设计分辨率 MUST 由 HTML 显式声明，提取脚本写入 `visual-ui.json.referenceResolution`。

推荐写法：

```html
<meta name="fui-design-resolution" content="1170x2532">
```

根容器建议同步声明：

```html
<div data-ui-id="GameView" data-ui-type="Container"
     data-design-width="1170" data-design-height="2532"
     style="position: relative; width: 1170px; height: 2532px;">
```

规则：

- `html`、`body`、根容器尺寸 MUST 与声明分辨率一致。
- `visual-ui.json` MUST 由固定提取脚本从 Web DOM 生成。
- `visual-ui.json` 是不可污染的提取产物，MUST NOT 写入 `design_visual_bbox`、`source_crop_bbox` 或新资源尺寸。
- `bbox-review.html` MUST 使用原始 `visual-ui.json` 作为蓝色 `html_rect` 来源，并从 `layer_plan.json` 读取已确认的 `design_visual_bbox`。禁止把派生的 recut JSON 再输入 bbox review，否则蓝框会变成设计框，失去对比意义。
- 如果确认后的 `design_visual_bbox` 需要改变 prefab 中 Image 的 rect 或 Sprite 路径，MAY 从原始 `visual-ui.json` 生成 `<ViewName>.visual-ui.recut.json`。该文件只用于 `ui.web_to_ugui_prefab` dry-run/正式生成，不能回写原始 `visual-ui.json`。
- 禁止从浏览器窗口大小、截图尺寸、CSS 缩放结果反推分辨率。
- 禁止把 `layout.json` 或其他手写文件变成第二套布局真相。
- HTML 元素 rect 只代表运行时布局、语义和交互热区参考，MUST NOT 直接作为最终资源裁切框或 `asset-manifest.json.size` 的来源。

### 3.6 Design BBox Review Is Mandatory

进入资源裁切前，MUST 以 `design-master.png` 为准确认每个独立资源的真实视觉边界。

规则：

- MUST 生成 `previews/<ViewName>.bbox-review.html`，并以设计图原始分辨率 1:1 显示，不得缩放或 CSS transform。
- review 页 MUST 同时显示 `html_rect` 和 `design_visual_bbox`：`html_rect` 用于布局/热区参考，`design_visual_bbox` 用于裁图。
- 按钮、头像框、面板、发光、阴影、描边、外扩装饰的 `design_visual_bbox` MAY 大于 `html_rect`。
- 执行者 MUST 根据视觉对比调整 `design_visual_bbox`，直到完整包含资源真实边界。
- 用户确认 bbox review 后，才允许写入 `layer_plan.json` 并进入 `sources/` 裁切。
- 如果用户指出裁切不完整，必须回到 bbox review 阶段修正，而不是在 imagegen 或后处理阶段补救。
- 一旦 `design_visual_bbox` / `source_crop_bbox` 改变，旧 `Source Crop`、mask、`edit_target`、imagegen 输出和 `asset-manifest.json.size` 全部视为过期，MUST 从新 bbox 重新生成。
- 将旧 `repairedAsset` / `alphaSource` 拉伸到新尺寸只允许作为快速验证或问题定位手段，MUST NOT 作为最终交付资源；最终资源必须基于新的 `Source Crop` 重新执行 imagegen 修复与后处理。

### 3.7 Temporary Outputs Stay Outside Assets

生成阶段 MUST 只写项目根目录 `FUI-CLI/<ViewName>/`。

用户确认、验证通过、Unity 导入规则明确后，才允许复制到 `Assets/Resources/UI/<ViewName>/`。

### 3.8 Runtime Text Is Not Baked By Default

普通文案、数字、价格、倒计时、动态奖励数值 MUST 由 UGUI/FUI 文本节点渲染。

只有以下内容 MAY 进入图片资源：

- logo
- 特殊艺术字
- 不可拆分纹样
- 用户明确要求且不需要运行时编辑的位图文字

任何位图文字 MUST 在 `layer_plan.json` 和 `asset-manifest.json` 中标记 `text_mode: "bitmap"` 或 `textPolicy: "bitmapAllowed"`。

### 3.9 No Full-Screen Hotzone Shortcut

`design-master.png`、用户确认稿、`web-composited.png` 和 diff 图都只能作为参考或验证产物。

禁止以下做法：

- 把确认稿或 `design-master.png` 复制到 `Assets/Resources/UI/<ViewName>/` 当作完整 UI 背景。
- 用一张整屏 UI 截图显示所有视觉，再叠加透明按钮、协议区或点击热区。
- 因字体、标题书法、卡片内部图与确认稿不一致，就改用整屏主视觉图绕过资源拆分。
- 将包含按钮、标题、价格、协议文案、卡片内容的整屏图登记进 `asset-manifest.json`。

正确处理：

- 哪个局部不一致，就重新生成或修正哪个资源图。
- 背景可以是全屏背景资源，但不得包含可交互控件、普通文案或完整 UI 截图。
- Web 预览必须由独立资源图和运行时文本拼装。

## 4. Concepts

| Term                                | Meaning                                                                                                                           |
| ----------------------------------- | --------------------------------------------------------------------------------------------------------------------------------- |
| `Web Prototype`                   | 用于表达布局、节点语义和设计分辨率的 HTML 原型。                                                                                  |
| `visual-ui.json`                  | 由固定提取脚本从 Web DOM 生成的布局数据，禁止手写。                                                                               |
| `<ViewName>.visual-ui.recut.json` | 从原始 `visual-ui.json` 派生出的 prefab 生成输入；只用于采用确认后的设计 bbox 尺寸和新 Sprite 路径，不能作为 bbox review 输入。 |
| `design-master.png`               | 用户确认过的完整 UI 设计图，是视觉对齐基准。                                                                                      |
| `layer_plan.json`                 | 资源拆分、运行时用途、遮挡关系和补绘需求的规划契约。                                                                              |
| `extraction_plan_overlay.png`     | 在设计图上标注拟拆元素、bbox、z-order 和补绘要求的确认图。                                                                        |
| `BBox Review HTML`                | 以 `design-master.png` 为 1:1 背景、可调整资源真实视觉边界的确认页。                                                            |
| `html_rect`                       | 从 HTML /`visual-ui.json` 得到的布局框或交互热区参考，不代表最终资源边界。                                                      |
| `design_visual_bbox`              | 在 `design-master.png` 上确认的资源真实美术边界，是裁图和资源尺寸依据。                                                         |
| `source_crop_bbox`                | 实际裁出 `Source Crop` 的 bbox，默认等于确认后的 `design_visual_bbox`。                                                       |
| `hit_rect`                        | 运行时点击或交互热区，可与 `design_visual_bbox` 不同。                                                                          |
| `placement_offset`                | `design_visual_bbox` 相对 `html_rect` 的偏移，用于拼装和 Unity 对齐。                                                         |
| `Source Crop`                     | 从 `design-master.png` 裁出的元素参考图，用于锁定形状、风格、比例、mask 和验收对照；不再默认作为最终像素主体。                  |
| `Visible Mask`                    | 标记 `Source Crop` 中已可见且应锁定保留的像素区域。                                                                             |
| `Repair Mask`                     | 标记允许 imagegen 或后处理修改的遮挡、缺失、破边、透明边缘区域。                                                                  |
| `Occluder Mask`                   | 由上层按钮、输入框、文字、图标等遮挡物区域合并得到的 mask。                                                                       |
| `Hole Source`                     | 把 `Source Crop` 中 `Occluder Mask` 覆盖区域掏空后的修复输入。                                                                |
| `Direct Repaired Asset`           | imagegen 输出的完整独立 sprite，经 chroma-key、alpha、bbox、尺寸和来源校验后直接进入 `assets_png/`。                            |
| `Chroma Source`                   | imagegen 输出的纯色背景修复源，默认保存到 `ai_chroma_sources/`。                                                                |
| `Alpha Source`                    | 从 chroma source 扣色、despill 和 alpha 清理后的中间图，默认保存到 `ai_alpha_sources/`。                                        |
| `Patch Donor`                     | Source-First 回退路线中使用的局部补丁图，只能按 `repair_mask` 局部取用。                                                        |
| `Source-First Composition`        | 以 `Source Crop` 为主体，将 donor 的修复区域局部合成回去的回退生成方式。                                                        |
| `Locked Pixels`                   | `Visible Mask` 覆盖的源图像素；仅在 strict / source-first 路线中必须覆盖回最终资源。                                            |
| `Production Asset`                | 最终放入 `assets_png/` 的游戏可用 PNG。                                                                                         |
| `AI Edit Scope`                   | 单个资源允许 AI 修改的范围，例如 `direct_repaired_asset`、`repair_mask_only` 或 `full_redraw_allowed`。                     |
| `Asset Similarity`                | 对 `Source Crop` 与 `Production Asset` 的逐资源保真校验，先于 Web 整体拼装验收。                                              |
| `asset-manifest.json`             | 记录资源与 Unity 目标路径、UI 节点、Image 类型和导入参数的交付清单。                                                              |
| `Resource Preview HTML`           | 只使用 `assets_png/` 中图片资源拼装出的 Web 预览副本。                                                                          |
| `web-composited.png`              | 对 `Resource Preview HTML` 截图得到的资源拼装结果。                                                                             |
| `web-validation.json`             | 机器可读校验报告。                                                                                                                |
| `Runtime Text`                    | 进入 Unity 后由 FUI/UGUI 文本节点渲染的文案，不烘焙进图片资源。                                                                   |
| `Alpha BBox`                      | 基于透明度计算出的非透明像素包围盒。                                                                                              |
| `Chroma Key`                      | 用纯色背景生成元素，再扣除纯色生成透明 PNG 的方法。                                                                               |

## 5. Inputs And Outputs

### 5.1 Required Inputs

- `FUI-CLI/<ViewName>/<ViewName>.html`
- `FUI-CLI/<ViewName>/<ViewName>.visual-ui.json`
- `FUI-CLI/<ViewName>/design-master.png`
- 目标画布尺寸，且必须与 Web 原型声明分辨率一致，例如 `1170x2532`

### 5.2 Recommended Inputs

- 用户需求和项目风格说明。
- 哪些元素可点击、会变状态、可复用或包含动态内容。
- 哪些普通文字必须保持运行时可编辑。
- 已有项目资源或 `_StyleReference` 可复用资源。
- 用户给出的拆分偏好或已有 `layer_plan.json` 草稿。
- 资源命名规则、状态命名规则和 Sprite border 规则。

### 5.3 Working Directory Contract

最终交付 MUST 采用以下目录结构或等价超集：

```text
FUI-CLI/<ViewName>/
├── <ViewName>.html
├── <ViewName>.web.png
├── <ViewName>.visual-ui.json
├── <ViewName>.visual-ui.recut.json      # 可选派生文件，只用于 prefab 生成
├── design-master.png
├── style-tokens.json
├── layer_plan.json
├── bbox-review-data.json
├── extraction_plan_overlay.png
├── asset-manifest.json
├── asset-generation-log.json
├── sources/
├── asset_requests/
├── ai_chroma_sources/
├── ai_alpha_sources/
├── assets_png/
├── debug/
└── previews/
    ├── <ViewName>.bbox-review.html
    ├── <ViewName>.resource-preview.html
    ├── web-composited.png
    ├── web-vs-master-diff.png
    ├── coordinate_compare.png
    └── web-validation.json
```

目录规则：

- `FUI-CLI/<ViewName>/` 是单个 View 的中间产物根目录；`assets_png/`、`sources/`、`debug/`、`previews/` 等相对路径都相对该目录书写。
- `asset-manifest.json`、`asset-generation-log.json` 和其它报告文件 MUST 写在 `FUI-CLI/<ViewName>/` 或其子目录内。
- manifest 中间产物路径禁止使用绝对路径、盘符、UNC 路径或 `..` 逃逸；项目根相对路径只允许 `FUI-CLI/...` 和最终交付用的 `Assets/...`。
- `assets_png/` MUST 只包含最终游戏可用资源。
- `bbox-review-data.json` MUST 记录已确认或待确认的 `html_rect`、`design_visual_bbox`、`source_crop_bbox`、`hit_rect` 和 `placement_offset`。
- `sources/` MUST 只包含从 `design-master.png` 按 `source_crop_bbox` 裁出的 `Source Crop`，并作为修复参考、mask 基底和校验输入。
- `asset_requests/` SHOULD 保存每个资源提交给 imagegen 前的 source、hole、mask、edit target 和 prompt。
- `ai_chroma_sources/` MUST 保存 imagegen 生成或修复后的纯色背景图。
- `ai_alpha_sources/` SHOULD 保存 chroma-key 扣色、despill 和 alpha 清理后的中间图。
- `debug/` SHOULD 保存 `visible_mask`、`repair_mask`、`occluder_mask`、`hole_source`、bbox 报告、逐资源相似度和失败样本。
- `previews/` MUST 只保存 Web 拼装确认和校验产物。
- `previews/<ViewName>.bbox-review.html` MUST 只用于裁切规划确认，不得作为最终 UI 图层。
- `design-master.png`、`web-composited.png`、`web-vs-master-diff.png`、`coordinate_compare.png` MUST NOT 被复制为 Unity UI Sprite。

### 5.4 Unity Output

用户确认后复制到：

```text
Assets/Resources/UI/<ViewName>/
```

复制后 MUST 设置 Sprite Importer，并通过 `ui.web_to_ugui_prefab` dry-run。

## 6. Workflow Phases

```text
Phase A: Baseline
  Web Prototype + visual-ui.json + design-master.png

Phase B: Planning
  bbox-review.html + bbox-review-data.json + layer_plan.json + extraction_plan_overlay.png

Phase C: Asset Production
  sources/ + masks + asset_requests/ + ai_chroma_sources/ + ai_alpha_sources/ + direct repaired assets + assets_png/ + asset-manifest.json

Phase D: Web Verification
  resource-preview.html + web-composited.png + diff + coordinate_compare + web-validation.json

Phase E: Unity Handoff
  Assets/Resources/UI/<ViewName>/ + Sprite Importer + ui.web_to_ugui_prefab
```

### 6.1 Phase A: Baseline

MUST 完成：

1. 固定 Web 原型。
2. 用固定提取脚本生成 `visual-ui.json`。
3. 固定 `design-master.png`。
4. 校验 HTML 声明分辨率、`visual-ui.json.referenceResolution`、`design-master.png` 尺寸一致。

### 6.2 Phase B: Planning

MUST 完成：

1. 按游戏 UI 语义盘点元素。
2. 分析遮挡关系。
3. 判断资源粒度、状态资源、运行时文本和复用资源。
4. 生成 `previews/<ViewName>.bbox-review.html`，在 `design-master.png` 上叠加 `html_rect` 和可调整的 `design_visual_bbox`。
5. 根据设计图视觉对比调整每个 `design_visual_bbox`，生成 `bbox-review-data.json`。
6. 用户确认 bbox review 中的裁切框。
7. 根据已确认 bbox 生成 `layer_plan.json`。
8. 生成 `extraction_plan_overlay.png`。
9. 用户或执行者确认拆分粒度。

进入 Phase C 前，交互元素、状态变化元素、复用元素、动态内容区域、被遮挡但需独立使用的元素 MUST 都已明确处理策略。
进入 Phase C 前，`layer_plan.json` 中的 `design_visual_bbox` 和 `source_crop_bbox` MUST 来自已确认的 `bbox-review-data.json`，不得直接采用 `html_rect`。

### 6.3 Phase C: Asset Production

MUST 完成：

1. 按已确认的 `source_crop_bbox` 从 `design-master.png` 裁出 `sources/`，每个独立资源都必须有对应 `Source Crop`。
2. 根据资源类型选择 `asset_strategy`，明确每个资源是 `direct_repaired_asset`、source-crop-only 还是回退策略。
3. 为每个资源生成或确认 `visible_mask`、`repair_mask`；被遮挡资源还必须生成 `occluder_mask` 与 `hole_source`。
4. 根据 `asset_strategy` 判断哪些资源可走 source-crop-only，哪些必须进入 imagegen 修复。
5. 需要 AI 的资源生成完整干净独立 sprite，并写入 `ai_chroma_sources/` 或等价目录。
6. 对透明修复源执行 chroma key、despill、alpha 清理、bbox 和尺寸对齐，必要时写入 `ai_alpha_sources/`。
7. 默认将通过校验的完整修复资源作为 `direct_repaired_asset` 写入 `assets_png/`。
8. 仅在 strict 锁像素、AI 输出明显跑偏或用户明确要求时，执行 Source-First Composition 回退。
9. 执行逐资源校验：尺寸、alpha、chroma-key 残留、runtime text 是否误烤、背景是否含完整 UI。
10. 生成或更新 `asset-manifest.json`。
11. 记录重试、失败原因、mask 路径、修复源路径、alpha 源路径、prompt 摘要和校验结果到 `asset-generation-log.json`。

进入 Phase D 前，`asset-manifest.json` 中所有正式资源 MUST 引用 `assets_png/`，不得引用旧目录、确认稿、Web 拼合预览图或 diff 图。

### 6.4 Phase D: Web Verification

MUST 完成：

1. 生成 `previews/<ViewName>.resource-preview.html`。
2. 截图生成 `previews/web-composited.png`。
3. 生成 `previews/web-vs-master-diff.png`。
4. 生成 `previews/coordinate_compare.png`。
5. 生成 `previews/web-validation.json`。
6. 人工检查最终 Web 拼装效果。

进入 Phase E 前，`web-validation.json` MUST 没有阻断问题，用户 MUST 确认 Web 拼装结果。

### 6.5 Phase E: Unity Handoff

MUST 完成：

1. 如果确认 bbox 改变了 Image rect 或 Sprite 路径，从原始 `visual-ui.json` 生成 `<ViewName>.visual-ui.recut.json`；不要修改原始 `visual-ui.json`。
2. 复制已确认资源到 `Assets/Resources/UI/<ViewName>/`。
3. 确认 Unity Editor 处于 Edit Mode；`ui.web_to_ugui_prefab` 在 Play Mode 下会失败。
4. 刷新并导入 Unity 资源；新 PNG 写入后 MUST 执行 AssetDatabase refresh，避免 dry-run 报 `sprite_not_found`。
5. 设置 Sprite Importer，尤其是 sliced border。
6. 使用原始 `visual-ui.json` 或派生的 `<ViewName>.visual-ui.recut.json` 执行 `ui.web_to_ugui_prefab` dry-run。
7. dry-run 无 error 后执行正式 prefab 生成。
8. Unity Console 无 error 后才视为完成。

## 7. Contract Files

### 7.1 layer_plan.json

`layer_plan.json` MUST 在裁切之前生成，并在用户或执行者确认后才能进入资源生成。

推荐结构：

```json
{
  "viewName": "LoginView",
  "canvas": {
    "width": 1170,
    "height": 2532
  },
  "source": {
    "webPrototype": "LoginView.html",
    "visualUi": "LoginView.visual-ui.json",
    "masterImage": "design-master.png"
  },
  "items": [
    {
      "id": "login_panel",
      "type": "container",
      "runtime_role": "panel_background",
      "z_order": 20,
      "used_by": ["LoginPanel"],
      "interactive": false,
      "stateful": false,
      "html_rect": [126, 620, 916, 1068],
      "design_visual_bbox": [118, 590, 934, 1150],
      "source_crop_bbox": [118, 590, 934, 1150],
      "hit_rect": [126, 620, 916, 1068],
      "placement_offset": [-8, -30],
      "visible_bbox": [126, 620, 1042, 1688],
      "full_bbox_estimate": [118, 590, 1052, 1740],
      "target_bbox": [118, 590, 1052, 1740],
      "occluded_by": ["phone_input", "password_input", "login_button"],
      "occludes": ["background"],
      "asset_strategy": "direct_repaired_asset",
      "preserve_visible_pixels": false,
      "source_crop": "sources/login_panel_source.png",
      "occluder_mask": "debug/login_panel_occluder_mask.png",
      "hole_source": "debug/login_panel_hole_source.png",
      "visible_mask": "debug/login_panel_visible_mask.png",
      "repair_mask": "debug/login_panel_repair_mask.png",
      "edit_target": "asset_requests/login_panel/edit_target.png",
      "repaired_asset": "ai_chroma_sources/login_panel.ai.png",
      "alpha_source": "ai_alpha_sources/login_panel.alpha.png",
      "composition_policy": "direct_repaired_asset",
      "ai_edit_scope": "direct_repaired_asset",
      "similarity_policy": "normal",
      "repair_required": true,
      "alpha_required": true,
      "text_mode": "none",
      "notes": "Panel must be reconstructed behind controls."
    }
  ]
}
```

字段要求：

- 本节新增的 `html_rect`、`design_visual_bbox`、`source_crop_bbox`、`hit_rect` 均使用 `[x, y, width, height]`，坐标基于 `design-master.png` 左上角。
- `id` MUST 与资源名、预览图层名和 manifest 项稳定关联。
- `type` MUST 使用语义分类：`background`、`container`、`control`、`icon`、`text`、`decoration`、`state_asset`。
- `runtime_role` SHOULD 描述该元素在游戏中的用途。
- `z_order` MUST 表示从背景到前景的显示顺序。
- `used_by` SHOULD 对应 `visual-ui.json` 中的 `data-ui-id`。
- `html_rect` MUST 记录来自 `visual-ui.json` 的布局/热区参考框；它不能作为裁图依据。
- `design_visual_bbox` MUST 记录在 `bbox-review.html` 中基于 `design-master.png` 确认的真实美术边界。
- `source_crop_bbox` MUST 记录实际裁出 `Source Crop` 的 bbox；默认等于 `design_visual_bbox`。
- `hit_rect` SHOULD 记录运行时交互热区；可与 `design_visual_bbox` 不同。
- `placement_offset` SHOULD 记录 `design_visual_bbox` 相对 `html_rect` 的偏移，用于 Web/Unity 拼装对齐。
- `visible_bbox` MUST 表示原图中实际可见区域。
- `full_bbox_estimate` SHOULD 表示资源补完整后的目标范围。
- `target_bbox` SHOULD 表示最终 alpha bbox 在画布中的目标位置。
- `occluded_by` / `occludes` MUST 描述遮挡关系。
- `asset_strategy` MUST 明确处理策略。
- `preserve_visible_pixels` 在 `source_first_patch_only` 路线中 SHOULD 为 `true`；在 `direct_repaired_asset` 路线中可为 `false` 或省略。
- `source_crop` MUST 指向该资源从 `design-master.png` 按 `source_crop_bbox` 裁出的源图。
- `occluder_mask` SHOULD 指向上层遮挡物合成 mask；`occluded_by` 非空且需要补绘时 MUST 存在。
- `hole_source` SHOULD 指向被遮挡区域掏空后的 source；`occluded_by` 非空且需要 imagegen 清理时 MUST 存在。
- `visible_mask` SHOULD 指向锁定可见像素 mask；没有 mask 时必须说明如何确定锁定区域。
- `repair_mask` SHOULD 指向允许补绘或改动的 mask；`repair_required: true` 时 MUST 存在。
- `edit_target` SHOULD 指向提交给 imagegen 的修复输入图。
- `repaired_asset` SHOULD 指向 imagegen 修复输出；透明资源通常位于 `ai_chroma_sources/`。
- `alpha_source` SHOULD 指向 chroma-key 扣色后的 alpha 源；没有透明需求时可省略。
- `patch_donor` 只在 Source-First 回退路线中使用；它不能替代 `repaired_asset`。
- `composition_policy` MUST 使用 `direct_repaired_asset`、`source_first_patch_only`、`source_crop_only`、`full_redraw_allowed` 之一。
- `ai_edit_scope` MUST 使用 `none`、`direct_repaired_asset`、`repair_mask_only`、`visible_edge_cleanup`、`full_redraw_allowed` 之一。
- `similarity_policy` MUST 使用 `strict`、`normal`、`loose`、`skip_with_reason` 之一；`skip_with_reason` 必须在 `notes` 写明理由。
- `repair_required` MUST 标记是否必须补绘。
- `alpha_required` MUST 标记是否需要透明 PNG。
- `text_mode` MUST 使用 `none`、`bitmap`、`runtime_text` 之一。

`asset_strategy` 推荐值：

- `source_crop_exact_png`
- `source_crop_alpha_png`
- `source_crop_with_repaired_edge`
- `background_underlay_repair`
- `direct_repaired_asset`
- `source_first_patch_only`
- `full_redraw_allowed`
- `background_merged`
- `runtime_text`
- `runtime_icon`
- `merge_to_parent`
- `skip`

### 7.2 extraction_plan_overlay.png

`extraction_plan_overlay.png` 是资源拆分确认图。

该图 SHOULD 包含：

- 每个拟拆元素的 bbox。
- 每个元素的 `id`。
- 每个元素的 `z_order`。
- 需要补绘的元素标记。
- 合并到背景或父级的元素标记。
- 跳过不提取的元素标记。

`extraction_plan_overlay.png` 不能替代正式资源，也不能作为 Unity UI Sprite。

### 7.3 asset-manifest.json

`asset-manifest.json` 用于把最终资源交付给 Unity，不得替代 `layer_plan.json`。

推荐结构：

```json
{
  "viewName": "LoginView",
  "designMaster": "design-master.png",
  "layerPlan": "layer_plan.json",
  "referenceResolution": {
    "width": 1170,
    "height": 2532
  },
  "assets": [
    {
      "id": "login_button_normal",
      "layerPlanId": "login_button",
      "file": "assets_png/login_button_normal.png",
      "path": "Assets/Resources/UI/LoginView/login_button_normal.png",
      "usedBy": ["LoginButton"],
      "element": "ButtonElement",
      "size": {
        "width": 480,
        "height": 132
      },
      "htmlRect": [345, 1520, 460, 112],
      "designVisualBBox": [333, 1508, 504, 150],
      "sourceCropBBox": [333, 1508, 504, 150],
      "hitRect": [345, 1520, 460, 112],
      "placementOffset": [-12, -12],
      "imageType": "sliced",
      "spriteBorder": [48, 36, 48, 36],
      "reuseKey": "login.button.normal",
      "variantOf": "login_button_pressed",
      "textPolicy": "noText",
      "priority": 2,
      "transparent": true,
      "alphaMode": "trim",
      "generationMode": "direct_repaired_asset",
      "sourceCrop": "sources/login_button_source.png",
      "repairedAsset": "ai_chroma_sources/login_button_normal.ai.png",
      "alphaSource": "ai_alpha_sources/login_button_normal.alpha.png",
      "compositionPolicy": "direct_repaired_asset",
      "aiEditScope": "direct_repaired_asset",
      "fullRedrawAllowed": false,
      "target_bbox": [345, 1520, 825, 1652]
    }
  ]
}
```

字段要求：

- `file` MUST 指向当前 View 中间产物目录下的 `assets_png/` 文件；推荐写成相对 `asset-manifest.json` 的路径，例如 `assets_png/login_button_normal.png`。
- `path` MUST 指向用户确认后复制到 `Assets/Resources/UI/<ViewName>/` 的目标路径。
- `usedBy` SHOULD 对应 `visual-ui.json` 中需要使用该 sprite 的节点。
- `size` MUST 来自已确认的 `designVisualBBox` / `sourceCropBBox` 或最终 Production Asset 尺寸，不得直接来自 HTML rect。
- `htmlRect` SHOULD 记录布局/热区参考框。
- `designVisualBBox` SHOULD 记录设计图真实美术边界。
- `sourceCropBBox` SHOULD 记录实际裁切 bbox。
- `hitRect` SHOULD 记录运行时交互热区。
- `placementOffset` SHOULD 记录 `designVisualBBox` 相对 `htmlRect` 的偏移。
- `imageType` MUST 与 Unity Image 用法一致，例如 `simple` 或 `sliced`。
- `spriteBorder` SHOULD 为 sliced 资源提供 Unity Sprite border。
- `textPolicy` MUST 使用 `noText`、`bitmapAllowed`、`runtimeText` 之一。
- `generationMode` SHOULD 记录该资源是 `direct_repaired_asset`、`source_crop_exact_png`、`source_crop_alpha_png`、`source_first_patch_only`、`background_underlay_repair`、`runtime_text` 还是 `full_redraw_allowed`。
- `sourceCrop` SHOULD 指向资源的 `Source Crop`。
- `repairedAsset` SHOULD 指向 imagegen 修复输出；透明资源通常是 chroma-key 背景图。
- `alphaSource` SHOULD 指向扣色后的 alpha 源；如果直接使用 true-alpha 修复输出，可与 `repairedAsset` 相同或省略。
- `aiChromaSource` / `aiAlphaSource` MAY 作为 `repairedAsset` / `alphaSource` 的别名字段。
- `patchDonor` 只在 Source-First 回退路线中使用；source-crop-only 资源可省略。
- `compositionPolicy` SHOULD 记录最终资源使用 `direct_repaired_asset` 还是 `source_first_patch_only`。
- `aiEditScope` SHOULD 记录 `direct_repaired_asset`、`repair_mask_only`、`visible_edge_cleanup` 或 `none`。
- `fullRedrawAllowed` MAY 为 `false` 或省略；为 `true` 时必须在 `asset-generation-log.json` 记录用户批准原因。
- `target_bbox` SHOULD 与 `layer_plan.json` 保持一致。

进入 Phase C 前 MUST 校验：

- `referenceResolution` 与 `visual-ui.json.referenceResolution` 一致。
- 每个 `file` 都在 `assets_png/` 下。
- 每个 `path` 都在 `Assets/Resources/UI/<ViewName>/` 或 `_StyleReference/` 下。
- 同一 `path` 不存在尺寸、类型、border 冲突。
- 普通文字、价格、动态数值没有错误加入位图资源。
- 没有把 `design-master.png`、确认稿、Web 拼合预览图或 diff 图登记为资源。

### 7.4 web-validation.json

最终交付前 MUST 生成 `previews/web-validation.json`。

推荐结构：

```json
{
  "viewName": "LoginView",
  "referenceResolution": {
    "width": 1170,
    "height": 2532
  },
  "previewHtml": "previews/LoginView.resource-preview.html",
  "webComposite": "previews/web-composited.png",
  "diff": "previews/web-vs-master-diff.png",
  "allPreviewImagesUnderAssetsPng": true,
  "noMasterImageAsUiLayer": true,
  "allManifestFilesUnderAssetsPng": true,
  "missingPlannedAssets": [],
  "unusedAssets": [],
  "assetSimilarity": [
    {
      "id": "login_panel",
      "sourceCrop": "sources/login_panel_source.png",
      "asset": "assets_png/login_panel.png",
      "occluderMask": "debug/login_panel_occluder_mask.png",
      "holeSource": "debug/login_panel_hole_source.png",
      "visibleMask": "debug/login_panel_visible_mask.png",
      "repairMask": "debug/login_panel_repair_mask.png",
      "patchDonor": "asset_requests/login_panel/ai_patch_candidate.png",
      "compositionPolicy": "source_first_patch_only",
      "assetEqualsPatchDonor": false,
      "preserveVisiblePixels": true,
      "lockedPixelDiffCount": 0,
      "alphaMaskIoU": 0.98,
      "meanColorDelta": 1.8,
      "blocking": false
    }
  ],
  "chromaResidueIssues": [],
  "bboxIssues": [],
  "fullScreenSpriteSuspects": [],
  "blockingIssues": []
}
```

`blockingIssues` MUST 为空才能进入 Unity 交付。

## 8. Planning Rules

### 8.1 UI Semantic Inventory

规划阶段 MUST 按游戏 UI 语义盘点元素：

- `background`：整图背景、远景、氛围底图。
- `container`：登录面板、弹窗底板、输入区域底板。
- `control`：按钮、输入框、勾选框、关闭按钮、返回按钮。
- `icon`：账号图标、密码图标、平台图标。
- `text`：标题、按钮文字、协议文字、placeholder。
- `decoration`：光效、粒子、边框、角标、挂件。
- `state_asset`：按钮 normal / pressed / disabled，勾选 on / off。

分类 MUST 以运行时用途为准，而不是只按视觉边界决定。

### 8.2 Resource Granularity

资源粒度 MUST 按以下规则确定：

- 会点击的元素 MUST 独立拆分。
- 会变状态的元素 MUST 独立拆分，并记录需要的状态资源。
- 会动态替换文字、数字、头像、图标的区域 SHOULD 拆分。
- 未来可能复用的按钮、面板、输入框 SHOULD 拆成完整资源。
- 纯装饰且不动、不复用的元素 MAY 合并到背景或父级容器。
- 与背景强绑定且不会单独动的氛围光效 MAY 并入背景。
- 需要本地化或运行时修改的文字 SHOULD 标记为 `runtime_text`。

### 8.3 Occlusion Analysis

规划阶段 MUST 建立遮挡关系。

每个被遮挡元素 MUST 判断：

- 上层元素是否需要独立资源。
- 下层元素是否需要在游戏中独立移动、缩放、复用或显示。
- 下层元素被遮挡区域是否必须补绘完整。
- 如果不补齐，被单独使用时是否会露出缺块。

如果底板、面板、按钮背景或输入框背景会作为独立资源使用，则其被遮挡区域 MUST 标记为 `repair_required: true`。

## 9. Asset Production Rules

### 9.1 Source Crop

每个需要拆出的元素 MUST 先根据已确认的 `layer_plan.json.source_crop_bbox` 从 `design-master.png` 裁切出 `Source Crop`。

裁切前置条件：

- `source_crop_bbox` MUST 来自用户确认后的 `bbox-review-data.json`。
- `source_crop_bbox` MUST 完整包含设计图中的描边、阴影、发光、圆角、外扩装饰和透明边缘。
- `html_rect` 只能作为搜索区域和热区参考，MUST NOT 直接复制成 `source_crop_bbox`。
- 如果 `design_visual_bbox` 大于 `html_rect`，后续 Web/Unity 拼装必须使用 `placement_offset` 对齐，而不是把资源压回 HTML rect。
- 如果 `source_crop_bbox` 被修正，必须重新裁出 `Source Crop`，并重新生成依赖它的 mask、`edit_target`、imagegen 输入、`asset-manifest.json.size` 和 prefab 派生 JSON。

`Source Crop` 是 imagegen 修复前的主要参考和校验输入，不再默认要求成为最终像素主体。

`Source Crop` 用于：

- 作为 source-crop-only 资源的直接基底。
- 作为 imagegen 生成完整独立修复资源的风格、形状和比例参考。
- 作为边缘、颜色、纹理、文字位置的对比依据。
- 作为 `visible_mask` 和 `repair_mask` 的生成依据。
- 作为 `Alpha BBox` 计算目标。

`Source Crop` MAY 直接成为最终资源，前提是该元素无需补绘、没有运行时文字或遮挡污染、透明边缘准确、bbox 正确，并通过逐资源与 Web 拼装验收。

### 9.2 AI Repair / Redraw

除非元素本身已经完美可用，否则缺失区域 SHOULD 经过 imagegen 或等价 AI 修复流程。

`repair_required: true` 的元素 MUST 经过修复、补绘或手工边缘重建，不能只使用原图裁切结果交付。

AI 修复默认生成完整干净的独立资源。通过校验后，imagegen 输出可以作为 `direct_repaired_asset` 进入 `assets_png/`。

AI 处理 MUST 保持：

- 与 `design-master.png` 一致的形状。
- 与 `design-master.png` 一致的颜色和材质倾向。
- 与 `design-master.png` 一致的按钮、面板、装饰位置关系。
- 资源边缘完整，不缺块、不破边。

AI 处理 SHOULD 补齐：

- 被其他元素遮挡的区域。
- 裁切边缘不完整区域。
- 半透明边缘。
- 阴影、描边、发光等需要独立使用的外扩像素。

AI 输出后默认执行 direct repaired asset 采用流程：对纯色背景输出执行 chroma key、alpha 清理、despill、尺寸和 bbox 对齐；检查没有 runtime text 误烤、没有完整 UI 截图、没有 key color 残留；通过后写入 `assets_png/` 并记录 `generationMode: direct_repaired_asset`。

如果 bbox 修正导致资源尺寸变化，必须基于新的 `Source Crop` 重新调用 imagegen。复用旧 `ai_alpha_sources/` 或旧 `repairedAsset` 后再缩放，只能作为临时验证定位问题，最终验收必须 fail。

### 9.3 Occlusion Punch-Out

被上层 UI 元素遮挡、但自身需要作为独立资源使用的元素，MUST 先执行遮挡掏空。

适用场景：

- 面板被按钮、输入框、标题或图标遮挡。
- 背景需要在最终 UI 图层覆盖区域下方补齐。
- 按钮需要清掉文字、图标或残影后作为按钮底图复用。
- 装饰件被其他元素局部遮挡但需要单独移动、缩放或复用。

必须生成：

```text
sources/<id>_source.png
debug/<id>_occluder_mask.png
debug/<id>_hole_source.png
debug/<id>_visible_mask.png
debug/<id>_repair_mask.png
asset_requests/<id>/inpaint_input.png
asset_requests/<id>/inpaint_mask.png
asset_requests/<id>/prompt.md
```

规则：

- `occluder_mask` MUST 由遮挡该资源的上层元素 bbox、alpha 或人工 mask 合并得到。
- `hole_source` MUST 从 `Source Crop` 复制后，把 `occluder_mask` 区域掏空为透明或纯色。
- `repair_mask` SHOULD 等于 `occluder_mask` 膨胀 2-6px，并合并破边、阴影外扩和透明边缘修复区。
- `visible_mask` MUST 排除 `repair_mask`，表示当前资源中必须原样保留的可见像素。
- imagegen 输入 SHOULD 使用 `hole_source` / `inpaint_input` 和 `repair_mask` / `inpaint_mask`，而不是原始 `Source Crop`。

### 9.4 Direct Repaired Asset Adoption

需要修复的资源默认直接采用 imagegen 修复后的完整独立资源。

标准顺序：

1. 从 `Source Crop`、`hole_source`、`repair_mask` 和 `edit_target` 准备 imagegen 输入。
2. imagegen 输出完整独立 sprite；透明资源输出到纯色 chroma-key 背景。
3. 把原始输出保存到 `ai_chroma_sources/` 或等价目录。
4. 对透明资源执行 chroma key、alpha 清理、edge contract、despill 和尺寸对齐；需要追溯时保存到 `ai_alpha_sources/`。
5. 检查尺寸、alpha bbox、key color 残留、运行时文字误烤、整屏截图误用。
6. 通过后写入 `assets_png/`，并在 `asset-manifest.json` / `asset-generation-log.json` 记录 `sourceCrop`、`editTarget`、`repairMask`、`repairedAsset`、`alphaSource`、prompt 摘要和 `generationMode: direct_repaired_asset`。

阻断规则：

- `assets_png/` 使用修复输出但未记录 `direct_repaired_asset` 来源、prompt 和校验结果时 MUST fail。
- 透明资源仍有明显 key color 残留时 MUST fail。
- 输出包含不属于该资源的运行时文字、按钮、整屏 UI 或参考图背景时 MUST fail。
- 输出的形状、颜色、材质、比例与 `Source Crop` 明显不一致时 MUST fail。

### 9.5 Source-First Patch Composition

只有 strict 锁像素、AI 明显跑偏、AI 只适合作为局部补丁或用户明确要求时，才使用 Source-First Composition。

标准顺序：

1. 从 `Source Crop` 建立初始透明或不透明资源。
2. 根据资源策略判断是否需要 `Occlusion Punch-Out`。
3. 根据遮挡关系、alpha 边缘和目标 bbox 生成 `visible_mask` 与 `repair_mask`。
4. 对 `repair_mask` 区域调用 imagegen 生成 donor，或手工确定性清边。
5. 只把 donor 的 `repair_mask` 区域合成回源图基底。
6. 用 `Source Crop` 覆盖回 `visible_mask` 内的锁定像素。
7. 生成 `assetSimilarity` 记录，检查未授权区域是否被改动。
8. 通过后才写入 `assets_png/`。

阻断规则：

- `preserve_visible_pixels: true` 且 `lockedPixelDiffCount` 超过阈值时 MUST fail。
- `ai_edit_scope: repair_mask_only` 时，`repair_mask` 外出现明显颜色、形状、纹理变化 MUST fail。
- `similarity_policy: strict` 的资源 SHOULD 让 `lockedPixelDiffCount` 为 0；如果存在抗锯齿或 alpha 量化差异，必须记录阈值和原因。

### 9.6 Chroma Key Transparency

透明资源 SHOULD 使用纯色背景生成，再扣除纯色背景。

推荐流程：

1. 用 imagegen 生成元素在纯色背景上的修复图。
2. 纯色背景使用图中不存在的颜色，例如 `#ff00ff`。
3. 用 chroma key 工具扣除背景。
4. 对 alpha 边缘进行收缩、羽化、despill。
5. 检查是否仍有纯色残留。

扣色后 MUST 检查：

- 背景纯色残留像素为 0 或低于项目阈值。
- 半透明边缘没有明显脏边。
- 资源阴影没有被误删。
- `Alpha BBox` 与目标元素边界匹配。

### 9.7 BBox Alignment

对透明资源，坐标对齐 MUST 使用 alpha 后的真实可见区域，而不是 PNG 画布尺寸。

如果资源画布包含外扩阴影或透明留白：

- `file` 图片可以保留透明边缘。
- `target_bbox` 表示可见内容在设计图中的目标位置。
- 对齐工具或执行者 MUST 计算 alpha bbox 与 target bbox 的偏移。

## 10. Web Preview And Validation

最终校验中心 MUST 是 Web 拼装预览，而不是 PSD。

### 10.1 Preview HTML

`previews/<ViewName>.resource-preview.html` MUST 满足：

- 画布尺寸等于 `visual-ui.json.referenceResolution`。
- 只在预览副本中引用 `assets_png/` 资源，不修改原始 `<ViewName>.html`。
- 图片资源的层级顺序与 `layer_plan.json.items[].z_order` 或 `visual-ui.json` 层级规则一致。
- 普通文字、数字和动态内容使用 HTML 文本节点模拟运行时文本。
- 允许使用 CSS 做布局、定位、字体、透明度和点击区域可视化。
- 禁止使用 CSS 渐变、box-shadow、filter、canvas、SVG 或整屏截图补出最终美术视觉。
- 禁止引用 `design-master.png` 作为可见 UI 图层。

### 10.2 Screenshot And Diff

`web-composited.png` MUST 由浏览器或等价渲染流程截取 `Resource Preview HTML` 得到。

截图要求：

- 尺寸 MUST 等于设计分辨率。
- 缩放 MUST 为 1:1。
- 背景透明或背景色处理 MUST 与 Web 预览约定一致。
- 截图不能包含浏览器边框、滚动条或调试覆盖层，除非该截图专用于 debug。

`web-vs-master-diff.png` SHOULD 比较 `web-composited.png` 与 `design-master.png`。

`coordinate_compare.png` SHOULD 标注：

- `design-master.png` 的目标位置。
- Web 拼装后的资源 alpha bbox。
- 偏移量。
- 缺失或未使用资源。

diff 结果分两类：

- 路径、尺寸、资源覆盖、chroma 残留、明显 bbox 偏移是硬失败。
- 全图像素差异用于人工检查，不能要求像 PSD 回渲染那样接近零误差，因为 Web 文本和浏览器抗锯齿可能不同。

### 10.3 Machine Validation

`web-validation.json` SHOULD 至少验证：

- `assetSimilarity` 中没有 `blocking: true` 的资源。
- `preserve_visible_pixels: true` 的资源，其 `visible_mask` 锁定区域没有未授权改动。
- `repair_mask_only` 资源的 AI 改动没有越界到锁定区域。
- `alphaMaskIoU`、`meanColorDelta`、`lockedPixelDiffCount` 满足 `similarity_policy` 对应阈值。
- 预览 HTML 中所有可见图片文件存在。
- 所有可见图片文件都在当前输出目录的 `assets_png/` 下。
- `asset-manifest.json` 中每个 `file` 都在 `assets_png/` 下。
- 不存在引用旧版资源目录的路径。
- 不存在引用 `design-master.png`、确认稿、Web 拼合图或 diff 图的可见 UI 图层。
- 每个 `asset_strategy` 为独立资源的 `layer_plan.json` 项都有对应 PNG。
- 每个 `asset-manifest.json` 项都能追溯到 `layer_plan.json` 或明确说明为复用资源。
- 每个需要 sprite 的 `visual-ui.json` 节点都有资源或明确标记为 runtime text。
- 每个交互、状态变化、复用资源都已出现在 Web 预览中。
- 使用 chroma key 的资源没有明显 key color 残留。
- 有 `target_bbox` 的资源没有明显 bbox 偏移。

### 10.4 Full-Screen Sprite Detection

如果某个资源接近设计分辨率且包含控件、文案或完整 UI，MUST 标记为 `fullScreenSpriteSuspects`。

只有纯背景 MAY 接近全屏尺寸。

纯背景资源不得包含按钮、输入框、普通文案、价格或完整 UI 截图。

### 10.5 Visual Validation

人工检查 MUST 包含：

- 是否缺失元素。
- 面板、背景是否缺块。
- 按钮、输入框、图标是否与设计图一致。
- 透明边缘是否脏。
- 坐标是否整体漂移。
- Web 预览是否确实由当前 `assets_png/` 拼装。
- 普通文字和动态内容是否仍是运行时文本。

## 11. Tooling Interface

本 SPEC 定义目标工作流契约。工具实现可以分阶段接入；如果某个自动化命令尚未存在，执行者 MUST 用现有脚本、浏览器截图或手工检查生成等价产物，并在报告中记录。

推荐工具接口：

```text
fui-resource-pipeline
├── bbox-review
├── crop-sources
├── build-masks
├── build-occlusion-punchout
├── prepare-imagegen-requests
├── chroma-key
├── adopt-direct-repaired-asset
├── compose-patch
├── bbox-report
├── align-bbox
├── asset-similarity
├── build-web-preview
├── web-diff
├── coordinate-compare
└── validate-web-assets
```

工具路径规则：

- 脚本路径 SHOULD 相对项目根目录或 skill 根目录书写。
- 交付 JSON 中的资源路径 SHOULD 使用相对路径。
- `web-validation.json` SHOULD 优先写入相对路径，避免把本机目录写入交付文件。
- 工具不能创建最终美术，只能处理 imagegen 已生成的图像或执行校验。

## 12. Failure Handling

| Failure                                       | Required Response                                                                                                                                                                                       |
| --------------------------------------------- | ------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| 元素裁切不准确                                | 回到 `bbox-review.html` 调整 `design_visual_bbox` / `source_crop_bbox` 并让用户重新确认；重新生成 `Source Crop`、mask、imagegen 输入和后续资源。                                                |
| 直接用 HTML rect 裁图                         | 标记为阻断问题；HTML rect 只能作为布局/热区参考，必须重新执行 bbox review。                                                                                                                             |
| `visual-ui.json` 被写入设计 bbox            | 恢复原始提取文件；把设计 bbox 写入 `layer_plan.json` / `bbox-review-data.json`；如需改变 prefab 尺寸，生成 `<ViewName>.visual-ui.recut.json`。                                                    |
| bbox 变化后复用旧 AI 输出                     | 只能保留为验证记录；重新基于新 `Source Crop` 生成 imagegen 输入、修复资源和 alpha 源。                                                                                                                |
| AI 输出偏离设计图                             | 降低重绘范围，改为局部修复；使用纯色背景重新生成；对比 `Source Crop` 修正颜色、形状和边缘。                                                                                                           |
| imagegen 输出像重新生成                       | 先对比 `Source Crop`、目标语义和用户确认图；若仍是干净、完整且可接受的独立 sprite，可记录为 `direct_repaired_asset` 并继续校验；若形状、材质或语义偏离，则重新生成或退回 Source-First Composition。 |
| AI 修改了锁定可见像素                         | 丢弃该次输出或将 `Source Crop` 的 `visible_mask` 区域覆盖回最终图；收紧 prompt、`repair_mask` 和 `ai_edit_scope`；重新跑逐资源相似度。                                                          |
| 逐资源相似度失败                              | 不进入 Web 拼装；优先切换为 `source_crop_alpha_png` 或 `source_crop_with_repaired_edge`；只对失败区域做局部补绘。                                                                                   |
| 被遮挡资源没有掏空                            | 根据 `occluded_by` 生成 `occluder_mask` 和 `hole_source`；重新生成 `repair_mask`；用 `hole_source` 作为 imagegen 输入。                                                                       |
| 未记录来源的 AI 输出直接成为最终资源          | 标记为阻断问题；补齐 `generationMode: direct_repaired_asset`、`repairedAsset`、`alphaSource`、prompt 摘要和校验结果；无法追溯时重新生成。                                                         |
| chroma key 脏边                               | 更换 key color；调整 transparent / opaque threshold；增加 edge contract、edge feather、despill。                                                                                                        |
| 面板或背景缺块                                | 回到 `design-master.png` 检查目标 bbox；重新生成完整元素；重新跑 bbox、Web 预览、diff 和 validation。                                                                                                 |
| Web 预览引用错误资源                          | 检查 `web-validation.json` 的 `allPreviewImagesUnderAssetsPng`；检查预览 HTML 的 `img` / CSS `background-image`；重新截图并生成 diff。                                                          |
| 坐标错误                                      | 使用 bbox 报告找出 alpha bbox 与 target bbox 偏移；更新资源边缘、目标 bbox 或 Web 预览定位；重新生成 `coordinate_compare.png`。                                                                       |
| `ui.web_to_ugui_prefab` 返回 `wrong_mode` | 停止 Play Mode，确认 Editor 处于 Edit Mode 后重试。                                                                                                                                                     |
| dry-run 返回 `sprite_not_found`             | 确认 PNG 已复制到 `Assets/Resources/UI/<ViewName>/`，执行 AssetDatabase refresh，重新设置 Sprite Importer 后重试。                                                                                    |
| `layer_plan.json` 漏掉运行时元素            | 回到 `design-master.png` 和 `visual-ui.json` 重新做语义盘点；更新 `layer_plan.json` 和 overlay。                                                                                                  |
| 被遮挡元素未标记补绘                          | 更新 `occluded_by` / `occludes`；把下层资源标记为 `repair_required: true`；重新补绘完整资源。                                                                                                     |
| 出现整屏图热区方案                            | 立即停止；从 manifest 和 Web 预览中移除整屏 UI 图；回到 `layer_plan.json` 拆分真实资源。                                                                                                              |

## 13. Acceptance Criteria

最终产物只有同时满足以下条件才算完成：

- `visual-ui.json` 是固定提取脚本生成。
- 原始 `visual-ui.json` 未被设计 bbox、资源尺寸或新 Sprite 路径污染；如需按确认 bbox 生成 prefab，已使用 `<ViewName>.visual-ui.recut.json`。
- `design-master.png` 是用户确认版本。
- `layer_plan.json` 已确认，并覆盖所有应作为游戏 UI 资源提取的元素。
- `bbox-review.html` / `bbox-review-data.json` 已生成，且关键资源的 `design_visual_bbox` 已经用户确认。
- `extraction_plan_overlay.png` 已确认，遮挡关系和补绘要求清晰可见。
- 每个独立资源都有基于 `source_crop_bbox` 裁出的 `Source Crop`；需要修复的资源有 `repair_mask`、`editTarget`、`repairedAsset` 和采用说明。
- bbox 修正过的资源已基于新 `Source Crop` 重新执行 imagegen 修复；没有把旧 AI 输出缩放后作为最终资源。
- 被遮挡且需要独立使用的资源有 `occluder_mask` 和 `hole_source`。
- 需要透明的修复资源已从 `ai_chroma_sources/` 或 true-alpha 源生成 `alphaSource`，并通过 chroma 残留校验。
- `assets_png/` 可采用通过校验的 `direct_repaired_asset`；Source-First Composition 只用于 strict 锁像素、AI 跑偏或用户明确要求的回退场景。
- strict / source-first 资源的锁定可见区域已经按 `Source Crop` 校验，`assetSimilarity` 没有阻断项。
- 每个正式资源都在 `assets_png/`。
- `asset-manifest.json` 未引用旧目录。
- Web 预览中所有可见图片都来自 `assets_png/`。
- `assets_png/` 中资源是游戏可用独立 PNG，不是整图截图。
- 需要透明的资源 alpha 正确，没有明显纯色残留。
- 背景和面板完整，没有底部或边缘缺失。
- 已生成 `previews/<ViewName>.resource-preview.html`。
- 已生成 `previews/web-composited.png`。
- 已生成 `previews/web-vs-master-diff.png`。
- 已生成 `previews/coordinate_compare.png`。
- 已生成 `previews/web-validation.json`。
- `web-validation.json` 中没有阻断问题。
- 人工检查确认 Web 拼装结果可接受。
- 用户确认后资源才复制到 `Assets/Resources/UI/<ViewName>/`。
- Unity 已处于 Edit Mode，资源复制后已刷新 AssetDatabase 并导入为 Sprite。
- `ui.web_to_ugui_prefab` dry-run 无 error。

## 14. Handoff Checklist

- [ ] `design-master.png` 是用户确认版本。
- [ ] `visual-ui.json` 是固定提取脚本生成。
- [ ] 原始 `visual-ui.json` 未写入设计 bbox；如需调整 prefab 尺寸，已生成派生的 `<ViewName>.visual-ui.recut.json`。
- [ ] 已按游戏 UI 语义盘点元素。
- [ ] 已分析遮挡关系。
- [ ] 已生成并确认 `bbox-review.html` / `bbox-review-data.json`。
- [ ] 已生成并确认 `layer_plan.json`。
- [ ] 已生成并确认 `extraction_plan_overlay.png`。
- [ ] `layer_plan.json` 覆盖所有需要拆出的元素。
- [ ] 每个独立资源都有 `source_crop` 路径。
- [ ] 每个 `source_crop` 都来自确认后的 `source_crop_bbox`，不是直接来自 HTML rect。
- [ ] bbox 修正过的资源已重新裁 `Source Crop`，并重新生成 imagegen 输入和最终修复资源。
- [ ] 需要修复的资源都有 `repair_mask`、`edit_target`、`repaired_asset` 和采用说明。
- [ ] 被遮挡且需要独立使用的资源都有 `occluder_mask` 和 `hole_source`。
- [ ] imagegen 修复输出按 `direct_repaired_asset` 或 Source-First 回退路线记录清楚。
- [ ] 采用 `direct_repaired_asset` 的资源已执行 chroma/alpha、bbox、尺寸、来源和 runtime text 校验。
- [ ] Source-First 回退资源已执行局部合成和锁定像素校验。
- [ ] `preserve_visible_pixels`、`ai_edit_scope`、`similarity_policy` 已明确。
- [ ] 被遮挡但需要独立使用的元素已标记补绘。
- [ ] 普通文字、动态数值已标记为 runtime text 或有位图化理由。
- [ ] 每个正式资源都在 `assets_png/`。
- [ ] `asset-manifest.json` 未引用旧目录、确认稿、拼合预览图或 diff 图。
- [ ] Web 预览 HTML 只引用 `assets_png/` 中的美术图片。
- [ ] 已生成 `web-composited.png`。
- [ ] 已生成 `web-vs-master-diff.png`。
- [ ] 已生成 `coordinate_compare.png`。
- [ ] 已生成 `web-validation.json`。
- [ ] `assetSimilarity` 无阻断项，锁定可见像素没有未授权改动。
- [ ] 已人工检查缺块、偏移、脏边和图层来源。
- [ ] 用户已确认由资源图拼出的 Web 预览。
- [ ] 资源已复制到 `Assets/Resources/UI/<ViewName>/`。
- [ ] Unity Editor 处于 Edit Mode，且新 PNG 写入后已执行 AssetDatabase refresh。
- [ ] Unity Sprite Importer 已设置。
- [ ] `ui.web_to_ugui_prefab` dry-run 已通过。

## Appendix A: Imagegen Prompt Template

透明元素修复推荐提示词结构：

```text
Use the provided source crop as the primary pixel reference.
Do not redesign the element.
Create one complete, clean, independent UI sprite for this exact element.
Repair the masked missing, occluded, broken, text-contaminated, or transparent-edge area.
Keep visible unmasked areas visually consistent with the source crop unless cleanup is required.
Keep the original shape, color, bevel, shadow, glow, texture, and proportions.
Reconstruct missing or occluded edges so the sprite can be used alone in Unity.
Place the repaired element on a flat pure #ff00ff background if chroma key is required.
Do not include runtime text, placeholder text, full-screen UI, debug overlays, or unrelated controls.
Do not add new decorative elements.
The output should be suitable for chroma-key removal into a transparent PNG.
```

中文说明：

- 必须强调 `exact UI element`。
- 必须强调 `source crop` 是主要像素参考。
- 必须明确输出是完整、干净、独立的 UI sprite。
- 必须明确修复缺失、遮挡、破边、文字污染或透明边缘区域。
- 必须明确不能包含运行时文字、整屏 UI 或无关控件。
- 必须要求保持原形状、颜色、质感和比例。
- 必须要求纯色背景。
- 不要让模型自由设计新风格。

## Appendix B: ShopView Example

输入：

```text
用户需求：麻将游戏商城，中国风暖金配色，深色底
FUI-CLI/ShopView/ShopView.html
FUI-CLI/ShopView/ShopView.web.png
FUI-CLI/ShopView/ShopView.visual-ui.json
FUI-CLI/ShopView/design-master.png
```

`ShopView.html` MUST 声明设计分辨率，例如 `1170x2532`。

规划产物：

```text
FUI-CLI/ShopView/layer_plan.json
FUI-CLI/ShopView/extraction_plan_overlay.png
```

资源产物示例：

```text
FUI-CLI/ShopView/assets_png/bg_shop.png
FUI-CLI/ShopView/assets_png/bg_nav_bar.png
FUI-CLI/ShopView/assets_png/btn_tab_selected.png
FUI-CLI/ShopView/assets_png/btn_tab_normal.png
FUI-CLI/ShopView/assets_png/btn_buy.png
FUI-CLI/ShopView/assets_png/icon_coin.png
```

验证产物：

```text
FUI-CLI/ShopView/previews/ShopView.resource-preview.html
FUI-CLI/ShopView/previews/web-composited.png
FUI-CLI/ShopView/previews/web-vs-master-diff.png
FUI-CLI/ShopView/previews/coordinate_compare.png
FUI-CLI/ShopView/previews/web-validation.json
```

交付顺序：

1. 确认 `web-validation.json` 没有阻断问题。
2. 人工检查 `web-composited.png` 和 `web-vs-master-diff.png`。
3. 用户确认资源拼装结果。
4. 复制 `assets_png/` 中已确认资源到 `Assets/Resources/UI/ShopView/`。
5. 设置 Sprite Importer，尤其是 sliced border。
6. 调用 `ui.web_to_ugui_prefab` dry-run。
7. dry-run 通过后正式生成 prefab。
