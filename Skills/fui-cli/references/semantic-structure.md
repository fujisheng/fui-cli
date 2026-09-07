# UI 语义结构契约（web-to-ugui-1.1）

HTML 是结构来源，禁止通过手改提取 JSON 或 prefab 修补层级。按钮底图、文字和图标应当在 DOM 中属于按钮；显式 owner 可用于迁移旧布局，不能仅按空间重叠猜测归属。

- `data-ui-id`：视图内唯一稳定 ID；绑定名称不因重组改变。
- `data-ui-owner`：父节点 ID；省略时使用 DOM 语义父节点。规范化后 JSON children 是唯一层级来源。
- `data-ui-part`：如 background、label、icon、control，说明部件用途。
- `data-ui-target`：按钮反馈使用的子树 Graphic ID（Image、RawImage 或 Text）；仅有一个 background 时可自动解析。不能指向兄弟节点。
- `data-ui-binding="none"`：纯装饰 Image/Text/Container 不生成 FUI 元素包装；不要用于交互控件。
- `data-ui-hit-region="true"`：显式无视觉热区；普通按钮不能依赖空透明图形作为唯一视觉。
- `data-ui-preview-only="true"` 或 `data-ui-implementation="true"`：整个子树仅用于网页展示，不导出。
- `data-ui-clip="true"`：标明遮罩有实际用途；未声明用途的遮罩会告警，不自动删掉必要裁切。

九宫格采用一个 `data-ui-type="Image"` 节点，设置 `data-image-type="sliced"`、`data-sprite-border="left,bottom,right,top"`（源图像素）和可选 `data-pixels-per-unit-multiplier`。同一 Sprite 导入边距必须一致；不同显示厚度通过每个 Image 的倍率控制。网页可用 CSS border-image，不要导出九块裁切容器和像素子节点。`data-sprite-alpha="1"` 可明确图片 tint alpha，避免透明 CSS 背景误当图片透明度。

```html
<button data-ui-id="BattleButton" data-ui-type="Button" data-ui-target="BattleBackground">
  <img data-ui-id="BattleBackground" data-ui-type="Image" data-ui-part="background" data-ui-binding="none" src="battle.png">
  <span data-ui-id="BattleLabel" data-ui-type="Text" data-ui-part="label">开始对战</span>
</button>
```

提取出的 rect 始终是设计画布绝对坐标；重组 children 不改 rect。Unity 仅在生成 RectTransform 时换算一次父级相对坐标。

提取器拒绝重复 ID、丢失 owner、循环依赖、无视觉按钮、错误 target 和展开的九宫格节点。Unity 生成器再次校验树结构、装饰标记、Sprite 边距与源图尺寸；实际写入前完整 dry-run，已有错误不保存 prefab。此预检不等于磁盘写入事务，异常 I/O 仍需保留备份。

验证：在 scripts/extract-visual-ui 运行 `npm test`；之后正式提取、工具 dry-run/生成、Unity 编译与渲染、FUI 绑定和交互检查。截图相似不能替代层级检查。控件移位、隐藏和缩放时，其所属部件应随父节点整体变化。


## UGUI 基础控件完整结构检查（2026-09-07）

本契约覆盖 UnityEngine.UI（UGUI）基础控件，不等同于 UI Toolkit 或 TextMeshPro。`Text/Input/Dropdown` 明确生成 UnityEngine.UI 对应类型；TMP、自定义控件或未知 data-ui-type 必须显式扩展映射，禁止默默降级为 Image。Canvas、CanvasScaler、GraphicRaycaster 仍由视图根节点生成；EventSystem 由运行环境统一提供，不在每个 prefab 内重复创建。

| data-ui-type | 结构和引用规则 |
|---|---|
| Image | 单个 Image，支持 simple/sliced/tiled/filled；装饰可 binding=none |
| RawImage | 单个 RawImage，ui-sprite 指向 Texture 资产路径 |
| Text | 单个 UGUI Text；可声明为控件文字或独立文案 |
| Button | 可见自身图形或子树 target；保持背景/文字/图标归属 |
| Toggle | 必须有唯一 Image/RawImage checkmark；可选 background；生成 graphic 引用 |
| ToggleGroup | 子 Toggle 自动绑定最近的 ToggleGroup，可选 allowSwitchOff |
| Slider | fill 和 handle 至少一个；允许合法的仅填充或仅滑块样式，角色不可重复 |
| Scrollbar | 必须有唯一 Image/RawImage handle |
| Input / InputField | 必须有 Text text，placeholder 为可选 Text；不额外自动创建文字 |
| Dropdown | 必须有 Text caption 和 Container/ScrollView template；模板内一个 Toggle item，item 内 Text item-label 和 checkmark；模板生成后关闭 |
| ScrollView | 必须有 viewport 和其子树内 content；可选 horizontal-scrollbar/vertical-scrollbar；只使用声明的结构 |
| Mask | 单个 Image + Mask，showMaskGraphic 可配置 |
| RectMask2D | 单个矩形裁切组件，无多余可见图片；在 viewport 中按需补透明射线图形 |
| CanvasGroup | 整组透明度、交互及射线开关 |
| HorizontalLayoutGroup / VerticalLayoutGroup / GridLayoutGroup | 内容属于布局节点；不要再由业务脚本逐个按画布坐标排版 |
| ContentSizeFitter / AspectRatioFitter / LayoutElement | 参与尺寸约束，避免同一轴上与父布局争抢尺寸控制权 |
| Container / Panel | RectTransform 语义分组；只有需要可见背景时才创建 Image |

角色统一使用 `data-ui-part`，如 `fill`、`handle`、`checkmark`、`text`、`placeholder`、`caption`、`template`、`item`、`item-label`、`viewport`、`content`。生成器不会再按 fill/knob/thumb 等节点名称猜测基础复合控件引用。嵌套控件拥有自己的部件，父控件不得借用它的内部部件。

Slider 的 fill 和 handle 通常分别置于 FillArea / HandleArea 下，轨道行程由区域 RectTransform 控制；不能通过业务脚本硬编码滑块宽度或跨父级坐标补偿。数值文字可放在所属设置行中，不强制成为 Slider 子节点。Scrollbar 的 handle 同理。

`data-ui-options` 接受 JSON 对象，配置以下基础属性（枚举使用 Unity 名称，区分大小写）：

- 通用交互：interactable；Toggle：isOn；ToggleGroup：allowSwitchOff。
- Input：characterLimit、lineType（SingleLine/MultiLineSubmit/MultiLineNewline）、contentType。
- CanvasGroup：alpha、interactable、blocksRaycasts；Mask：showMaskGraphic。
- Image：preserveAspect、fillMethod、fillAmount、fillOrigin、fillClockwise；类型仍用 data-image-type。
- 布局：spacing、padding（四边相同）、cellWidth、cellHeight。
- ContentSizeFitter：horizontalFit、verticalFit；AspectRatioFitter：aspectRatio、aspectMode。
- LayoutElement：preferredWidth、preferredHeight、flexibleWidth、flexibleHeight。

可通过 data-ui-component 把布局约束组件附加到语义节点；不得用任意组件名规避已声明控件的部件验证。当前参数不是所有 Unity 组件属性的完整序列化接口；如需高级布局参数须增加明确字段及测试。

可运行样例：`scripts/extract-visual-ui/examples/ugui-controls.html`。结构单测为同目录 structure-contract.test.mjs；Unity 菜单 `FUI/Validate UGUI Control Structure` 使用可销毁实例验证真实组件引用、Slider 数值驱动、输入文字同步、模板状态和无效结构拦截，不写资源。

迁移旧页面必须先修改 HTML，再提取并预检；不能为通过校验把普通控件改成 Container 或独立热区。FUI 的 ListView/StaticList/Template 是额外框架能力，保留原有模板生成协议，不应与 UGUI ScrollView 的显式 viewport/content 混用。DuelFront 已将 42 个 V4 页面迁移到这些结构；其他项目仍需先迁移 HTML。


## 整页迁移补充规则

- 保留绘制顺序；父级背景位于内容后方，卡片的图标和文字必须归到正确的条目。
- CSS border-image 有边框定位偏移，进度轨道与填充应放在共同的无边框容器中。进度使用 Filled Image 的 fillAmount，不再修改宽度和补偿子节点位置。
- Slider/Scrollbar 驱动锚点会参与最终尺寸计算，应保留素材外扩量，不能再次叠加完整父区域尺寸。
- DOM 视图根折叠成 Canvas 时保留显式纯色背景，模态视图的整屏遮罩负责变暗和拦截射线。
- InputField 的长度和输入模式以作者声明为准，运行时代码不得统一覆盖。
- 纯文字链接允许子 Text 作为 Button 的 targetGraphic。
- 原生 UGUI 节点无需为了工具检查而添加 FUI 包装。选择器会在已解析视图内查找唯一的原生节点，重名时报错，不改变列表 itemIndex/child 的语义。
