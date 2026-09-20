const fs = require('fs');
const crypto = require('crypto');
function read(path) {
  const s=fs.readFileSync(path,'utf8'), objects=[];
  let depth=0,start=0,quoted=false,escaped=false;
  for(let i=0;i<s.length;i++) { const c=s[i];
    if(quoted){if(escaped)escaped=false;else if(c==='\\')escaped=true;else if(c==='"')quoted=false;continue;}
    if(c==='"')quoted=true;
    else if(c==='{'){if(depth++===0)start=i;}
    else if(c==='}' && --depth===0){const raw=s.slice(start,i+1),o=JSON.parse(raw);Object.defineProperty(o,'original',{value:{raw,json:JSON.stringify(o)}});objects.push(o);}
  } return objects;
}
const base='MapiX/MashBox/ImpostorBaker/Resources/ImpostorRuntime/';
const template=read('MapiX/TimeGhostGrass/Source/Art/Vegetation/Shader/foliage_sg.shadergraph');
const normal=template.find(x=>x.m_Type?.endsWith('.NormalStrengthNode'));
const clone=x=>JSON.parse(JSON.stringify(x));
const id=()=>crypto.randomUUID().replaceAll('-','');
const edge=(from,slot,to,input)=>({m_OutputSlot:{m_Node:{m_Id:from},m_SlotId:slot},m_InputSlot:{m_Node:{m_Id:to},m_SlotId:input}});
for(const name of ['MashBoxProbeLitFoliageImpostor','MashBoxFoliageImpostor']) {
 const a=read(base+name+'.shadergraph');
 if(a.some(x=>x.m_OverrideReferenceName==='_ImpostorNormalStrength'))throw Error('Already patched '+name);
 const smooth=a.find(x=>x.m_Name==='Smoothness' && x.m_Type.endsWith('ShaderProperty'));
 const prop=clone(smooth);prop.m_ObjectId=id();prop.m_Guid.m_GuidSerialized=crypto.randomUUID();
 prop.m_Name='Normal Strength';prop.m_DefaultReferenceName=prop.m_OverrideReferenceName='_ImpostorNormalStrength';prop.m_RefNameGeneratedByDisplayName='Normal Strength';prop.m_Value=1;prop.m_RangeValues={x:0,y:2};
 const pn=clone(a.find(x=>x.m_Type.endsWith('.PropertyNode') && x.m_Property.m_Id===smooth.m_ObjectId));
 const ps=clone(a.find(x=>x.m_ObjectId===pn.m_Slots[0].m_Id));ps.m_ObjectId=id();pn.m_ObjectId=id();pn.m_Property.m_Id=prop.m_ObjectId;pn.m_Slots=[{m_Id:ps.m_ObjectId}];pn.m_DrawState.m_Position.x=150;pn.m_DrawState.m_Position.y=1000;
 const n=clone(normal);n.m_ObjectId=id();n.m_Group.m_Id='';n.m_DrawState.m_Position.x=380;n.m_DrawState.m_Position.y=1150;
 const slots=normal.m_Slots.map(s=>{const x=clone(template.find(t=>t.m_ObjectId===s.m_Id));x.m_ObjectId=id();return x;});n.m_Slots=slots.map(x=>({m_Id:x.m_ObjectId}));
 const frag=a.find(x=>x.m_Name==='Octahedral Impostor Fragment');
 const normalSlot=frag.m_Slots.map(s=>a.find(x=>x.m_ObjectId===s.m_Id)).find(x=>x.m_SlotType===1 && /normal/i.test(x.m_DisplayName));
 if(!normalSlot)throw Error('Missing normal output');
 const outgoing=a[0].m_Edges.filter(e=>e.m_OutputSlot.m_Node.m_Id===frag.m_ObjectId && e.m_OutputSlot.m_SlotId===normalSlot.m_Id);
 if(outgoing.length!==1)throw Error('Unexpected normal consumers');
 outgoing.forEach(e=>{e.m_OutputSlot.m_Node.m_Id=n.m_ObjectId;e.m_OutputSlot.m_SlotId=2;});
 a[0].m_Edges.push(edge(frag.m_ObjectId,normalSlot.m_Id,n.m_ObjectId,0),edge(pn.m_ObjectId,ps.m_Id,n.m_ObjectId,1));
 a[0].m_Nodes.push({m_Id:pn.m_ObjectId},{m_Id:n.m_ObjectId});
 const insert=list=>list.splice(list.findIndex(x=>x.m_Id===smooth.m_ObjectId),0,{m_Id:prop.m_ObjectId});
 insert(a[0].m_Properties);insert(a.find(x=>x.m_Type.endsWith('.CategoryData') && x.m_ChildObjectList.some(p=>p.m_Id===smooth.m_ObjectId)).m_ChildObjectList);
 a.push(prop,pn,ps,n,...slots);
 const ids=new Set(a.map(x=>x.m_ObjectId));if(ids.size!==a.length)throw Error('Duplicate IDs');
 for(const e of a[0].m_Edges)for(const k of ['m_OutputSlot','m_InputSlot']){const s=e[k],node=a.find(x=>x.m_ObjectId===s.m_Node.m_Id);if(!node || !node.m_Slots.some(r=>a.find(x=>x.m_ObjectId===r.m_Id)?.m_Id===s.m_SlotId))throw Error('Invalid edge');}
 fs.writeFileSync(base+name+'.shadergraph',a.map(x=>x.original?.json===JSON.stringify(x)?x.original.raw:JSON.stringify(x,null,4)).join('\n\n')+'\n');
 console.log('PASS '+name+': slider 0–2, default 1; '+outgoing.length+' normal consumers rewired; all graph edges valid.');
}
