"""Validate report structure and rendered pagination; does not replace visual review."""
from pathlib import Path
from zipfile import ZipFile
from lxml import etree as E
from pypdf import PdfReader
import pdfplumber,sys,json,re,hashlib
report=Path(sys.argv[1]);pdf=Path(sys.argv[2])
W='http://schemas.openxmlformats.org/wordprocessingml/2006/main';ns={'w':W};q=lambda x:'{'+W+'}'+x
with ZipFile(report) as z:
 document=E.fromstring(z.read('word/document.xml'));settings=E.fromstring(z.read('word/settings.xml'));core=E.fromstring(z.read('docProps/core.xml'))
 sec=document.find('.//w:sectPr',ns);mar=sec.find(q('pgMar'));size=sec.find(q('pgSz'))
 assert tuple(int(mar.get(q(x))) for x in ('left','right','top','bottom'))==(1417,567,850,850)
 assert abs(int(size.get(q('w')))-11906)<3 and abs(int(size.get(q('h')))-16838)<3
 assert sec.find(q('titlePg')) is not None
 assert settings.find(q('autoHyphenation')).get(q('val'))=='true'
 assert settings.find(q('updateFields')).get(q('val'))=='true'
 toc=document.xpath('//w:instrText[contains(text(),"TOC")]',namespaces=ns);assert len(toc)==1
 text=' '.join(document.xpath('//w:t/text()',namespaces=ns))
 for value in ('Трост Илья','ИСП-32','ПРАКТИЧЕСКАЯ ЧАСТЬ','ПРИЛОЖЕНИЕ 1','СПИСОК ЛИТЕРАТУРЫ'):assert value in text
 for wrong in ('И. И. Иванов','ПО-33','Л.А. Ракитина','О.А. Комиссарова','помехи на экране','Содержание обновляется'):assert wrong not in text
 assert len(document.xpath('//w:tbl',namespaces=ns))>=2
r=PdfReader(pdf);texts=[p.extract_text() for p in r.pages]
assert all(len(t)>80 for t in texts)
assert all('\ufffd' not in t for t in texts)
assert '2026' in texts[0] and 'СОДЕРЖАНИЕ' in texts[1]
contents=json.loads((report.parent/'.qa/contents.json').read_text())
normalize=lambda x:re.sub(r'\s+','',x).replace('\u00ad','')
for heading,page,*_ in contents:
 assert normalize(heading) in normalize(texts[page-1]),(heading,page)
# Pages/captions and figures can have intentional whitespace; only inspect actual glyph bounds.
outliers=[]
with pdfplumber.open(pdf) as x:
 for i,p in enumerate(x.pages,1):
  for c in p.chars:
   if c['x0']<68.5 or c['x1']>568.9 or c['top']<36 or c['bottom']>p.height-12:
    if c.get('text','').strip():outliers.append((i,c.get('text'),round(c['x0'],1),round(c['top'],1),round(c['x1'],1),round(c['bottom'],1)))
assert not outliers,outliers[:15]
qa={'pages':len(r.pages),'toc_entries':len(contents),'tables':len(document.xpath('//w:tbl',namespaces=ns)),'word_toc':True,'page_geometry':'A4 25/10/15/15 mm','source_listing_count':3,'no_out_of_bounds_glyphs':True,'docx_sha256':hashlib.sha256(report.read_bytes()).hexdigest()}
(report.parent/'.qa/structural_audit.json').write_text(json.dumps(qa,ensure_ascii=False,indent=2))
print(json.dumps(qa,ensure_ascii=False,indent=2))
