import {Workbook} from '@oai/artifact-tool';
const w=Workbook.create();
console.log(w.help('worksheet', {search:'pageLayout|pageSetup|print|visibility|definedNames|names', include:'index,examples,notes', maxChars:13000}).ndjson);
