import {validateLayout} from './layout-contract.mjs';
// Normalize explicit ownership independently of browser layout. Rects remain design-space pixels;
// the Unity builder converts them relative to the resolved parent exactly once.
export function normalizeStructure(plan) {
  const flat = [], byId = new Map(), parent = new Map();
  function collect(nodes, domParent = '') {
    for (const node of nodes) {
      if (!node.id || byId.has(node.id)) throw new Error(`duplicate_ui_id: ${node.id}`);
      byId.set(node.id, node); flat.push(node);
      parent.set(node.id, node.owner || domParent);
      collect(node.children || [], node.id);
    }
  }
  collect(plan.nodes);
  for (const node of flat) {
    const visited = new Set([node.id]);
    let id = parent.get(node.id);
    while (id) {
      if (!byId.has(id)) throw new Error(`owner_not_found: ${node.id} -> ${id}`);
      if (visited.has(id)) throw new Error(`ownership_cycle: ${node.id}`);
      visited.add(id); id = parent.get(id);
    }
  }
  for (const node of flat) node.children = [];
  plan.nodes = [];
  for (const node of flat) {
    const owner = parent.get(node.id);
    (owner ? byId.get(owner).children : plan.nodes).push(node);
  }
  for(const node of plan.nodes)validateLayout(node,null);
  const descendants = node => (node.children || []).flatMap(child => [child, ...descendants(child)]);
  const visible = node => Boolean(node.style?.sprite) || Boolean(node.text?.content?.trim()) ||
    (Boolean(node.style?.color) && (node.style?.alpha ?? 1) > 0);
  const controls = new Set(['ButtonElement','SliderElement','ScrollbarElement','ToggleElement','InputFieldElement','DropdownElement','ScrollView']);
  // A nested control owns its own parts; its internals must never satisfy its parent's slots.
  const partsOf = node => (node.children || []).flatMap(child => [child, ...(controls.has(child.element) ? [] : partsOf(child))]);
  const requirePart = (node, role, types, required=true) => {
    const matches=partsOf(node).filter(n=>n.part===role);
    if(matches.length>1 || (required && !matches.length)) throw new Error(`control_part_count: ${node.id}.${role}`);
    if(matches.length && types && !types.includes(matches[0].element)) throw new Error(`control_part_type: ${node.id}.${role}`);
    return matches[0];
  };
  const supported = new Set(['Container','ImageElement','TextElement','RawImage','ButtonElement','SliderElement','ScrollbarElement','ToggleElement','InputFieldElement','DropdownElement','ScrollView','ToggleGroup','CanvasGroup','MaskElement','RectMask2D','HorizontalLayoutGroup','VerticalLayoutGroup','GridLayoutGroup','ContentSizeFitter','AspectRatioFitter','LayoutElement','ListView','StaticListViewElement','Template','DynamicViewElement','StarElement']);
  const warnings = [];
  for (const node of flat) {
    if(!supported.has(node.element)) throw new Error(`unsupported_ui_type: ${node.element}`);
    if(node.binding==='none'&&controls.has(node.element))throw new Error(`invalid_decorative_control: ${node.id}`);
    if (node.owner && node.owner !== parent.get(node.id)) throw new Error(`invalid_owner: ${node.id}`);
    if (/_(Slice|Pixels)\d+$/.test(node.id)) throw new Error(`expanded_slice_nodes: ${node.id}; declare one sliced Image`);
    if (node.style?.imageType === 'sliced') {
      const border = node.style.spriteBorder;
      if (!Array.isArray(border) || border.length !== 4 || border.some(x => !Number.isFinite(x) || x < 0) || border.every(x => x === 0))
        throw new Error(`sliced_border_missing: ${node.id}; expected left,bottom,right,top source pixels`);
    }
    const image=['ImageElement','RawImage'], text=['TextElement'];
    if (['SliderElement','ScrollbarElement'].includes(node.element)) {
      const fill=node.element==='SliderElement' ? requirePart(node,'fill',image,false) : null;
      const handle=requirePart(node,'handle',image,node.element==='ScrollbarElement');
      if(!fill&&!handle) throw new Error(`control_part_missing: ${node.id}; fill or handle required`);
    }
    if(node.element==='ToggleElement') requirePart(node,'checkmark',image);
    if(node.element==='InputFieldElement') { requirePart(node,'text',text);requirePart(node,'placeholder',text,false); }
    if(node.element==='DropdownElement') {
      requirePart(node,'caption',text);const template=requirePart(node,'template',['Container','ScrollView']);
      const items=descendants(template).filter(n=>n.part==='item'&&n.element==='ToggleElement');
      if(items.length!==1) throw new Error(`dropdown_item_missing: ${node.id}`);
      requirePart(items[0],'item-label',text);
    }
    if(node.element==='ScrollView') {
      const viewport=requirePart(node,'viewport',['Container','RectMask2D','MaskElement']);
      const content=requirePart(node,'content',['Container','VerticalLayoutGroup','HorizontalLayoutGroup','GridLayoutGroup']);
      if(!descendants(viewport).includes(content))throw new Error(`scroll_content_outside_viewport: ${node.id}`);
      requirePart(node,'horizontal-scrollbar',['ScrollbarElement'],false);requirePart(node,'vertical-scrollbar',['ScrollbarElement'],false);
    }
    if (node.element === 'ButtonElement') {
      const children = descendants(node);
      if (!node.targetGraphic) {
        const backgrounds = children.filter(n => n.part === 'background');
        if (backgrounds.length > 1) throw new Error(`ambiguous_button_background: ${node.id}`);
        if (backgrounds.length === 1) node.targetGraphic = backgrounds[0].id;
      }
      if (node.targetGraphic && !children.some(n => n.id === node.targetGraphic && ['ImageElement','RawImage','TextElement'].includes(n.element) && visible(n)))
        throw new Error(`invalid_button_target: ${node.id} -> ${node.targetGraphic}`);
      if (!node.hitRegion && !visible(node) && !node.targetGraphic)
        throw new Error(`button_visual_missing: ${node.id}; own a background or declare data-ui-hit-region="true"`);
    }
    if (node.component?.includes('RectMask2D') && !node.clip) warnings.push({code:'mask_without_intent',node:node.id});
  }
  plan.schemaVersion = 'web-to-ugui-1.1';
  plan.structureReport = {nodes:flat.length,buttons:flat.filter(n=>n.element==='ButtonElement').length,warnings};
  return plan;
}
