import sqlite3,json
from pathlib import Path
p=Path(__file__).resolve().parents[3] / 'WorkTrail/Assets/WorldClocks/world-clocks.sqlite3'
c=sqlite3.connect(p.as_uri()+'?mode=ro',uri=True)
c.row_factory=sqlite3.Row
print(json.dumps({'cities':[dict(r) for r in c.execute('SELECT id,name,country_code,latitude,longitude,timezone_id FROM city ORDER BY name')], 'metadata':dict(c.execute('SELECT key,value FROM catalog_metadata'))},ensure_ascii=False))
c.close()
