import test from 'node:test';
import assert from 'node:assert/strict';
const module = await import('../../wwwroot/session.mjs').catch(e=>{if(e.code==='ERR_MODULE_NOT_FOUND') return {}; throw e;});
function session(fetch) { assert.equal(typeof module.NavSession,'function','NavSession must be implemented'); return new module.NavSession(fetch); }
const value = {id:1,fundCode:'GEF',fundName:'Global Equity',shareClass:'A',isin:'ZZ0000000011',currency:'USD',navDate:'2026-09-01',navPerShare:12.3456,sharesOutstanding:10,totalNetAssets:123.46};
const response = (body, status=200) => new Response(JSON.stringify(body),{status,headers:{'Content-Type':'application/json'}});

test('authenticated read is same-origin with no cache or credentials in URL', async()=>{
  const requests=[];
  const s=session(async(url,options)=>{requests.push({url,options});return response([value]);});
  await s.connect('test-only-placeholder');
  assert.equal(requests[0].url,'/api/navs');
  assert.equal(requests[0].options.headers['X-Api-Key'],'test-only-placeholder');
  assert.equal(requests[0].options.cache,'no-store');
  assert.equal(s.state.status,'ready');
  assert.equal(s.state.records.length,1);
  assert.equal(JSON.stringify(s.state).includes('test-only-placeholder'),false);
});
test('refresh clears old records immediately and errors never show them as current', async()=>{
  let reject;
  let n=0;
  const s=session(()=>++n===1?Promise.resolve(response([value])):new Promise((_,r)=>{reject=r;}));
  await s.connect('test-only-placeholder');
  const pending=s.refresh();
  assert.equal(s.state.status,'loading');
  assert.deepEqual(s.state.records,[]);
  reject(new TypeError('Failed to fetch'));
  await pending;
  assert.equal(s.state.status,'error');
  assert.deepEqual(s.state.records,[]);
  assert.equal(s.state.updatedAt,null);
});
test('a superseded read cannot overwrite the newest response', async()=>{
  let first;
  let n=0;
  const s=session(()=>++n===1?new Promise(r=>{first=r;}):Promise.resolve(response([{...value,id:2}])));
  const old=s.connect('test-only-placeholder');
  await s.refresh();
  first(response([value]));
  await old;
  assert.equal(s.state.records[0].id,'2');
});
test('disconnect clears records and prevents in-flight responses restoring them', async()=>{
  let done;
  const s=session(()=>new Promise(r=>{done=r;}));
  const pending=s.connect('test-only-placeholder');
  s.disconnect();
  done(response([value]));
  await pending;
  assert.equal(s.connected,false);
  assert.equal(s.state.status,'disconnected');
  assert.deepEqual(s.state.records,[]);
  assert.equal(s.state.error,null);
});
test('create uses corrected POST collection route, exact decimals and verifies/refetches', async()=>{
  const requests=[];
  const s=session(async(url,options)=>{
    requests.push({url,options});
    return response(options.method==='POST'?value:url==='/api/navs/1'?value:[value],options.method==='POST'?201:200);
  });
  await s.connect('test-only-placeholder');
  const result=await s.save({...value,navPerShare:'12.3456',sharesOutstanding:'10'});
  assert.equal(result,true);
  assert.deepEqual(requests.map(r=>r.url),['/api/navs','/api/navs','/api/navs/1','/api/navs']);
  assert.equal(requests[1].options.method,'POST');
  assert.ok(requests[1].options.body.includes('"navPerShare":12.3456'));
  assert.equal(s.state.status,'ready');
});
test('edit uses PUT with stable identifier', async()=>{
  let put;
  const s=session(async(url,options)=>{
    if(options.method==='PUT') put={url,options};
    return response(url==='/api/navs/1'?value:[value]);
  });
  // PUT response must be one record, not list.
  const s2=session(async(url,options)=>{
    if(options.method==='PUT') put={url,options};
    return response(url==='/api/navs'? [value]:value);
  });
  await s2.connect('test-only-placeholder');
  assert.equal(await s2.save({...value,navPerShare:'12.3456',sharesOutstanding:'10'},'1'),true);
  assert.equal(put.url,'/api/navs/1');
  assert.equal(put.options.method,'PUT');
});
test('409 retains actionable error details but clears report data', async()=>{
  const s=session(async(url,options)=>options.method==='POST'?response({title:'Conflict',detail:'ISIN and date already exist.'},409):response([value]));
  await s.connect('test-only-placeholder');
  assert.equal(await s.save({...value,navPerShare:'12.3456',sharesOutstanding:'10'}),false);
  assert.equal(s.state.error.status,409);
  assert.match(s.state.error.message,/already exist/);
  assert.deepEqual(s.state.records,[]);
});
test('401 is actionable and drops the rejected key', async()=>{
  const s=session(async()=>response({title:'Unauthorized'},401));
  await s.connect('test-only-placeholder');
  assert.equal(s.connected,false);
  assert.equal(s.state.status,'error');
  assert.match(s.state.error.message,/API key/i);
});
