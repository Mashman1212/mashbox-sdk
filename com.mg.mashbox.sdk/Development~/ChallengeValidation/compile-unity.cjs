// Compile with installed Unity references; all output stays in this validation directory.
const fs = require('fs');
const path = require('path');
const { spawnSync } = require('child_process');
const project = path.resolve(process.argv[2]);
const unity = path.resolve(process.argv[3]);
const sdk = path.resolve(__dirname, '../..');
const output = path.join(__dirname, 'artifacts');
fs.mkdirSync(output, { recursive: true });
const slash = p => p.replaceAll('\\', '/');
const newSources = {
  MashBoxSDK: fs.readdirSync(path.join(sdk, 'Runtime/Maps')).filter(n => /^MB.*Challenge.*\.cs$/.test(n)).map(n => 'Runtime/Maps/' + n).concat('Runtime/Services/MBChallengeServices.cs'),
  'MashBoxSDK.Tools.Editor': ['MBChallengeAuthoring', 'MBChallengePreview', 'MBChallengeManifest', 'MashBoxMapToolsWindow.TrickChallenges'].map(n => 'Editor/Maps/' + n + '.cs'),
  MashBoxBridge: ['Runtime/MashBoxBridge/Common/Sys/SDKChallengePlayerService.cs']
};
for (const assembly of Object.keys(newSources)) {
  const artifacts = path.join(project, 'Library/Bee/artifacts');
  const candidates = fs.readdirSync(artifacts, { withFileTypes: true }).filter(d => d.isDirectory())
    .map(d => path.join(artifacts, d.name, assembly + '.rsp')).filter(p => fs.existsSync(p))
    .sort((a, b) => fs.statSync(b).mtimeMs - fs.statSync(a).mtimeMs);
  if (!candidates.length) throw new Error('Missing Unity response file for ' + assembly);
  let lines = fs.readFileSync(candidates[0], 'utf8').replace(/^\uFEFF/, '').split(/\r?\n/).map(line => {
    if (/^-out:|^-refout:/.test(line)) {
      const prefix = line.split(':')[0];
      return prefix + ':"' + slash(path.join(output, assembly + (prefix === '-refout' ? '.ref.dll' : '.dll'))) + '"';
    }
    return line.replace(/"([^"]+)"/g, (_, value) => {
      let resolved = path.isAbsolute(value) ? value : path.resolve(project, value);
      if (assembly !== 'MashBoxSDK' && path.basename(resolved) === 'MashBoxSDK.ref.dll') resolved = path.join(output, 'MashBoxSDK.ref.dll');
      return '"' + slash(resolved) + '"';
    });
  });
  const existing = new Set(lines.map(l => l.toLowerCase()));
  for (const file of newSources[assembly]) {
    const entry = '"' + slash(path.join(sdk, file)) + '"';
    if (!existing.has(entry.toLowerCase())) lines.push(entry);
  }
  const response = path.join(output, assembly + '.rsp');
  fs.writeFileSync(response, lines.join('\n'));
  const result = spawnSync('dotnet', [path.join(unity, 'Editor/Data/DotNetSdkRoslyn/csc.dll'), '@' + response], { cwd: project, encoding: 'utf8', windowsHide: true });
  const log = (result.stdout || '') + (result.stderr || '');
  fs.writeFileSync(path.join(output, assembly + '.log'), log);
  console.log(assembly + ': exit ' + result.status);
  console.log(log.split(/\r?\n/).filter(l => /error |MBChallenge|MBTrickChallenge/.test(l)).join('\n'));
  if (result.status !== 0) process.exit(result.status || 1);
}
