import test from 'node:test';
import assert from 'node:assert/strict';
const reporting = await import('../../wwwroot/reporting.mjs').catch(error => {
  if (error.code === 'ERR_MODULE_NOT_FOUND') return {};
  throw error;
});
const call = (name, ...args) => {
  assert.equal(typeof reporting[name], 'function', `${name} must be implemented`);
  return reporting[name](...args);
};
const record = (changes = {}) => ({id: '1', fundCode: 'GEF', fundName: 'Global Equity', shareClass: 'A Acc', isin: 'ZZ0000000011', currency: 'USD', navDate: '2026-09-01', navPerShare: '12.3456', sharesOutstanding: '10', totalNetAssets: '123.46', ...changes});
const rows = [record(), record({id:'2', navDate:'2026-09-03', navPerShare:'13.456789', totalNetAssets:'134.57'}), record({id:'3', fundCode:'EBF', isin:'ZZ0000000029', currency:'EUR', totalNetAssets:'50.01'})];

test('raw JSON preserves decimal tokens beyond IEEE-754 precision', () => {
  const raw = '[{"id":1,"fundCode":"GEF","fundName":"\\\"price 123.45\\\"","shareClass":"A","isin":"ZZ0000000011","currency":"USD","navDate":"2026-09-01","navPerShare":123456789.1234567890123456789,"sharesOutstanding":1e2,"totalNetAssets":12345678912.35}]';
  const [r] = call('parseRecords', raw);
  assert.equal(r.navPerShare, '123456789.1234567890123456789');
  assert.equal(r.sharesOutstanding, '1e2');
  assert.equal(r.fundName, '"price 123.45"');
});
test('parser rejects invalid JSON and unexpected record shapes', () => {
  assert.throws(() => call('parseRecords', '[{"id":01}]'), /JSON|number|position|Unexpected/i);
  assert.throws(() => call('parseRecords', '{}'), /array/i);
  assert.throws(() => call('parseRecords', '[{"id":1}]'), /record/i);
});
test('filters combine fund, currency, partial ISIN, inclusive date bounds', () => {
  assert.deepEqual(call('filterRecords', rows, {fund:'GEF', currency:'USD', isin:'0000011', from:'2026-09-02', to:'2026-09-03'}).map(r=>r.id), ['2']);
  assert.equal(call('filterRecords', rows, {isin:'zz0000000011', from:'2026-09-01', to:'2026-09-01'}).length, 1);
});
test('filter choices retain a selected value that disappeared after an edit', () => {
  assert.deepEqual(call('filterOptions', [['GEF','Global Equity']], 'OLD'), {
    options: [['GEF','Global Equity'], ['OLD','OLD · no longer in current data']],
    selection: 'OLD', unavailable: true
  });
  assert.deepEqual(call('filterOptions', [['USD','USD'],['EUR','EUR']], 'USD'), {
    options: [['USD','USD'],['EUR','EUR']], selection: 'USD', unavailable: false
  });
});
test('reversed date filter yields no observations', () => assert.deepEqual(call('filterRecords', rows, {from:'2026-09-03', to:'2026-09-01'}), []));
test('latest selects one observation per ISIN independent of input order', () => {
  assert.deepEqual(call('latestRecords', [rows[1], rows[2], rows[0]]).map(r=>r.id).sort(), ['2','3']);
});
test('date sort is chronological across months and years without mutating input', () => {
  const input = [record({navDate:'2026-10-01'}), record({navDate:'2025-12-31'}), record({navDate:'2026-02-01'})];
  assert.deepEqual(call('sortRecords', input, 'navDate', 'asc').map(r=>r.navDate), ['2025-12-31','2026-02-01','2026-10-01']);
  assert.equal(input[0].navDate, '2026-10-01');
});
test('decimal sort distinguishes precision beyond JavaScript numbers', () => {
  const input = [record({navPerShare:'1.00000000000000000002'}),record({navPerShare:'1.00000000000000000001'})];
  assert.equal(call('sortRecords', input, 'navPerShare', 'asc')[0].navPerShare, '1.00000000000000000001');
});
test('summary counts history but values only latest per class in separate currencies', () => {
  const summary = call('summarize', rows);
  assert.equal(summary.observations, 3);
  assert.equal(summary.classes, 2);
  assert.equal(summary.funds, 2);
  assert.deepEqual(summary.valuations, [{currency:'EUR', total:'50.01', classes:1, from:'2026-09-01', to:'2026-09-01'}, {currency:'USD', total:'134.57', classes:1, from:'2026-09-03', to:'2026-09-03'}]);
});
test('summary exact addition avoids floating point rounding and history duplication', () => {
  const summary = call('summarize', [record({totalNetAssets:'9007199254740993.01'}),record({isin:'ZZ0000000029', totalNetAssets:'0.09'})]);
  assert.equal(summary.valuations[0].total, '9007199254740993.10');
});
test('empty summary has no fabricated currency valuations', () => {
  assert.deepEqual(call('summarize', []).valuations, []);
  assert.equal(call('summarize', []).classes, 0);
});
test('decimal display pads four places without truncating extra precision', () => {
  assert.equal(call('formatDecimal', '4.5'), '4.5000');
  assert.equal(call('formatDecimal', '1234.1234567890123456789'), '1,234.1234567890123456789');
  assert.equal(call('formatDecimal', '1.23456e-4'), '0.000123456');
  assert.equal(call('formatDecimal', '1e3'), '1,000.0000');
});
test('trend uses real date distance and never fabricates missing observations', () => {
  const points = call('trendPoints', [record({navDate:'2026-09-01'}),record({navDate:'2026-09-02'}),record({navDate:'2026-09-11'})], 'ZZ0000000011','USD');
  assert.equal(points.length, 3);
  assert.deepEqual(points.map(p=>p.x), [0,0.1,1]);
});
test('trend isolates share classes and currencies; singleton is centered', () => {
  const points = call('trendPoints', [record(), record({currency:'EUR'}), rows[2]], 'ZZ0000000011','USD');
  assert.equal(points.length,1);
  assert.equal(points[0].x,0.5);
  assert.equal(points[0].y,0.5);
  assert.deepEqual(call('trendPoints', rows, 'missing','USD'), []);
});
test('CSV quotes every cell, escapes quotes/newlines, and retains raw decimals', () => {
  const csv = call('toCsv', [record({fundName:'Fund, "A"\nIncome', navPerShare:'123456789.1234567890123456789'})]);
  assert.ok(csv.includes('"Fund, ""A""\nIncome"'));
  assert.ok(csv.includes('"123456789.1234567890123456789"'));
  assert.ok(csv.startsWith('"ID","Fund code"'));
});
test('CSV neutralizes formulas even after leading whitespace/control characters', () => {
  for (const value of ['=1+1', '+cmd', '-cmd', '@SUM(A1)', '  =1+1', '\t@bad', '\r\n =bad', '\u0000=bad', '\uFEFF=bad']) {
    assert.equal(call('csvCell', value), '"\'' + value.replaceAll('"','""') + '"');
  }
  assert.equal(call('csvCell','ordinary'), '"ordinary"');
});
test('valid form preserves exact decimal request tokens and all fields', () => {
  const values = record({navPerShare:'1.1234567890123456789012345678'});
  assert.deepEqual(call('validateRecord', values), {});
  const body = call('serializeRecord', values);
  assert.ok(body.includes('"navPerShare":1.1234567890123456789012345678'));
  assert.ok(body.includes('"sharesOutstanding":10'));
  assert.equal(JSON.parse(body).fundName, 'Global Equity');
  assert.equal('id' in JSON.parse(body), false);
});
test('form rejects blank text, invalid ISIN/currency/date and invalid decimal ranges', () => {
  const errors = call('validateRecord', record({fundCode:' ', fundName:' ', shareClass:'', isin:'zz0000000011', currency:'usd', navDate:'2026-02-30', navPerShare:'0', sharesOutstanding:'-1'}));
  assert.deepEqual(Object.keys(errors).sort(), ['currency','fundCode','fundName','isin','navDate','navPerShare','shareClass','sharesOutstanding'].sort());
});
test('form rejects decimals that .NET decimal cannot preserve and JSON injection', () => {
  for(const value of ['1e999', '1,"admin":true', '0.00000000000000000000000000001', '79228162514264337593543950336']) {
    assert.ok(call('validateRecord', record({navPerShare:value})).navPerShare);
    assert.throws(()=>call('serializeRecord',record({navPerShare:value})), /valid/i);
  }
});
