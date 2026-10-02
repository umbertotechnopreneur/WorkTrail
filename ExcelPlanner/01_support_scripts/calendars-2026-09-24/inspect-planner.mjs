import fs from 'node:fs/promises';
import {FileBlob, SpreadsheetFile} from '@oai/artifact-tool';
process.on('uncaughtException',e=>{console.error(e.message);process.exit(1);});
process.on('unhandledRejection',e=>{console.error(e?.message??String(e));process.exit(1);});
const [source,output]=process.argv.slice(2);
await fs.mkdir(output,{recursive:true});
const wb=await SpreadsheetFile.importXlsx(await FileBlob.load(source));
const ranges=['Parametri!A11:M18','Calendari!A5:F15','Calendari!A19:E24','Info!A1:J9','Calcoli!B10:H15'];
const reports=[];
for(const range of ranges) reports.push((await wb.inspect({kind:'table',range,include:'values,formulas',tableMaxRows:14,tableMaxCols:13,maxChars:5000})).ndjson);
reports.push(wb.help('workbook',{search:'PowerQuery|powerQuery|queries|connections',include:'index,notes',maxChars:1000}).ndjson);
await fs.writeFile(`${output}/baseline-inspection.ndjson`,reports.join('\n'));
for(const [sheet,range] of [['Planner','A1:H42'],['Parametri','A1:M29'],['Calendari','A1:F23']]){
  const blob=await wb.render({sheetName:sheet,range,scale:1});
  await fs.writeFile(`${output}/${sheet}-before.png`,new Uint8Array(await blob.arrayBuffer()));
}
console.log(reports.slice(0,3).join('\n'));
