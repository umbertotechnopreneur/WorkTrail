"""Generate staged national holiday and informational saint datasets; never edit the live workbook."""
import argparse
import calendar
import csv
import datetime as dt
import hashlib
import json
from pathlib import Path
import sys
import urllib.parse
import urllib.request

HERE = Path(__file__).resolve().parent
sys.path.insert(0, str(HERE / 'vendor'))
import holidays

YEARS = [2026, 2027, 2028]
COUNTRIES = {
    'CN': ('Cina continentale', 'china'), 'IN': ('India', 'india'),
    'KR': ('Corea del Sud', 'south_korea'), 'JP': ('Giappone', 'japan'),
    'BD': ('Bangladesh', 'bangladesh'), 'IT': ('Italia', 'italy'),
    'FR': ('Francia', 'france'), 'NO': ('Norvegia', 'norway'),
    'SE': ('Svezia', 'sweden'), 'US': ('Stati Uniti', 'united_states'),
    'CA': ('Canada', 'canada'),
}
QUERY = '''SELECT DISTINCT ?saint ?name ?dayLabel ?religionLabel WHERE {
  ?saint wdt:P411 wd:Q43115; p:P841 ?statement; rdfs:label ?name.
  FILTER(LANG(?name) = "it")
  ?statement ps:P841 ?day; wikibase:rank ?rank.
  FILTER(?rank != wikibase:DeprecatedRank)
  ?day rdfs:label ?dayLabel. FILTER(LANG(?dayLabel) = "en")
  OPTIONAL { ?statement pq:P140 ?religion. ?religion rdfs:label ?religionLabel. FILTER(LANG(?religionLabel)="it") }
} ORDER BY ?dayLabel ?name'''

def download_json(url):
    request = urllib.request.Request(url, headers={
        'User-Agent': 'WorkTrail-Planner-Prototype/1.0 (local calendar preparation)',
        'Accept': 'application/sparql-results+json, application/json',
    })
    with urllib.request.urlopen(request, timeout=45) as response:
        return json.load(response)

def write_csv(path, headers, rows):
    with path.open('w', encoding='utf-8-sig', newline='') as stream:
        writer = csv.writer(stream, delimiter=';', lineterminator='\r\n')
        writer.writerow(headers)
        writer.writerows(rows)

def load_existing(path):
    with path.open(encoding='utf-8-sig', newline='') as stream:
        return list(csv.DictReader(stream, delimiter=';'))

def main():
    parser = argparse.ArgumentParser()
    parser.add_argument('--data-folder', type=Path, required=True)
    parser.add_argument('--skip-saints-download', action='store_true')
    parser.add_argument('--holidays-only', action='store_true')
    args = parser.parse_args()
    stage = HERE / 'dati_preparati'
    stage.mkdir(exist_ok=True)
    raw_dir = HERE / 'fonti'
    raw_dir.mkdir(exist_ok=True)
    timestamp = dt.datetime.now(dt.timezone.utc).isoformat(timespec='seconds')
    headers = ['Calendar', 'Date', 'Name', 'Active', 'Source', 'Kind', 'Status', 'Version', 'Updated']
    existing = load_existing(args.data_folder / 'Calendars.csv')
    rows = []
    # Existing countries outside this request remain untouched in meaning.
    for row in existing:
        if row['Calendar'] not in COUNTRIES:
            rows.append([row['Calendar'], row['Date'], row['Name'], int(row['Active']), row['Source'],
                         row.get('Kind', 'HOLIDAY'), row.get('Status', 'Fonte preesistente'),
                         row.get('Version', 'preesistente'), row.get('Updated', timestamp)])
    coverage = []
    for code, (name, module) in COUNTRIES.items():
        language = 'it' if code == 'IT' else 'en_US'
        if code == 'SE':
            # Weekly rest belongs to the person's column, not to imported Sundays.
            data = holidays.Sweden(years=YEARS, observed=True, expand=False, language=language,
                                   include_sundays=False, categories=('public', 'de_facto'))
        else:
            data = holidays.country_holidays(code, years=YEARS, observed=True, expand=False,
                                             language=language,
                                             categories=('public', 'government') if code == 'CA' else ('public',))
        source = f'https://github.com/vacanza/holidays/blob/v{holidays.__version__}/holidays/countries/{module}.py'
        # Generated dates are a national rule-based baseline, not an official completeness claim.
        for day, label in sorted(data.items()):
            if day.year not in YEARS:
                continue
            status = 'Stimata' if 'estimated' in label.lower() else 'Da regole; non verificata singolarmente'
            if code == 'CN' and day.year > 2026:
                status = 'Provvisoria: ponti e recuperi annuali da aggiornare'
            rows.append([code, day.isoformat(), label, 1, source, 'HOLIDAY', status, holidays.__version__, timestamp])
        for day in sorted(data.weekend_workdays):
            if day.year in YEARS:
                if day in data:
                    raise ValueError(f'Contradictory holiday and workday: {code} {day}')
                rows.append([code, day.isoformat(), 'Recupero lavorativo previsto dal calendario nazionale', 1,
                             source, 'WORKDAY', 'Da calendario; verificare applicabilita', holidays.__version__, timestamp])
        summary = []
        for year in YEARS:
            subset = [r for r in rows if r[0] == code and r[1].startswith(str(year))]
            holidays_count = sum(r[5] == 'HOLIDAY' for r in subset)
            if not holidays_count:
                raise ValueError(f'No holiday rows: {code} {year}')
            summary.append({'year': year, 'holidays': holidays_count,
                            'workdays': sum(r[5] == 'WORKDAY' for r in subset),
                            'estimated': sum(r[6] == 'Stimata' for r in subset)})
        scope = 'Nazionale; senza suddivisioni'
        if code == 'CA': scope = 'Base federale (public + government); non tutte le aziende'
        if code == 'SE': scope = 'Public + de_facto; domeniche ricorrenti escluse, gestite in Parametri'
        coverage.append({'code': code, 'name': name, 'scope': scope,
                         'source': source, 'years': summary})
    rows.sort(key=lambda r: (r[0], r[1], r[5]))
    keys = [(r[0], r[1], r[5]) for r in rows]
    if len(set(keys)) != len(keys):
        raise ValueError('Duplicate calendar/date/kind')
    write_csv(stage / 'Calendars.csv', headers, rows)

    holiday_report = {'generated_utc': timestamp, 'holiday_library': f'holidays {holidays.__version__}',
                      'years': YEARS, 'countries': coverage, 'holiday_rows': len(rows),
                      'licence': 'MIT', 'scope': 'National baseline, not individually official-verified; subdivisions excluded'}
    (HERE / 'holiday-manifest.json').write_text(json.dumps(holiday_report, ensure_ascii=False, indent=2), encoding='utf-8')
    if args.holidays_only:
        print(json.dumps(holiday_report, ensure_ascii=False, indent=2))
        return

    snapshot = raw_dir / 'wikidata-santi.json'
    if not args.skip_saints_download:
        result = download_json('https://query.wikidata.org/sparql?' + urllib.parse.urlencode({'query': QUERY, 'format': 'json'}))
        snapshot.write_text(json.dumps(result, ensure_ascii=False, indent=2), encoding='utf-8')
        (raw_dir / 'wikidata-santi.sparql').write_text(QUERY, encoding='utf-8')
    result = json.loads(snapshot.read_text(encoding='utf-8'))
    days = {}
    records = []
    months = {name: number for number, name in enumerate(calendar.month_name) if name}
    for item in result['results']['bindings']:
        label = item['dayLabel']['value']
        parts = label.split()
        if len(parts) != 2 or parts[0] not in months or not parts[1].isdigit():
            # Non-Gregorian recurring dates cannot be safely reduced to month/day.
            records.append({'excluded_day': label, 'id': item['saint']['value']})
            continue
        month, day = months[parts[0]], int(parts[1])
        dt.date(2000, month, day)
        entry = {'name': item['name']['value'], 'source': item['saint']['value'],
                 'tradition': item.get('religionLabel', {}).get('value', 'Non specificata'),
                 'month': month, 'day': day}
        records.append(entry)
        days.setdefault((month, day), {})[(entry['name'], entry['source'])] = entry
    if len(days) < 100:
        raise ValueError(f'Unexpectedly small saint coverage: {len(days)} dates')
    # Keep the two original sourced examples, without turning them into closures.
    for row in load_existing(args.data_folder / 'Saints.csv'):
        key = (int(row['Month']), int(row['Day']))
        days.setdefault(key, {})[(row['Name'], row['Source'])] = {'name': row['Name'], 'source': row['Source']}
    saints = []
    for (month, day), entries in sorted(days.items()):
        unique_names = sorted(set(e['name'] for e in entries.values()), key=str.casefold)
        sources = sorted(set(e['source'] for e in entries.values()))
        saints.append([month, day, '; '.join(unique_names), ' | '.join(sources)])
    write_csv(stage / 'Saints.csv', ['Month', 'Day', 'Name', 'Source'], saints)
    (raw_dir / 'santi-dettaglio.json').write_text(json.dumps(records, ensure_ascii=False, indent=2), encoding='utf-8')
    report = {'generated_utc': timestamp, 'holiday_library': f'holidays {holidays.__version__}',
              'years': YEARS, 'countries': coverage, 'holiday_rows': len(rows),
              'saint_days': len(saints), 'saint_entries': sum(len(v) for v in days.values()),
              'saints_scope': 'Selezione Wikidata con nomi italiani; tradizioni diverse, non calendario liturgico completo',
              'licences': {'holidays': 'MIT', 'wikidata': 'CC0'},
              'files': {p.name: hashlib.sha256(p.read_bytes()).hexdigest() for p in stage.glob('*.csv')}}
    (HERE / 'dataset-manifest.json').write_text(json.dumps(report, ensure_ascii=False, indent=2), encoding='utf-8')
    print(json.dumps(report, ensure_ascii=False, indent=2))

if __name__ == '__main__':
    main()
