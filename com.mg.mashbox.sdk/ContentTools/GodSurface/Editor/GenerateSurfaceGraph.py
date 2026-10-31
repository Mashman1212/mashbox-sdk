"""Regenerate the array variant from the project's MG Lit Basic graph; preserves its shading network."""
import json, pathlib, uuid, copy, sys
ROOT = pathlib.Path(__file__).resolve().parents[1]
SOURCE = ROOT.parents[1]/'Shaders/HDRP/Lit/Surfaces/Basic/MG_Lit_Basic.shadergraph'
def read(path):
    s=path.read_text(); dec=json.JSONDecoder(); result=[]
    while s.strip():
        s=s.lstrip(); obj,n=dec.raw_decode(s); result.append(obj); s=s[n:]
    return result
objects=read(SOURCE); graph=objects[0]; by={o['m_ObjectId']:o for o in objects}
# Run from the authoring project's Assets directory, or pass that directory as the first argument.
grass=read((pathlib.Path(sys.argv[1]) if len(sys.argv)>1 else pathlib.Path.cwd())/'Shared Materials/MG_GodGrass_V2.shadergraph')
def ident(): return uuid.uuid4().hex
def ref(o): return {'m_Id':o['m_ObjectId']}
def add(o): objects.append(o); by[o['m_ObjectId']]=o; return o
def slot(kind, number, name, output=False, value=0):
    o={'m_SGVersion':0,'m_Type':'UnityEditor.ShaderGraph.'+kind+'MaterialSlot','m_ObjectId':ident(),'m_Id':number,'m_DisplayName':name,'m_SlotType':int(output),'m_Hidden':False,'m_ShaderOutputName':name,'m_StageCapability':3}
    if kind.startswith('Texture'):
        o['m_BareResource']=False
        o['m_TextureArray' if 'Array' in kind else 'm_Texture']={'m_SerializedTexture':'','m_Guid':''}
        if 'Array' not in kind: o['m_DefaultType']=0
    else:
        o.update(m_Value=value,m_DefaultValue=value,m_Labels=[])
    return add(o)
function_template=next(o for o in grass if o.get('m_FunctionName')=='MGGrassSampleRemapped')
property_node_template=copy.deepcopy(next(o for o in objects if o['m_Type'].endswith('.PropertyNode')))
array_template=next(o for o in grass if o['m_Type'].endswith('.Texture2DArrayShaderProperty'))
float_template=next(o for o in objects if o['m_Type'].endswith('.Vector1ShaderProperty'))
tex_template=copy.deepcopy(next(o for o in objects if o['m_Type'].endswith('.Texture2DShaderProperty')))
INCLUDE_GUID='b9bb36f7cff746ecbe91c5b1bf13d540'
def prop(template,name):
    p=copy.deepcopy(template); p.update(m_ObjectId=ident(),m_Name=name,m_DefaultReferenceName=name,m_OverrideReferenceName=name,m_RefNameGeneratedByDisplayName=name)
    p['m_Guid']={'m_GuidSerialized':str(uuid.uuid4())}; add(p); return p
def propnode(p,kind):
    node=copy.deepcopy(property_node_template);node.update(m_ObjectId=ident(),m_Property=ref(p),m_Group={'m_Id':''})
    node['m_Slots']=[ref(slot(kind,0,'Out',True))];add(node);graph['m_Nodes'].append(ref(node));return node
def connect(a,sa,b,sb):graph['m_Edges'].append({'m_OutputSlot':{'m_Node':ref(a),'m_SlotId':sa},'m_InputSlot':{'m_Node':ref(b),'m_SlotId':sb}})
def function(node,name,slots):
    keep={k:copy.deepcopy(node[k]) for k in ['m_ObjectId','m_DrawState','m_Group']}
    node.clear(); node.update(copy.deepcopy(function_template)); node.update(keep)
    node.update(m_Name=name+' (Custom Function)',m_FunctionName=name,m_FunctionSource=INCLUDE_GUID,m_Slots=[ref(s) for s in slots],m_Precision=1)
original_props=[by[r['m_Id']] for r in graph['m_Properties']]
scalars=[p for p in original_props if 'Texture' not in p['m_Type']]
maps=[next(p for p in original_props if (p.get('m_OverrideReferenceName') or p['m_DefaultReferenceName'])==n) for n in ['_BaseColorMap','_NormalMap','_MaskMap','_EmissiveColorMap','_DetailMap','_DecalMap']]
params=prop(tex_template,'_MGSurfaceParameters'); params['m_Value']={'m_SerializedTexture':'','m_Guid':''};params['m_DefaultType']=0
surface=prop(float_template,'_MGSurfaceID');surface.update(m_Value=0,m_FloatType=0,overrideHLSLDeclaration=True,hlslDeclarationOverride=3)
pn=propnode(params,'Texture2D');sn=propnode(surface,'Vector1')
original_nodes=[by[r['m_Id']] for r in graph['m_Nodes'] if r not in [ref(pn),ref(sn)]]
for node in original_nodes:
    if not node['m_Type'].endswith('.PropertyNode'):continue
    p=by[node['m_Property']['m_Id']]
    if p not in scalars:continue
    column=2+scalars.index(p);out=by[node['m_Slots'][0]['m_Id']];out['m_ShaderOutputName']='Out';out['m_DisplayName']='Out'
    kind='Color' if 'ColorShaderProperty' in p['m_Type'] else 'Bool' if 'Boolean' in p['m_Type'] else 'Float'
    function(node,'MGSurface'+kind,[slot('Texture2DInput',1,'Parameters'),slot('Vector1',2,'SurfaceID'),slot('Vector1',3,'Column',value=column),out])
    connect(pn,0,node,1);connect(sn,0,node,2)
array_props=[]
for index,p in enumerate(maps):
    original_id=p['m_ObjectId']; original_guid=p['m_Guid']; name='_MGSurface'+['Base','Normal','Mask','Emission','Detail','Decal'][index]+'Array'
    p.clear();p.update(copy.deepcopy(array_template));p.update(m_ObjectId=original_id,m_Guid=original_guid,m_Name=name,m_DefaultReferenceName=name,m_OverrideReferenceName=name,m_RefNameGeneratedByDisplayName=name)
    array_props.append(p)
    for node in original_nodes:
        if not node['m_Type'].endswith('.PropertyNode') or node['m_Property']['m_Id']!=original_id:continue
        output=by[node['m_Slots'][0]['m_Id']];output['m_Type']='UnityEditor.ShaderGraph.Texture2DArrayMaterialSlot'
        for edge in list(graph['m_Edges']):
            if edge['m_OutputSlot']['m_Node']['m_Id']!=node['m_ObjectId']:continue
            sample=by[edge['m_InputSlot']['m_Node']['m_Id']]
            assert sample['m_Type'].endswith('.SampleTexture2DNode')
            slots={by[r['m_Id']]['m_Id']:by[r['m_Id']] for r in sample['m_Slots']}
            uv=slots[2];uv['m_ShaderOutputName']='UV';uv['m_DisplayName']='UV'
            # Original sample uses UV0 when not explicitly connected. Preserve UVMaterialSlot binding.
            outputs=[slots[i] for i in [0,4,5,6,7]]
            for o,n in zip(outputs,['RGBA','R','G','B','A']):o['m_ShaderOutputName']=n;o['m_DisplayName']=n
            function(sample,'MGSurfaceSample',[slot('Texture2DArrayInput',1,'Layers'),uv,slot('Texture2DInput',8,'Parameters'),slot('Vector1',9,'SurfaceID'),slot('Vector1',10,'Channel',value=index),slot('Vector1',11,'STColumn',value=2+len(scalars)+index),slot('Vector1',12,'IsNormal',value=int(index==1))]+outputs)
            connect(pn,0,sample,8);connect(sn,0,sample,9)
# Interpolated vertex red stores the same isolated AO used by God Grass.
ao_strength=prop(next(o for o in grass if o.get('m_DefaultReferenceName')=='_GrassVertexAOStrength'),'_MGSurfaceVertexAOStrength')
ao_strength.update(m_Name='Vertex AO Strength',m_Value=0)
ao_property=propnode(ao_strength,'Vector1')
colour_template=next(o for o in grass if o['m_Type'].endswith('.VertexColorNode'))
colour=copy.deepcopy(colour_template);colour.update(m_ObjectId=ident(),m_Group={'m_Id':''})
colour['m_Slots']=[]
for r in colour_template['m_Slots']:
    original=next(o for o in grass if o['m_ObjectId']==r['m_Id'])
    copied=copy.deepcopy(original);copied['m_ObjectId']=ident();add(copied);colour['m_Slots'].append(ref(copied))
add(colour);graph['m_Nodes'].append(ref(colour))
combine=copy.deepcopy(function_template);combine['m_ObjectId']=ident();add(combine)
function(combine,'MGSurfaceVertexAO',[slot('Vector1',0,'TextureAO',value=1),slot('Vector4',1,'VertexColor',value={'x':1,'y':1,'z':1,'w':1}),slot('Vector1',2,'Strength'),slot('Vector1',3,'CombinedAO',True)])
graph['m_Nodes'].append(ref(combine))
occlusion=next(o for o in objects if o.get('m_SerializedDescriptor')=='SurfaceDescription.Occlusion')
edge=next(e for e in graph['m_Edges'] if e['m_InputSlot']['m_Node']['m_Id']==occlusion['m_ObjectId'])
edge['m_InputSlot']={'m_Node':ref(combine),'m_SlotId':0}
connect(colour,0,combine,1);connect(ao_property,0,combine,2);connect(combine,3,occlusion,0)
# Geometry-ID vertex reconstruction. Native meshes (ID -1) use their original attributes.
geometry_props=[]; geometry_nodes=[]
for name in ['_MGGeometryVertices','_MGGeometryIndices','_MGGeometryChunks']:
    p=prop(tex_template,name);p['m_Value']={'m_SerializedTexture':'','m_Guid':''};p['m_DefaultType']=0
    geometry_props.append(p);geometry_nodes.append(propnode(p,'Texture2D'))
gid=prop(float_template,'_MGGeometryID');gid.update(m_Value=-1,m_FloatType=0,overrideHLSLDeclaration=True,hlslDeclarationOverride=3)
geometry_props.append(gid);gid_node=propnode(gid,'Vector1')
vertex_blocks=[next(o for o in objects if o.get('m_SerializedDescriptor')=='VertexDescription.'+n) for n in ['Position','Normal','Tangent']]
def bound(block,number,name):
    o=copy.deepcopy(by[block['m_Slots'][0]['m_Id']]);o.update(m_ObjectId=ident(),m_Id=number,m_DisplayName=name,m_ShaderOutputName=name);return add(o)
inputs=[slot('Texture2DInput',0,'Vertices'),slot('Texture2DInput',1,'Indices'),slot('Texture2DInput',2,'Chunks'),slot('Vector1',3,'GeometryID')]
inputs += [bound(b,4+i,'Source'+n) for i,(b,n) in enumerate(zip(vertex_blocks,['Position','Normal','Tangent']))]
uv_template=next(o for o in objects if o['m_Type'].endswith('.UVMaterialSlot'))
for i in range(4):
    o=copy.deepcopy(uv_template);o.update(m_ObjectId=ident(),m_Id=7+i,m_DisplayName='SourceUV'+str(i),m_ShaderOutputName='SourceUV'+str(i),m_Channel=i);inputs.append(add(o))
inputs.append(slot('Vector4',11,'SourceColor',value={'x':1,'y':1,'z':1,'w':1}))
outputs=[slot('Vector3',12,'Position',True),slot('Vector3',13,'Normal',True),slot('Vector3',14,'Tangent',True),slot('Vector4',15,'UV01',True),slot('Vector4',16,'UV23',True),slot('Vector2',17,'AOSign',True)]
geo=copy.deepcopy(function_template);geo['m_ObjectId']=ident();add(geo);function(geo,'MGSurfaceGeometry',inputs+outputs);graph['m_Nodes'].append(ref(geo))
for i,node in enumerate(geometry_nodes):connect(node,0,geo,i)
connect(gid_node,0,geo,3);connect(colour,0,geo,11)
# Custom function input slots alone do not declare vertex UV requirements.
uv_source_template=next(o for o in grass if o['m_Type'].endswith('.UVNode'))
for i in range(4):
    n=copy.deepcopy(uv_source_template);n.update(m_ObjectId=ident(),m_OutputChannel=i,m_Slots=[ref(slot('Vector4',0,'Out',True))]);add(n);graph['m_Nodes'].append(ref(n));connect(n,0,geo,7+i)
for i,b in enumerate(vertex_blocks):connect(geo,12+i,b,0)
block_template=next(o for o in grass if o.get('m_Name')=='VertexDescription.CustomInterpolator')
interp_template=next(o for o in grass if o['m_Type'].endswith('.CustomInterpolatorNode'))
def interpolator(name,size,source,port):
    b=copy.deepcopy(block_template);b.update(m_ObjectId=ident(),m_SerializedDescriptor='VertexDescription.'+name+'#'+str(size),m_Slots=[ref(slot('Vector'+str(size),0,name))]);add(b);graph['m_VertexContext']['m_Blocks'].append(ref(b));graph['m_Nodes'].append(ref(b))
    connect(source,port,b,0)
    n=copy.deepcopy(interp_template);n.update(m_ObjectId=ident(),m_Name=name+' (Custom Interpolator)',customBlockNodeName=name,serializedType=size,m_Slots=[ref(slot('Vector'+str(size),0,'Out',True))]);add(n);graph['m_Nodes'].append(ref(n));return n
uv01=interpolator('MGGeometryUV01',4,geo,15);uv23=interpolator('MGGeometryUV23',4,geo,16);aosign=interpolator('MGGeometryAOSign',2,geo,17)
uv_nodes=[]
for i in range(4):
    n=copy.deepcopy(function_template);n['m_ObjectId']=ident();add(n);function(n,'MGSurfaceUV',[slot('Vector4',0,'UV01'),slot('Vector4',1,'UV23'),slot('Vector1',2,'Channel',value=i),slot('Vector2',3,'Out',True)]);graph['m_Nodes'].append(ref(n));connect(uv01,0,n,0);connect(uv23,0,n,1);uv_nodes.append(n)
# Redirect all implicit UV inputs, including UV-selection subgraphs, to reconstructed UVs.
for node in original_nodes:
    for r in node.get('m_Slots',[]):
        sl=by[r['m_Id']]
        if sl['m_Type'].endswith('.UVMaterialSlot') and not any(e['m_InputSlot']['m_Node']['m_Id']==node['m_ObjectId'] and e['m_InputSlot']['m_SlotId']==sl['m_Id'] for e in graph['m_Edges']):
            connect(uv_nodes[sl['m_Channel']],3,node,sl['m_Id'])
aocolor=copy.deepcopy(function_template);aocolor['m_ObjectId']=ident();add(aocolor);function(aocolor,'MGSurfaceAOColor',[slot('Vector2',0,'AOSign'),slot('Vector4',1,'Color',True)]);graph['m_Nodes'].append(ref(aocolor));connect(aosign,0,aocolor,0)
graph['m_Edges']=[e for e in graph['m_Edges'] if not (e['m_InputSlot']['m_Node']['m_Id']==combine['m_ObjectId'] and e['m_InputSlot']['m_SlotId']==1)]
connect(aocolor,1,combine,1)
normal_block=next(o for o in objects if o.get('m_SerializedDescriptor')=='SurfaceDescription.NormalTS')
normal_edge=next(e for e in graph['m_Edges'] if e['m_InputSlot']['m_Node']['m_Id']==normal_block['m_ObjectId'])
normal_sign=copy.deepcopy(function_template);normal_sign['m_ObjectId']=ident();add(normal_sign);function(normal_sign,'MGSurfaceTangentSign',[slot('Vector3',0,'Normal'),slot('Vector2',1,'AOSign'),slot('Vector3',2,'Out',True)]);graph['m_Nodes'].append(ref(normal_sign))
normal_edge['m_InputSlot']={'m_Node':ref(normal_sign),'m_SlotId':0};connect(aosign,0,normal_sign,1);connect(normal_sign,2,normal_block,0)
graph['m_Properties']=[ref(p) for p in array_props+[params,surface,ao_strength]+geometry_props];graph['m_CategoryData']=[]
graph['m_Path']='MGShaders/HDRP';graph['m_GraphPrecision']=1
for o in objects:
    if o['m_Type'].endswith('.HDTarget'):o['m_CustomEditorGUI']=''
# Remove orphaned authoring properties so source texture GUIDs cannot become shader dependencies.
seen=set()
def visit(o):
    if isinstance(o,dict):
        for k,v in o.items():
            if k=='m_Id' and isinstance(v,str) and v in by and v not in seen:seen.add(v);visit(by[v])
            elif k!='m_ObjectId':visit(v)
    elif isinstance(o,list):
        for v in o:visit(v)
seen.add(graph['m_ObjectId']);visit(graph)
folder=ROOT/'Shaders';folder.mkdir(exist_ok=True)
(folder/'God Surface.shadergraph').write_text('\n\n'.join(json.dumps(o,indent=4) for o in objects if o['m_ObjectId'] in seen)+'\n')
def name(p):return p.get('m_OverrideReferenceName') or p['m_DefaultReferenceName']
cs='// Generated with GenerateSurfaceGraph.py. Column layout shared with the shader graph.\nnamespace MG.GodSurface.Editor { internal static class MGSurfaceSchema {\n'
cs+='internal static readonly string[] Maps = { '+', '.join(json.dumps(n) for n in ['_BaseColorMap','_NormalMap','_MaskMap','_EmissiveColorMap','_DetailMap','_DecalMap'])+' };\n'
cs+='internal static readonly string[] Arrays = { '+', '.join(json.dumps(name(p)) for p in array_props)+' };\n'
cs+='internal static readonly string[] Parameters = { '+', '.join(json.dumps(name(p)) for p in scalars)+' };\n'
cs+='internal static readonly int[] Kinds = { '+', '.join('1' if 'Color' in p['m_Type'] else '2' if 'Boolean' in p['m_Type'] else '0' for p in scalars)+' };\n'
cs+='} }\n'
(ROOT/'Editor/MGSurfaceSchema.cs').write_text(cs)
print('Generated',len(seen),'graph objects;',len(scalars),'parameters; 6 independent arrays')

