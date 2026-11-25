#!/usr/bin/env python3
"""Fetch a single, explicitly TDB-dated geometric heliocentric state for each planet."""
import argparse
import csv
import datetime as dt
import io
import json
import math
import pathlib
import urllib.parse
import urllib.request

BODIES = [(199, 'Mercury'), (299, 'Venus'), (399, 'Earth'), (499, 'Mars'),
          (599, 'Jupiter'), (699, 'Saturn'), (799, 'Uranus'), (899, 'Neptune')]
API = 'https://ssd.jpl.nasa.gov/api/horizons.api'
ROOT = pathlib.Path(__file__).resolve().parents[2]


def fetch(body, date):
    # A calendar date in Horizons vector tables is TDB, not UTC.
    params = dict(format='json', COMMAND=f"'{body}'", CENTER="'500@10'",
                  MAKE_EPHEM='YES', OBJ_DATA='NO', EPHEM_TYPE='VECTORS',
                  TLIST=f"'{date} 00:00:00'", TLIST_TYPE='CAL',
                  REF_SYSTEM='ICRF', REF_PLANE='ECLIPTIC', OUT_UNITS='AU-D',
                  VEC_CORR='NONE', VEC_TABLE='2', CSV_FORMAT='YES', VEC_LABELS='NO',
                  TIME_TYPE='TDB')
    with urllib.request.urlopen(API + '?' + urllib.parse.urlencode(params, quote_via=urllib.parse.quote), timeout=60) as response:
        data = json.load(response)
    if data.get('error') or not isinstance(data.get('result'), str):
        raise RuntimeError(f"Horizons {body}: {data.get('error', 'missing result')}")
    result = data['result']
    if '$$SOE' not in result or '$$EOE' not in result:
        raise RuntimeError(f'Horizons {body}: no ephemeris table: {result[:500]}')
    table = result.split('$$SOE', 1)[1].split('$$EOE', 1)[0].strip()
    rows = list(csv.reader(io.StringIO(table)))
    if len(rows) != 1 or len(rows[0]) < 8:
        raise RuntimeError(f'Horizons {body}: expected one vector row')
    row = rows[0]
    numbers = [float(row[0]), *map(float, row[2:8])]
    if not all(math.isfinite(n) for n in numbers):
        raise ValueError('Non-finite state vector')
    return numbers, data.get('signature', {}), result


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--date', default=dt.datetime.now(dt.timezone.utc).date().isoformat(),
                        type=lambda value: dt.date.fromisoformat(value).isoformat())
    parser.add_argument('--output', type=pathlib.Path,
                        default=ROOT / 'samples/OrbitalCommand/Data/horizons.csv')
    args = parser.parse_args()
    rows = []
    signatures = {}
    raw = {}
    for body, name in BODIES:  # JPL requests should be sequential.
        values, signature, result = fetch(body, args.date)
        rows.append([body, name, *values]); signatures[str(body)] = signature; raw[str(body)] = result
        print(f'{name}: {values[1:4]} AU')
    if len({row[2] for row in rows}) != 1:
        raise RuntimeError('Planet epochs do not agree')
    args.output.parent.mkdir(parents=True, exist_ok=True)
    tmp = args.output.with_suffix('.tmp')
    with tmp.open('w', newline='', encoding='utf-8') as f:
        writer = csv.writer(f); writer.writerow(['id','name','jd_tdb','x_au','y_au','z_au','vx_au_day','vy_au_day','vz_au_day']); writer.writerows(rows)
    # Keep the provenance alongside the cache; commit neither until reviewed.
    args.output.with_suffix('.provenance.json').write_text(json.dumps(dict(source=API,
        date_tdb=args.date, center='500@10', frame='ICRF / J2000 ecliptic', units='AU-D',
        correction='NONE', signatures=signatures, raw=raw), indent=2), encoding='utf-8')
    tmp.replace(args.output)
    print(args.output)

if __name__ == '__main__':
    main()
