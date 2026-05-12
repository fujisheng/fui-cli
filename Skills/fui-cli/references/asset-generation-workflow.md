# 资源生成工作流

本文定义从 Web 原型生成 Unity UI 精灵资源的正式流程。目标是先用整体设计图锁定统一风格，再逐个生成可导入、可复用、可验证的 UI 素材，最后交给 `ui.web_to_ugui_prefab` 生成 UGUI/FUI prefab。

核心原则：

- 布局来自 Web 原型和 `visual-ui.json`
- 视觉表现来自用户需求、项目风格和 `design-master.png`
- 真正的美术位图只能通过 Codex `imagegen` 生成
- 代码只负责提取、裁剪、后处理、校验、预览和 Unity 导入

---

## 硬规则

### 1. 位图创作必须使用 imagegen

以下产物必须通过 Codex `imagegen` 创建或改图：

- `design-master.png`
- 背景、面板、卡片底图
- 按钮底图与状态变体
- 图标、装饰、分隔线、高光条
- 重新生成的失败版本

禁止用以下方式创作最终美术图：

- HTML / CSS / canvas / SVG
- Python / Pillow / ImageMagick / Unity 代码
- 纯色块、渐变、占位矢量冒充真实资源
- imagegen 不可用时自动退回代码绘制

允许代码处理的事项：

- 从 HTML 提取 `visual-ui.json`
- 截取 Web 原型布局参考图
- 从 `design-master.png` 裁剪局部参考图
- 对 imagegen 输出做透明度处理、尺寸校验、拼合预览、diff 报告
- 复制已确认资源并设置 Unity Sprite Importer

如果当前会话没有可用的 Codex `imagegen`，资源生成流程必须暂停并说明阻塞原因。

### 2. 设计分辨率来自 HTML

设计分辨率必须由原型 HTML 显式声明，提取脚本会写入 `visual-ui.json.referenceResolution`。

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

- `html`、`body`、根容器尺寸必须与声明分辨率一致
- `--width` / `--height` 只用于旧原型兼容或一致性校验
- 项目文档只作为校验基线；HTML 与项目约定冲突时，先修正 HTML
- 禁止从浏览器窗口大小、截图尺寸、CSS 缩放结果反推分辨率

### 3. 临时目录与 Assets 隔离

生成阶段只写 `Temp/WebToUgui/<ViewName>/`。用户确认、验证通过、Unity 导入规则明确后，才复制到 `Assets/Resources/UI/<ViewName>/`。

### 4. 普通文字不得烘焙进图片

普通文案、数字、价格、倒计时、动态奖励数值必须由 UGUI 文本节点渲染。只有 logo、特殊艺术字、不可拆分纹样允许进入图片资源，并必须在 `asset-manifest.json` 的 `textPolicy` 中说明。

### 5. 禁止整屏确认稿热区方案

`design-master.png`、用户确认稿、拼合预览图都只能作为参考或验证产物，禁止作为 prefab 的整屏主视觉 Sprite。

禁止以下做法：

- 把确认稿或 `design-master.png` 复制到 `Assets/Resources/UI/<ViewName>/` 当作完整 UI 背景
- 用一张整屏 UI 截图显示所有视觉，再叠加透明按钮、协议区、点击热区
- 因字体、标题书法、卡片内部位图与确认稿不一致，就改用整屏主视觉图绕过资源拆分
- 将包含按钮、标题、价格、协议文案、卡片内容的整屏图登记进 `asset-manifest.json`

正确处理：

- prefab 必须由 `asset-manifest.json` 中的独立资源图拼装
- 哪个局部不一致，就重新生成或修正哪个资源图
- 文字、标题、价格等应优先用 UGUI 文本或独立艺术字资源解决
- 只有纯装饰背景允许作为全屏背景资源；该背景不得包含可交互控件、普通文案或完整 UI 截图

---

## 产物目录

```text
Temp/WebToUgui/<ViewName>/
├── <ViewName>.html                    Web 原型
├── <ViewName>.web.png                 浏览器截图，仅作布局参考
├── <ViewName>.visual-ui.json          布局数据
├── design-master.png                  阶段 A：整体设计图
├── style-tokens.json                  阶段 A：风格令牌
├── asset-manifest.json                阶段 B：资源清单
├── asset-generation-log.json          阶段 C：生成尝试记录
├── assets_raw/                        阶段 C：imagegen 原始输出与失败版本
├── assets/                            阶段 C：通过单资源检查的临时精灵
└── previews/                          阶段 D：拼合预览、9-slice 预览、diff、报告
    ├── <ViewName>.preview.html
    ├── preview-composited.png
    ├── diff.png
    └── asset-verification-report.json

用户确认后复制到：

Assets/Resources/UI/<ViewName>/
```

项目级风格参考库：

```text
Temp/WebToUgui/_StyleReference/
Assets/Resources/UI/_StyleReference/
```

---

## 流程总览

```text
用户需求 + 项目风格 + visual-ui.json
        |
        v
阶段 A：生成整体设计图
  -> design-master.png
  -> style-tokens.json
        |
        v
阶段 B：提取并校验资源清单
  -> asset-manifest.json
        |
        v
阶段 C：按优先级用 imagegen 逐资源生成
  -> assets_raw/
  -> assets/
  -> asset-generation-log.json
        |
        v
阶段 D：拼合验证、用户确认、复制、Unity 导入、prefab dry-run
```

---

## 阶段 A：整体设计图

### A1. 收集输入

布局来源只提供结构，不提供最终视觉：

| 来源 | 提取内容 | 用途 |
| --- | --- | --- |
| `visual-ui.json` | 元素层级、rect 坐标/尺寸、`imageType`、`borderRadius` | 布局骨架、元素比例 |
| `data-ui-id` | 功能语义，如 `CoinGroup`、`TabSelected` | 元素用途、交互语义 |
| HTML 结构 | 父子嵌套、DOM 顺序 | 视觉分组、Z 序 |
| HTML 分辨率声明 | `referenceResolution` | 尺寸基准 |

风格来源决定表现：

| 来源 | 提取内容 | 用途 |
| --- | --- | --- |
| 用户描述 | 风格方向、色调、材质偏好 | 视觉方向 |
| 项目已有资源 | 已确认精灵、截图、规范 | 风格一致性 |
| 项目文档 | 游戏类型、目标平台、受众 | 风格校验 |

不要参考 Web 原型的 CSS 颜色、渐变、阴影。它们是布局定位稿的临时占位。

### A2. 构建 imagegen prompt

prompt 必须分清“布局骨架”和“视觉风格”。

```text
【布局骨架】
一个 1170×2532 的移动端商城页面，从上到下分为：
- 顶部栏：返回按钮、标题、货币区
- 分类标签栏：4 个胶囊 Tab，包含选中态
- 商品列表：卡片、商品图、名称、描述、购买按钮
- 底部导航栏：3 个 Tab

【视觉风格】
- 主题：麻将休闲游戏商城，中国风融合现代扁平
- 色调：深色基底，暖金强调
- 层次：背景 < 面板 < 按钮 < 图标
- 材质：干净利落，微弱高光，柔和内阴影
- 圆角：大圆角和胶囊按钮统一

【参考图】
附 Web 原型截图，仅用于布局约束，不作为风格参考。
```

### A3. 生成与验收

调用 Codex `imagegen` 生成 `design-master.png`。代码生成的 HTML/CSS/canvas/SVG/Python 图只能作为布局参考，不能作为设计图。

设计图检查项：

- [ ] 每个 `data-ui-id` 对应元素的位置大致正确
- [ ] 主色、强调色、辅助色体系一致
- [ ] 圆角半径视觉统一
- [ ] 背景、面板、按钮、图标层次清晰
- [ ] 文字区域留白充足
- [ ] 风格符合项目方向和已有资源

设计图不通过，不进入阶段 B。

### A4. 固化风格令牌

设计图通过后，生成 `style-tokens.json`。它用于把颜色、材质、圆角、图标风格结构化，便于后续页面复用。

```json
{
  "viewName": "ShopView",
  "referenceResolution": { "width": 1170, "height": 2532 },
  "theme": "麻将休闲游戏，中国风暖金，深色基底",
  "palette": {
    "background": "#17121F",
    "panel": "#2B2334",
    "primary": "#D99A2B",
    "accent": "#F6D37A",
    "textPrimary": "#FFF2C7",
    "textSecondary": "#C8BFAE"
  },
  "materials": {
    "panel": "半透明深色玉石质感，顶部细高光",
    "buttonPrimary": "暖金按钮，轻微内阴影，边缘清晰",
    "buttonSecondary": "深色胶囊按钮，低饱和高光",
    "icon": "扁平高对比图标，少量金色描边"
  },
  "radii": {
    "small": 12,
    "medium": 24,
    "pill": 999
  },
  "shadow": {
    "panel": "只在整体设计图中表达，单个透明精灵不要带外投影",
    "button": "按钮内部质感允许，外部阴影禁止烘焙"
  }
}
```

规则：

- 后续所有资源 prompt 必须引用 `style-tokens.json`
- 多个 View 复用同一风格时，同步到 `_StyleReference/style-tokens.json`
- 设计图与风格令牌冲突时，以设计图为准，并修正令牌

---

## 阶段 B：资源清单

### B1. manifest 结构

遍历 `visual-ui.json` 中所有 `data-ui-sprite` 不为空的节点，生成 `asset-manifest.json`。

```json
{
  "designMaster": "Temp/WebToUgui/ShopView/design-master.png",
  "styleTokens": "Temp/WebToUgui/ShopView/style-tokens.json",
  "referenceResolution": { "width": 1170, "height": 2532 },
  "assets": [
    {
      "path": "Assets/Resources/UI/ShopView/btn_tab_normal.png",
      "tempPath": "Temp/WebToUgui/ShopView/assets/btn_tab_normal.png",
      "usedBy": ["TabTile", "TabTheme", "TabPowerUp"],
      "element": "ButtonElement",
      "size": { "width": 128, "height": 60 },
      "imageType": "sliced",
      "borderRadius": 30,
      "reuseKey": "tab.normal",
      "variantOf": "tab.selected",
      "textPolicy": "noText",
      "priority": 2
    }
  ]
}
```

字段说明：

| 字段 | 说明 |
| --- | --- |
| `path` | 最终复制到 `Assets/` 的路径 |
| `tempPath` | 阶段 C 临时输出路径 |
| `usedBy` | 引用该资源的 `data-ui-id` 列表 |
| `size` | 输出像素尺寸，来自节点 rect |
| `imageType` | `simple`、`sliced` 等 UGUI Image 类型 |
| `borderRadius` | 生成和 9-slice border 的参考值 |
| `reuseKey` | 跨 View 复用语义 |
| `variantOf` | 同族状态关系 |
| `textPolicy` | 是否允许文字进入图片 |
| `priority` | 阶段 C 生成顺序 |

### B2. 去重与复用

去重规则：

- 同一 `path` 只保留一条记录
- `usedBy` 合并所有引用者
- 同一 `path` 的 `size`、`imageType`、`borderRadius` 不一致时，停止并修正原型

复用规则：

- 生成前先查 `_StyleReference`
- 已存在同 `reuseKey` 且尺寸、状态匹配的资源时，优先复用
- `variantOf` 用于关联普通态、选中态、按下态、禁用态
- 不为普通文字、价格、动态数值生成 sprite

### B3. 优先级

| priority | 类别 | 说明 |
| --- | --- | --- |
| 0 | 背景 / 大面板 | 最先生成，为页面定色调 |
| 1 | 面板 / 卡片底图 | 与背景协调，可能大面积复用 |
| 2 | 按钮底图 | 高频复用，优先考虑 9-slice |
| 3 | 图标 | 小尺寸，高对比，需叠加按钮验证 |
| 4 | 装饰 | 细线、分隔符、高光条 |

同优先级内按尺寸从大到小排序。

### B4. 清单校验

进入阶段 C 前必须确认：

- [ ] `referenceResolution` 与 `visual-ui.json.referenceResolution` 一致
- [ ] `path` 位于 `Assets/Resources/UI/<ViewName>/` 或 `_StyleReference/`
- [ ] `tempPath` 位于 `Temp/WebToUgui/<ViewName>/assets/`
- [ ] 没有同一路径的尺寸、类型、圆角冲突
- [ ] `sliced` 资源尺寸足够容纳四角和拉伸区
- [ ] 文本、价格、动态数值没有错误加入资源清单
- [ ] 可复用资源已查 `_StyleReference`
- [ ] 没有把 `design-master.png`、确认稿、拼合预览图登记为整屏 UI Sprite
- [ ] 没有用透明点击热区替代真实按钮、协议区或可交互控件资源

---

## 阶段 C：逐资源生成

### C1. 生成输入

每个资源生成时必须使用：

- `design-master.png` 的局部裁剪图
- `style-tokens.json`
- `asset-manifest.json` 中的规格参数
- 同族或下级已通过资源作为辅助参考

单资源 prompt 示例：

```text
【设计图参考】
从 design-master.png 裁剪该元素及周边区域，作为风格锚点。

【风格令牌】
读取 style-tokens.json 的 palette、materials、radii。

【规格】
- 文件路径: Assets/Resources/UI/ShopView/btn_tab_normal.png
- 输出尺寸: 128 × 60 px
- 渲染类型: sliced
- 圆角半径: 30px
- 复用语义: tab.normal
- 变体关系: tab.selected 的同族变体

【生成要求】
- 使用 Codex imagegen 生成位图，不得用代码绘制
- 不包含普通文字、数字、价格、倒计时
- 按钮/面板类使用 #FF00FF 纯品红背景，便于后续色键透明
- 图标类使用中性灰背景，便于后续抠图
- 不要在精灵外部烘焙阴影、光晕、环境反射
- 边缘清晰，不要羽化模糊
- 9-slice 资源四边可拉伸区域至少 4px
```

### C2. 生成顺序

严格按 `priority` 生成：

1. P0 背景 / 大面板：可引用 `design-master.png` 全图作为参考，但输出必须是独立背景或大面板资源；禁止直接把完整确认稿作为背景图
2. P1 面板 / 卡片：以背景为下级参考；用品红背景生成后色键透明
3. P2 按钮底图：重点检查 9-slice 拉伸区、圆角和状态变体一致性
4. P3 图标：叠加到按钮或面板上检查对比度
5. P4 装饰：保持色彩和材质一致，不喧宾夺主

同优先级可以并行准备 prompt 和参考图，但进入下一优先级前，当前批次必须全部验证通过。

### C3. 透明度后处理

imagegen 原始输出通常不含 alpha。按类型处理：

| 资源类型 | 处理方式 | 命令 | 说明 |
| --- | --- | --- | --- |
| 按钮 / 面板 | 色键抠图 | `magick input.png -fuzz 10% -transparent "#FF00FF" -alpha set output.png` | 规则几何形状 |
| 图标 | rembg | `rembg i input.png output.png` | 不规则前景 |
| 全屏背景 | 跳过 | 不处理 | 不需要透明 |

ImageMagick、rembg 只能处理 imagegen 已生成的原始图，不能用于从零绘制最终资源。

透明结果检查：

- [ ] 边缘无白边、无杂色残留
- [ ] 圆角区域平滑
- [ ] 没有不该透明的区域被误清空
- [ ] 输出尺寸与 manifest 完全一致

### C4. 输出与日志

保留原始版本和通过版本：

```text
Temp/WebToUgui/<ViewName>/assets_raw/btn_buy.v01.png
Temp/WebToUgui/<ViewName>/assets/btn_buy.png
```

每次尝试记录到 `asset-generation-log.json`：

```json
[
  {
    "asset": "btn_tab_normal.png",
    "attempt": 1,
    "rawPath": "Temp/WebToUgui/ShopView/assets_raw/btn_tab_normal.v01.png",
    "status": "failed",
    "issue": "颜色偏蓝，与选中态不协调",
    "fixDirection": "降低蓝色通道，增加暖色",
    "promptHash": "prompt 的短哈希"
  },
  {
    "asset": "btn_tab_normal.png",
    "attempt": 2,
    "rawPath": "Temp/WebToUgui/ShopView/assets_raw/btn_tab_normal.v02.png",
    "status": "accepted",
    "outputPath": "Temp/WebToUgui/ShopView/assets/btn_tab_normal.png"
  }
]
```

中断后恢复逻辑：

```text
读取 asset-manifest.json
逐项检查 Temp/WebToUgui/<ViewName>/assets/<filename>
已存在则跳过
不存在则加入待生成列表
```

---

## 阶段 D：验证与交付

### D1. 拼合预览

复制原型为：

```text
Temp/WebToUgui/<ViewName>/previews/<ViewName>.preview.html
```

只修改预览副本，将 `Temp/WebToUgui/<ViewName>/assets/` 下的精灵通过本地路径引用进去，再用浏览器截图。不要直接修改原始 `<ViewName>.html`。

固定输出：

```text
Temp/WebToUgui/<ViewName>/previews/preview-composited.png
Temp/WebToUgui/<ViewName>/previews/diff.png
Temp/WebToUgui/<ViewName>/previews/asset-verification-report.json
```

### D2. 验证清单

- [ ] 每个精灵的位置、尺寸与设计图一致
- [ ] 整体色彩没有偏暖或偏冷
- [ ] 按钮、面板材质感与设计图一致
- [ ] 9-slice 拉伸后圆角和边缘不变形
- [ ] 图标在按钮底图上清晰可辨
- [ ] 相邻精灵之间无接缝或视觉断层
- [ ] 普通文案、数字、价格没有烘焙到图片里
- [ ] `asset-verification-report.json` 没有缺失资源、尺寸不符、透明异常
- [ ] 没有代码绘制的最终美术资源混入
- [ ] 没有把 `design-master.png`、确认稿或拼合预览图作为整屏主视觉 Sprite
- [ ] 没有用透明点击热区覆盖整屏主视觉图来冒充真实 UI 拼装

### D3. 问题处理

| 问题 | 处理方式 |
| --- | --- |
| 试图用确认稿整屏图加透明点击热区实现 UI | 立即停止，废弃该方案；回阶段 B/C 拆分并重新生成缺失或不匹配的资源图 |
| 资源是代码绘制而非 imagegen 生成 | 废弃资源，回阶段 A 或 C 用 imagegen 重新生成 |
| 单个精灵色彩偏差 | 回阶段 C 重新生成该精灵 |
| 多个精灵整体偏色 | 检查阶段 A prompt 和 `style-tokens.json` |
| 9-slice 拉伸异常 | 重新生成并增大拉伸区，或修正 Sprite border |
| 精灵之间视觉断层 | 检查 prompt 是否引用一致的设计图和风格令牌 |
| 普通文字被烘焙进图片 | 回阶段 B 修正清单，保留 UGUI Text 节点 |
| 已有通用资源重复生成 | 回阶段 B 修正 `reuseKey`，复用 `_StyleReference` |
| Unity 中 sliced 无效 | 检查 Sprite border，不只检查 `imageType` |
| 字体、标题书法或卡片内部图与确认稿不一致 | 生成独立艺术字、字体资源或卡片内部资源；不得改用整屏确认稿 |

### D4. 用户确认与 Unity 交付

展示以下内容给用户确认：

- `assets/` 下通过检查的精灵
- `preview-composited.png`
- `diff.png`
- `asset-verification-report.json`

确认时只确认由资源图拼合出的结果，不接受“整屏确认稿 + 透明热区”的替代方案。

确认后按 `asset-manifest.json` 复制：

```text
Temp/WebToUgui/<ViewName>/assets/bg_shop.png
  -> Assets/Resources/UI/<ViewName>/bg_shop.png

Temp/WebToUgui/<ViewName>/assets/btn_tab_normal.png
  -> Assets/Resources/UI/<ViewName>/btn_tab_normal.png
```

Unity 导入设置：

- PNG 导入为 `Sprite (2D and UI)`
- 启用透明通道
- `sliced` 资源必须设置 Sprite border
- 为 sliced 资源生成宽、高、等比三种拉伸预览

最终交付顺序：

1. 复制资源到 `Assets/Resources/UI/<ViewName>/`
2. 刷新并导入 Unity 资源
3. 设置 Sprite Importer
4. 执行 `ui.web_to_ugui_prefab`，`dry_run: true`
5. dry-run 无 error 后执行 `dry_run: false`
6. Unity Console 无 error 后才视为完成

---

## ShopView 示例

输入：

```text
用户需求：麻将游戏商城，中国风暖金配色，深色底
Temp/WebToUgui/ShopView/ShopView.html
Temp/WebToUgui/ShopView/ShopView.web.png
Temp/WebToUgui/ShopView/ShopView.visual-ui.json
```

`ShopView.html` 必须声明设计分辨率，例如 `1170x2532`。

阶段 A 产物：

```text
Temp/WebToUgui/ShopView/design-master.png
Temp/WebToUgui/ShopView/style-tokens.json
```

阶段 B 清单示例：

```text
P0: bg_shop.png                 1170×2532 simple
P1: bg_nav_bar.png              1170×332  simple
P1: btn_tab_selected.png        128×60    sliced
P1: btn_tab_normal.png          128×60    sliced   3 个引用
P2: btn_round.png               60×60     simple
P2: btn_currency_bg.png         98×72     sliced   2 个引用
P2: btn_buy.png                 144×80    sliced
P3: icon_back.png               30×36     simple
P3: icon_coin.png               44×44     simple
P3: icon_gem.png                44×44     simple
P3: icon_shop.png               72×72     simple
P3: icon_bag.png                72×72     simple
P3: icon_settings.png           72×72     simple
```

阶段 C 顺序：

1. `bg_shop.png`
2. `bg_nav_bar.png`
3. `btn_tab_selected.png`
4. `btn_tab_normal.png`
5. `btn_currency_bg.png`
6. `btn_buy.png`
7. `btn_round.png`
8. `icon_coin.png`
9. `icon_gem.png`
10. `icon_back.png`
11. `icon_shop.png`
12. `icon_bag.png`
13. `icon_settings.png`

以上每一步都必须用 Codex `imagegen` 产出原始图，再做透明处理和验证。

阶段 D：

1. 生成 `previews/ShopView.preview.html`
2. 输出 `preview-composited.png`、`diff.png`、`asset-verification-report.json`
3. 有问题回阶段 B 或 C
4. 用户确认后复制到 `Assets/Resources/UI/ShopView/`
5. 设置 Sprite Importer，尤其是 sliced border
6. 调用 `ui.web_to_ugui_prefab` dry-run，通过后正式生成 prefab
