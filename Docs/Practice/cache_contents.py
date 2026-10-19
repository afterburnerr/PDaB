"""Materialize the cache of a genuine Word TOC using verified PDF pagination.
The TOC field remains editable/updatable in Word; body content is not changed.
"""
from pathlib import Path
from zipfile import ZipFile, ZIP_DEFLATED
from copy import deepcopy
from lxml import etree as E
from pypdf import PdfReader
import re,sys,json
p=Path(sys.argv[1]);pdf=Path(sys.argv[2])
W='http://schemas.openxmlformats.org/wordprocessingml/2006/main'
ns={'w':W};q=lambda n:'{'+W+'}'+n
with ZipFile(p) as z:parts={x:z.read(x) for x in z.namelist()}
r=E.fromstring(parts['word/document.xml']);body=r.find(q('body'))
norm=lambda s:re.sub(r'\s+','',s).replace('\u00ad','')
pages=[norm(x.extract_text()) for x in PdfReader(pdf).pages]
heads=[]
# Clear only bookmarks created by this helper before an idempotent cache refresh.
owned_ids={x.get(q('id')) for x in body.findall('.//'+q('bookmarkStart')) if (x.get(q('name')) or '').startswith('ReportHeading')}
for x in list(body.findall('.//'+q('bookmarkStart')))+list(body.findall('.//'+q('bookmarkEnd'))):
 if x.get(q('id')) in owned_ids:x.getparent().remove(x)
for paragraph in body.findall(q('p')):
 st=paragraph.find('w:pPr/w:pStyle',ns)
 if st is None or st.get(q('val')) not in ('Heading1','Heading2'):continue
 text=''.join(paragraph.itertext())
 text=''.join(paragraph.xpath('.//w:t/text()',namespaces=ns))
 page=next((i+1 for i,t in enumerate(pages) if i>=2 and norm(text) in t),None)
 if page is None:raise RuntimeError('Heading absent from render: '+text)
 index=len(heads);anchor='ReportHeading'+str(index)
 start=E.Element(q('bookmarkStart'));start.set(q('id'),str(1000+index));start.set(q('name'),anchor)
 end=E.Element(q('bookmarkEnd'));end.set(q('id'),str(1000+index))
 paragraph.insert(1,start);paragraph.append(end)
 heads.append((text,page,st.get(q('val')),anchor))
# Locate the single initial TOC placeholder paragraph and replace it with a multi-paragraph cache.
original=next(x for x in body.findall(q('p')) if x.xpath('.//w:instrText[contains(text(),"TOC")]',namespaces=ns))
pos=list(body).index(original)
cached_nodes=[]
for node in list(body)[pos:]:
 cached_nodes.append(node)
 if node.xpath('.//w:fldChar[@w:fldCharType="end"]',namespaces=ns):break
else:raise RuntimeError('TOC has no end marker')
for node in cached_nodes:body.remove(node)
for i,(text,page,style,anchor) in enumerate(heads):
 pa=E.Element(q('p'));pr=E.SubElement(pa,q('pPr'))
 indent=E.SubElement(pr,q('ind'));indent.set(q('left'),'0' if style=='Heading1' else '284');indent.set(q('firstLine'),'0')
 space=E.SubElement(pr,q('spacing'));space.set(q('line'),'276');space.set(q('lineRule'),'auto');space.set(q('after'),'100')
 tabs=E.SubElement(pr,q('tabs'));tab=E.SubElement(tabs,q('tab'));tab.set(q('val'),'right');tab.set(q('leader'),'dot');tab.set(q('pos'),'9800')
 def run(t=None):
  rr=E.SubElement(pa,q('r'));rp=E.SubElement(rr,q('rPr'));f=E.SubElement(rp,q('rFonts'));f.set(q('ascii'),'Times New Roman');f.set(q('hAnsi'),'Times New Roman');sz=E.SubElement(rp,q('sz'));sz.set(q('val'),'28')
  if t is not None:E.SubElement(rr,q('t')).text=t
  return rr
 if i==0:
  x=E.SubElement(run(),q('fldChar'));x.set(q('fldCharType'),'begin')
  x=E.SubElement(run(),q('instrText'));x.set('{http://www.w3.org/XML/1998/namespace}space','preserve');x.text=' TOC \\o "1-2" \\h \\z \\u '
  x=E.SubElement(run(),q('fldChar'));x.set(q('fldCharType'),'separate')
 rr=run(text)
 E.SubElement(run(),q('tab'));run(str(page))
 if i==len(heads)-1:
  x=E.SubElement(run(),q('fldChar'));x.set(q('fldCharType'),'end')
 body.insert(pos+i,pa)
parts['word/document.xml']=E.tostring(r,xml_declaration=True,encoding='UTF-8',standalone=True)
with ZipFile(p,'w',ZIP_DEFLATED) as z:
 for name,b in parts.items():z.writestr(name,b)
(p.parent/'.qa/contents.json').write_text(json.dumps(heads,ensure_ascii=False,indent=2))
print('Cached',len(heads),'headings from',len(pages),'rendered pages')
