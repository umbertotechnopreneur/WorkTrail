import fs from 'node:fs/promises';
import path from 'node:path';
import { fileURLToPath } from 'node:url';
import { FileBlob, SpreadsheetFile } from '@oai/artifact-tool';

const here = path.dirname(fileURLToPath(import.meta.url));
const root = path.resolve(here, '../..');
const source = path.join(root, 'Planner_Availability_A4.xlsx');
const wb = await SpreadsheetFile.importXlsx(await FileBlob.load(source));
const patch = [];
const formula = (sheet, cell) => wb.worksheets.getItem(sheet).getRange(cell).formulas[0][0];
const set = (sheet, cell, content, isFormula = true) => {
  const range = wb.worksheets.getItem(sheet).getRange(cell);
  const previousFormula = range.formulas[0]?.[0] ?? '';
  const previousValue = previousFormula ? null : (range.values[0]?.[0] ?? null);
  patch.push({sheet, cell, content, isFormula, previousFormula, previousValue});
  if (isFormula) range.formulas = [[content]];
  else range.values = [[content]];
};

// Before/after views are diagnostic only. The native workbook is never replaced
// by an artifact-tool export: its Power Query package must remain intact.
for (const [name, range] of [['header', 'A1:H9'], ['weather', 'A40:H42']]) {
  const img = await wb.render({sheetName:'Planner', range, scale:1.5, format:'png'});
  await fs.writeFile(path.join(here, `before-${name}.png`), new Uint8Array(await img.arrayBuffer()));
}
await fs.writeFile(path.join(here, 'source-summary.json'), JSON.stringify({
  moon: formula('Info','B3'), city: formula('Planner','B8'), weather: formula('Planner','B41')
}, null, 2));
if (process.argv.includes('--inspect')) process.exit(0);

set('Calcoli','A94','Paese ISO città',false);
set('Calcoli','A95','Descrizione meteo',false);
set('Calcoli','A96','Simbolo meteo',false);
set('Calcoli','J94','Fase lunare',false);
set('Calcoli','K94','Simbolo Unicode',false);
const moons = [
  ['Luna nuova','🌑'], ['Falce crescente','🌒'], ['Primo quarto','🌓'],
  ['Gibbosa crescente','🌔'], ['Luna piena','🌕'], ['Gibbosa calante','🌖'],
  ['Ultimo quarto','🌗'], ['Falce calante','🌘']
];
moons.forEach(([phase, icon],i) => {
  set('Calcoli',`J${95+i}`,phase,false); set('Calcoli',`K${95+i}`,icon,false);
});
for (let i=0;i<7;i++) {
  const c=String.fromCharCode(66+i);
  set('Calcoli',`${c}94`,`=IF(COUNTIF(ZoneCatalog[Code],Planner!${c}9)=1,UPPER(INDEX(ZoneCatalog[CountryCode],MATCH(Planner!${c}9,ZoneCatalog[Code],0))),"")`);
  set('Calcoli',`${c}95`,`=IF(COUNTIF(Weather[Code],Planner!${c}9)=1,INDEX(Weather[Description],MATCH(Planner!${c}9,Weather[Code],0)),"")`);
  const d=`${c}95`;
  const terms = [
    [['temporale','thunder'],'⛈️'], [['tornado'],'🌪️'],
    [['nev','snow','sleet'],'🌨️'], [['piogg','piov','rain','drizz'],'🌧️'],
    [['nebb','foschi','caligin','fog','mist','smoke','dust','sabb','cener'],'🌫️'],
    [['poche nuvol','few clouds'],'🌤️'], [['nubi sparse','scattered clouds'],'⛅'],
    [['nuvol','nub','cloud','copert'],'☁️'], [['seren','clear'],'☀️']
  ];
  let weatherIcon='"🌡️"';
  for (const [words,icon] of terms.toReversed()) {
    const condition = words.map(w=>`ISNUMBER(SEARCH("${w}",${d}))`).join(',');
    weatherIcon=`IF(OR(${condition}),"${icon}",${weatherIcon})`;
  }
  set('Calcoli',`${c}96`,`=IF(${d}="","",${weatherIcon})`);
  const country=`Calcoli!${c}94`;
  const flag=`IF(LEN(${country})=2,UNICHAR(127397+CODE(LEFT(${country},1)))&UNICHAR(127397+CODE(RIGHT(${country},1)))&" ","")`;
  const city = formula('Planner',`${c}8`);
  if (!city.startsWith('=Parametri!')) throw new Error('Unexpected city formula; inspect current workbook.');
  set('Planner',`${c}8`,`=${flag}&${city.slice(1)}`);
  set('Estesa',`${c}8`,`=Planner!${c}8`);
  for (const [sheet,row] of [['Planner',41],['Estesa',65]]) {
    const old = formula(sheet,`${c}${row}`);
    const anchor='&ROUND(INDEX(Weather[TemperatureC]';
    if (!old.includes(anchor)) throw new Error('Weather formula changed; inspect before applying.');
    set(sheet,`${c}${row}`,old.replace(anchor,`&Calcoli!${c}96&" "${anchor}`));
  }
}
const current='IF(ABS(Calcoli!B6-INDEX(MoonPhases[Utc],MATCH(Calcoli!B6,MoonPhases[Utc],1)))<1/1440,INDEX(MoonPhases[Phase],MATCH(Calcoli!B6,MoonPhases[Utc],1)),INDEX(MoonPhases[Between],MATCH(Calcoli!B6,MoonPhases[Utc],1)))';
const next='INDEX(MoonPhases[Phase],MATCH(Calcoli!B6,MoonPhases[Utc],1)+1)';
const oldMoon=formula('Info','B3');
if (!oldMoon.includes(current) || !oldMoon.includes(next)) throw new Error('Moon formula changed; inspect before applying.');
set('Info','B3',oldMoon.replace(current,`VLOOKUP(${current},Calcoli!$J$95:$K$102,2,FALSE)&" "&${current}`).replace(next,`VLOOKUP(${next},Calcoli!$J$95:$K$102,2,FALSE)&" "&${next}`));

wb.recalculate();
const scan=await wb.inspect({kind:'match',searchTerm:'#REF!|#DIV/0!|#VALUE!|#NAME\\?|#N/A|#NUM!|#NULL!',options:{useRegex:true,maxResults:12},maxChars:2500});
await fs.writeFile(path.join(here,'artifact-diagnostics.txt'),scan.ndjson);
await fs.writeFile(path.join(here,'symbols-patch.json'),JSON.stringify(patch,null,2));
await (await SpreadsheetFile.exportXlsx(wb)).save(path.join(here,'symbols-authoring.xlsx'));
console.log(JSON.stringify({patchCells:patch.length,patch:'symbols-patch.json',nativeVerificationRequired:true}));
