# Licenza e provenienza

Dataset preparato il 24 settembre 2026 per le note private di pianificazione
WorkTrail. Contiene nomi e ricorrenze informative; non include logica di
chiusura, obblighi liturgici, biografie o immagini.

## Dati principali: Apache-2.0

Attribuzione: **LiturgicalCalendarAPI, John R. D'Orazio e contributori**.

- Repository: <https://github.com/Liturgical-Calendar/LiturgicalCalendarAPI>
- Revisione acquisita: `1bb2b7c503a701a9713b2f881795afe46044af3b`.
- Licenza originale conservata integralmente: `raw/LICENSE-Apache-2.0.txt`.
- [Licenza nella revisione usata](https://github.com/Liturgical-Calendar/LiturgicalCalendarAPI/blob/1bb2b7c503a701a9713b2f881795afe46044af3b/LICENSE).
- Dati usati: `propriumdesanctis_1970`, aggiunte `2002` e `2008`, traduzioni
  `it`, decreti e traduzioni italiane dei decreti applicabili nel 2026.
- `raw/CalendarHandler.php` è conservato come fonte Apache-2.0 della
  correzione permanente di Giovanna Francesca de Chantal. Non viene eseguito.
- I due file del messale italiano del 1983 sono stati esaminati ma non usati:
  ripetono voci già incluse nel calendario generale. Non si dichiara una
  ricostruzione completa del calendario nazionale italiano.

Conservare questa attribuzione e la licenza originale con una redistribuzione
dei dati derivati. Le copie in `raw/` sono immutate rispetto alle risposte
scaricate; `raw/sources.json` ne registra URL e hash SHA-256.

## Modifiche rispetto alle fonti

La derivazione seleziona le ricorrenze a data fissa dei santi e alcune
ricorrenze mariane, unisce le traduzioni tramite `event_key`, applica le
aggiunte dei decreti e gli aggiornamenti espliciti dei nomi. Sono inclusi,
tra gli altri, il nome aggiornato dei santi Marta, Maria e Lazzaro e le
aggiunte dei decreti fino alla snapshot, incluso Newman dal 2026.

La data di Giovanna Francesca de Chantal passa dal 12 dicembre della tabella
storica al 12 agosto, come documentato nel codice della stessa revisione
upstream. Il nome di Francesco d'Assisi usa l'apostrofo tipografico
dell'esempio del proprietario. Tutte le modifiche sono elencate in
`manifest.json → changes_from_raw`; esclusioni in `excluded_records`.

Le voci sono ordinate e aggregate per mese/giorno, con `; ` tra nomi e URL.
Le etichette di gruppi di santi restano quelle originali. Non sono stati
inventati nomi o aggiunte per riempire le lacune.

## Esempi forniti dal proprietario

I fatti nome/data/link del 24 settembre e del 4 ottobre sono conservati in
`raw/user-examples.json`. Le pagine collegate sono state aperte per verifica:

- [24 settembre, Beata Vergine Maria della Mercede](https://www.vaticannews.va/en/saints/09/24/b--v--mary-of-the--mercy.html).
- [4 ottobre, San Francesco d'Assisi](https://www.vaticannews.va/it/santo-del-giorno/10/04/san-francesco-d-assisi.html).

Vatican News conserva i diritti sul proprio sito. I due esempi fattuali
forniti dall'utente non rendono il sito una fonte Apache-2.0 o CC0. Nessun
testo biografico, fotografia o corpo di articolo è stato copiato o salvato.
Per Francesco la data e il nome sono presenti anche nella fonte Apache-2.0;
il riferimento Vatican News e la grafia richiesta sono mantenuti.

## Wikidata: tentativo non usato

I [dati strutturati Wikidata sono CC0](https://www.wikidata.org/wiki/Wikidata:Licensing).
La query conservata seleziona lo stato di canonizzazione `P411 = Q43115`,
etichette italiane e valori `P841` ricorrenti. Il tentativo locale registrato
alle 20:51:36 UTC del 23 settembre (03:51:36 del 24 settembre in Asia/Saigon)
è terminato per timeout. Il precedente HTTP 429 alle circa 03:45 è un fatto
segnalato nella richiesta del proprietario, non una risposta acquisita qui.

Non sono stati eseguiti ritenti automatici. Non si attribuisce a Wikidata
alcuna riga del CSV e non si dichiara il CSV interamente CC0.

## Copertura e verifica

191 date, 211 voci, 18 giorni con più voci. Copertura in tutti i 12 mesi;
175 date ricorrenti mancanti su 366, incluso il 29 febbraio. Nel 2026:
191 giorni coperti e 174 mancanti su 365. Non è un martirologio completo,
né un calendario liturgico ufficiale o una fonte per chiusure lavorative.

La ricostruzione offline controlla hash delle fonti, etichette italiane,
validità delle date, unicità mese/giorno e rilettura del CSV con BOM e quattro
colonne esatte. Copertura e hash del CSV sono in `manifest.json`.
