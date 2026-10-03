import fs from 'node:fs/promises';
import {Workbook,SpreadsheetFile} from '@oai/artifact-tool';
process.on('uncaughtException',e=>{console.error(e.message);process.exit(1);});
process.on('unhandledRejection',e=>{console.error(e?.message??String(e));process.exit(1);});
const [out]=process.argv.slice(2);
const wb=Workbook.create();
const sheet=wb.worksheets.add('Eccezioni');
const C={navy:'#233B4D',teal:'#286567',cream:'#FFF3D6',light:'#F1F5F6',off:'#D9A0A0'};
sheet.showGridLines=false;
sheet.getRange('A1:G109').format.font={name:'Arial',size:11,color:C.navy};
sheet.getRange('A1:G109').format.verticalAlignment='center';
for(const [c,w] of Object.entries({A:11,B:15,C:15,D:16,E:45,F:13,G:18}))sheet.getRange(`${c}:${c}`).format.columnWidth=w;
const title=(r,text)=>{sheet.mergeCells(`A${r}:G${r}`);sheet.getRange(`A${r}`).values=[[text]];};
title(1,'Eccezioni personali');sheet.getRange('A1').format.font={name:'Arial',size:18,color:C.teal,bold:true};sheet.getRange('A1:G1').format.rowHeight=30;
title(3,'Le eccezioni hanno precedenza su festività, recuperi nazionali e riposi settimanali.');
title(4,'Riposo: chiusura del giorno intero. Attivita: applica gli orari e i margini della colonna.');
title(5,'Colonna = numero 1–7 in Parametri. Dal e Al sono date locali incluse. Attiva: 1 sì, 0 no.');
title(7,'Una sola eccezione attiva per colonna e giorno. Una sovrapposizione rende lo stato non valido.');
sheet.getRange('A3:G7').format.rowHeight=23;
sheet.getRange('A9:G9').values=[['Colonna','Dal','Al','Effetto','Motivo','Attiva 0/1','Validazione']];
sheet.getRange('A9:G9').format={fill:C.navy,font:{name:'Arial',size:11,bold:true,color:'#FFFFFF'},rowHeight:25};
sheet.getRange('A10:F109').format.fill=C.cream;
sheet.getRange('G10:G109').format.fill=C.light;
sheet.getRange('A10:G109').format.rowHeight=23;
sheet.getRange('B10:C109').setNumberFormat('dd/mm/yyyy');
const validations=[];
for(let r=10;r<=109;r++){
  const formula=`=IFERROR(IF(COUNTA(A${r}:F${r})=0,"",IF(AND(ISNUMBER(F${r}),F${r}=0),"",IF(AND(F${r}=1,ISNUMBER(A${r}),A${r}=INT(A${r}),A${r}>=1,A${r}<=7,ISNUMBER(B${r}),ISNUMBER(C${r}),B${r}=INT(B${r}),C${r}=INT(C${r}),B${r}>0,C${r}>=B${r},OR(D${r}="Riposo",D${r}="Attivita")),"OK","CONTROLLA"))),"CONTROLLA")`;
  sheet.getRange(`G${r}`).formulas=[[formula]];
  validations.push([`G${r}`,formula]);
}
sheet.tables.add('A9:G109',true,'PersonalExceptions');
sheet.getRange('A10:A109').dataValidation={rule:{type:'whole',operator:'between',formula1:1,formula2:7}};
sheet.getRange('F10:F109').dataValidation={rule:{type:'list',values:['0','1']}};
sheet.getRange('D10:D109').dataValidation={rule:{type:'list',values:['Riposo','Attivita']}};
sheet.getRange('G10:G109').conditionalFormats.addCustom('=G10="CONTROLLA"',{fill:'#FBE4B4',font:{bold:true,color:'#8F3F24'}});
sheet.freezePanes.freezeRows(9);sheet.tabColor=C.teal;

// Native Excel applies these authored formulas while preserving its Power Query package.
const formulas={Calcoli:{B8:'=COUNTIF(PersonalExceptions[Validazione],"CONTROLLA")=0'},Planner:{},Estesa:{},Info:{}};
const values={Calcoli:{A8:'Eccezioni valide'},Parametri:{A22:'Patroni locali in Calendari; ferie, chiusure e recuperi personali in Eccezioni (hanno precedenza).',A27:'CSV: Citta, Periodi_DST, Calendari, Santi, Fasi_lunari, Meteo. Le modifiche manuali restano separate.'}};
const letters=n=>{let result='';for(;n;n=Math.floor((n-1)/26))result=String.fromCharCode(65+(n-1)%26)+result;return result;};
const cols=['B','C','D','E','F','G','H'];
for(let i=0;i<7;i++){
  const c=cols[i],rr=12+i;
  formulas.Calcoli[`${c}10`]=`=IFERROR(AND(COUNTIF(ZoneCatalog[Code],Planner!${c}9)=1,COUNTIF(Calendari!$A$6:$A$25,Planner!${c}10)=1,ISNUMBER(Planner!${c}11),Planner!${c}11>=0,Planner!${c}11<1,ISNUMBER(Planner!${c}12),Planner!${c}12>=0,Planner!${c}12<1,MOD(ROUND((Planner!${c}12-Planner!${c}11)*1440,0),1440)>0,COUNT(Parametri!G${rr}:M${rr})=7,COUNTIF(Parametri!G${rr}:M${rr},0)+COUNTIF(Parametri!G${rr}:M${rr},1)=7),FALSE)`;
  for(const [row,from,to] of [[14,17,40],[15,41,88]]){
    formulas.Calcoli[`${c}${row}`]=`=IFERROR(IF(Planner!${c}10="Nessuno","Disattivato",IF(COUNTIFS(Calendari!$A$6:$A$25,Planner!${c}10,Calendari!$E$6:$E$25,1)=0,"Non caricato",IF(COUNTIFS(Calendari!$A$6:$A$25,Planner!${c}10,Calendari!$C$6:$C$25,"<="&INT(MIN(${c}${from}:${c}${to})),Calendari!$D$6:$D$25,">="&INT(MAX(${c}${from}:${c}${to})),Calendari!$E$6:$E$25,1)=1,IF(Planner!${c}10="GB-ENG","Fonte GOV.UK","Base nazionale"),"Fuori periodo"))),"Errore fuso")`;
  }
  const rest=letters(41+i),work=letters(49+i),recovery=letters(57+i),base=letters(65+i);
  for(const [col,label] of [[rest,'eccezioni riposo'],[work,'eccezioni attività'],[recovery,'recuperi nazionali'],[base,'stato orario']])values.Calcoli[`${col}16`]=`${i+1}: ${label}`;
  for(let r=17;r<=88;r++){
    const minute=letters(10+i),state=letters(18+i),holiday=letters(26+i),weekend=letters(34+i);
    const criteria=`PersonalExceptions[Colonna],${i+1},PersonalExceptions[Dal],"<="&INT(${c}${r}),PersonalExceptions[Al],">="&INT(${c}${r}),PersonalExceptions[Attiva 0/1],1`;
    formulas.Calcoli[`${rest}${r}`]=`=COUNTIFS(${criteria},PersonalExceptions[Effetto],"Riposo")`;
    formulas.Calcoli[`${work}${r}`]=`=COUNTIFS(${criteria},PersonalExceptions[Effetto],"Attivita")`;
    formulas.Calcoli[`${holiday}${r}`]=`=IF(Planner!${c}$10="Nessuno",0,COUNTIFS(HolidayDates[Calendario],Planner!${c}$10,HolidayDates[Data locale],INT(${c}${r}),HolidayDates[Attiva 0/1],1)+COUNTIFS(ImportedHolidays[Calendar],Planner!${c}$10,ImportedHolidays[Date],INT(${c}${r}),ImportedHolidays[Active],1,ImportedHolidays[Kind],"HOLIDAY"))`;
    formulas.Calcoli[`${recovery}${r}`]=`=IF(Planner!${c}$10="Nessuno",0,COUNTIFS(ImportedHolidays[Calendar],Planner!${c}$10,ImportedHolidays[Date],INT(${c}${r}),ImportedHolidays[Active],1,ImportedHolidays[Kind],"WORKDAY"))`;
    const distance=`MOD(${minute}${r}-${c}$11,1440)`;
    formulas.Calcoli[`${base}${r}`]=`=IF(${distance}<${c}$12,2,IF(OR(${distance}<${c}$12+ROUND(Planner!$D$5*60,0),${distance}>=1440-ROUND(Planner!$B$5*60,0)),1,0))`;
    formulas.Calcoli[`${state}${r}`]=`=IFERROR(IF(AND($B$7,$B$8,${c}$10),IF(${rest}${r}+${work}${r}>1,-1,IF(${rest}${r}=1,0,IF(${work}${r}=1,${base}${r},IF(${holiday}${r}>0,0,IF(AND(${weekend}${r},${recovery}${r}=0),0,${base}${r}))))),-1),-1)`;
  }
  const match=`MATCH(${c}9,Weather[Code],0)`;
  const observed=`INDEX(Weather[ObservedUtc],${match})`;
  const stamp=`TEXT(DAY(${observed}),"00")&"/"&TEXT(MONTH(${observed}),"00")&" "&TEXT(HOUR(${observed}),"00")&":"&TEXT(MINUTE(${observed}),"00")`;
  const weather=`=IF(COUNTIF(Weather[Code],${c}9)=0,"Non caricato",IF(INDEX(Weather[TemperatureC],${match})="","Non disponibile",IF(OR(LEFT(INDEX(Weather[Status],${match}),5)="stale",ISNUMBER(SEARCH("aged",INDEX(Weather[Status],${match})))),"! ","")&ROUND(INDEX(Weather[TemperatureC],${match}),0)&" °C "&LEFT(INDEX(Weather[Description],${match}),15)&CHAR(10)&${stamp}&" UTC"))`;
  formulas.Planner[`${c}41`]=weather;
  formulas.Estesa[`${c}65`]=weather;
}
const warning='=IF(NOT(Calcoli!$B$8),"CONTROLLA ECCEZIONI: completa le righe attive o disattivale.",IF(NOT(Calcoli!$B$7),"CONTROLLA PARAMETRI: data, ora, fuso o margini non validi.",IF(COUNTIF(Calcoli!$B$10:$H$10,FALSE)>0,"CONTROLLA PARAMETRI: fuso, calendario, orari o riposi non validi.",IF(COUNTIF(Calcoli!$R$17:$X$88,-1)>0,"CONTROLLA ECCEZIONI/FUSI: sovrapposizione di date o dati fuori copertura.","Eccezioni personali > festività > recuperi nazionali > riposi settimanali > orari."))))';
formulas.Planner.A7=warning;formulas.Estesa.A7=warning;
formulas.Info.B4='=IF(COUNTIF(Saints[Key],MONTH(Parametri!C5)*100+DAY(Parametri!C5))=0,"Ricorrenze: nessun dato nella selezione (non è un calendario completo)","Ricorrenze (solo info): "&LEFT(INDEX(Saints[Name],MATCH(MONTH(Parametri!C5)*100+DAY(Parametri!C5),Saints[Key],0)),110)&IF(LEN(INDEX(Saints[Name],MATCH(MONTH(Parametri!C5)*100+DAY(Parametri!C5),Saints[Key],0)))>110,"... vedi Info",""))';
await fs.mkdir(out,{recursive:true});
await fs.writeFile(`${out}/revision-patch.json`,JSON.stringify({formulas,values,exceptionValidation:validations},null,2));
wb.recalculate();
console.log((await wb.inspect({kind:'table',range:'Eccezioni!A9:G12',include:'values,formulas',maxChars:1600})).ndjson);
await (await SpreadsheetFile.exportXlsx(wb)).save(`${out}/eccezioni.xlsx`);
const preview=await wb.render({sheetName:'Eccezioni',range:'A1:G14',scale:1.2});
await fs.writeFile(`${out}/eccezioni-preview.png`,new Uint8Array(await preview.arrayBuffer()));
