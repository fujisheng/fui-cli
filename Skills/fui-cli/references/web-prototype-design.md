# Web 原型设计规范

编写用于 `extract-visual-ui.mjs` 提取的 Web 原型时，必须遵守本文约定。本文默认约束的是 **Layout Probe（布局定位稿）**：用于确认 FUI element 的位置、尺寸、层级、命名、控件类型和基础视觉语义，不是最终美术稿。

提取脚本只读取特定 `data-*` 属性和少量 CSS 样式，其余 CSS 不会进入 `visual-ui.json`。

> **工具支持状态标注**：✅ 已提取并由 prefab 生成端应用；📄 仅提取到 `visual-ui.json` 或作为元信息保留；⚠️ 行为需注意。

## 目标与边界

### Layout Probe 的目标

- 确认每个 element 的位置、尺寸、层级和父子关系
- 确认 `data-ui-id` 命名、`data-ui-type` 类型和关键控件参数
- 用纯色表达必要的视觉语义，例如高亮、选中、禁用、警告、特殊效果范围、稀有度、交互状态
- 生成稳定的 `visual-ui.json`，再通过 `ui.web_to_ugui_prefab` 生成 UGUI/FUI prefab

### 不负责的内容

- 不表达最终贴图、真实图标、纹理、渐变、阴影、滤镜、动画
- 不用 CSS 模拟 Unity 运行时效果
- 不自动接入 ViewModel/Presenter 绑定逻辑
- 不会凭布局猜测 Safe Area；需要通过 `data-ui-component` 显式声明运行时组件

## 核心硬规则

### 纯色语义块

所有带 `data-ui-id` 的可提取 UI 元素默认必须使用**纯色矩形**表达。

**允许：**

- `background-color`：任意纯色或 `rgba()` 半透明色，用于表达面板、按钮、状态、高亮、遮罩、特效范围
- `color`：文字颜色，可表达状态或强调
- `opacity` / `rgba()` alpha：半透明遮罩、禁用态、效果区域
- `border`：1-2px 纯色线框，仅用于 Web 预览中的选中框、范围框、调试边界；不要依赖它进入 prefab

**禁止：**

- `data-ui-sprite`、`background-image`、真实 sprite、图标图片、纹理图
- `linear-gradient()`、`radial-gradient()`、渐变色
- `box-shadow`、`text-shadow`、发光阴影
- `filter`、`backdrop-filter`、`mask`、`clip-path`
- `transform`、`animation`、`transition` 作为视觉或位置依据
- 圆角、复杂边框、装饰性伪元素作为最终视觉依据

需要表达发光、粒子、技能范围、选中态等特殊效果时，使用额外的半透明纯色矩形节点表示其范围、层级和颜色倾向。例如 `SelectedHighlight`、`SkillEffectArea`。

### 两阶段资源规则

1. **Layout Probe 阶段**：不写 `data-ui-sprite`，所有进入 prefab 的节点先用纯色块。
2. **正式皮肤阶段**：位置确认后，才把需要真实图片的纯色块替换为 `data-ui-sprite`。

正式皮肤阶段的 Sprite 路径应统一到项目约定目录：

```text
✅ Assets/Resources/UI/<ViewName>/icon_xxx.png
❌ Assets/Resources/Texture/xxx.png       （混用多套路径）
```

> CSS `background-image` 不会进入 `visual-ui.json`。即使进入正式皮肤阶段，也应使用 `data-ui-sprite`，不要依赖 CSS 背景图。

## 基础 HTML 结构

```html
<!doctype html>
<html lang="zh-CN">
<head>
  <meta charset="utf-8">
  <meta name="fui-design-resolution" content="<设计宽>x<设计高>">
  <meta name="viewport" content="width=<设计宽>, height=<设计高>, initial-scale=1">
  <title>ViewName</title>
  <style>
    * { box-sizing: border-box; user-select: none; }
    html, body {
      width: <设计宽>px; height: <设计高>px;
      margin: 0; overflow: hidden;
      font-family: "Microsoft YaHei", sans-serif;
    }
  </style>
</head>
<body>
  <div data-ui-id="ViewName" data-ui-type="Container"
       data-design-width="<设计宽>" data-design-height="<设计高>"
       style="position: relative; width: <设计宽>px; height: <设计高>px;">
    <!-- 子元素 -->
  </div>
</body>
</html>
```

**约束：**

- 原型 HTML 必须显式声明设计分辨率，优先使用 `<meta name="fui-design-resolution" content="1170x2532">`
- 根容器建议同时写 `data-design-width` / `data-design-height`，便于工具和人工检查
- `html, body` 尺寸必须等于设计分辨率
- 除根容器外，所有 `data-ui-id` 元素必须使用 `position: absolute`
- 根容器可以使用 `position: relative`，但内部子元素必须 `absolute`
- 如果根容器 `data-ui-id` 等于 `--view` 传入的 View 名，生成端会展开根容器，只把子节点写入 prefab 根下
- 如果根容器使用 `RootView`，prefab 中会保留一个 `RootView` 子节点

## 坐标、层级与命名

### 坐标与尺寸

提取脚本使用 `getBoundingClientRect()` 读取位置和尺寸。

```html
<!-- ✅ 推荐：inline style 写明位置和尺寸 -->
<div data-ui-id="StartButton" data-ui-type="Button"
     style="position: absolute; left: 68px; top: 94px; width: 240px; height: 88px;
            background-color: #2E8B57;"></div>

<!-- ❌ 避免：主要坐标藏在 class 中，降低可读性 -->
<div data-ui-id="StartButton" class="start-button"></div>
```

**规则：**

- `left/top/width/height` 使用设计分辨率下的像素值
- 子元素坐标相对于父容器左上角
- 不用百分比、`vw/vh`、`transform: translate/scale` 作为最终坐标来源
- 生成工具会统一处理 RectTransform 坐标映射，具体行为见 `@references/web-to-ugui-prefab.md`

### Z 序

UGUI 渲染顺序 = sibling index，与 DOM 顺序一致。HTML 中按从底到上的顺序书写。

```html
<div data-ui-id="GameView" data-ui-type="Container" ...>
  <div data-ui-id="BackgroundBlock" data-ui-type="Image" ...></div>
  <div data-ui-id="ContentPanel" data-ui-type="Container" ...></div>
  <div data-ui-id="TopBar" data-ui-type="Container" ...></div>
</div>
```

跨 Container 层级时，父节点的 DOM 顺序决定整个子树的整体 Z 序。

### 命名规范

`data-ui-id` 是 prefab 节点名，也是 FUI 绑定标识符。

| 元素类型      | 命名模式                   | 示例                          |
| ------------- | -------------------------- | ----------------------------- |
| 按钮          | `动词 + Button`          | `BackButton`, `BuyButton` |
| 文本          | `内容 + Text`            | `TitleText`, `ScoreText`  |
| 图标/图片占位 | `内容 + Icon/Image/Bg`   | `CoinIcon`, `CardBg`      |
| 容器/面板     | `功能 + Panel/Group/Bar` | `TopBar`, `HintGroup`     |
| 列表          | `内容 + List`            | `ItemList`, `LevelList`   |
| 输入框        | `字段 + Input`           | `NameInput`                 |
| 开关          | `功能 + Toggle`          | `SoundToggle`               |
| 滚动视图      | `区域 + Scroll`          | `HelpScroll`                |

**规则：**

- 使用 PascalCase，如 `BackButton`
- 名字描述内容/功能，不描述颜色或临时视觉表现
- 同类元素用序号区分：`Slot0`, `Slot1`, `Slot2`
- 避免重复种类名：`ButtonButton` ❌

## 属性映射

### `data-ui-type` → UGUI 组件

| `data-ui-type` | 生成组件 | 状态 | 说明 |
| -------------- | -------- | ---- | ---- |
| `Button` | `ButtonElement` | ✅ | 可点击按钮 |
| `Text` | `TextElement` | ✅ | 文本标签，提取 innerText |
| `Image` | `ImageElement` | ✅ | 纯色块/图片占位，默认值 |
| `Input` | `InputFieldElement` | ✅ | 文本输入框 |
| `Toggle` | `ToggleElement` | ✅ | 开关/复选框 |
| `Container` | `Container` | ✅ | 分组、布局锚点 |
| `Panel` | `Container` | ✅ | 同 Container |
| `ScrollView` | `ScrollView` | ✅ | 可滚动区域 |
| `ListView` | `ListView` | ✅ | 动态列表 |
| `Grid` | `ListView` | ✅ | 同 ListView |
| `StaticList` | `StaticListViewElement` | ✅ | 使用首个 Template 作为 itemPrefab 的静态列表 |
| `Template` | `Template` | ✅ | 动态模板节点 |
| `DynamicView` | `DynamicViewElement` | ✅ | 按 Source 动态加载子 View/角色展示 |
| `Mask` | `ImageElement` + `Mask` | ✅ | 使用 Sprite alpha 裁剪子图片 |
| `Star` | `StarElement` | ✅ | 图片星级；每个直属子节点的第一个子节点为点亮图片 |
| `Slider` | `SliderElement` | ✅ | 滑动条 |
| `Dropdown` | `DropdownElement` | ✅ | 下拉选择框 |
| `Scrollbar` | `ScrollbarElement` | ✅ | 滚动条 |

> 省略 `data-ui-type` 时默认为 `ImageElement`。

### 数据属性一览

| 属性                                   | 状态 | 用途                               | 示例值                                              |
| -------------------------------------- | ---- | ---------------------------------- | --------------------------------------------------- |
| `data-ui-id`                         | ✅   | 唯一标识，必填                     | `"BackButton"`                                    |
| `data-ui-type`                       | ✅   | 组件类型                           | `"Button"`                                        |
| `data-design-width`                  | ✅   | 设计分辨率宽度，通常写在根容器     | `"1170"`                                          |
| `data-design-height`                 | ✅   | 设计分辨率高度，通常写在根容器     | `"2532"`                                          |
| `data-ui-sprite`                     | ✅   | Sprite 资源路径，Layout Probe 禁用 | `"Assets/Resources/UI/GameView/icon_coin.png"`    |
| `data-image-type`                    | ✅   | Image 渲染类型                     | `"simple"`, `"sliced"`                          |
| `data-list-layout`                   | ✅   | 列表排列方向                       | `"vertical"`, `"horizontal"`                    |
| `data-list-binding`                  | 📄   | 列表数据绑定元信息                 | `"Items"`                                         |
| `data-item-view`                     | 📄   | 列表项 View 名元信息               | `"LevelNodeItem"`                                 |
| `data-row-view`                      | 📄   | 行 View 名元信息                   | —                                                  |
| `data-scroll-direction`              | ✅   | 滚动方向                           | `"vertical"`, `"horizontal"`                    |
| `data-scroll-movement`               | ✅   | 滚动边界行为                       | `"elastic"`, `"clamped"`, `"unrestricted"`    |
| `data-scroll-inertia`                | ✅   | 是否启用惯性                       | `"true"`, `"false"`                             |
| `data-grid-constraint`               | ✅   | 网格约束模式                       | `"fixedColumnCount"`, `"fixedRowCount"`         |
| `data-grid-count`                    | ✅   | 网格行列数                         | `3`                                               |
| `data-cell-width`                    | ✅   | 单元格宽度                         | `160`                                             |
| `data-cell-height`                   | ✅   | 单元格高度                         | `180`                                             |
| `data-spacing-x/y`                   | ✅   | 间距，缺省回退 CSS gap             | `12`                                              |
| `data-padding-left/right/top/bottom` | ✅   | 内边距，缺省回退 CSS padding       | `8`                                               |
| `data-text-overflow`                 | ✅   | 水平溢出方式                       | `"wrap"`, `"overflow"`                          |
| `data-text-truncate`                 | ✅   | 垂直截断方式                       | `"truncate"`, `"overflow"`                      |
| `data-text-best-fit`                 | ✅   | 自适应字号                         | `"true"`, `"false"`, `"8-36"`                 |
| `data-slider-min-value`              | ✅   | 滑动条最小值                       | `0`                                               |
| `data-slider-max-value`              | ✅   | 滑动条最大值                       | `100`                                             |
| `data-slider-value`                  | ✅   | 滑动条当前值                       | `50`                                              |
| `data-slider-direction`              | ✅   | 滑动条方向                         | `"leftToRight"`, `"topToBottom"`                |
| `data-slider-whole-numbers`          | ✅   | 仅整数                             | `"true"`, `"false"`                             |
| `data-dropdown-options`              | ✅   | 选项列表，逗号分隔                 | `"低,中,高"`                                      |
| `data-dropdown-value`                | ✅   | 当前选中索引                       | `0`                                               |
| `data-scrollbar-direction`           | ✅   | 滚动条方向                         | `"vertical"`, `"horizontal"`, `"bottomToTop"` |
| `data-scrollbar-size`                | ✅   | 滑块尺寸，比例或像素               | `0.25` 或 `60`                                  |
| `data-scrollbar-value`               | ✅   | 当前位置                           | `0`                                               |
| `data-template-kind`                 | 📄   | 模板类型元信息                     | —                                                  |
| `data-template-view`                 | 📄   | 模板 View 名元信息                 | —                                                  |

## 可提取样式

### 颜色与透明度

提取脚本读取 `background-color`。

| CSS 写法             | 提取结果                  | 典型用途               |
| -------------------- | ------------------------- | ---------------------- |
| `#3A1608`          | `"#3A1608"`             | 纯色面板               |
| `rgba(0,0,0,0.01)` | `"#000000"`, alpha=0.01 | 透明占位区域、点击热区 |
| `transparent`      | 空字符串                  | 完全透明、无背景       |

颜色可以表达视觉语义，但必须保持纯色。例如选中态可用金色半透明块，危险状态可用红色块，特殊效果范围可用蓝紫色半透明块。

### 文本样式

提取脚本从 `getComputedStyle` 读取以下文本属性。

| CSS 属性        | 提取到              | 说明                                  |
| --------------- | ------------------- | ------------------------------------- |
| `font-size`   | `text.fontSize`   | 必须用 px，如 `58px`                |
| `font-weight` | `text.fontWeight` | `"400"`, `"700"`, `"900"`       |
| `color`       | `text.color`      | 支持 `#FFF`, `rgba()`             |
| `text-align`  | `text.alignment`  | `"left"`, `"center"`, `"right"` |
| innerText       | `text.content`    | 直接子文本节点                        |

```html
<div data-ui-id="TitleText" data-ui-type="Text"
     style="position: absolute; left: 120px; top: 80px; width: 930px; height: 80px;
            font-size: 58px; font-weight: 900; color: #F9E6A5; text-align: center;">关卡 11</div>
```

**不支持或禁用：**

- `line-height` 不会提取到 JSON
- `text-shadow` 在 Layout Probe 中禁用；正式文字描边/阴影在 Unity/FUI 侧处理
- `text-overflow`、`word-break` 不提取；使用下面的 `data-*` 参数声明

### 文本溢出与自适应

UGUI Text 的溢出和自适应参数通过 `data-*` 属性声明。

| 属性                   | 说明                                                   | 推荐值                        |
| ---------------------- | ------------------------------------------------------ | ----------------------------- |
| `data-text-overflow` | 水平溢出：`"wrap"` 换行 / `"overflow"` 不换行      | 按钮标签用 `"overflow"`     |
| `data-text-truncate` | 垂直截断：`"truncate"` 截断 / `"overflow"` 溢出    | 固定高度文本用 `"truncate"` |
| `data-text-best-fit` | 自适应字号：`"true"` / `"false"` / 范围 `"8-36"` | 默认 `"false"`              |

```html
<div data-ui-id="ButtonLabelText" data-ui-type="Text"
     data-text-overflow="overflow"
     style="position: absolute; left: 20px; top: 16px; width: 200px; height: 48px;
            font-size: 28px; color: #FFFFFF; text-align: center;">开始游戏</div>
```

## 组件写法

### Container / Panel

用 Container 包裹同一功能模块，便于 prefab 中整体移动、复用或绑定 ViewModel。

```html
<div data-ui-id="LevelPanel" data-ui-type="Container"
     style="position: absolute; left: 50px; top: 600px; width: 1070px; height: 1500px;
            background-color: rgba(32, 48, 72, 0.72);">
  <div data-ui-id="SectionHeaderBg" data-ui-type="Image"
       style="position: absolute; left: 0; top: 0; width: 332px; height: 118px;
              background-color: #A56B2A;"></div>
  <div data-ui-id="SectionTitleText" data-ui-type="Text"
       style="position: absolute; left: 78px; top: 22px; width: 190px; height: 56px;
              font-size: 50px; font-weight: 900; color: #FFE88C; text-align: center;">关卡</div>
</div>
```

如果只是布局锚点且不需要可见背景，可以不写 `background-color`。如果需要不可见点击热区，可用 `rgba(0,0,0,0.01)`。

### Button

按钮的文字、图标占位、数量标记等应嵌套在按钮内部。

```html
<button data-ui-id="HintButton" data-ui-type="Button"
        style="position: absolute; left: 89px; top: 2140px; width: 214px; height: 280px;
               background-color: #2E8B57; border: 2px solid #FFE88C;">
  <div data-ui-id="HintIcon" data-ui-type="Image"
       style="position: absolute; left: 57px; top: 36px; width: 100px; height: 88px;
              background-color: #9EE6B8;"></div>
  <div data-ui-id="HintLabelText" data-ui-type="Text"
       style="position: absolute; left: 30px; top: 130px; width: 154px; height: 54px;
              font-size: 38px; font-weight: 900; color: #FFF0BB; text-align: center;">提示</div>
  <div data-ui-id="HintCountText" data-ui-type="Text"
       style="position: absolute; left: 30px; top: 190px; width: 154px; height: 52px;
              font-size: 34px; font-weight: 900; color: #FFF0BB; text-align: center;">3</div>
</button>
```

避免把按钮、图标、文字平铺成兄弟节点：

```html
<!-- ❌ 避免 -->
<button data-ui-id="HintButton" data-ui-type="Button" ...></button>
<div data-ui-id="HintIcon" data-ui-type="Image" ...></div>
<div data-ui-id="HintLabelText" data-ui-type="Text" ...>提示</div>
```

### 图标占位

Layout Probe 中不要用 Unicode 符号或真实图标图片表达图标。用纯色 `Image` 占位确认位置和尺寸。

```html
<!-- ❌ 避免：Unicode 字符作图标 -->
<div data-ui-id="BackIconText" data-ui-type="Text" ...>←</div>

<!-- ✅ 推荐：纯色块图标占位 -->
<div data-ui-id="BackIcon" data-ui-type="Image"
     style="position: absolute; left: 98px; top: 121px; width: 90px; height: 86px;
            background-color: #8BD3FF;"></div>
```

纯文本标签（如“关卡”“提示”“撤销”）无需改成图片。正式皮肤阶段再把需要真实图标的纯色块替换为 `data-ui-sprite`。

### ListView / Grid

动态列表不要在 HTML 中硬编码所有 item。声明 ListView 容器，并最多保留 1 个 Template 子节点用于视觉验证。

```html
<div data-ui-id="LevelList" data-ui-type="ListView"
     data-list-layout="vertical"
     data-list-binding="LevelItems"
     data-item-view="LevelNodeItem"
     data-scroll-direction="vertical"
     data-spacing-y="160"
     style="position: absolute; left: 80px; top: 650px; width: 1010px; height: 1500px;">
  <!-- 最多保留 1 个 Template 子节点 -->
</div>
```

Grid 模式：

```html
<div data-ui-id="TileGrid" data-ui-type="Grid"
     data-grid-constraint="fixedColumnCount"
     data-grid-count="3"
     data-cell-width="160"
     data-cell-height="180"
     data-spacing-x="10" data-spacing-y="10"
     style="position: absolute; left: 80px; top: 700px; width: 520px; height: 620px;">
</div>
```

间距和 padding 优先读取 `data-spacing-*` / `data-padding-*`，未设置时回退到 CSS `gap` / `padding`。

### ScrollView

`data-ui-type="ScrollView"` 用于非列表的通用滚动区域，如帮助文本、设置面板。

```html
<div data-ui-id="HelpScroll" data-ui-type="ScrollView"
     data-scroll-direction="vertical"
     data-scroll-movement="elastic"
     data-scroll-inertia="true"
     style="position: absolute; left: 40px; top: 200px; width: 670px; height: 900px;
            background-color: rgba(20, 30, 46, 0.80);">
  <div data-ui-id="HelpContent" data-ui-type="Text"
       style="position: absolute; left: 20px; top: 20px; width: 630px; height: 1200px;
              font-size: 26px; color: #FFFFFF;">帮助文本内容...</div>
</div>
```

| 属性                      | 说明                                               | 默认值         |
| ------------------------- | -------------------------------------------------- | -------------- |
| `data-scroll-direction` | `"vertical"` / `"horizontal"`                  | `"vertical"` |
| `data-scroll-movement`  | `"elastic"` / `"clamped"` / `"unrestricted"` | `"clamped"`  |
| `data-scroll-inertia`   | `"true"` 惯性减速 / `"false"` 立即停止         | `"true"`     |

ScrollView 内部应放一个内容容器，其宽/高可超出 ScrollView 自身尺寸以产生滚动。

### Slider

```html
<div data-ui-id="VolumeSlider" data-ui-type="Slider"
     data-slider-min-value="0" data-slider-max-value="100"
     data-slider-value="50" data-slider-direction="leftToRight"
     data-slider-whole-numbers="true"
     style="position: absolute; left: 40px; top: 200px; width: 400px; height: 60px;">
  <div data-ui-id="SliderTrack" data-ui-type="Image"
       style="position: absolute; left: 0; top: 24px; width: 400px; height: 12px;
              background-color: #4A5568;"></div>
  <div data-ui-id="SliderFill" data-ui-type="Image"
       style="position: absolute; left: 0; top: 24px; width: 200px; height: 12px;
              background-color: #39C16C;"></div>
  <div data-ui-id="SliderHandle" data-ui-type="Image"
       style="position: absolute; left: 184px; top: 6px; width: 48px; height: 48px;
              background-color: #FFFFFF; border: 2px solid #39C16C;"></div>
</div>
```

### Dropdown

```html
<div data-ui-id="QualityDropdown" data-ui-type="Dropdown"
     data-dropdown-options="低,中,高,超高"
     data-dropdown-value="1"
     style="position: absolute; left: 40px; top: 300px; width: 280px; height: 60px;
            background-color: #34495E; border: 2px solid #5DADE2;">
  <div data-ui-id="DropdownLabel" data-ui-type="Text"
       style="position: absolute; left: 16px; top: 12px; width: 220px; height: 36px;
              font-size: 28px; font-weight: 700; color: #FFFFFF; text-align: left;">中</div>
  <div data-ui-id="DropdownArrow" data-ui-type="Image"
       style="position: absolute; left: 240px; top: 14px; width: 32px; height: 32px;
              background-color: #5DADE2;"></div>
</div>
```

### Scrollbar

```html
<div data-ui-id="VScrollbar" data-ui-type="Scrollbar"
     data-scrollbar-direction="vertical"
     data-scrollbar-size="80"
     style="position: absolute; left: 720px; top: 0; width: 12px; height: 400px;">
  <div data-ui-id="ScrollbarTrack" data-ui-type="Image"
       style="position: absolute; left: 0; top: 0; width: 12px; height: 400px;
              background-color: #26364A;"></div>
  <div data-ui-id="ScrollbarHandle" data-ui-type="Image"
       style="position: absolute; left: 0; top: 0; width: 12px; height: 80px;
              background-color: #8BD3FF;"></div>
</div>
```

`data-scrollbar-size` 可写 0-1 的归一化比例，也可写像素值。像素值会按滚动条主轴长度换算为 Unity `Scrollbar.size`。

## 视觉参考层

纯参考层不要加 `data-ui-id`，这些元素不会进入 prefab。

**不要标记：**

- 背景图、天空、地面、山体、建筑、云、光效
- 角色立绘、怪物、武器、装饰物件
- 纯装饰边框、纹理、粒子、阴影

如果某个装饰元素上需要放 UI 文字，只给文字加 `data-ui-id`，不标记装饰本身。

## Safe Area（移动端适配）

项目目标平台为 Android + iOS。设计时应为设备安全区域预留空间。

| 区域     | 建议留白 | 说明                                    |
| -------- | -------- | --------------------------------------- |
| 顶部     | ≥ 60px  | 状态栏 / 刘海区域，不放可交互元素       |
| 底部     | ≥ 40px  | Home Indicator 区域，不放按钮等交互元素 |
| 左右边缘 | ≥ 20px  | 曲面屏边缘，不放关键内容                |

```html
<!-- 顶部安全区域占位，不加 data-ui-id -->
<div style="position: absolute; left: 0; top: 0; width: <设计宽>px; height: 60px;"></div>

<!-- 实际 UI 从安全区域下方开始 -->
<div data-ui-id="TopBar" data-ui-type="Container"
     style="position: absolute; left: 0; top: 60px; width: <设计宽>px; height: 120px;">
  ...
</div>
```

Safe Area 的具体像素值依赖目标设备。需要运行时适配时，把全屏背景留在安全区容器外，并在前景容器上显式声明组件：

```html
<div data-ui-id="SafeAreaPanel" data-ui-type="Container"
     data-ui-component="SafeAreaAdapter"
     style="position:absolute; left:0; top:0; width:<设计宽>px; height:<设计高>px;">
  ...
</div>
```

`data-ui-component` 必须填写 Unity 当前已加载、继承自 `Component` 的类型名；找不到类型时 dry-run 会报错。

## 生成前检查清单

- [ ] HTML 已显式声明设计分辨率，且 `html, body` 尺寸等于设计分辨率
- [ ] `data-ui-id` 不重复，根容器命名策略明确：展开根容器用 View 名，保留根容器用 `RootView`
- [ ] 除根容器外，所有可提取节点都有明确的 `position:absolute; left/top/width/height`
- [ ] 不使用 `transform: scale/translate`、百分比、`vw/vh` 作为最终坐标来源
- [ ] 布局定位稿中的 `data-ui-id` 节点都使用纯色矩形
- [ ] 颜色只表达状态、高亮、禁用、特殊效果范围等视觉语义
- [ ] 布局定位稿不使用 `data-ui-sprite`、`background-image`、渐变、阴影、滤镜、纹理、动画
- [ ] 纯视觉参考层、角色、装饰图不加 `data-ui-id`
- [ ] 按钮的文字、图标占位、徽章等子元素嵌套在 Button 内部
- [ ] ListView 最多保留 1 个 Template 子节点
- [ ] 如果已进入正式皮肤阶段，新增的 `data-ui-sprite` 指向项目内存在的 `Assets/...` Sprite，且不混用资源目录
- [ ] 运行 `ui.web_to_ugui_prefab` dry-run，确认 `issues` 为空，`warnings` 可解释

## 已知限制

| 限制 | 说明 |
| ---- | ---- |
| 不支持 flexbox/grid 布局 | 必须使用绝对定位，布局不会自动转换为 UGUI Layout Group |
| 仅读取 `borderTopLeftRadius` | 提取脚本假设四角圆角一致；Layout Probe 应保持矩形 |
| 不支持 CSS `background-image` | Layout Probe 禁用；正式皮肤阶段也应使用 `data-ui-sprite` |
| `text-shadow` 不提取 | Layout Probe 禁用；正式文字描边/阴影在 Unity/FUI 侧处理 |
| `line-height` 不提取 | prefab 使用默认行高 |
| `border-radius` 可能产生 warning | Layout Probe 应保持矩形；已知 `border_radius_not_supported` 可忽略 |
| ListView 绑定元信息不自动接线 | `data-list-binding` / `data-item-view` / `data-row-view` 会进入 JSON，但运行时数据绑定仍由 ViewModel/Presenter 接入 |
| Safe Area 不会自动推断 | 将背景留在容器外，并在 `SafeAreaPanel` 上声明 `data-ui-component="SafeAreaAdapter"` |

## 参考原型

- `@examples/ShopView.html` — 商城页面结构参考，涵盖按钮嵌套、Container 分组、ListView 声明、Z 序控制等设计原则。如果示例中包含 Sprite 路径，应按正式皮肤阶段参考；新的布局定位稿默认改用纯色语义块。
