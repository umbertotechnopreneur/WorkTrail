import sqlite3, json
from pathlib import Path
p=Path(__file__).resolve().parents[3] / 'WorkTrail/Assets/WorldClocks/world-clocks.sqlite3'
c=sqlite3.connect(p.as_uri()+'?mode=ro',uri=True)
c.row_factory=sqlite3.Row
tables=c.execute("select name,sql from sqlite_master where type='table' order by name").fetchall()
for t in tables:
    name=t['name']
    if not name.replace('_','').isalnum():
        continue
    print(json.dumps({'table':name,'schema':t['sql'],'count':c.execute('select count(*) from "'+name+'"').fetchone()[0]},ensure_ascii=False))
    print(json.dumps([dict(r) for r in c.execute('select * from "'+name+'" limit 2')],ensure_ascii=False))
print('METADATA',json.dumps([dict(r) for r in c.execute('select * from catalog_metadata')],ensure_ascii=False))
print('SELECTED',json.dumps([dict(r) for r in c.execute("select id,name,country_code,timezone_id from city where name in ('Hanoi','Mumbai','Minsk','Rome','London','New York City','Los Angeles','Dhaka')")],ensure_ascii=False))
c.close()
