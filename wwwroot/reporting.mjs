// Pure reporting logic. Monetary values remain decimal strings, never binary floats.
const numericFields = new Set(['navPerShare', 'sharesOutstanding', 'totalNetAssets']);
const textFields = ['fundCode', 'fundName', 'shareClass', 'isin', 'currency', 'navDate'];
const numberPattern = /^-?(?:0|[1-9]\d*)(?:\.\d+)?(?:[eE][+-]?\d+)?$/;

export function parseRecords(raw) {
  // Validate the original grammar, then tokenize strings atomically. Only unquoted
  // number tokens are converted to strings. This works without reviver context.source.
  JSON.parse(raw);
  const preserved = raw.replace(/"(?:\\.|[^"\\])*"|-?(?:0|[1-9]\d*)(?:\.\d+)?(?:[eE][+-]?\d+)?/g,
    token => token.startsWith('"') ? token : JSON.stringify(token));
  const records = JSON.parse(preserved);
  if (!Array.isArray(records)) throw new Error('Expected an array of NAV records.');
  for (const r of records) {
    if (!r || !/^\d+$/.test(r.id) || textFields.some(k => typeof r[k] !== 'string') ||
        !validDate(r.navDate) || [...numericFields].some(k => typeof r[k] !== 'string' || !numberPattern.test(r[k]))) {
      throw new Error('The API returned an invalid NAV record.');
    }
    for (const k of numericFields) decimalParts(r[k]);
  }
  return records;
}

function decimalParts(value) {
  const text = String(value);
  if (!numberPattern.test(text)) throw new Error('Invalid decimal.');
  const [mantissa, exponent = '0'] = text.toLowerCase().split('e');
  const exp = Number(exponent);
  if (!Number.isInteger(exp) || Math.abs(exp) > 100) throw new Error('Decimal exponent out of range.');
  const negative = mantissa.startsWith('-');
  const [whole, fraction = ''] = mantissa.replace('-', '').split('.');
  let coefficient = BigInt(whole + fraction) * (negative ? -1n : 1n);
  let scale = fraction.length - exp;
  if (scale < 0) { coefficient *= 10n ** BigInt(-scale); scale = 0; }
  return {coefficient, scale};
}
function decimalString(coefficient, scale) {
  const sign = coefficient < 0n ? '-' : '';
  const digits = (coefficient < 0n ? -coefficient : coefficient).toString().padStart(scale + 1, '0');
  return sign + (scale ? `${digits.slice(0, -scale)}.${digits.slice(-scale)}` : digits);
}
function compareDecimal(a, b) {
  const x = decimalParts(a), y = decimalParts(b), scale = Math.max(x.scale, y.scale);
  const delta = x.coefficient * 10n ** BigInt(scale-x.scale) - y.coefficient * 10n ** BigInt(scale-y.scale);
  return delta < 0n ? -1 : delta > 0n ? 1 : 0;
}
function addDecimal(a, b) {
  const x = decimalParts(a), y = decimalParts(b), scale = Math.max(x.scale, y.scale);
  return decimalString(x.coefficient * 10n ** BigInt(scale-x.scale) + y.coefficient * 10n ** BigInt(scale-y.scale), scale);
}
export function formatDecimal(value, minimumPlaces = 4) {
  const {coefficient, scale} = decimalParts(value);
  const [whole, fraction = ''] = decimalString(coefficient, scale).split('.');
  const places = fraction.padEnd(minimumPlaces, '0');
  return whole.replace(/\B(?=(\d{3})+(?!\d))/g, ',') + (places ? '.' + places : '');
}
export function filterRecords(records, {fund='', currency='', isin='', from='', to=''} = {}) {
  const search = isin.trim().toUpperCase();
  return records.filter(r => (!fund || r.fundCode === fund) && (!currency || r.currency === currency) &&
    (!search || r.isin.toUpperCase().includes(search)) && (!from || r.navDate >= from) && (!to || r.navDate <= to));
}
export function filterOptions(options, selection='') {
  const result = [...options];
  const unavailable = Boolean(selection && !result.some(([value])=>value===selection));
  if(unavailable) result.push([selection, `${selection} · no longer in current data`]);
  return {options:result, selection, unavailable};
}
export function latestRecords(records) {
  const byClass = new Map();
  for (const r of records) {
    const previous = byClass.get(r.isin);
    if (!previous || r.navDate > previous.navDate) byClass.set(r.isin, r);
  }
  return [...byClass.values()];
}
export function sortRecords(records, field='navDate', direction='desc') {
  const sign = direction === 'asc' ? 1 : -1;
  return [...records].sort((a,b) => sign * (numericFields.has(field)
    ? compareDecimal(a[field], b[field]) : String(a[field]).localeCompare(String(b[field]))));
}
export function summarize(records) {
  const latest = latestRecords(records), currencies = new Map();
  for (const r of latest) {
    const item = currencies.get(r.currency) || {currency:r.currency, total:'0', classes:0, from:r.navDate, to:r.navDate};
    item.total = addDecimal(item.total, r.totalNetAssets);
    item.classes++;
    if (r.navDate < item.from) item.from = r.navDate;
    if (r.navDate > item.to) item.to = r.navDate;
    currencies.set(r.currency, item);
  }
  return {observations:records.length, classes:latest.length, funds:new Set(records.map(r=>r.fundCode)).size,
    valuations:[...currencies.values()].sort((a,b)=>a.currency.localeCompare(b.currency))};
}
export function trendPoints(records, isin, currency) {
  const ordered = sortRecords(records.filter(r=>r.isin===isin && r.currency===currency), 'navDate','asc');
  if (!ordered.length) return [];
  const start = Date.parse(ordered[0].navDate), span = Date.parse(ordered.at(-1).navDate)-start;
  // Only chart geometry uses Number. Text, CSV, sorting, totals and edits stay exact.
  const values = ordered.map(r=>Number(r.navPerShare));
  const low = Math.min(...values), high = Math.max(...values);
  return ordered.map((record,i)=>({record, x:span ? (Date.parse(record.navDate)-start)/span : 0.5,
    y:high===low ? 0.5 : (values[i]-low)/(high-low)}));
}
export function csvCell(value) {
  let text = String(value ?? '');
  if (/^[\s\u0000-\u001f\u007f]*[=+\-@]/u.test(text)) text = "'" + text;
  return '"' + text.replaceAll('"','""') + '"';
}
export function toCsv(records) {
  const columns = [['ID','id'],['Fund code','fundCode'],['Fund name','fundName'],['Share class','shareClass'],['ISIN','isin'],['Currency','currency'],['NAV date (YYYY-MM-DD)','navDate'],['NAV per share (currency/share)','navPerShare'],['Shares outstanding (shares)','sharesOutstanding'],['Total net assets (currency)','totalNetAssets']];
  return [columns.map(([name])=>csvCell(name)).join(','), ...records.map(r=>columns.map(([,key])=>csvCell(r[key])).join(','))].join('\r\n') + '\r\n';
}
function validDate(value) {
  if (!/^\d{4}-\d{2}-\d{2}$/.test(value) || value.startsWith('0000')) return false;
  const date = new Date(value + 'T00:00:00Z');
  return !Number.isNaN(date.valueOf()) && date.toISOString().slice(0,10) === value;
}
function supportedDecimal(value, positive) {
  try {
    let {coefficient, scale} = decimalParts(value);
    if (positive ? coefficient <= 0n : coefficient < 0n) return false;
    while(scale > 0 && coefficient % 10n === 0n) { coefficient /= 10n; scale--; }
    return scale <= 28 && coefficient <= 79228162514264337593543950335n;
  } catch { return false; }
}
export function validateRecord(values) {
  const errors = {};
  for(const [key,min,max] of [['fundCode',2,20],['fundName',1,200],['shareClass',1,50]]) {
    const text = String(values[key] ?? '').trim();
    if (text.length < min || text.length > max) errors[key] = `Enter ${min}–${max} characters.`;
  }
  if (!/^[A-Z]{2}[A-Z0-9]{9}[0-9]$/.test(values.isin ?? '')) errors.isin = 'Use 2 uppercase letters, 9 alphanumerics and 1 digit (12 characters).';
  if (!/^[A-Z]{3}$/.test(values.currency ?? '')) errors.currency = 'Enter a three-letter uppercase currency code, e.g. USD.';
  if (!validDate(values.navDate ?? '')) errors.navDate = 'Enter a valid date in YYYY-MM-DD format.';
  if (!supportedDecimal(values.navPerShare, true)) errors.navPerShare = 'Enter a positive decimal within .NET decimal precision (up to 28 fractional places).';
  if (!supportedDecimal(values.sharesOutstanding, false)) errors.sharesOutstanding = 'Enter zero or a positive decimal within .NET decimal precision.';
  return errors;
}
export function serializeRecord(values) {
  if (Object.keys(validateRecord(values)).length) throw new Error('Record is not valid.');
  const fields = textFields.map(key=>`${JSON.stringify(key)}:${JSON.stringify(values[key])}`);
  for (const key of ['navPerShare','sharesOutstanding']) fields.push(`${JSON.stringify(key)}:${values[key]}`);
  return '{' + fields.join(',') + '}';
}
