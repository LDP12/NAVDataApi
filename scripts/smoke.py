#!/usr/bin/env python3
"""Exercise a disposable local sandbox. Explicit --reset clears this key's data."""
import argparse
import json
import os
import urllib.error
import urllib.request
from decimal import Decimal
from urllib.parse import urlparse


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--url', default='http://localhost:5225')
    parser.add_argument('--reset', action='store_true', required=True,
                        help="Allow resetting the supplied API key's sandbox before and after the test")
    args = parser.parse_args()
    key = os.environ.get('NAV_API_KEY')
    if not key:
        parser.error('Set NAV_API_KEY to a disposable development sandbox key.')
    base = args.url.rstrip('/')
    if urlparse(base).hostname not in ('localhost', '127.0.0.1', '::1'):
        parser.error('This smoke test is restricted to a local demo server.')

    def request(method, path, payload=None, authenticated=True):
        headers = {'Content-Type': 'application/json'}
        if authenticated:
            headers['X-Api-Key'] = key
        data = None if payload is None else json.dumps(payload).encode()
        req = urllib.request.Request(base + path, data=data, headers=headers, method=method)
        try:
            response = urllib.request.urlopen(req, timeout=15)
        except urllib.error.HTTPError as error:
            response = error
        with response:
            raw = response.read()
            body = json.loads(raw, parse_float=Decimal) if raw else None
            return response.status, response.headers, body

    def expect(expected, method, path, payload=None, authenticated=True):
        status, headers, body = request(method, path, payload, authenticated)
        assert status == expected, f'{method} {path}: expected {expected}, received {status}'
        print(f'PASS {method} {path}: {status}')
        return headers, body

    payload = dict(fundCode='demo', fundName='Demo Fund', shareClass='A',
                   isin='ZZ0000000999', currency='USD', navDate='2026-10-01',
                   navPerShare=123.4567, sharesOutstanding=1000)
    expect(401, 'GET', '/api/navs', authenticated=False)
    expect(200, 'POST', '/api/admin/reset')
    try:
        _, before = expect(200, 'GET', '/api/navs')
        headers, created = expect(201, 'POST', '/api/navs', payload)
        assert created['fundCode'] == 'DEMO'
        assert created['navPerShare'] == Decimal('123.4567')
        location = headers['Location']
        path = urlparse(location).path
        assert path == f"/api/navs/{created['id']}"
        _, stored = expect(200, 'GET', path)
        assert stored == created
        expect(409, 'POST', '/api/navs', payload)
        payload['navPerShare'] = 124.9876
        _, updated = expect(200, 'PUT', path, payload)
        assert updated['id'] == created['id']
        assert updated['navPerShare'] == Decimal('124.9876')
        _, stored = expect(200, 'GET', path)
        assert stored == updated
        expect(404, 'PUT', '/api/navs/2147483647', payload)
        expect(400, 'POST', '/api/navs', {**payload, 'navDate': None})
        expect(400, 'PUT', path, {**payload, 'navPerShare': 0})
        _, after = expect(200, 'GET', '/api/navs')
        assert len(after) == len(before) + 1
        assert all(record in after for record in before), 'An existing record changed'
        print('PASS read-back precision, stable ID, no unrelated changes')
    finally:
        expect(200, 'POST', '/api/admin/reset')
    _, restored = expect(200, 'GET', '/api/navs')
    assert restored == before
    print('PASS sandbox restored to seed data')


if __name__ == '__main__':
    main()
