import fs from 'node:fs/promises';
import path from 'node:path';
import { fileURLToPath } from 'node:url';
import { spawnSync } from 'node:child_process';
import { createHash } from 'node:crypto';
import { SpreadsheetFile, Workbook } from '@oai/artifact-tool';

// Run with the bundled Node executable. node_modules is a junction to the
// bundled dependency directory. All outputs stay beside this script.
// Native Excel page setup is deliberately owned by the importing parent:
// PrintArea = '$A$1:$H$44'; PaperSize = 9 (A4); Orientation = 1 (portrait);
// Zoom = false; FitToPagesWide = 1; FitToPagesTall = 1;
// margins = 0.35 inch; CenterHorizontally = true; PrintGridlines = false.
// This file creates only the static Copertina sheet, with no data connections,
// formulas, input fields, secrets or changes to the destination planner.

const outputDir = path.dirname(fileURLToPath(import.meta.url));
const printArea = 'A1:H44';
const colors = {
  navy: '#233B4D',
  teal: '#286567',
  cream: '#FFF3D6',
  white: '#FFFFFF',
  rose: '#F2D9D5',
  red: '#D9A0A0',
};

const workbook = Workbook.create();
const sheet = workbook.worksheets.add('Copertina');
sheet.showGridLines = false;
sheet.tabColor = colors.navy;
sheet.getRange(printArea).format = {
  fill: colors.white,
  font: { name: 'Arial', size: 11, color: colors.navy },
  horizontalAlignment: 'left',
  verticalAlignment: 'center',
  wrapText: false,
  columnWidthPx: 88,
  rowHeight: 18,
};

const heights = {
  1: 8, 2: 18, 3: 29, 4: 20, 5: 8, 6: 23,
  12: 9, 13: 23, 18: 9, 19: 23, 24: 22, 25: 22,
  26: 9, 27: 23, 32: 9, 33: 23,
  40: 18, 41: 24, 42: 9,
};
for (const [row, height] of Object.entries(heights)) {
  sheet.getRange(`A${row}:H${row}`).format.rowHeight = height;
}

const authoredText = [];
function text(address, value, options = {}) {
  const range = sheet.getRange(address);
  if (address.includes(':')) range.merge();
  sheet.getRange(address.split(':')[0]).values = [[value]];
  range.format.font = {
    name: 'Arial', size: options.size ?? 11,
    color: options.color ?? colors.navy,
    bold: options.bold ?? false,
    italic: options.italic ?? false,
  };
  if (options.fill) range.format.fill = options.fill;
  if (options.align) range.format.horizontalAlignment = options.align;
  authoredText.push({ address, value });
  return range;
}

function line(row, value, options = {}) {
  return text(`A${row}:H${row}`, value, options);
}

function section(row, value) {
  const range = line(row, value, { bold: true, color: colors.teal, fill: colors.cream });
  range.format.borders = {
    bottom: { style: 'thin', color: colors.teal },
  };
}

line(2, 'WORKTRAIL', { bold: true, color: colors.teal });
line(3, 'Pianifica tra fusi orari', { bold: true, size: 20 });
line(4, 'Guida rapida. Questa pagina è informativa, senza campi da compilare.');

section(6, '1   Imposta il planner in Parametri');
line(7, 'C5: data. C6: ora iniziale. J5 / J6: margini.');
line(8, 'C7: passo degli istanti, 30 o 60 minuti.');
line(9, 'Righe 12:18: configura le 7 colonne indipendenti. La prima è quella di casa.');
line(10, 'Per ogni colonna scegli città, fuso orario, calendario e orari locali.');
line(11, 'Giorni della settimana: 1 = riposo, 0 = attività.');

section(13, '2   Scegli la vista e stampa');
line(14, 'Planner: 24 istanti orari su un A4 orizzontale.');
line(15, 'Estesa: 48 istanti su due A4. Il passo segue Parametri C7.');
line(16, 'Stampa solo il foglio attivo, Planner o Estesa, non l’intera cartella.');
line(17, 'Nell’anteprima controlla formato A4, orientamento e numero di pagine.');

section(19, '3   Leggi le regole dei giorni');
const rules = [
  [20, 'Santi / ricorrenze religiose', 'Solo informazioni.'],
  [21, 'Festività nazionali / patrono', 'Festività: riposo. Patrono: se applicabile.'],
  [22, 'Fine settimana', 'Segue il riposo settimanale personale.'],
  [23, 'Ferie / chiusure / recuperi', 'Eccezioni personali con priorità.'],
];
for (const [row, label, meaning] of rules) {
  text(`A${row}:C${row}`, label, { bold: true });
  text(`D${row}:H${row}`, meaning);
}
line(24, 'Eccezioni: colonna 1–7, date iniziale e finale incluse. Scegli Riposo o Attivita.');
line(25, 'Attivita usa orari standard e margini. Le eccezioni precedono festività e weekend.');

section(27, '4   Conosci la copertura dei calendari');
line(28, 'Base nazionale 2026–2028: Cina continentale, India, Corea del Sud, Giappone,');
line(29, 'Bangladesh, Italia, Francia, Norvegia, Svezia, Stati Uniti e Canada.');
line(30, 'Niente suddivisioni regionali. Le date manuali dei patroni restano in Calendari.');
line(31, 'VN / BY / GB conservati, non tutti caricati. Date stimate o future: non certe ufficialmente.');

section(33, '5   Aggiorna i dati e consulta il meteo');
line(34, 'Dati > Aggiorna tutto: 6 query. Di norma leggono i 6 CSV locali.');
line(35, 'Parametri B32: chiave OpenWeather facoltativa. Vuota = meteo da Weather.csv.');
line(36, 'Con una chiave, la query Meteo chiama l’API per ogni città selezionata distinta.');
line(37, 'Meteo attuale, non previsioni per la data scelta. Ultimo aggiornamento in Info.');
line(38, 'Refresh solo manuale, mai all’apertura. Se sposti i CSV: Parametri B25.');
line(39, 'La chiave in B32 è salvata in chiaro: non condividere una copia che la contiene.');
line(40, 'Errore di refresh: conserva gli ultimi dati e controlla Query e connessioni.');

text('A41:C41', 'Disponibile', { align: 'center', bold: true, fill: colors.white });
text('D41:F41', 'Riserva', { align: 'center', bold: true, fill: colors.rose });
text('G41:H41', 'Riposo', { align: 'center', bold: true, fill: colors.red });
sheet.getRange('A41:H41').format.borders = {
  top: { style: 'thin', color: colors.navy },
  bottom: { style: 'thin', color: colors.navy },
};
line(43, 'Luna: solo informativa, senza effetto sugli orari.', { italic: true });
line(44, 'Santi: selezione non completa, non un calendario religioso ufficiale.', { italic: true });

workbook.recalculate();

// The bundled renderer on this Windows host exits with native 0xC0000409
// after completing PNG generation. Isolate it from the exporting process.
// Accept that specific shutdown fault only after the child confirms a complete
// write and the parent verifies the resulting PNG signature, trailer and hash.
if (process.argv.includes('--render-preview')) {
  const preview = await workbook.render({
    sheetName: 'Copertina', range: printArea, scale: 2, format: 'png', headers: false,
  });
  const pngBytes = new Uint8Array(await preview.arrayBuffer());
  await fs.writeFile(path.join(outputDir, 'cover-preview.png'), pngBytes);
  const digest = createHash('sha256').update(pngBytes).digest('hex');
  console.log(`COVER_PREVIEW_SAVED:${digest}`);
  process.exit(0);
}

// Verify the authored static text at its actual merged-cell anchors.
for (const { address, value } of authoredText) {
  const actual = sheet.getRange(address.split(':')[0]).values[0][0];
  if (actual !== value) throw new Error(`Text mismatch at ${address}`);
}
const formulas = sheet.getRange(printArea).formulas.flat().filter(Boolean);
if (formulas.length) throw new Error('The cover must remain static.');

const inspected = await workbook.inspect({
  kind: 'table', range: 'Copertina!A33:A40', include: 'values,formulas',
  tableMaxRows: 8, tableMaxCols: 1, tableMaxCellChars: 120, maxChars: 1800,
});
console.log(inspected.ndjson);

const output = await SpreadsheetFile.exportXlsx(workbook);
await output.save(path.join(outputDir, 'cover.xlsx'));
// Artifact Tool may emit a companion inspection dump. It is disposable output
// from this builder and is not one of the three requested deliverables.
await fs.rm(path.join(outputDir, 'cover.xlsx.inspect.ndjson'), { force: true });

const rendered = spawnSync(process.execPath, [fileURLToPath(import.meta.url), '--render-preview'], {
  cwd: outputDir, encoding: 'utf8', windowsHide: true, timeout: 60000,
});
if (rendered.error) throw rendered.error;
const marker = rendered.stdout.match(/COVER_PREVIEW_SAVED:([a-f0-9]{64})/);
const knownRendererShutdown = (rendered.status >>> 0) === 0xC0000409;
if (!marker || (rendered.status !== 0 && !knownRendererShutdown)) {
  throw new Error(`Preview failed (${rendered.status}): ${rendered.stderr}`);
}
const png = await fs.readFile(path.join(outputDir, 'cover-preview.png'));
if (png.subarray(0, 8).toString('hex') !== '89504e470d0a1a0a'
    || png.subarray(-12).toString('hex') !== '0000000049454e44ae426082'
    || createHash('sha256').update(png).digest('hex') !== marker[1]) {
  throw new Error('Preview PNG verification failed.');
}
if (knownRendererShutdown) {
  console.warn('Renderer Windows: native shutdown fault after a completed PNG write; signature, trailer and SHA-256 verified.');
}

console.log(JSON.stringify({
  workbook: path.join(outputDir, 'cover.xlsx'),
  preview: path.join(outputDir, 'cover-preview.png'),
  sheets: ['Copertina'], printArea: '$A$1:$H$44',
  paper: 'A4', orientation: 'portrait', fitToPagesWide: 1, fitToPagesTall: 1,
  suggestedMarginsInches: 0.35, nativePageSetup: 'Applied by importing parent',
  authoredTextBlocks: authoredText.length, formulas: formulas.length,
}, null, 2));
