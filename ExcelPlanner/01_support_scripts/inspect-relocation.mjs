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

import fs from 'node:fs/promises';
import {FileBlob, SpreadsheetFile} from '@oai/artifact-tool';
process.on('uncaughtException',e=>{console.error(e.message);process.exit(1);});
process.on('unhandledRejection',e=>{console.error(e?.message??String(e));process.exit(1);});
const [path,previewPath]=process.argv.slice(2);
const wb=await SpreadsheetFile.importXlsx(await FileBlob.load(path));
console.log((await wb.inspect({kind:'table',range:'Parametri!A24:M29',include:'values,formulas',tableMaxRows:6,tableMaxCols:13,maxChars:2500})).ndjson);
console.log(wb.help('workbook',{search:'PowerQuery|powerQuery|queries|connections',include:'index,notes',maxChars:1200}).ndjson);
const preview=await wb.render({sheetName:'Parametri',range:'A24:M29',scale:1.2});
await fs.writeFile(previewPath,new Uint8Array(await preview.arrayBuffer()));
