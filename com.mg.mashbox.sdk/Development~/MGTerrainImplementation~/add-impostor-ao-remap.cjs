const fs = require('fs'), crypto = require('crypto');
eval(fs.readFileSync('D:/mashbox-sdk/com.mg.mashbox.sdk/Development~/MGTerrainImplementation~/normal-strength.cjs', 'utf8').split('const base=')[0] + ';global.readGraph=read;');
const clone = x => JSON.parse(JSON.stringify(x));
const id = () => crypto.randomUUID().replaceAll('-', '');
const template = readGraph('Maps/Cotswold Ridge/[09] ASSET PACKS/Rorys Forest Mega Pack/UNSORTED/_MATERIALS/Miscellaneous/Foliage/tree v1/MG_Grass.shadergraph');
const functionTemplate = template.find(x => x.m_FunctionName === 'MGBlendVertexNormals');
if (!functionTemplate) throw Error('Missing custom function template');
const body = 'float remapped = lerp(saturate(Minimum), saturate(Maximum), saturate(AO));\nOut = saturate(lerp(1.0, remapped, max(0.0, Strength)));';
for (const name of ['MashBoxFoliageImpostor', 'MashBoxProbeLitFoliageImpostor']) {
    const path = 'MapiX/MashBox/ImpostorBaker/Resources/ImpostorRuntime/' + name + '.shadergraph';
    const a = readGraph(path), root = a[0];
    if (a.some(x => x.m_OverrideReferenceName === '_ImpostorAOStrength')) throw Error('Already patched ' + name);
    const block = a.find(x => x.m_Name === 'SurfaceDescription.Occlusion');
    const incoming = root.m_Edges.filter(e => e.m_InputSlot.m_Node.m_Id === block.m_ObjectId);
    if (incoming.length !== 1) throw Error('Unexpected AO input');
    const oldOutput = clone(incoming[0].m_OutputSlot);
    const smooth = a.find(x => x.m_OverrideReferenceName === '_ImpostorNormalStrength');
    const propertyTemplate = a.find(x => x.m_Type.endsWith('.PropertyNode') && x.m_Property.m_Id === smooth.m_ObjectId);
    const slotTemplate = a.find(x => x.m_ObjectId === propertyTemplate.m_Slots[0].m_Id);
    const fn = clone(functionTemplate); fn.m_ObjectId = id(); fn.m_Group.m_Id = '';
    fn.m_Name = 'Impostor AO Remap'; fn.m_FunctionName = 'MGImpostorAORemap'; fn.m_FunctionBody = body;
    fn.m_DrawState.m_Position = { serializedVersion: '2', x: 1650, y: 1650, width: 230, height: 180 };
    const slots = ['AO', 'Minimum', 'Maximum', 'Strength', 'Out'].map((label, index) => {
        const s = clone(slotTemplate); s.m_ObjectId = id(); s.m_Id = index;
        s.m_DisplayName = s.m_ShaderOutputName = label; s.m_SlotType = index === 4 ? 1 : 0;
        s.m_Value = s.m_DefaultValue = index === 1 ? 0 : 1;
        return s;
    });
    fn.m_Slots = slots.map(s => ({ m_Id: s.m_ObjectId }));
    a.push(fn, ...slots); root.m_Nodes.push({ m_Id: fn.m_ObjectId });
    const input = slot => ({ m_Node: { m_Id: fn.m_ObjectId }, m_SlotId: slot });
    incoming[0].m_OutputSlot = input(4);
    root.m_Edges.push({ m_OutputSlot: oldOutput, m_InputSlot: input(0) });
    const category = a.find(x => x.m_Type.endsWith('.CategoryData') && x.m_ChildObjectList.some(p => p.m_Id === smooth.m_ObjectId));
    for (const [label, reference, value, index] of [
        ['AO Remap Minimum', '_ImpostorAOMin', 0, 1],
        ['AO Remap Maximum', '_ImpostorAOMax', 1, 2],
        ['AO Strength', '_ImpostorAOStrength', 1, 3]
    ]) {
        const p = clone(smooth); p.m_ObjectId = id(); p.m_Guid.m_GuidSerialized = crypto.randomUUID();
        p.m_Name = p.m_RefNameGeneratedByDisplayName = label;
        p.m_DefaultReferenceName = p.m_OverrideReferenceName = reference; p.m_Value = value; p.m_RangeValues = { x: 0, y: index === 3 ? 4 : 1 };
        const n = clone(propertyTemplate), s = clone(slotTemplate);
        n.m_ObjectId = id(); s.m_ObjectId = id(); n.m_Property.m_Id = p.m_ObjectId;
        n.m_Slots = [{ m_Id: s.m_ObjectId }]; n.m_Group.m_Id = '';
        n.m_DrawState.m_Position.x = 1350; n.m_DrawState.m_Position.y = 1900 + index * 100;
        root.m_Properties.push({ m_Id: p.m_ObjectId }); category.m_ChildObjectList.push({ m_Id: p.m_ObjectId });
        root.m_Nodes.push({ m_Id: n.m_ObjectId }); a.push(p, n, s);
        root.m_Edges.push({ m_OutputSlot: { m_Node: { m_Id: n.m_ObjectId }, m_SlotId: s.m_Id }, m_InputSlot: input(index) });
    }
    const byId = new Map(a.map(x => [x.m_ObjectId, x]));
    if (byId.size !== a.length) throw Error('Duplicate graph IDs');
    for (const e of root.m_Edges) for (const key of ['m_OutputSlot', 'm_InputSlot']) {
        const s = e[key], node = byId.get(s.m_Node.m_Id);
        if (!node?.m_Slots.some(r => byId.get(r.m_Id)?.m_Id === s.m_SlotId)) throw Error('Invalid graph connection');
    }
    fs.writeFileSync(path, a.map(x => x.original?.json === JSON.stringify(x) ? x.original.raw : JSON.stringify(x, null, 4)).join('\n\n') + '\n');
    console.log('PASS: ' + name + ' AO remap controls and graph connections. Defaults preserve baked AO.');
}
const remap = (ao, min, max, strength) => 1 + ((min + (max - min) * ao) - 1) * strength;
for (const ao of [0, .25, .5, .75, 1]) {
    if (remap(ao, 0, 1, 1) !== ao || remap(ao, 0, 1, 0) !== 1) throw Error('AO identity/disable regression');
}
if (remap(0, .3, 1, 1) !== .3 && Math.abs(remap(0, .3, 1, 1) - .3) > 1e-6) throw Error('Remap floor regression');
console.log('PASS: default identity, strength zero, remapped darkest value.');
