const fs = require('fs'), crypto = require('crypto');
eval(fs.readFileSync('D:/mashbox-sdk/com.mg.mashbox.sdk/Development~/MGTerrainImplementation~/normal-strength.cjs', 'utf8').split('const base=')[0] + ';global.readGraph=read;');
const clone = x => JSON.parse(JSON.stringify(x));
const id = () => crypto.randomUUID().replaceAll('-', '');
const body = `Out = LowAngle;
#if !defined(SHADERGRAPH_PREVIEW)
    #if SHADERPASS != SHADERPASS_SHADOWS
        // Inverse-view +Z points back toward the camera. Its world Y is
        // positive when the camera looks down. Works for perspective and ortho.
        float3 towardCamera = normalize(mul((float3x3)UNITY_MATRIX_I_V, float3(0, 0, 1)));
        float downward = saturate(towardCamera.y);
        float blend = downward * downward * (3.0 - 2.0 * downward);
        Out = lerp(LowAngle, TopDown, blend);
    #endif
#endif`;
for (const name of ['MashBoxFoliageImpostor', 'MashBoxProbeLitFoliageImpostor']) {
    const path = 'MapiX/MashBox/ImpostorBaker/Resources/ImpostorRuntime/' + name + '.shadergraph';
    const a = readGraph(path), root = a[0];
    if (a.some(x => x.m_OverrideReferenceName === '_PixelDepthOffsetTopDown')) throw Error('Already patched');
    const low = a.find(x => x.m_DefaultReferenceName === '_PixelDepthOffset' && x.m_Type.endsWith('ShaderProperty'));
    const sourceNode = a.find(x => x.m_Property?.m_Id === low.m_ObjectId);
    const sourceSlot = a.find(x => x.m_ObjectId === sourceNode.m_Slots[0].m_Id);
    const consumers = root.m_Edges.filter(e => e.m_OutputSlot.m_Node.m_Id === sourceNode.m_ObjectId);
    if (consumers.length !== 1) throw Error('Unexpected depth offset consumers');
    const p = clone(low); p.m_ObjectId = id(); p.m_Guid.m_GuidSerialized = crypto.randomUUID();
    p.m_Name = p.m_RefNameGeneratedByDisplayName = 'Pixel Depth Offset Top Down';
    p.m_DefaultReferenceName = p.m_OverrideReferenceName = '_PixelDepthOffsetTopDown'; p.m_Value = 1;
    const pn = clone(sourceNode), ps = clone(sourceSlot);
    pn.m_ObjectId = id(); ps.m_ObjectId = id(); pn.m_Property.m_Id = p.m_ObjectId;
    pn.m_Slots = [{ m_Id: ps.m_ObjectId }]; pn.m_DrawState.m_Position.y += 100;
    const fn = clone(a.find(x => x.m_FunctionName === 'MGImpostorAORemap'));
    fn.m_ObjectId = id(); fn.m_FunctionName = 'MGImpostorAngleDepthOffset'; fn.m_Name = 'Camera Angle Depth Offset'; fn.m_FunctionBody = body;
    fn.m_DrawState.m_Position = { serializedVersion: '2', x: -380, y: 1700, width: 230, height: 140 };
    const slots = ['LowAngle', 'TopDown', 'Out'].map((label, index) => {
        const s = clone(sourceSlot); s.m_ObjectId = id(); s.m_Id = index;
        s.m_DisplayName = s.m_ShaderOutputName = label; s.m_SlotType = index === 2 ? 1 : 0;
        s.m_Value = s.m_DefaultValue = index === 0 ? 2 : 1; return s;
    });
    fn.m_Slots = slots.map(s => ({ m_Id: s.m_ObjectId }));
    const slot = (n, i) => ({ m_Node: { m_Id: n.m_ObjectId }, m_SlotId: i });
    consumers[0].m_OutputSlot = slot(fn, 2);
    root.m_Edges.push({ m_OutputSlot: slot(sourceNode, sourceSlot.m_Id), m_InputSlot: slot(fn, 0) }, { m_OutputSlot: slot(pn, ps.m_Id), m_InputSlot: slot(fn, 1) });
    root.m_Nodes.push({ m_Id: pn.m_ObjectId }, { m_Id: fn.m_ObjectId });
    const insert = list => list.splice(list.findIndex(x => x.m_Id === low.m_ObjectId) + 1, 0, { m_Id: p.m_ObjectId });
    insert(root.m_Properties);
    insert(a.find(x => x.m_Type.endsWith('.CategoryData') && x.m_ChildObjectList.some(p => p.m_Id === low.m_ObjectId)).m_ChildObjectList);
    a.push(p, pn, ps, fn, ...slots);
    const byId = new Map(a.map(x => [x.m_ObjectId, x]));
    if (byId.size !== a.length) throw Error('Duplicate IDs');
    for (const e of root.m_Edges) for (const key of ['m_OutputSlot', 'm_InputSlot']) {
        const s = e[key], n = byId.get(s.m_Node.m_Id);
        if (!n?.m_Slots.some(r => byId.get(r.m_Id)?.m_Id === s.m_SlotId)) throw Error('Invalid graph edge');
    }
    fs.writeFileSync(path, a.map(x => x.original?.json === JSON.stringify(x) ? x.original.raw : JSON.stringify(x, null, 4)).join('\n\n') + '\n');
    console.log('PASS ' + name + ': angle blend wired to visible depth offset; shadow input unchanged.');
}
let previous = 2;
for (let angle = 0; angle <= 90; angle++) {
    const t = Math.sin(angle * Math.PI / 180), value = 2 - t * t * (3 - 2 * t);
    if (value < 1 || value > 2 || value > previous) throw Error('Invalid angle interpolation');
    previous = value;
}
if (previous !== 1) throw Error('Top-down endpoint mismatch');
console.log('PASS: continuous monotonic 2-to-1 blend from horizontal to top-down.');
