import {filterRecords, filterOptions, latestRecords, sortRecords, summarize, formatDecimal, trendPoints, toCsv, validateRecord} from './reporting.mjs';
import {NavSession} from './session.mjs';

const $ = id => document.getElementById(id);
const session = new NavSession();
const fields = ['fundCode','fundName','shareClass','isin','currency','navDate','navPerShare','sharesOutstanding'];
let view = 'latest', sortField = 'navDate', sortDirection = 'desc', selectedTrend = '', editingId = null, visibleRows = [];
const exportUrls = new Set();
function element(tag, text='', className='') {
  const node = document.createElement(tag);
  if (text !== '') node.textContent = text;
  if (className) node.className = className;
  return node;
}
function svgElement(tag, attributes={}, text='') {
  const node = document.createElementNS('http://www.w3.org/2000/svg', tag);
  for (const [key,value] of Object.entries(attributes)) node.setAttribute(key,value);
  if (text) node.textContent = text;
  return node;
}
function notice(message='', kind='') {
  $('notice').textContent = message;
  $('notice').className = 'notice' + (kind ? ' '+kind : '');
  $('notice').hidden = !message;
}
function filters() {
  return {fund:$('fund-filter').value, currency:$('currency-filter').value, isin:$('isin-filter').value,
    from:$('from-filter').value, to:$('to-filter').value};
}
function setOptions(select, options, placeholder=null, preserveMissing=false) {
  const previous = select.value;
  const prepared = preserveMissing ? filterOptions(options, previous) : {options, selection:previous};
  select.replaceChildren();
  if (placeholder !== null) select.add(new Option(placeholder,''));
  for(const [value,label] of prepared.options) select.add(new Option(label,value));
  if ([...select.options].some(o=>o.value===prepared.selection)) select.value=prepared.selection;
}
function populateFilters(records) {
  const funds = new Map(records.map(r=>[r.fundCode,`${r.fundCode} · ${r.fundName}`]));
  setOptions($('fund-filter'),[...funds].sort(([a],[b])=>a.localeCompare(b)),'All funds',true);
  setOptions($('currency-filter'),[...new Set(records.map(r=>r.currency))].sort().map(v=>[v,v]),'All currencies',true);
}
function clearReport() {
  visibleRows = [];
  for (const id of ['records-body','valuations','counts','chart','trend-observations','trend-class']) $(id).replaceChildren();
  for(const id of ['class-title','class-nav','class-date','class-identity','trend-summary','chart-unit','table-description','table-count']) $(id).textContent='';
  $('report').hidden=true;
}
function resetFilters() {
  for(const id of ['fund-filter','currency-filter','isin-filter','from-filter','to-filter']) $(id).value='';
  $('filter-error').hidden=true;
  $('filter-error').textContent='';
  $('from-filter').removeAttribute('aria-invalid');
  $('to-filter').removeAttribute('aria-invalid');
}
function clearSensitiveState() {
  clearReport();
  resetFilters();
  setOptions($('fund-filter'),[],'All funds');
  setOptions($('currency-filter'),[],'All currencies');
  $('api-key').value='';
  selectedTrend=''; editingId=null; view='latest'; sortField='navDate'; sortDirection='desc';
  closeEditor();
  for(const url of exportUrls) URL.revokeObjectURL(url);
  exportUrls.clear();
}
function showState(state) {
  const ready=state.status==='ready', busy=['loading','saving'].includes(state.status);
  $('refresh').disabled=!session.connected || busy;
  $('new-record').disabled=!ready;
  $('filters').disabled=!ready;
  $('clear-filters').disabled=!ready;
  $('connect').disabled=busy;
  $('api-key').disabled=busy;
  $('connect-form').hidden=session.connected;
  $('connected-controls').hidden=!session.connected;
  $('connection-description').textContent=session.connected ? 'Authenticated requests · key held in memory · disconnect to clear this page.' : 'Connect with an API key. It stays in this tab’s memory only.';
  for(const input of $('record-form').querySelectorAll('input')) input.disabled=busy;
  $('save-record').disabled=busy || !session.connected;
  $('save-record').textContent=state.status==='saving' ? 'Saving & verifying…' : 'Save observation';
  $('cancel-editor').disabled=busy;
  $('close-editor').disabled=busy;
  $('record-dialog').setAttribute('aria-busy',String(state.status==='saving'));
  $('report').setAttribute('aria-busy',String(busy));
  if(ready) {
    $('welcome').hidden=true;
    notice();
    populateFilters(state.records);
    $('updated-at').textContent='Refreshed '+state.updatedAt.toLocaleTimeString([], {hour:'2-digit',minute:'2-digit',second:'2-digit'});
    renderReport();
  } else {
    clearReport();
    $('updated-at').textContent='No current data loaded';
    $('scope-label').textContent=busy ? 'Retrieving your workspace' : 'Connect to explore your NAV data';
    $('welcome').hidden=busy;
    if(state.status==='disconnected') { clearSensitiveState(); notice(); }
    if(busy) notice(state.status==='saving' ? 'Saving your observation, verifying the stored record and refreshing the report…' : 'Loading your NAV observations…', 'loading');
    if(state.status==='error') {
      notice(state.error.message,'error');
      $('scope-label').textContent='Report unavailable · previous values have been cleared';
      if($('record-dialog').open) showSaveError(state.error);
      if(!session.connected) clearSensitiveState();
    }
  }
}
function renderReport() {
  if(session.state.status!=='ready') return;
  const scope=filters(), reversed=Boolean(scope.from && scope.to && scope.from>scope.to);
  $('filter-error').hidden=!reversed;
  $('filter-error').textContent=reversed?'From date must be on or before To date. No observations are shown.':'';
  for(const id of ['from-filter','to-filter']) $(id).setAttribute('aria-invalid',String(reversed));
  const matching=filterRecords(session.state.records,scope), summary=summarize(matching);
  $('report').hidden=false;
  $('scope-label').textContent=`${matching.length} of ${session.state.records.length} observations in scope`;
  $('counts').replaceChildren();
  for(const [value,label] of [[summary.funds,'funds'],[summary.classes,'classes'],[summary.observations,'observations']]) {
    const item=element('span'); item.append(element('strong',String(value)),document.createTextNode(label)); $('counts').append(item);
  }
  $('valuations').replaceChildren();
  for(const item of summary.valuations) {
    const card=element('article','','valuation-card'), head=element('div','','valuation-card-head');
    head.append(element('span','Net assets'),element('span',item.currency,'currency-badge'));
    card.append(head,element('p',formatDecimal(item.total,2),'valuation-total'),element('p',`${item.classes} share ${item.classes===1?'class':'classes'} · ${item.from===item.to?'as of '+item.to:item.from+' to '+item.to}`,'valuation-dates'));
    $('valuations').append(card);
  }
  if(!summary.valuations.length) $('valuations').append(element('p','No eligible valuations. Try another report scope.','valuation-empty panel'));
  const classes=new Map();
  for(const r of sortRecords(matching,'fundCode','asc')) {
    const key=JSON.stringify([r.isin,r.currency]);
    classes.set(key,`${r.fundCode} / ${r.shareClass} · ${r.currency} · ${r.isin}`);
  }
  setOptions($('trend-class'),[...classes]);
  if(classes.has(selectedTrend)) $('trend-class').value=selectedTrend;
  selectedTrend=$('trend-class').value;
  $('trend-class').disabled=!classes.size;
  renderTrend(matching);
  visibleRows=sortRecords(view==='latest'?latestRecords(matching):matching,sortField,sortDirection);
  renderTable();
  $('latest-view').setAttribute('aria-pressed',String(view==='latest'));
  $('history-view').setAttribute('aria-pressed',String(view==='history'));
  $('table-description').textContent=view==='latest'?'One latest eligible observation per ISIN.':'All eligible observations, including historical values. Snapshot totals above are not history sums.';
}
function renderTrend(records) {
  $('chart').replaceChildren(); $('trend-observations').replaceChildren();
  if(!selectedTrend) {
    $('chart').append(element('p','No share-class history in this scope.','empty-results'));
    $('trend-summary').textContent='Choose a broader scope to explore NAV history.';
    $('chart-unit').textContent='NAV / share'; $('class-title').textContent='No share class selected';
    $('class-nav').textContent='—'; $('class-identity').textContent=''; $('class-date').textContent='';
    return;
  }
  const [isin,currency]=JSON.parse(selectedTrend), points=trendPoints(records,isin,currency);
  const first=points[0].record, last=points.at(-1).record;
  const ranked=sortRecords(points.map(p=>p.record),'navPerShare','asc'), low=ranked[0].navPerShare, high=ranked.at(-1).navPerShare;
  const description=`${points.length} ${points.length===1?'observation':'observations'}. ${first.navDate}: ${formatDecimal(first.navPerShare)} ${currency} per share${points.length>1?`; ${last.navDate}: ${formatDecimal(last.navPerShare)} ${currency} per share`:''}. Range ${formatDecimal(low)}–${formatDecimal(high)}. No values are filled for missing dates.`;
  $('chart-unit').textContent=`NAV / share (${currency})`;
  $('trend-summary').textContent=description;
  $('class-title').textContent=`${last.fundCode} / ${last.shareClass}`;
  $('class-identity').textContent=`${last.fundName} · ${isin}`;
  $('class-nav').textContent=`${formatDecimal(last.navPerShare)} ${currency}`;
  $('class-date').textContent='Observation date · '+last.navDate;
  const svg=svgElement('svg',{viewBox:'0 0 700 200',role:'img','aria-labelledby':'chart-title chart-description'});
  svg.append(svgElement('title',{id:'chart-title'},`${last.fundCode} ${last.shareClass}: NAV per share in ${currency}`),svgElement('desc',{id:'chart-description'},description));
  // The exact endpoints are given in text; chart geometry alone is approximate.
  for(const y of [27,72,117,162]) svg.append(svgElement('line',{x1:18,x2:682,y1:y,y2:y,stroke:'#e4ebe5','stroke-dasharray':'3 4'}));
  svg.append(svgElement('text',{x:18,y:16,class:'axis-value'},'High '+formatDecimal(high)),svgElement('text',{x:18,y:176,class:'axis-value'},'Low '+formatDecimal(low)));
  const coords=points.map(p=>[18+p.x*664,152-p.y*118]);
  if(points.length>1) {
    const line=coords.map(([x,y],i)=>`${i?'L':'M'}${x},${y}`).join(' ');
    svg.append(svgElement('path',{d:`${line} L${coords.at(-1)[0]},162 L${coords[0][0]},162 Z`,fill:'#e8f3ec'}));
    svg.append(svgElement('path',{d:line,fill:'none',stroke:'#087363','stroke-width':2.6,'stroke-linejoin':'round','stroke-linecap':'round'}));
  }
  points.forEach((point,i)=>{
    const [cx,cy]=coords[i], circle=svgElement('circle',{cx,cy,r:3.5,fill:'#fff',stroke:'#087363','stroke-width':2});
    circle.append(svgElement('title',{},`${point.record.navDate}: ${formatDecimal(point.record.navPerShare)} ${currency} / share`)); svg.append(circle);
    $('trend-observations').append(element('li',`${point.record.navDate} — ${formatDecimal(point.record.navPerShare)} ${currency} / share`));
  });
  svg.append(svgElement('text',{x:18,y:196},first.navDate));
  if(points.length>1) svg.append(svgElement('text',{x:682,y:196,'text-anchor':'end'},last.navDate));
  $('chart').append(svg);
}
function renderTable() {
  const body=$('records-body'); body.replaceChildren();
  for(const record of visibleRows) {
    const row=element('tr'), fund=element('td','','fund-cell'), name=element('span',record.fundName);
    name.title=record.fundName; fund.append(element('strong',record.fundCode),name);
    const currency=element('td'); currency.append(element('span',record.currency,'currency-badge'));
    const date=element('td'), time=element('time',record.navDate); time.dateTime=record.navDate; date.append(time);
    row.append(fund,element('td',record.shareClass),element('td',record.isin,'isin-cell'),currency,date,
      element('td',formatDecimal(record.navPerShare),'numeric nav-cell'),element('td',formatDecimal(record.sharesOutstanding),'numeric'),element('td',formatDecimal(record.totalNetAssets,2),'numeric'));
    const action=element('td'), edit=element('button','Edit','edit-button');
    edit.type='button'; edit.setAttribute('aria-label',`Edit ${record.fundCode} ${record.shareClass}, ${record.navDate}`);
    edit.addEventListener('click',()=>openEditor(record)); action.append(edit);row.append(action);body.append(row);
  }
  for(const button of document.querySelectorAll('[data-sort]')) {
    const active=button.dataset.sort===sortField;
    button.parentElement.setAttribute('aria-sort',active?(sortDirection==='asc'?'ascending':'descending'):'none');
    button.querySelector('span').textContent=active?(sortDirection==='asc'?'↑':'↓'):'↕';
  }
  $('empty-results').hidden=visibleRows.length>0;
  $('export').disabled=!visibleRows.length;
  $('table-count').textContent=`${visibleRows.length} ${visibleRows.length===1?'record':'records'} · ${view==='latest'?'latest per class':'history'} view`;
}
function clearEditorErrors() {
  $('save-error').hidden=true; $('save-error').textContent='';
  for(const field of fields) { $('error-'+field).textContent=''; $('record-form').elements[field].removeAttribute('aria-invalid'); }
}
function openEditor(record=null) {
  editingId=record?.id || null;
  $('record-form').reset(); clearEditorErrors();
  for(const field of fields) $('record-form').elements[field].value=record ? record[field] : field==='sharesOutstanding'?'0':'';
  $('editor-title').textContent=record?'Edit NAV observation':'Add NAV observation';
  $('editor-description').textContent=record?`Replace all fields on record #${record.id}. The record ID will not change.`:'One record per ISIN and date. All fields are required; shares may be zero.';
  $('record-dialog').showModal();
  $('record-form').elements[record?'navPerShare':'fundCode'].focus();
}
function closeEditor() {
  if($('record-dialog').open) $('record-dialog').close();
  $('record-form').reset(); editingId=null; clearEditorErrors();
}
function fieldErrors(errors) {
  for(const [field,message] of Object.entries(errors)) {
    if(!fields.includes(field)) continue;
    $('error-'+field).textContent=Array.isArray(message)?message.join(' '):message;
    $('record-form').elements[field].setAttribute('aria-invalid','true');
  }
}
function showSaveError(error) {
  $('save-error').textContent=error.message;
  $('save-error').hidden=false;
  const errors={};
  for(const [key,value] of Object.entries(error.fields || {})) {
    const field=key.split('.').at(-1); errors[field.charAt(0).toLowerCase()+field.slice(1)]=value;
  }
  fieldErrors(errors);
  $('save-error').scrollIntoView({block:'nearest'});
}
$('connect-form').addEventListener('submit',event=>{
  event.preventDefault(); const key=$('api-key').value; if(!key) return;
  $('api-key').value=''; session.connect(key);
});
$('disconnect').addEventListener('click',()=>{session.disconnect(); $('api-key').focus();});
$('refresh').addEventListener('click',()=>session.refresh());
$('new-record').addEventListener('click',()=>openEditor());
$('clear-filters').addEventListener('click',()=>{resetFilters();renderReport();});
for(const id of ['fund-filter','currency-filter','from-filter','to-filter']) $(id).addEventListener('change',renderReport);
$('isin-filter').addEventListener('input',renderReport);
$('trend-class').addEventListener('change',()=>{selectedTrend=$('trend-class').value;renderTrend(filterRecords(session.state.records,filters()));});
$('latest-view').addEventListener('click',()=>{view='latest';renderReport();});
$('history-view').addEventListener('click',()=>{view='history';renderReport();});
for(const button of document.querySelectorAll('[data-sort]')) button.addEventListener('click',()=>{
  sortDirection=sortField===button.dataset.sort && sortDirection==='asc'?'desc':'asc'; sortField=button.dataset.sort;renderReport();
});
$('export').addEventListener('click',()=>{
  if(session.state.status!=='ready' || !visibleRows.length) return;
  const url=URL.createObjectURL(new Blob([toCsv(visibleRows)],{type:'text/csv;charset=utf-8'})); exportUrls.add(url);
  const link=element('a');link.href=url;link.download=`nav-${view}-${new Date().toISOString().slice(0,10)}.csv`;
  document.body.append(link);link.click();link.remove();
  setTimeout(()=>{URL.revokeObjectURL(url);exportUrls.delete(url);},1000);
});
$('close-editor').addEventListener('click',closeEditor);
$('cancel-editor').addEventListener('click',closeEditor);
$('record-dialog').addEventListener('cancel',event=>{event.preventDefault();if(session.state.status!=='saving') closeEditor();});
$('record-form').addEventListener('submit',async event=>{
  event.preventDefault(); clearEditorErrors();
  const values=Object.fromEntries(fields.map(field=>[field,$('record-form').elements[field].value]));
  const errors=validateRecord(values);
  if(Object.keys(errors).length) { fieldErrors(errors);$('record-form').elements[Object.keys(errors)[0]].focus();return; }
  const saved=await session.save(values,editingId);
  if(saved) { closeEditor();notice('Observation saved and verified. The report has been refreshed; your filters still apply.'); }
});
window.addEventListener('pagehide',()=>session.disconnect());
session.subscribe(showState);
