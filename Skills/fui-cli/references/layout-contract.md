# Layout, collections and screen adaptation

Use `data-ui-layout` JSON in the authoring DOM. The extractor preserves it as `node.layout`;
reference rects describe the initial pose, not the sole runtime positioning policy.

- `mode`: `free` for artwork/local control parts; `anchored` for responsive regions;
  `managed` for children positioned by a parent layout. Do not bake runtime positions for managed items.
- Anchored nodes require `anchorMin`, `anchorMax` pairs in Unity bottom-left coordinates;
  optional `pivot`. The compiler derives offsets from reference rects once, preserving design margins.
- `group`: `horizontal`, `vertical`, `grid`; `spacingX/Y`, `padding` in left/right/top/bottom order,
  `alignment` as Unity TextAnchor, `controlWidth/Height`, `expandWidth/Height`.
- Managed children declare min/preferred/flexible width/height; `preferredHeight:-2` delegates height
  to the native Text or other ILayoutElement provider. Omitted preferred sizes use the reference rect.
- `ignore:true` excludes a decorative child from the parent layout without discarding its anchors.
- Grid uses positive `columns`, `cellWidth`, `cellHeight`. No implicit responsive column inference.
- `contentHeight:true` adds a vertical ContentSizeFitter to an anchored content node. It must not
  compete with a parent layout owning the same height.
- `safeArea:true` adds WebSafeArea. It fits the reference content width and minimum usable height
  into Screen.safeArea; tall screens gain usable height, wide screens retain a centered content column.
  Background art belongs outside this node. Verify safe-area changes at runtime.

## Collections

Distinguish finite authored controls from data records. Finite controls may use LayoutGroup even
when their count never changes. For dynamic records, use an explicit ScrollView / viewport / content
with one Template and an FUI ScrollListElement. Bind Items to an ObservableList of item ViewModels;
item bindings belong to the nested View, not the page. Resolve commands from item identity, never
from sibling index or copied sample names. Preserve item ViewModel identity when refreshing.

ScrollListElement reuses items but is not assumed to virtualize offscreen items. Use pagination
or a separately verified virtualization strategy for unbounded data. Do not combine virtual-list
position calculations with an independent LayoutGroup calculating the same positions.

## Validation

The Node layout contract and Unity preflight reject missing/inverted anchors, unknown modes,
managed children without layout ownership, competing positions, invalid padding/grid dimensions,
and fitter/parent height conflicts. Declare unsupported CSS constraints explicitly before import;
never silently infer flex/grid from visual overlap.

When changing this pipeline, run Node contract tests, real Editor compilation/Console checks,
and Unity layout checks at short/tall phone and tablet ratios with top/bottom safe insets.
Exercise dynamic lists with zero, one and many records, add/remove/refresh, long text, scrolling
both ends and item commands. Restore synthetic data after tests and avoid business submissions.
A single-size screenshot or successful binding audit does not establish adaptation correctness.
