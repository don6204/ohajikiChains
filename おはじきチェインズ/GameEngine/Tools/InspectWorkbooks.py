"""Read-only workbook evidence for the DirectX 12 port. No Excel write operations."""
from pathlib import Path
import zipfile, xml.etree.ElementTree as ET, posixpath, json, hashlib

root = Path(__file__).resolve().parents[1]
out = root / 'Review' / 'Migration'
out.mkdir(parents=True, exist_ok=True)
ns = {'s':'http://schemas.openxmlformats.org/spreadsheetml/2006/main', 'a':'http://schemas.openxmlformats.org/drawingml/2006/main'}
def cell_text(node):
    return ''.join(t.text or '' for t in node.findall('s:t',ns)+node.findall('s:r/s:t',ns))
for filename in ['【おはじきチェインズ】仕様書.xlsx','MasterData.xlsx']:
    path = root.parent / filename
    with path.open('rb') as f: raw=f.read()
    from io import BytesIO
    with zipfile.ZipFile(BytesIO(raw)) as z:
        strings=[cell_text(x) for x in ET.fromstring(z.read('xl/sharedStrings.xml'))] if 'xl/sharedStrings.xml' in z.namelist() else []
        rel={r.attrib['Id']:r.attrib['Target'] for r in ET.fromstring(z.read('xl/_rels/workbook.xml.rels'))}
        book={}; lines=[]
        for sheet in ET.fromstring(z.read('xl/workbook.xml')).find('s:sheets',ns):
            name=sheet.attrib['name']; target=rel[sheet.attrib['{http://schemas.openxmlformats.org/officeDocument/2006/relationships}id']]
            target=posixpath.normpath('xl/'+target) if not target.startswith('/') else target[1:]
            cells={}
            for c in ET.fromstring(z.read(target)).findall('.//s:c',ns):
                v=c.find('s:v',ns); inline=c.find('s:is',ns)
                val=(strings[int(v.text)] if c.attrib.get('t')=='s' else v.text) if v is not None else cell_text(inline) if inline is not None else ''
                if val: cells[c.attrib['r']]=val
            book[name]=cells;lines.append('\nSHEET '+name)
            lines += [ref+' '+val.replace('\n',' | ') for ref,val in cells.items()]
        for entry in z.namelist():
            if entry.startswith('xl/drawings/drawing') and entry.endswith('.xml'):
                words=[t.text for t in ET.fromstring(z.read(entry)).findall('.//a:t',ns) if t.text]
                lines += ['\nDRAWING '+entry,' | '.join(words)]
            if entry.startswith('xl/media/') and entry.endswith(('.png','.emf')):
                (out/Path(entry).name).write_bytes(z.read(entry))
        stem='spec' if filename.startswith('【') else 'master'
        (out/(stem+'.json')).write_text(json.dumps(book,ensure_ascii=False,indent=2),encoding='utf-8')
        (out/(stem+'.txt')).write_text('\n'.join(lines),encoding='utf-8')
        print(filename,hashlib.sha256(raw).hexdigest(),[(s,len(c)) for s,c in book.items()])
