import fs from 'node:fs/promises';
import path from 'node:path';
import {fileURLToPath} from 'node:url';
import {Workbook,SpreadsheetFile} from '@oai/artifact-tool';
process.on('uncaughtException',e=>{console.error(e.message);process.exit(1);});
process.on('unhandledRejection',e=>{console.error(e?.message??String(e));process.exit(1);});

const scratch=path.dirname(fileURLToPath(import.meta.url));
const out=path.resolve(scratch,'../../artifacts/base-planner');
const data=JSON.parse(await fs.readFile(`${scratch}/planner-data.json`,'utf8'));
const target=`${out}/Planner_Availability_A4.xlsx`;
if(!process.argv.includes('--replace-generated'))try {await fs.access(target); throw new Error('Output already exists; use a deliberate revision.');} catch(e){if(e.code!=='ENOENT')throw e;}
const wb=Workbook.create();
const sheets=Object.fromEntries(['Parametri','Planner','Estesa','Calendari','Da_file','Info','Fusi','Guida','Calcoli'].map(n=>[n,wb.worksheets.add(n)]));
const displayZones=data.zones.slice(0,7);
const zEnd=5+data.zones.length;
const C={navy:'#233B4D',teal:'#286567',muted:'#586A75',line:'#D8E0E3',input:'#FFF3D6',white:'#FFFFFF',reserve:'#F2D9D5',off:'#D9A0A0',light:'#F1F5F6',error:'#FBE4B4'};
const cols=['B','C','D','E','F','G','H'];
const serial=s=>(Date.parse(s)-Date.UTC(1899,11,30))/86400000;
const dateFmt='dd/mm/yyyy';
const localFmt='[$-it-IT]ddd hh:mm';
const dateText=(x,year=true)=>`RIGHT("0"&DAY(${x}),2)&"/"&RIGHT("0"&MONTH(${x}),2)${year?'&"/"&YEAR('+x+')':''}`;
const timeText=x=>`RIGHT("0"&HOUR(${x}),2)&":"&RIGHT("0"&MINUTE(${x}),2)`;
function values(sh,r,v){sh.getRange(r).values=v;}
function formula(sh,r,f){sh.getRange(r).formulas=[[f]];}
function merge(sh,r,text){sh.mergeCells(r);if(text!==undefined)sh.getRange(r.split(':')[0]).values=[[text]];}
function base(sh,range){sh.showGridLines=false;sh.getRange(range).format.font={name:'Arial',size:11,color:C.navy};sh.getRange(range).format.verticalAlignment='center';}
function header(sh,r){sh.getRange(r).format.fill=C.navy;sh.getRange(r).format.font={name:'Arial',size:11,bold:true,color:C.white};sh.getRange(r).format.horizontalAlignment='center';}
function input(sh,r){sh.getRange(r).format.fill=C.input;sh.getRange(r).format.font.color=C.navy;}
function list(sh,r,v){sh.getRange(r).dataValidation={rule:{type:'list',values:v}};}
function bound(sh,r,type,min,max){sh.dataValidations.add({range:r,rule:{type,operator:'between',formula1:min,formula2:max}});}

// A single, readable control panel; the printable sheets contain no editable inputs.
const settings=sheets.Parametri;base(settings,'A1:M32');settings.getRange('A1:M32').format.fill=C.white;
for(const [c,w] of Object.entries({A:7,B:21,C:14,D:17,E:13,F:13,G:7,H:7,I:7,J:7,K:7,L:7,M:7}))settings.getRange(`${c}:${c}`).format.columnWidth=w;
merge(settings,'A1:M1','Il tuo pannello di pianificazione');settings.getRange('A1').format.font={name:'Arial',size:20,bold:true,color:C.teal};settings.getRange('A1:M1').format.rowHeight=32;
merge(settings,'A2:M2','Modifica le celle crema. Il Planner si aggiorna subito; per stampare usa il foglio Planner.');
merge(settings,'A4:D4','01  GIORNATA E VISUALIZZAZIONE');header(settings,'A4:D4');
values(settings,'B5:C8',[['Data di casa',serial('2026-09-24T00:00:00Z')],['Inizio vista',0],['Passo estesa (min)',30],['Durata estesa (h)',null]]);formula(settings,'C8','=48*C7/60');
settings.getRange('C5').setNumberFormat(dateFmt);settings.getRange('C6').setNumberFormat('hh:mm');input(settings,'C5:C7');list(settings,'C7',['30','60']);
merge(settings,'F4:M4','02  REPERIBILITÀ CON RISERVA');header(settings,'F4:M4');
merge(settings,'F5:I5','Ore prima dell’attività');values(settings,'J5',[[2]]);input(settings,'J5');
merge(settings,'F6:I6','Ore dopo l’attività');values(settings,'J6',[[2]]);input(settings,'J6');settings.getRange('J5:J6').setNumberFormat('0.0');
merge(settings,'F8:M8','Esempio: 09–18, margini 2/2 → riserva 07–09 e 18–20.');settings.getRange('F8:M8').format.font.size=9;
merge(settings,'A10:M10','03  LE TUE COLONNE  ·  prima riga = fuso di casa');header(settings,'A10:M10');
values(settings,'A11:M11',[['N.','Località','Fuso','Calendario','Dalle','Fino','Lun','Mar','Mer','Gio','Ven','Sab','Dom']]);header(settings,'A11:M11');
values(settings,'A12:M18',displayZones.map((z,i)=>[i+1,z.label,z.key,['VN','IN','BY','IT','GB-ENG','US','US'][i],(i?9:5)/24,(i?18:23)/24,0,0,0,0,0,i?1:0,i?1:0]));input(settings,'B12:M18');settings.getRange('E12:F18').setNumberFormat('hh:mm');settings.getRange('C12:M18').format.horizontalAlignment='center';settings.getRange('G12:M18').setNumberFormat('0');
list(settings,'C12:C18',[...displayZones.map(z=>z.key),'dhaka']);list(settings,'D12:D18',['Nessuno','VN','IN','BY','IT','GB-ENG','US','BD']);list(settings,'G12:M18',['0','1']);
settings.getRange('G12:M18').conditionalFormats.addCustom('=G12=1',{fill:C.off,font:{bold:true}});
merge(settings,'A20:M20','Giorni della settimana: 1 = riposo, 0 = applica gli orari. Puoi scegliere qualsiasi combinazione.');
merge(settings,'A21:M21','Esempio Bangladesh: fuso dhaka, calendario BD, Ven = 1 se il venerdì è riposo. Gli altri giorni li scegli tu.');
merge(settings,'A22:M22','Una festa patronale o una chiusura aziendale va in Calendari, con una data e un codice specifico (es. IT-ROMA).');
settings.getRange('A20:M22').format.font.size=10;
merge(settings,'A24:M24','04  CALENDARI DA FILE  ·  Dati → Aggiorna tutto');header(settings,'A24:M24');
values(settings,'A25',[['Cartella']]);merge(settings,'B25:M25',`${out}/Calendar data`);input(settings,'B25:M25');settings.getRange('B25:M25').format.font.size=9;
merge(settings,'A27:M27','CSV separati: Citta, Periodi_DST, Calendari, Santi, Fasi_lunari. Le aggiunte in Calendari restano manuali.');
merge(settings,'A28:M28','Aggiorna tutto rilegge il CSV. Non sorveglia il disco in tempo reale; senza refresh rimane l’ultima copia caricata.');
merge(settings,'A29:M29','Il percorso è modificabile se sposti i file. Errori di importazione: controlla Dati → Query e connessioni.');
settings.getRange('A27:M29').format.font.size=10;
settings.getRange('A2:M29').format.rowHeight=23;settings.getRange('A12:M18').format.rowHeight=29;settings.freezePanes.freezeRows(11);settings.tabColor=C.teal;
bound(settings,'C5','date',serial('2026-01-01'),serial('2028-12-31'));bound(settings,'C6','decimal',0,0.9993055556);bound(settings,'E12:F18','decimal',0,0.9993055556);bound(settings,'J5:J6','decimal',0,24);

// The visible planner and the extended print view share all inputs.
for(const name of ['Planner','Estesa']){
 const s=sheets[name];const rows=name==='Planner'?24:48;const last=16+rows;
 base(s,`A1:X${last+2}`);s.getRange(`A1:H${last+2}`).format.fill=C.white;
 s.getRange('A:A').format.columnWidth=20; s.getRange('B:H').format.columnWidth=18.5;
 merge(s,'A1:H1',name==='Planner'?'Disponibilità e fusi orari':'Disponibilità e fusi orari — vista estesa');
 s.getRange('A1').format.font={name:'Arial',size:17,bold:true,color:C.teal};
 merge(s,'A2:H2');formula(s,'A2',`="Giornata di "&Planner!B8&"  |  "&${dateText('Planner!B4')}&"  |  "&${name==='Planner'?'"24 ore, intervalli di 60 minuti"':'Planner!H4&" ore, intervalli di "&Planner!F4&" minuti"'}`);
 s.getRange('A2:H2').format.font={name:'Arial',size:10,color:C.muted};
 merge(s,'A3:H3');formula(s,'A3','=Info!B3');s.getRange('A3:H3').format.font={name:'Arial',size:9,color:C.teal};
 merge(s,'A15:H15');formula(s,'A15','=Info!B4');s.getRange('A15:H15').format.font={name:'Arial',size:9,color:C.teal};
 values(s,'A4:H4',[['Data a casa',serial('2026-09-24T00:00:00Z'),'Dalle',0,'Estesa (min)',30,'Ore estese',null]]);
 values(s,'A5:E5',[['Riserva prima (h)',2,'Dopo (h)',2,'Disponibilità']]);merge(s,'F5:H5','Orari locali per colonna');
 formula(s,'H4','=48*F4/60');s.getRange('B4').setNumberFormat(dateFmt);s.getRange('D4').setNumberFormat('hh:mm');
 s.getRange('B5').setNumberFormat('0.0');s.getRange('D5').setNumberFormat('0.0');
 for(const r of ['B4','D4','F4','B5','D5'])input(s,r);
 merge(s,'A6:B6','DISPONIBILE');merge(s,'C6:D6','CON RISERVA');merge(s,'E6:F6','NON DISPONIBILE');merge(s,'G6:H6','F = festività locale');
 s.getRange('A6:H6').format.font={name:'Arial',size:9,bold:true,color:C.navy};s.getRange('A6:H6').format.horizontalAlignment='center';
 s.getRange('A6:B6').format.borders={preset:'outside',style:'thin',color:C.line};s.getRange('C6:D6').format.fill=C.reserve;s.getRange('E6:F6').format.fill=C.off;s.getRange('G6:H6').format.fill=C.light;
 merge(s,'A7:H7');formula(s,'A7','=IF(Calcoli!$B$7=FALSE,"CONTROLLA DATA, ORA O MARGINI: ora assente/ambigua al cambio DST oppure input non valido.",IF(COUNTIF(Calcoli!$B$10:$H$10,FALSE)>0,"CONTROLLA LE COLONNE: fuso, calendario, orari o weekend non validi.","Celle crema modificabili. La prima colonna è il fuso di casa. Calendari incompleti segnalati sotto."))');
 s.getRange('A7:H7').format.font={name:'Arial',size:9,color:C.muted};
 values(s,'A8:H8',[['Località',...displayZones.map(z=>z.label)]]);header(s,'A8:H8');
 values(s,'A9:H9',[['Fuso (codice)',...displayZones.map(z=>z.key)]]);
 values(s,'A10:H10',[['Calendario','VN','IN','BY','IT','GB-ENG','US','US']]);
 values(s,'A11:H11',[['Attività dalle',5/24,...Array(6).fill(9/24)]]);
 values(s,'A12:H12',[['Attività fino',23/24,...Array(6).fill(18/24)]]);
 values(s,'A13:H13',[['Giorni di riposo','—',...Array(6).fill('sab dom')]]);
 values(s,'A14', [['Copertura feste']]);
 for(let i=0;i<7;i++){
  const c=cols[i];const cr=name==='Planner'?14:15;
  formula(s,`${c}14`,`=Calcoli!${c}${cr}`);
 }
 s.getRange('B11:H12').setNumberFormat('hh:mm');s.getRange('B9:H14').format.horizontalAlignment='center';
 s.getRange('B9:H13').format.font.size=10;s.getRange('B9:H9').format.font.size=9;s.getRange('B14:H14').format.font={name:'Arial',size:9,color:C.muted};
 input(s,'B9:H13');s.getRange('A9:A14').format.font.size=9;
 s.getRange('A16').values=[['UTC']];for(const c of cols)formula(s,`${c}16`,`=${c}8`);header(s,'A16:H16');
 for(let row=17;row<=last;row++){
  const src=name==='Planner'?row:row+24;
  formula(s,`A${row}`,`=Calcoli!A${src}`);
  for(let i=0;i<7;i++){
   const c=cols[i],state=String.fromCharCode(74+i),flag=String.fromCharCode(82+i),calcState=String.fromCharCode(82+i),calcFlag=i===0?'Z':'A'+String.fromCharCode(64+i);
   formula(s,`${c}${row}`,`=Calcoli!${c}${src}`);
   formula(s,`${state}${row}`,`=Calcoli!${calcState}${src}`);
   formula(s,`${flag}${row}`,`=Calcoli!${calcFlag}${src}`);
  }
 }
 s.getRange(`A17:H${last}`).setNumberFormat(localFmt);s.getRange(`A17:H${last}`).format.horizontalAlignment='center';
 s.getRange(`A17:A${last}`).format.fill=C.light;s.getRange(`A17:A${last}`).format.font.color=C.muted;
 const g=s.getRange(`B17:H${last}`);g.format.fill=C.off;
 g.conditionalFormats.addCustom('=J17=2',{fill:C.white});
 g.conditionalFormats.addCustom('=J17=1',{fill:C.reserve});
 g.conditionalFormats.addCustom('=J17=-1',{fill:C.error,font:{bold:true,color:'#8F3F24'}});
 g.conditionalFormats.addCustom('=R17>=1',{numberFormat:'[$-it-IT]ddd hh:mm" F"'});
 merge(s,`A${last+2}:H${last+2}`,'F: giorno festivo nel calendario selezionato. Bianco = attività; rosa = riserva; rosso = fuori orario/riposo.');
 s.getRange(`A${last+2}:H${last+2}`).format.font={name:'Arial',size:9,color:C.muted};
 const heights={1:23,2:16,3:17,4:20,5:20,6:18,7:17,8:21,9:16,10:16,11:16,12:16,13:16,14:16,15:17,16:21};
 for(const [r,h] of Object.entries(heights))s.getRange(`A${r}:H${r}`).format.rowHeight=h;
 s.getRange(`A17:H${last}`).format.rowHeight=13.5;s.getRange(`A${last+1}:H${last+1}`).format.rowHeight=5;s.getRange(`A${last+2}:H${last+2}`).format.rowHeight=16;
 for(const [cell,source] of Object.entries({B4:'C5',D4:'C6',F4:'C7',B5:'J5',D5:'J6'}))formula(s,cell,`=Parametri!${source}`);
 for(let i=0;i<7;i++){
  const c=cols[i],rr=12+i;
  for(const [row,source] of [[8,'B'],[9,'C'],[10,'D'],[11,'E'],[12,'F']])formula(s,`${c}${row}`,`=Parametri!${source}${rr}`);
  const daytext=['lun','mar','mer','gio','ven','sab','dom'].map((d,j)=>`IF(Parametri!${String.fromCharCode(71+j)}${rr}=1,"${d} ","")`).join('&');
  formula(s,`${c}13`,`=IF(SUM(Parametri!G${rr}:M${rr})=0,"—",TRIM(${daytext}))`);
 }
 s.getRange('B9:H13').format.fill=C.light;for(const r of ['B4','D4','F4','B5','D5','F5:H5'])s.getRange(r).format.fill=C.light;
 formula(s,'A7','=IF(Calcoli!$B$7=FALSE,"CONTROLLA PARAMETRI: data/ora ambigua o inesistente, fuso fuori periodo, oppure input non valido.",IF(COUNTIF(Calcoli!$B$10:$H$10,FALSE)>0,"CONTROLLA PARAMETRI: fuso, calendario, orari o giorni di riposo non validi.","Modifica dal foglio Parametri. Ogni colonna ha i propri orari e giorni di riposo; margini comuni."))');
 s.freezePanes.freezeRows(16);s.tabColor=C.teal;
}

// User-owned holiday calendars and an expandable date list.
const cal=sheets.Calendari;base(cal,'A1:F535');cal.getRange('A:A').format.columnWidth=16;cal.getRange('B:B').format.columnWidth=29;cal.getRange('C:C').format.columnWidth=38;cal.getRange('D:D').format.columnWidth=17;cal.getRange('E:E').format.columnWidth=46;cal.getRange('F:F').format.columnWidth=58;
merge(cal,'A1:F1','Calendari delle festività');cal.getRange('A1').format.font={name:'Arial',size:17,bold:true,color:C.teal};
merge(cal,'A2:F2','Aggiungi il calendario, poi le date. Caricato = 1 solo se l’elenco copre tutto il periodo dichiarato.');
merge(cal,'A3:F3','Le festività sono giorni interi locali. I giorni di riposo settimanale si scelgono in Parametri. Le date sotto si sommano a Da_file.');
values(cal,'A5:F5',[['Codice','Paese / regione','Dal','Al','Caricato 0/1','Fonte / note']]);header(cal,'A5:F5');
values(cal,'A6:F13',[
 ['Nessuno','Festività disattivate',serial('2026-01-01'),serial('2028-12-31'),1,'Nessun calendario applicato.'],
 ['VN','Vietnam',null,null,0,'Inserire calendario ufficiale nazionale/locale.'],['IN','India',null,null,0,'Specificare stato o regione: non esiste un unico calendario completo.'],
 ['BY','Bielorussia',null,null,0,'Inserire calendario ufficiale.'],['IT','Italia',null,null,0,'Aggiungere anche la festa patronale se pertinente.'],
 ['GB-ENG','Inghilterra e Galles',serial('2026-01-01'),serial('2028-12-31'),1,'https://www.gov.uk/bank-holidays'],['US','Stati Uniti',null,null,0,'Scegliere federale, statale o aziendale.'],['BD','Bangladesh',null,null,0,'Riposo settimanale in Parametri; inserire qui le festività datate.']
]);
cal.getRange('C6:D15').setNumberFormat(dateFmt);input(cal,'A6:F15');bound(cal,'E6:E15','whole',0,1);
merge(cal,'A17:F17','DATE: aggiungi righe alla tabella; Attiva = 1 chiude la disponibilità per quel giorno locale.');
values(cal,'A19:E19',[['Calendario','Data locale','Festività / chiusura','Attiva 0/1','Fonte']]);header(cal,'A19:E19');
const events=data.holidays.map(e=>[e.calendar,serial(e.date),e.name,e.active,e.source]);
cal.getRange('B20:B519').setNumberFormat(dateFmt);cal.getRange('A20:E519').format.font.size=10;
cal.tables.add('A19:E519',true,'HolidayDates');bound(cal,'D20:D519','whole',0,1);
cal.getRange('A19:E19').format.fill=C.navy;cal.getRange('A19:E19').format.font.color=C.white;
cal.freezePanes.freezeRows(19);cal.tabColor='#BE9448';

const imported=sheets.Da_file;base(imported,'A1:E50');for(const [c,w] of Object.entries({A:17,B:18,C:40,D:12,E:52}))imported.getRange(`${c}:${c}`).format.columnWidth=w;
merge(imported,'A1:E1','Festività importate dal CSV');imported.getRange('A1').format.font={name:'Arial',size:17,bold:true,color:C.teal};
merge(imported,'A2:E2','Non modificare questa tabella: Aggiorna tutto la sostituisce con il contenuto del file indicato in Parametri.');
merge(imported,'A3:E3','Questa è l’ultima copia caricata. Per aggiunte locali che restano dopo il refresh usa Calendari.');
values(imported,'A5:E5',[['Calendar','Date','Name','Active','Source']]);values(imported,`A6:E${5+events.length}`,events);
imported.getRange('B6:B100').setNumberFormat(dateFmt);imported.tables.add(`A5:E${5+events.length}`,true,'ImportedHolidays');header(imported,'A5:E5');imported.freezePanes.freezeRows(5);imported.tabColor='#BE9448';
await fs.mkdir(`${out}/Calendar data`,{recursive:true});
const csvrow=a=>a.map(x=>'"'+String(x).replaceAll('"','""')+'"').join(';');
await fs.writeFile(`${out}/Calendar data/Calendars.csv`,'\uFEFF'+['Calendar;Date;Name;Active;Source',...data.holidays.map(e=>csvrow([e.calendar,e.date,e.name,e.active,e.source]))].join('\r\n')+'\r\n','utf8');
async function csv(name,head,rows){await fs.writeFile(`${out}/Calendar data/${name}.csv`,'\uFEFF'+[head.join(';'),...rows.map(csvrow)].join('\r\n')+'\r\n','utf8');}
await csv('Citta',['Code','City','WindowsId','IanaId'],data.zones.map(z=>[z.key,z.label,z.windows,z.iana]));
await csv('Periodi_DST',['Code','FromUtc','UntilUtc','OffsetHours'],data.periods.map(p=>[p.key,p.from.slice(0,19),p.until.slice(0,19),p.offset]));
const saints=[
 [9,24,'Beata Vergine Maria della Mercede','https://www.vaticannews.va/en/saints/09/24/b--v--mary-of-the--mercy.html'],
 [10,4,'San Francesco d’Assisi','https://www.vaticannews.va/it/santo-del-giorno/10/04/san-francesco-d-assisi.html']
];
await csv('Santi',['Month','Day','Name','Source'],saints);
await csv('Fasi_lunari',['Utc','Phase','Between','Source'],data.moon.map(m=>[m.utc,m.phase,m.between,m.source]));

const info=sheets.Info;base(info,'A1:J270');info.getRange('A:A').format.columnWidth=9;info.getRange('B:C').format.columnWidth=10;info.getRange('D:D').format.columnWidth=44;info.getRange('E:E').format.columnWidth=54;info.getRange('G:G').format.columnWidth=24;info.getRange('H:I').format.columnWidth=23;info.getRange('J:J').format.columnWidth=54;
merge(info,'A1:J1','Informazioni del giorno e fasi lunari');info.getRange('A1').format.font={name:'Arial',size:17,bold:true,color:C.teal};
merge(info,'A2:J2','Le informazioni non chiudono la disponibilità. Per una chiusura aggiungi una data attiva in Calendari o nel CSV.');
const nextMoon='INDEX(MoonPhases[Utc],MATCH(Calcoli!B6,MoonPhases[Utc],1)+1)';
merge(info,'B3:J3');formula(info,'B3',`=IFERROR("Luna all’inizio vista: "&IF(ABS(Calcoli!B6-INDEX(MoonPhases[Utc],MATCH(Calcoli!B6,MoonPhases[Utc],1)))<1/1440,INDEX(MoonPhases[Phase],MATCH(Calcoli!B6,MoonPhases[Utc],1)),INDEX(MoonPhases[Between],MATCH(Calcoli!B6,MoonPhases[Utc],1)))&" | prossima: "&INDEX(MoonPhases[Phase],MATCH(Calcoli!B6,MoonPhases[Utc],1)+1)&" "&${dateText(nextMoon,false)}&" "&${timeText(nextMoon)}&" UTC","Luna: data non valida o fuori copertura")`);
merge(info,'B4:J4');formula(info,'B4',`=IFERROR("Ricorrenza "&${dateText('Parametri!C5',false)}&": "&INDEX(Saints[Name],MATCH(MONTH(Parametri!C5)*100+DAY(Parametri!C5),Saints[Key],0))&" (campione, solo informazione)","Ricorrenze: nessun dato per questa data nel campione; non è un calendario completo")`);
merge(info,'A6:E6','SANTI: campione di due ricorrenze fisse, non un calendario liturgico completo.');
values(info,'A7:E7',[['Key','Month','Day','Name','Source']]);values(info,'A8:E9',saints.map(s=>[s[0]*100+s[1],...s]));info.tables.add('A7:E9',true,'Saints');header(info,'A7:E7');
merge(info,'G6:J6','FASI PRIMARIE USNO 2025–2029. Istanti in tempo universale; nessun effetto sulle ore disponibili.');
values(info,'G7:J7',[['Utc','Phase','Between','Source']]);values(info,`G8:J${7+data.moon.length}`,data.moon.map(m=>[serial(m.utc+'Z'),m.phase,m.between,m.source]));info.tables.add(`G7:J${7+data.moon.length}`,true,'MoonPhases');header(info,'G7:J7');info.getRange(`G8:G${7+data.moon.length}`).setNumberFormat('dd/mm/yyyy hh:mm');info.tabColor='#BE9448';info.freezePanes.freezeRows(7);

// Date-aware timezone periods. Snapshot, never guessed offsets.
const f=sheets.Fusi;base(f,`A1:M${data.periods.length+6}`);f.getRange('A:A').format.columnWidth=25;f.getRange('B:B').format.columnWidth=25;f.getRange('C:D').format.columnWidth=31;f.getRange('E:H').format.columnWidth=16;f.getRange('J:J').format.columnWidth=25;f.getRange('K:L').format.columnWidth=23;f.getRange('M:M').format.columnWidth=12;
merge(f,'A1:H1','Fusi orari e ora legale');f.getRange('A1').format.font={name:'Arial',size:17,bold:true,color:C.teal};
merge(f,'A2:H2','Regole Windows estratte il 24/09/2026. Intervallo UTC: 01/01/2026 incluso – 01/01/2029 escluso.');
merge(f,'A3:H3','Un cambio di legge richiede un aggiornamento dei periodi. Ore di casa inesistenti o ambigue vengono rifiutate.');
values(f,'A5:D5',[['Code','City','WindowsId','IanaId']]);header(f,'A5:D5');values(f,`A6:D${zEnd}`,data.zones.map(z=>[z.key,z.label,z.windows,z.iana]));f.tables.add(`A5:D${zEnd}`,true,'ZoneCatalog');
values(f,'J5:M5',[['Code','FromUtc','UntilUtc','OffsetHours']]);header(f,'J5:M5');
const pEnd=5+data.periods.length;values(f,`J6:M${pEnd}`,data.periods.map(p=>[p.key,serial(p.from),serial(p.until),p.offset]));f.getRange(`K6:L${pEnd}`).setNumberFormat('dd/mm/yyyy hh:mm');f.getRange(`M6:M${pEnd}`).setNumberFormat('+0.##;-0.##;0');
f.tables.add(`J5:M${pEnd}`,true,'ZonePeriods');
merge(f,`A${zEnd+3}:H${zEnd+3}`,'Catalogo: WorldClocks di WorkTrail, dati GeoNames CC BY 4.0. I periodi sono derivati da TimeZoneInfo; non editare i dati importati qui.');
merge(f,`A${zEnd+5}:H${zEnd+5}`,'Nuove città: aggiornare il catalogo canonico e rigenerare i CSV. Le tabelle Excel e il menu si espandono con Aggiorna tutto.');
f.tabColor='#8594A0';

const calc=sheets.Calcoli;base(calc,'A1:AN88');
values(calc,'A1',[['Calcoli del planner (minuti interi, UTC e stato locale)']]);
values(calc,'A3:A7',[['Inizio locale, casa'],['Candidati UTC'],['Offset casa, ore'],['Inizio UTC'],['Input generali validi']]);
const keys='ZonePeriods[Code]',starts='ZonePeriods[FromUtc]',ends='ZonePeriods[UntilUtc]',offsets='ZonePeriods[OffsetHours]';
formula(calc,'B3','=Planner!B4+Planner!D4');
const candidates=`(${keys}=Planner!$B$9)*($B$3>=${starts}+${offsets}/24)*($B$3<${ends}+${offsets}/24)`;
formula(calc,'B4',`=SUMPRODUCT(${candidates})`);formula(calc,'B5',`=IF(B4=1,SUMPRODUCT(${candidates}*${offsets}),NA())`);formula(calc,'B6','=ROUND((B3-B5/24)*1440,0)/1440');
formula(calc,'B7',`=IFERROR(AND(B4=1,ISNUMBER(Planner!B4),Planner!B4=INT(Planner!B4),ISNUMBER(Planner!D4),Planner!D4>=0,Planner!D4<1,ISNUMBER(Planner!B5),Planner!B5>=0,Planner!B5<=24,ISNUMBER(Planner!D5),Planner!D5>=0,Planner!D5<=24,OR(Planner!F4=30,Planner!F4=60)),FALSE)`);
values(calc,'A9:A15',[['Località'],['Colonna valida'],['Inizio, minuti'],['Durata, minuti'],['Riposo weekend'],['Feste: 24 ore'],['Feste: vista estesa']]);
for(let i=0;i<7;i++){
 const c=cols[i];formula(calc,`${c}9`,`=Planner!${c}8`);
 const rr=12+i;
 formula(calc,`${c}10`,`=IFERROR(AND(COUNTIF(ZoneCatalog[Code],Planner!${c}9)=1,COUNTIF(Calendari!$A$6:$A$15,Planner!${c}10)=1,ISNUMBER(Planner!${c}11),Planner!${c}11>=0,Planner!${c}11<1,ISNUMBER(Planner!${c}12),Planner!${c}12>=0,Planner!${c}12<1,MOD(ROUND((Planner!${c}12-Planner!${c}11)*1440,0),1440)>0,COUNT(Parametri!G${rr}:M${rr})=7,COUNTIF(Parametri!G${rr}:M${rr},0)+COUNTIF(Parametri!G${rr}:M${rr},1)=7),FALSE)`);
 formula(calc,`${c}11`,`=ROUND(Planner!${c}11*1440,0)`);formula(calc,`${c}12`,`=MOD(ROUND((Planner!${c}12-Planner!${c}11)*1440,0),1440)`);formula(calc,`${c}13`,`=Planner!${c}13`);
 for(const [row,from,to] of [[14,17,40],[15,41,88]]){
  formula(calc,`${c}${row}`,`=IFERROR(IF(Planner!${c}10="Nessuno","Disattivato",IF(COUNTIFS(Calendari!$A$6:$A$15,Planner!${c}10,Calendari!$E$6:$E$15,1)=0,"Non caricato",IF(COUNTIFS(Calendari!$A$6:$A$15,Planner!${c}10,Calendari!$C$6:$C$15,"<="&INT(MIN(${c}${from}:${c}${to})),Calendari!$D$6:$D$15,">="&INT(MAX(${c}${from}:${c}${to})),Calendari!$E$6:$E$15,1)=1,"Caricato","Fuori periodo"))),"Errore fuso")`);
 }
}
values(calc,'A16:H16',[['Istante UTC',...displayZones.map(z=>z.label)]]);values(calc,'J16:P16',[displayZones.map(z=>`${z.key}: minuto locale`)]);values(calc,'R16:X16',[displayZones.map(z=>`${z.key}: stato`)]);values(calc,'Z16:AF16',[displayZones.map(z=>`${z.key}: festa`)]);values(calc,'AH16:AN16',[displayZones.map(z=>`${z.key}: weekend`)]);
for(let r=17;r<=88;r++){
 const n=r<=40?r-17:r-41,step=r<=40?'60':'Planner!$F$4';
 formula(calc,`A${r}`,`=ROUND(($B$6+${n}*${step}/1440)*1440,0)/1440`);
 for(let i=0;i<7;i++){
  const c=cols[i],m=String.fromCharCode(74+i),st=String.fromCharCode(82+i),ho=i===0?'Z':'A'+String.fromCharCode(64+i),wk='A'+String.fromCharCode(72+i);
  const match=`${keys},Planner!${c}$9,${starts},"<="&$A${r},${ends},">"&$A${r}`;
  formula(calc,`${c}${r}`,`=IF(COUNTIFS(${match})=1,ROUND(($A${r}+SUMIFS(${offsets},${match})/24)*1440,0)/1440,NA())`);
  formula(calc,`${m}${r}`,`=MOD(ROUND(${c}${r}*1440,0),1440)`);
  formula(calc,`${ho}${r}`,`=IF(Planner!${c}$10="Nessuno",0,COUNTIFS(HolidayDates[Calendario],Planner!${c}$10,HolidayDates[Data locale],INT(${c}${r}),HolidayDates[Attiva 0/1],1)+COUNTIFS(ImportedHolidays[Calendar],Planner!${c}$10,ImportedHolidays[Date],INT(${c}${r}),ImportedHolidays[Active],1))`);
  formula(calc,`${wk}${r}`,`=INDEX(Parametri!$G${12+i}:$M${12+i},1,WEEKDAY(INT(${c}${r}),2))=1`);
  const distance=`MOD(${m}${r}-${c}$11,1440)`;
  formula(calc,`${st}${r}`,`=IFERROR(IF(AND($B$7,${c}$10),IF(OR(${ho}${r}>0,${wk}${r}),0,IF(${distance}<${c}$12,2,IF(OR(${distance}<${c}$12+ROUND(Planner!$D$5*60,0),${distance}>=1440-ROUND(Planner!$B$5*60,0)),1,0))),-1),-1)`);
 }
}
calc.getRange('A17:H88').setNumberFormat('dd/mm/yyyy hh:mm');calc.getRange('B3').setNumberFormat('dd/mm/yyyy hh:mm');calc.getRange('B6').setNumberFormat('dd/mm/yyyy hh:mm');

const guide=sheets.Guida;base(guide,'A1:B36');guide.getRange('A:A').format.columnWidth=28;guide.getRange('B:B').format.columnWidth=114;guide.getRange('A1:B36').format.wrapText=true;guide.getRange('A1:B36').format.rowHeight=36;
values(guide,'A1:B1',[['Guida al planner','Input, stampa e limiti della versione A4']]);header(guide,'A1:B1');
const help=[
 ['Obiettivo','Confrontare ore di attività e reperibilità nei diversi fusi. Tutte le celle di una riga rappresentano lo stesso istante UTC.'],
 ['Modifiche','Modifica le celle crema nel foglio Parametri. Planner ed Estesa sono viste di sola consultazione e stampa. La prima riga delle località è sempre il fuso di casa.'],
 ['Disponibile','Intervallo dalle/fino: inizio incluso, fine esclusa. 09:00–18:00 rende le 09:00 disponibili e le 18:00 già fuori attività.'],
 ['Con riserva','I margini Parametri J5 e J6 sono ore, anche decimali (0,5 = 30 minuti). Con 09:00–18:00 e 2/2: riserva 07:00–09:00 e 18:00–20:00.'],
 ['Mezzanotte','Le fasce possono attraversare mezzanotte: 22:00–06:00 funziona. Inizio uguale a fine viene rifiutato: questa versione non rappresenta disponibilità 24/24.'],
 ['Per colonna','Ogni colonna applica i propri orari locali, festività e riposo weekend. I margini prima/dopo sono comuni.'],
 ['Riposi settimanali','In Parametri scegli 1 nei giorni di riposo e 0 negli altri. Vale per il giorno locale intero. Per chi riposa il venerdì imposta Ven = 1; non viene imposta una regola nazionale uguale per tutti.'],
 ['Festività','Calendari contiene un registro e la tabella HolidayDates. Ogni data attiva chiude l’intero giorno locale, anche se un turno o un margine arriva dal giorno precedente. F appare accanto all’ora.'],
 ['Aggiungere un calendario','Usa uno degli spazi liberi nel registro A6:F15. Imposta codice, regione e copertura. Aggiungi date al CSV oppure alla tabella HolidayDates. Oltre riga 15 vanno estesi CalendarCodes e le formule del registro. Un calendario locale deve includere anche le date nazionali: non eredita altri codici automaticamente.'],
 ['Caricato / Non caricato','Caricato = elenco dichiarato completo nel periodo. Non caricato o Fuori periodo = copertura non garantita; le date già inserite restano applicate. Nessuno disattiva il calendario.'],
 ['Dati iniziali','Inghilterra e Galles: festività GOV.UK 2026–2028, scaricate il 24/09/2026. Gli altri calendari sono predisposti ma vuoti: inserisci una fonte ufficiale nazionale o regionale.'],
 ['A4 principale','Seleziona solo Planner e stampa il foglio attivo: A4 orizzontale, 24 istanti a passo orario, una pagina. La riga rappresenta l’istante di inizio, non un’intera ora uniformemente disponibile.'],
 ['A4 estesa','Estesa contiene 48 istanti e stampa su due pagine A4. Parametri C7 = 30: 24 ore a mezz’ora. C7 = 60: 48 ore a un’ora. Parametri e intestazioni si ripetono sulla seconda pagina.'],
 ['Ora legale','Fusi contiene periodi UTC con offset derivati dal sistema Windows. L’offset è calcolato per ogni riga: i salti e le ripetizioni dell’ora legale sono visibili.'],
 ['Date valide','Dati dei fusi: 2026–2028 in UTC. Una vista che esce dall’intervallo produce un errore visibile. L’inizio locale ambiguo o inesistente durante un cambio DST va spostato a un orario univoco.'],
 ['Aggiornare i fusi','I dati sono una fotografia delle regole Windows del 24/09/2026, non un servizio online. Un cambiamento delle leggi richiede rigenerare i periodi; non correggere soltanto l’etichetta della città.'],
 ['Regole semplici','Ogni tabella ha quattro regole: bianco, riserva, input non valido, suffisso F. Il rosso è lo sfondo. I calcoli sono in Calcoli; rendilo visibile per ispezionarli.'],
 ['File esterni','La cartella in Parametri B25 contiene cinque CSV UTF-8 con separatore ;: Citta, Periodi_DST, Calendari, Santi, Fasi_lunari. Date ISO AAAA-MM-GG e istanti UTC AAAA-MM-GGThh:mm:ss. Da_file, Fusi e le tabelle Info vengono sostituiti al refresh.'],
 ['Aggiornamento CSV','Dati → Aggiorna tutto. I file non vengono sorvegliati in tempo reale e il refresh automatico all’apertura è disattivato. Un errore di refresh conserva la vecchia copia: controlla tutte e cinque le query prima di fidarti dell’aggiornamento.'],
 ['Santi e ricorrenze','Saints.csv è un campione di due ricorrenze, non un elenco completo né il calendario liturgico annuale. Una riga per mese/giorno; più nomi nella stessa cella. Nessun effetto sugli orari. Eventi mobili vanno modellati con date specifiche in una futura versione.'],
 ['Luna','Fasi primarie USNO 2025–2029, istanti in tempo universale. Lo stato intermedio descrive il tratto tra due fasi primarie, non una percentuale di illuminazione. La fascia del Planner si riferisce all’istante iniziale della vista, anche nella seconda pagina Estesa.'],
 ['Excel','Il file è .xlsx senza macro, con cinque connessioni Power Query a CSV locali. Formula e stampa sono verificate in Excel desktop su questa macchina. Il percorso va aggiornato se si sposta la cartella.'],
 ['Per il futuro tool','Intervalli denominati: PlannerDate, StartLocalTime, ReserveBeforeHours, ReserveAfterHours, ExtendedStepMinutes, LocationLabels, ZoneKeys, CalendarKeys, ActivityStart, ActivityEnd, WeeklyRestDays, DataFolder, SchemaVersion.'],
 ['Provenienza','Workbook nuovo ispirato al flusso del template mostrato dal proprietario. Nessun foglio o elemento grafico Vertex42 copiato. Il prototipo originale conserva le proprie attribuzioni.']
];values(guide,`A3:B${2+help.length}`,help);guide.getRange(`A3:A${2+help.length}`).format.font.bold=true;guide.getRange('A:A').format.fill=C.light;guide.tabColor='#8594A0';

wb.recalculate();
console.log((await wb.inspect({kind:'table',range:'Planner!A4:H14',include:'values',maxChars:1800,tableMaxRows:11,tableMaxCols:8})).ndjson);
await fs.mkdir(out,{recursive:true});
await (await SpreadsheetFile.exportXlsx(wb)).save(target);
const preview=await wb.render({sheetName:'Planner',range:'A1:H42',scale:1.5,format:'png'});
await fs.writeFile(`${scratch}/planner-artifact.png`,new Uint8Array(await preview.arrayBuffer()));
const controlPreview=await wb.render({sheetName:'Parametri',range:'A1:M29',scale:1.3,format:'png'});
await fs.writeFile(`${scratch}/parametri-artifact.png`,new Uint8Array(await controlPreview.arrayBuffer()));
console.log(JSON.stringify({target,periods:data.periods.length,holidayRows:events.length}));
