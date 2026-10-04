export function validateLayout(node, parent) {
 const l=node.layout||{};
 const fail=message=>{throw Error(`layout_contract: ${node.id}: ${message}`)};
 if(l.mode && !['free','anchored','managed'].includes(l.mode))fail('unknown positioning mode');
 if(l.group && !['horizontal','vertical','grid'].includes(l.group))fail('unknown group');
 if(l.mode==='anchored'){
  if(l.anchorMin?.length!==2||l.anchorMax?.length!==2)fail('anchor pairs required');
  for(let i=0;i<2;i++)if(!Number.isFinite(l.anchorMin[i])||!Number.isFinite(l.anchorMax[i])||l.anchorMin[i]<0||l.anchorMax[i]>1||l.anchorMin[i]>l.anchorMax[i])fail('invalid anchors');
 }
 if(l.mode==='managed' && !parent?.layout?.group)fail('managed node needs parent layout');
 if(parent?.layout?.group && !l.ignore && l.mode!=='managed')fail('layout child has competing positioning');
 if(l.mode==='managed' && l.contentHeight)fail('parent and fitter both own height');
 if(l.padding && (l.padding.length!==4||l.padding.some(v=>!Number.isInteger(v)||v<0)))fail('padding requires four nonnegative integers');
 if(l.group==='grid'){
  const constraint=l.gridConstraint||'FixedColumnCount';
  if(!['Flexible','FixedColumnCount','FixedRowCount'].includes(constraint))fail('unknown grid constraint');
  if(!(l.cellWidth>0)||!(l.cellHeight>0)||(constraint!=='Flexible'&&!(l.columns>0)))fail('invalid grid dimensions');
 }
 for(const child of node.children||[])validateLayout(child,node);
}
