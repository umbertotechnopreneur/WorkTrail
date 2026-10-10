/* VBWR B
 *
 * Project: WorkTrail
 * Repository: https://github.com/umbertotechnopreneur/WorkTrail
 * Creator: Umberto Giacobbi | https://umbertogiacobbi.biz
 *
 * VibeWare: Human intent, AI execution, and plenty of tokens
 * Manifesto: https://umbertogiacobbi.biz/vibeware/manifesto
 *
 * Modified with AI: OpenAI Codex; added this header on 2026-10-10.
 * Human guidance: Umberto Giacobbi; requested VibeWare branding.
 *
 * Copyright (c) 2026 Umberto Giacobbi
 * License: MIT - see LICENSE
 * SPDX-License-Identifier: MIT
 *
 * VBWR E */

// Author a narrow privacy patch. Native Power Query parts are preserved separately.
import fs from 'node:fs/promises';
import path from 'node:path';
import {fileURLToPath} from 'node:url';
import {FileBlob, SpreadsheetFile} from '@oai/artifact-tool';

const root=path.resolve(path.dirname(fileURLToPath(import.meta.url)), '..');
const output=path.join(root,'artifacts/template-cleanup');
await fs.mkdir(output,{recursive:true});
const wb=await SpreadsheetFile.importXlsx(await FileBlob.load(path.join(root,'Planner_Availability_A4.xlsx')));
const sheet=wb.worksheets.getItem('Parametri');
const before=await wb.render({sheetName:'Parametri',range:'A24:M27',scale:1,format:'png'});
await fs.writeFile(path.join(output,'before.png'),new Uint8Array(await before.arrayBuffer()));
const cells=['B25','B32'];
for(const address of cells) sheet.getRange(address).values=[['']];
wb.recalculate();
const clean=cells.map(address=>({sheet:'Parametri',address,value:sheet.getRange(address).values[0][0]}));
if(clean.some(cell=>cell.value!=='' && cell.value!==null)) throw new Error('Privacy fields are not empty.');
await fs.writeFile(path.join(output,'clear-fields.json'),JSON.stringify(clean));
const after=await wb.render({sheetName:'Parametri',range:'A24:M27',scale:1,format:'png'});
await fs.writeFile(path.join(output,'after.png'),new Uint8Array(await after.arrayBuffer()));
await (await SpreadsheetFile.exportXlsx(wb)).save(path.join(output,'authoring-only.xlsx'));
console.log('Prepared two-cell privacy patch; native workbook must preserve original query parts.');
