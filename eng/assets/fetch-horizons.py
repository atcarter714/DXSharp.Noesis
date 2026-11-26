#!/usr/bin/env python3
"""Fetch geometric heliocentric J2000 ecliptic states at a TDB epoch or over a date range."""
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


def fetch(body, date, days=0, step_days=1):
    # A calendar date in Horizons vector tables is TDB, not UTC.
    params = dict(format='json', COMMAND=f"'{body}'", CENTER="'500@10'",
                  MAKE_EPHEM='YES', OBJ_DATA='NO', EPHEM_TYPE='VECTORS',
                  REF_SYSTEM='ICRF', REF_PLANE='ECLIPTIC', OUT_UNITS='AU-D',
                  VEC_CORR='NONE', VEC_TABLE='2', CSV_FORMAT='YES', VEC_LABELS='NO',
                  TIME_TYPE='TDB')
    if days:
        end = (dt.date.fromisoformat(date) + dt.timedelta(days=days)).isoformat()
        params.update(START_TIME=f"'{date}'", STOP_TIME=f"'{end}'", STEP_SIZE=f"'{step_days} d'")
    else:
        params.update(TLIST=f"'{date} 00:00:00'", TLIST_TYPE='CAL')
    with urllib.request.urlopen(API + '?' + urllib.parse.urlencode(params, quote_via=urllib.parse.quote), timeout=60) as response:
        data = json.load(response)
    if data.get('error') or not isinstance(data.get('result'), str):
        raise RuntimeError(f"Horizons {body}: {data.get('error', 'missing result')}")
    result = data['result']
    if '$$SOE' not in result or '$$EOE' not in result:
        raise RuntimeError(f'Horizons {body}: no ephemeris table: {result[:500]}')
    table = result.split('$$SOE', 1)[1].split('$$EOE', 1)[0].strip()
    rows = list(csv.reader(io.StringIO(table)))
    expected = days // step_days + 1 if days else 1
    if len(rows) != expected or any(len(row) < 8 for row in rows):
        raise RuntimeError(f'Horizons {body}: expected {expected} vector rows, got {len(rows)}')
    numbers = [[float(row[0]), *map(float, row[2:8])] for row in rows]
    if not all(math.isfinite(n) for row in numbers for n in row):
        raise ValueError('Non-finite state vector')
    if any(abs(b[0]-a[0]-step_days)>1e-8 for a,b in zip(numbers,numbers[1:])):
        raise ValueError('Unexpected epoch spacing')
    return numbers, data.get('signature', {}), result


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--date', default=dt.datetime.now(dt.timezone.utc).date().isoformat(),
                        type=lambda value: dt.date.fromisoformat(value).isoformat())
    parser.add_argument('--days', type=int, default=0, help='Number of days after the start date to cache (0 = snapshot)')
    parser.add_argument('--step-days', type=int, default=1)
    parser.add_argument('--output', type=pathlib.Path)
    args = parser.parse_args()
    if not 0 <= args.days <= 3650 or not 1 <= args.step_days <= 7 or args.days % args.step_days:
        parser.error('days must be 0..3650, step-days 1..7, and days divisible by step-days')
    if args.output is None:
        args.output = ROOT / 'samples/OrbitalCommand/Data' / ('horizons-series.csv' if args.days else 'horizons.csv')
    rows = []
    epochs = None
    signatures = {}
    raw = {}
    for body, name in BODIES:  # JPL requests should be sequential.
        values, signature, result = fetch(body, args.date, args.days, args.step_days)
        body_epochs = [v[0] for v in values]
        if epochs is not None and body_epochs != epochs:
            raise RuntimeError('Planet epochs do not agree')
        epochs = body_epochs
        rows.extend([body, name, *v] for v in values)
        signatures[str(body)] = signature; raw[str(body)] = result
        print(f'{name}: {len(values)} epochs; first position {values[0][1:4]} AU')
    args.output.parent.mkdir(parents=True, exist_ok=True)
    tmp = args.output.with_suffix('.tmp')
    with tmp.open('w', newline='', encoding='utf-8') as f:
        writer = csv.writer(f); writer.writerow(['id','name','jd_tdb','x_au','y_au','z_au','vx_au_day','vy_au_day','vz_au_day']); writer.writerows(rows)
    # Keep the provenance alongside the cache; commit neither until reviewed.
    args.output.with_suffix('.provenance.json').write_text(json.dumps(dict(source=API,
        date_tdb=args.date, days=args.days, step_days=args.step_days, center='500@10', frame='ICRF / J2000 ecliptic', units='AU-D',
        correction='NONE', signatures=signatures, raw=raw), indent=2), encoding='utf-8')
    tmp.replace(args.output)
    print(args.output)

if __name__ == '__main__':
    main()
