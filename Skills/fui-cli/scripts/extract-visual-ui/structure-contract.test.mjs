import test from 'node:test';
import assert from 'node:assert/strict';
import { normalizeStructure } from './structure-contract.mjs';
const node=(id,extra={})=>({id,element:'ImageElement',rect:{x:30,y:40,width:50,height:20},style:{color:'#fff',alpha:1},children:[],...extra});
const plan=nodes=>({nodes});
test('explicit owner builds a control subtree without rewriting design-space coordinates',()=>{
 const icon=node('Icon',{owner:'Button',part:'background'}),before={...icon.rect};
 const p=normalizeStructure(plan([icon,node('Button',{element:'ButtonElement',style:{alpha:0}})]));
 assert.equal(p.nodes.length,1);assert.equal(p.nodes[0].children[0].id,'Icon');assert.equal(p.nodes[0].targetGraphic,'Icon');assert.deepEqual(icon.rect,before);
});
test('nested DOM hierarchy is preserved',()=>assert.equal(normalizeStructure(plan([node('Group',{children:[node('Icon')]})])).nodes[0].children.length,1));
test('duplicate identifiers are errors, not silently discarded',()=>assert.throws(()=>normalizeStructure(plan([node('A'),node('A')])),/duplicate_ui_id/));
test('unknown owner is rejected',()=>assert.throws(()=>normalizeStructure(plan([node('A',{owner:'missing'})])),/owner_not_found/));
test('ownership cycle is rejected',()=>assert.throws(()=>normalizeStructure(plan([node('A',{owner:'B'}),node('B',{owner:'A'})])),/ownership_cycle/));
test('empty invisible buttons require explicit hit-region intent',()=>{
 const button=node('Button',{element:'ButtonElement',style:{alpha:0}});
 assert.throws(()=>normalizeStructure(plan([button])),/button_visual_missing/);
 button.hitRegion=true;assert.equal(normalizeStructure(plan([button])).nodes.length,1);
});
test('target cannot reference a sibling outside the control',()=>assert.throws(()=>normalizeStructure(plan([node('B',{element:'ButtonElement',targetGraphic:'Icon'}),node('Icon')])),/invalid_button_target/));
test('expanded web slices and missing native borders are rejected',()=>{
 assert.throws(()=>normalizeStructure(plan([node('Panel_Slice0')])),/expanded_slice_nodes/);
 assert.throws(()=>normalizeStructure(plan([node('Panel',{style:{imageType:'sliced'}})])),/sliced_border_missing/);
 const p=normalizeStructure(plan([node('Panel',{style:{imageType:'sliced',spriteBorder:[4,6,4,8]}})]));assert.equal(p.nodes.length,1);
});
for (const type of ['SliderElement','ScrollbarElement','ToggleElement','InputFieldElement','DropdownElement','ScrollView']) {
 test(`${type} rejects a control with no declared parts`,()=>assert.throws(()=>normalizeStructure(plan([node('Control',{element:type})])),/control_part|dropdown/));
}
test('slider accepts fill-only and handle-only controls, rejects ambiguous handles',()=>{
 for(const role of ['fill','handle'])assert.doesNotThrow(()=>normalizeStructure(plan([node('S',{element:'SliderElement',children:[node('Part',{part:role})]})])));
 assert.throws(()=>normalizeStructure(plan([node('S',{element:'SliderElement',children:[node('A',{part:'handle'}),node('B',{part:'handle'})]})])),/control_part_count/);
});
test('nested controls cannot donate their parts to an ancestor',()=>assert.throws(()=>normalizeStructure(plan([node('Outer',{element:'SliderElement',children:[node('Inner',{element:'SliderElement',children:[node('Handle',{part:'handle'})]})]})])),/control_part_missing/));
test('input accepts explicit text and placeholder and rejects an image as text',()=>{
 const input=node('I',{element:'InputFieldElement',children:[node('Text',{element:'TextElement',part:'text'}),node('Placeholder',{element:'TextElement',part:'placeholder'})]});
 assert.doesNotThrow(()=>normalizeStructure(plan([input])));input.children[0].element='ImageElement';assert.throws(()=>normalizeStructure(plan([input])),/control_part_type/);
});
test('scroll content must belong to viewport',()=>{
 const content=node('Content',{element:'Container',part:'content'}),vp=node('Viewport',{element:'RectMask2D',part:'viewport',children:[content]});
 assert.doesNotThrow(()=>normalizeStructure(plan([node('Scroll',{element:'ScrollView',children:[vp]})])));
 vp.children=[];assert.throws(()=>normalizeStructure(plan([node('Scroll',{element:'ScrollView',children:[vp,content]})])),/scroll_content_outside_viewport/);
});
test('dropdown requires a template with an item Toggle and label',()=>{
 const item=node('Item',{element:'ToggleElement',part:'item',children:[node('Check',{part:'checkmark'}),node('ItemLabel',{element:'TextElement',part:'item-label'})]});
 const d=node('D',{element:'DropdownElement',children:[node('Caption',{element:'TextElement',part:'caption'}),node('Template',{element:'Container',part:'template',children:[item]})]});
 assert.doesNotThrow(()=>normalizeStructure(plan([d])));item.children.pop();assert.throws(()=>normalizeStructure(plan([d])),/control_part_count/);
});

test('unknown controls and decorative interaction controls are rejected',()=>{
 assert.throws(()=>normalizeStructure(plan([node('Unknown',{element:'TMP_InputField'})])),/unsupported_ui_type/);
 assert.throws(()=>normalizeStructure(plan([node('B',{element:'ButtonElement',binding:'none'})])),/invalid_decorative_control/);
});
test('text links use an owned Text graphic for visible button feedback',()=>{
 const p=normalizeStructure(plan([node('Link',{element:'ButtonElement',style:{alpha:0},targetGraphic:'Label',children:[node('Label',{element:'TextElement',text:{content:'Help'},part:'label'})]})]));assert.equal(p.nodes[0].targetGraphic,'Label');
});
