"""Check native PDFs and ensure only the authorized workbook cells changed."""
import json
from pathlib import Path
from zipfile import ZipFile
from xml.etree import ElementTree as ET
from pypdf import PdfReader

HERE = Path(__file__).resolve().parent
ROOT = HERE.parent.parent
NS = {'m': 'http://schemas.openxmlformats.org/spreadsheetml/2006/main'}
RID = '{http://schemas.openxmlformats.org/officeDocument/2006/relationships}id'


def workbook_cells(path):
    with ZipFile(path) as z:
        book = ET.fromstring(z.read('xl/workbook.xml'))
        rels = {r.attrib['Id']: r.attrib['Target'] for r in ET.fromstring(z.read('xl/_rels/workbook.xml.rels'))}
        strings = [''.join(n.itertext()) for n in ET.fromstring(z.read('xl/sharedStrings.xml'))]
        cells, structures = {}, {}
        for sheet in book.find('m:sheets', NS):
            target = rels[sheet.attrib[RID]]
            node = ET.fromstring(z.read(target.lstrip('/') if target.startswith('/') else 'xl/' + target))
            name = sheet.attrib['name']
            for cell in node.findall('m:sheetData/m:row/m:c', NS):
                formula = cell.find('m:f', NS)
                value = cell.findtext('m:v', default='', namespaces=NS)
                if formula is not None:
                    semantic = ('formula', formula.text or '')
                elif cell.attrib.get('t') == 's':
                    semantic = ('text', strings[int(value)])
                elif cell.attrib.get('t') == 'inlineStr':
                    semantic = ('text', ''.join(cell.find('m:is', NS).itertext()))
                else:
                    semantic = ('value', value)
                if semantic != ('value', ''):
                    cells[(name, cell.attrib['r'])] = semantic
            structures[name] = {tag: ET.tostring(node.find('m:' + tag, NS), encoding='unicode') if node.find('m:' + tag, NS) is not None else None
                                for tag in ['pageSetup', 'pageMargins', 'mergeCells', 'dataValidations', 'conditionalFormatting', 'tableParts']}
            # Excel may reorder the non-overlapping merge records on save.
            structures[name]['mergeCells'] = sorted(n.attrib['ref'] for n in node.findall('m:mergeCells/m:mergeCell', NS))
            structures[name]['conditionalFormatting'] = sorted(ET.tostring(n, encoding='unicode') for n in node.findall('m:conditionalFormatting', NS))
        return cells, structures


before, before_structures = workbook_cells(ROOT / 'Planner_Availability_A4.xlsx')
after, after_structures = workbook_cells(HERE / 'Planner_symbols_staged.xlsx')
patch = json.loads((HERE / 'symbols-patch.json').read_text(encoding='utf-8'))
allowed = {(item['sheet'], item['cell']) for item in patch}
unexpected = [key for key in before.keys() | after.keys() if before.get(key) != after.get(key) and key not in allowed]
if unexpected:
    raise AssertionError(f'Unexpected content edits: {unexpected[:10]}')
if before_structures != after_structures:
    differences = [(sheet, key, before_structures[sheet][key], after_structures[sheet][key])
                   for sheet in before_structures for key in before_structures[sheet]
                   if before_structures[sheet][key] != after_structures[sheet][key]]
    raise AssertionError(f'Native structure differences: {differences[:3]}')
pages = {}
sizes = {}
for sheet, count in [('Planner', 1), ('Estesa', 2)]:
    for version in ['before', 'after']:
        pdf = PdfReader(HERE / f'{sheet}-{version}.pdf')
        assert len(pdf.pages) == count, (sheet, version, len(pdf.pages))
        sizes[f'{sheet}-{version}'] = [(float(page.mediabox.width), float(page.mediabox.height)) for page in pdf.pages]
        pages[f'{sheet}-{version}'] = count
    assert sizes[f'{sheet}-before'] == sizes[f'{sheet}-after'], 'Printed paper size changed'
report_path = HERE / 'verification.json'
report = json.loads(report_path.read_text(encoding='utf-8-sig'))
report.update(LayoutVerified=True, PageCounts=pages, PdfPageSizes=sizes, UnrelatedContentChanges=0, NativeStructuresPreserved=True,
              PrintNote='Existing workbook paperSize=9 (A4) preserved. Native PDF export uses Letter in this printer configuration, before and after this patch.')
report_path.write_text(json.dumps(report, ensure_ascii=False, indent=2), encoding='utf-8')
print(json.dumps({'pages': pages, 'unexpected_edits': 0, 'structure_preserved': True}))
