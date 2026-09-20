const fs=require('fs'),path=require('path'),cp=require('child_process');
const base='D:/MappyX', root='D:/mashbox-sdk/com.mg.mashbox.sdk';
const stage=path.join(__dirname,'staged').replace(/\\/g,'/');
const files=JSON.parse(fs.readFileSync(path.join(__dirname,'files.json'),'utf8'));
const dir=path.join(__dirname,'build');fs.mkdirSync(dir,{recursive:true});
const assemblies=process.argv.slice(2); if(!assemblies.length) assemblies.push('MashBoxSDK','Assembly-CSharp-Editor');
for(const assembly of assemblies){
 let rsp=fs.readFileSync(`${base}/Library/Bee/artifacts/500b0aE.dag/${assembly}.rsp`,'utf8');
 for(const script of ['MGTerrainTreeLodValidation.cs','TerrainCandidateRegression.cs'])
  rsp=rsp.replaceAll('Assets/MapiX/TimeGhostTrees/Editor/'+script,'D:/mashbox-sdk/com.mg.mashbox.sdk/Editor/Maps/Validation/'+script);
 rsp=rsp.replace(/^-out:.*$/m,`-out:"${dir}/${assembly}.dll"`).replace(/^-refout:.*$/m,`-refout:"${dir}/${assembly}.ref.dll"`);
 for(const f of files) rsp=rsp.replaceAll(root+'/'+f,stage+'/'+f);
 if(assembly==='Assembly-CSharp-Editor' && !rsp.includes('MGTerrainTreeLodValidation.cs')) rsp+='\n"D:/mashbox-sdk/com.mg.mashbox.sdk/Editor/Maps/Validation/MGTerrainTreeLodValidation.cs"\n';
 if(assembly!=='MashBoxSDK') rsp=rsp.replace(/-r:"[^"\r\n]*\/MashBoxSDK(?:\.ref)?\.dll"/g,`-r:"${dir}/MashBoxSDK.ref.dll"`);
 fs.writeFileSync(path.join(dir,assembly+'.rsp'),rsp);
 console.log('Prepared compiler response: '+assembly);
}
