const fs = require('fs'), crypto = require('crypto');
eval(fs.readFileSync('D:/mashbox-sdk/com.mg.mashbox.sdk/Development~/MGTerrainImplementation~/normal-strength.cjs', 'utf8').split('const base=')[0] + ';global.readGraph=read;');
const source = readGraph('Maps/Cotswold Ridge/[09] ASSET PACKS/Rorys Forest Mega Pack/UNSORTED/_MATERIALS/Miscellaneous/Foliage/tree v1/MG_Grass.shadergraph');
const bySource = new Map(source.map(x => [x.m_ObjectId, x]));
const clone = x => JSON.parse(JSON.stringify(x));
const id = () => crypto.randomUUID().replaceAll('-', '');
const variationEnd = '28b87bcc16e74cd287fb5e98a713d8c1';
const variationWhiteBalance = 'fd7e025f5372588492134c405b7839b6';
for (const name of ['MashBoxFoliageImpostor', 'MashBoxProbeLitFoliageImpostor']) {
    const path = 'MapiX/MashBox/ImpostorBaker/Resources/ImpostorRuntime/' + name + '.shadergraph';
    const a = readGraph(path), root = a[0];
    if (a.some(x => x.m_OverrideReferenceName === '_ImpostorColourVariation')) throw Error('Already patched ' + name);
    const category = { m_SGVersion: 0, m_Type: 'UnityEditor.ShaderGraph.CategoryData', m_ObjectId: id(), m_Name: 'Colour Adjustments', m_ChildObjectList: [] };
    a.push(category); root.m_CategoryData.push({ m_Id: category.m_ObjectId });
    const copied = new Map();
    function copyNode(original) {
        const n = clone(original); n.m_ObjectId = id(); n.m_Group = { m_Id: '' };
        n.m_Slots = original.m_Slots.map(r => {
            const slot = clone(bySource.get(r.m_Id)); slot.m_ObjectId = id(); a.push(slot); return { m_Id: slot.m_ObjectId };
        });
        a.push(n); root.m_Nodes.push({ m_Id: n.m_ObjectId }); return n;
    }
    function edge(from, output, to, input) {
        root.m_Edges.push({ m_OutputSlot: { m_Node: { m_Id: from.m_ObjectId }, m_SlotId: output }, m_InputSlot: { m_Node: { m_Id: to.m_ObjectId }, m_SlotId: input } });
    }
    function property(original, label, reference, value) {
        const p = clone(original); p.m_ObjectId = id(); p.m_Guid.m_GuidSerialized = crypto.randomUUID();
        p.m_Name = p.m_RefNameGeneratedByDisplayName = label;
        p.m_DefaultReferenceName = p.m_OverrideReferenceName = reference; p.m_Value = value;
        root.m_Properties.push({ m_Id: p.m_ObjectId }); category.m_ChildObjectList.push({ m_Id: p.m_ObjectId }); a.push(p);
        return p;
    }
    function copyBranch(sourceId) {
        if (copied.has(sourceId)) return copied.get(sourceId);
        const original = bySource.get(sourceId), n = copyNode(original); copied.set(sourceId, n);
        if (original.m_Property) {
            const p = bySource.get(original.m_Property.m_Id);
            const ref = p.m_Name === 'Colour Variation Amount' ? '_ImpostorColourVariation' : p.m_Name === 'Darkness Variation Amount' ? '_ImpostorDarknessVariation' : null;
            if (!ref) throw Error('Unexpected variation dependency: ' + p.m_Name);
            n.m_Property.m_Id = property(p, p.m_Name, ref, 0).m_ObjectId;
        }
        for (const e of source[0].m_Edges.filter(e => e.m_InputSlot.m_Node.m_Id === sourceId)) {
            // Feed atlas colour into the variation operation, without copying texture sampling or baked vertex AO.
            if (sourceId === variationWhiteBalance && e.m_InputSlot.m_SlotId === 0) continue;
            const upstream = copyBranch(e.m_OutputSlot.m_Node.m_Id);
            edge(upstream, e.m_OutputSlot.m_SlotId, n, e.m_InputSlot.m_SlotId);
        }
        return n;
    }
    const variation = copyBranch(variationEnd);
    const baseBlock = a.find(x => x.m_Name === 'SurfaceDescription.BaseColor');
    const incoming = root.m_Edges.filter(e => e.m_InputSlot.m_Node.m_Id === baseBlock.m_ObjectId);
    if (incoming.length !== 1) throw Error('Unexpected base colour input');
    let upstream = { m_ObjectId: incoming[0].m_OutputSlot.m_Node.m_Id }, outputSlot = incoming[0].m_OutputSlot.m_SlotId;
    function adjustment(type, controls, output) {
        const n = copyNode(source.find(x => x.m_Type.endsWith('.' + type)));
        edge(upstream, outputSlot, n, 0);
        for (const [originalName, label, reference, value, inputSlot] of controls) {
            const original = source.find(x => x.m_Type.endsWith('ShaderProperty') && x.m_Name === originalName);
            const p = property(original, label, reference, value);
            const pn = copyNode(source.find(x => x.m_Type.endsWith('.PropertyNode') && x.m_Property.m_Id === original.m_ObjectId));
            pn.m_Property.m_Id = p.m_ObjectId;
            edge(pn, 0, n, inputSlot);
        }
        upstream = n; outputSlot = output;
    }
    adjustment('ContrastNode', [['Albedo Contrast', 'Albedo Contrast', '_ImpostorAlbedoContrast', 1, 1]], 2);
    adjustment('SaturationNode', [['Albedo saturation', 'Albedo Saturation', '_ImpostorAlbedoSaturation', 1, 1]], 2);
    adjustment('WhiteBalanceNode', [
        ['Albedo Temperature', 'Albedo Temperature', '_ImpostorAlbedoTemperature', 0, 1],
        ['Albedo Tint', 'Albedo Tint', '_ImpostorAlbedoTint', 0, 2]
    ], 3);
    edge(upstream, outputSlot, copied.get(variationWhiteBalance), 0);
    incoming[0].m_OutputSlot = { m_Node: { m_Id: variation.m_ObjectId }, m_SlotId: 3 };
    // Match the source operations exactly; no new textures or render passes.
    const byId = new Map(a.map(x => [x.m_ObjectId, x]));
    if (byId.size !== a.length) throw Error('Duplicate IDs');
    for (const e of root.m_Edges) for (const key of ['m_OutputSlot', 'm_InputSlot']) {
        const s = e[key], n = byId.get(s.m_Node.m_Id);
        if (!n?.m_Slots.some(r => byId.get(r.m_Id)?.m_Id === s.m_SlotId)) throw Error('Invalid graph edge');
    }
    if (category.m_ChildObjectList.length !== 6) throw Error('Expected six colour controls');
    fs.writeFileSync(path, a.map(x => x.original?.json === JSON.stringify(x) ? x.original.raw : JSON.stringify(x, null, 4)).join('\n\n') + '\n');
    console.log('PASS ' + name + ': six neutral controls; exact MG_Grass variation branch (' + copied.size + ' nodes); all graph connections valid.');
}
