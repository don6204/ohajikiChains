"""Show saved-cell changes against the previous imported snapshot; read only."""
from pathlib import Path
import zipfile,xml.etree.ElementTree as E
root=Path(__file__).resolve().parents[1]
ns={'s':'http://schemas.openxmlformats.org/spreadsheetml/2006/main'}
def cells(path):
    with zipfile.ZipFile(path) as z:
        strings=[''.join(x.text or '' for x in item.findall('s:t',ns)+item.findall('s:r/s:t',ns)) for item in E.fromstring(z.read('xl/sharedStrings.xml'))]
        names=[s.attrib['name'] for s in E.fromstring(z.read('xl/workbook.xml')).find('s:sheets',ns)]
        result={}
        for i,name in enumerate(names,1):
            for c in E.fromstring(z.read(f'xl/worksheets/sheet{i}.xml')).findall('.//s:c',ns):
                v=c.find('s:v',ns)
                if v is not None:result[name+'!'+c.attrib['r']]=strings[int(v.text)] if c.attrib.get('t')=='s' else v.text
        return result
a=cells(root/'Assets/Data/MasterData.xlsx.bak');b=cells(root/'Assets/Data/MasterData.xlsx')
diff=[(k,a.get(k),b.get(k)) for k in a.keys()|b.keys() if a.get(k)!=b.get(k)]
for k,old,new in sorted(diff):print(k,repr(old),'->',repr(new))
print('Changed saved cell values:',len(diff))
