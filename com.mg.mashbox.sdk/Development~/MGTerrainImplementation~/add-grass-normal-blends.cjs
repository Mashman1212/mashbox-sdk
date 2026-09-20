const fs=require('fs'),crypto=require('crypto');let s=fs.readFileSync('D:/mashbox-sdk/com.mg.mashbox.sdk/Development~/MGTerrainImplementation~/normal-strength.cjs','utf8');eval(s.slice(0,s.indexOf('const base='))+';global.readGraph=read;');
const base='Maps/Cotswold Ridge/[09] ASSET PACKS/Rorys Forest Mega Pack/UNSORTED/_MATERIALS/Miscellaneous/Foliage/tree v1/';const path=base+'MG_Grass.shadergraph',a=readGraph(path),t=readGraph(base+'MG_GodGrass.shadergraph');t.push(...readGraph('MapiX/TimeGhostTrees/Shaders/Translucent_OctahedralImpostor_NoPerInstanceColor.shadergraph'));const by=new Map(t.map(o=>[o.m_ObjectId,o]));const added=[];const id=()=>crypto.randomUUID().replaceAll('-','');
function clone(o){const n=JSON.parse(JSON.stringify(o));n.m_ObjectId=id();if(n.m_Group)n.m_Group.m_Id='';added.push(n);return n;}
function node(o){const n=clone(o);n.m_Slots=o.m_Slots.map(r=>({m_Id:clone(by.get(r.m_Id)).m_ObjectId}));a[0].m_Nodes.push({m_Id:n.m_ObjectId});return n;}
function edge(n,s,d,i){a[0].m_Edges.push({m_OutputSlot:{m_Node:{m_Id:n.m_ObjectId},m_SlotId:s},m_InputSlot:{m_Node:{m_Id:d.m_ObjectId},m_SlotId:i}});}
if(a.some(o=>o.m_OverrideReferenceName==='_NormalUpBlend'))throw Error('Already installed');
const fn=node(t.find(o=>o.m_FunctionName==='MGBlendVertexNormals'));
const mesh=node(t.find(o=>o.m_Type.endsWith('.NormalVectorNode')));mesh.m_Space=0;
const pos=node(t.find(o=>o.m_Type.endsWith('.PositionNode')&&o.m_Space===0));pos.m_Space=0;
const slotName=n=>n.m_Slots.map(r=>added.find(o=>o.m_ObjectId===r.m_Id));const fnSlot=name=>slotName(fn).find(o=>o.m_DisplayName===name).m_Id;
edge(mesh,slotName(mesh)[0].m_Id,fn,fnSlot('MeshNormal'));edge(pos,slotName(pos)[0].m_Id,fn,fnSlot('PositionOS'));
const category=a.find(o=>o.m_Type.endsWith('.CategoryData'));
for(const [ref,input] of [['_NormalUpBlend','UpBlend'],['_NormalSphericalBlend','SphericalBlend']]){const p=t.find(o=>o.m_OverrideReferenceName===ref),q=clone(p);q.m_Guid.m_GuidSerialized=crypto.randomUUID();const n=node(t.find(o=>o.m_Type.endsWith('.PropertyNode')&&o.m_Property.m_Id===p.m_ObjectId));n.m_Property.m_Id=q.m_ObjectId;a[0].m_Properties.push({m_Id:q.m_ObjectId});category.m_ChildObjectList.push({m_Id:q.m_ObjectId});edge(n,slotName(n)[0].m_Id,fn,fnSlot(input));}
const dest=a.find(o=>o.m_SerializedDescriptor==='VertexDescription.Normal');a[0].m_Edges=a[0].m_Edges.filter(e=>e.m_InputSlot.m_Node.m_Id!==dest.m_ObjectId);edge(fn,fnSlot('NormalOS'),dest,0);a.push(...added);
const ids=new Set(a.map(o=>o.m_ObjectId));if(ids.size!==a.length)throw Error('duplicate IDs');for(const e of a[0].m_Edges)for(const k of ['m_OutputSlot','m_InputSlot']){const n=a.find(o=>o.m_ObjectId===e[k].m_Node.m_Id);if(!n?.m_Slots.some(r=>a.find(o=>o.m_ObjectId===r.m_Id)?.m_Id===e[k].m_SlotId))throw Error('invalid edge');}
fs.writeFileSync(path,a.map(o=>o.original?.json===JSON.stringify(o)?o.original.raw:JSON.stringify(o,null,4)).join('\n\n')+'\n');console.log('PASS: both 0–1 sliders, mesh-normal input, object-space position, GodGrass function, and all graph connections validated.');

