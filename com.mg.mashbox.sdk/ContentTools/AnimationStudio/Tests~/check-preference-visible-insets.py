from pathlib import Path
import re
root=Path('obj/StudioControlsValidation');checks=[]
def bounds(line):
 m=re.search(r'\(x:([\d.-]+), y:([\d.-]+), width:([\d.-]+), height:([\d.-]+)\)',line);return [float(v) for v in m.groups()]
for stage in range(7):
 lines=(root/f'preference-layout-rects-{stage}.txt').read_text().splitlines()
 for i,line in enumerate(lines):
  if 'padding=RectOffset (l:8 r:8 t:8 b:12)' not in line:continue
  depth=len(line)-len(line.lstrip());children=[]
  for candidate in lines[i+1:]:
   d=len(candidate)-len(candidate.lstrip())
   if d<=depth:break
   if d==depth+1:children.append(candidate)
  last=children[-1];_,y,_,h=bounds(line);_,cy,_,ch=bounds(last)
  overflow=int(re.search(r'overflow=RectOffset \(l:-?\d+ r:-?\d+ t:-?\d+ b:(-?\d+)\)',last).group(1))
  clear=y+h-(cy+ch+max(0,overflow))-2
  assert clear>=7.99,(stage,clear,line,last)
  checks.append(f'page {stage}: clear bottom={clear:g}px')
assert len(checks)>=10
(root/'preference-visible-insets-result.txt').write_text('PASS '+str(len(checks))+' native measured group insets >=8px after artwork overflow and 2px outline\n'+'\n'.join(checks))
print((root/'preference-visible-insets-result.txt').read_text())
