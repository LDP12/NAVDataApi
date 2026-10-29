import {parseRecords, serializeRecord} from './reporting.mjs';

export class NavSession {
  #key = '';
  #generation = 0;
  #controller;
  #fetch;
  #listeners = new Set();
  state = {status:'disconnected', records:[], updatedAt:null, error:null};
  constructor(fetcher = globalThis.fetch.bind(globalThis)) { this.#fetch = fetcher; }
  get connected() { return Boolean(this.#key); }
  subscribe(listener) { this.#listeners.add(listener); listener(this.state); return ()=>this.#listeners.delete(listener); }
  #set(state) { this.state = state; for(const listener of this.#listeners) listener(state); }
  #begin(status) {
    this.#controller?.abort();
    this.#controller = new AbortController();
    const generation = ++this.#generation;
    this.#set({status, records:[], updatedAt:null, error:null});
    return generation;
  }
  disconnect() {
    this.#key = '';
    this.#begin('disconnected');
  }
  async connect(key) {
    this.#key = key;
    return this.refresh();
  }
  async #request(url, options={}) {
    const response = await this.#fetch(url, {
      ...options, cache:'no-store', credentials:'same-origin', redirect:'error',
      signal:AbortSignal.any([this.#controller.signal, AbortSignal.timeout(30000)]),
      headers:{'Accept':'application/json', 'X-Api-Key':this.#key, ...(options.body ? {'Content-Type':'application/json'} : {})}
    });
    const raw = await response.text();
    if (!response.ok) {
      let details = {};
      try { details = JSON.parse(raw); } catch { /* Non-JSON server errors get a safe fallback. */ }
      const message = response.status === 401 || response.status === 403 ? 'API key was not accepted. Connect again with a valid key.' :
        response.status === 409 ? (details.detail || 'A record already exists for this ISIN and date. Choose a different date or edit that record.') :
        response.status === 404 ? 'This record no longer exists. Close the editor and refresh.' :
        response.status >= 500 ? 'The service could not complete this request. Refresh before trying again.' :
        (details.detail || details.title || `Request failed (${response.status}).`);
      throw Object.assign(new Error(message), {status:response.status, fields:details.errors || {}});
    }
    return raw;
  }
  #fail(error, generation, mutation=false) {
    if (generation !== this.#generation) return;
    if (error.status === 401 || error.status === 403) this.#key = '';
    const message = error.status ? error.message : mutation ?
      'Save could not be verified. It may have reached the server. Close the editor and refresh before retrying.' :
      error.name === 'TimeoutError' ? 'The request timed out. Check the service and refresh.' :
      'Unable to load NAV data. Check the service, your connection and the API response, then refresh.';
    this.#set({status:'error', records:[], updatedAt:null, error:{message, status:error.status || 0, fields:error.fields || {}}});
  }
  async refresh() {
    if (!this.#key) return false;
    const generation = this.#begin('loading');
    try {
      const records = parseRecords(await this.#request('/api/navs'));
      if (generation !== this.#generation) return false;
      this.#set({status:'ready', records, updatedAt:new Date(), error:null});
      return true;
    } catch(error) { this.#fail(error,generation); return false; }
  }
  async save(values, id=null) {
    if (!this.#key || ['loading','saving'].includes(this.state.status)) return false;
    const body = serializeRecord(values);
    const generation = this.#begin('saving');
    try {
      const [saved] = parseRecords('[' + await this.#request(id ? `/api/navs/${encodeURIComponent(id)}` : '/api/navs', {method:id?'PUT':'POST', body}) + ']');
      if (generation !== this.#generation) return false;
      if (id && saved.id !== String(id)) throw new Error('Record identifier changed.');
      const [verified] = parseRecords('[' + await this.#request(`/api/navs/${encodeURIComponent(saved.id)}`) + ']');
      if (generation !== this.#generation) return false;
      if (JSON.stringify(verified) !== JSON.stringify(saved)) throw new Error('Read-back did not match the saved record.');
      const records = parseRecords(await this.#request('/api/navs'));
      if (generation !== this.#generation) return false;
      this.#set({status:'ready', records, updatedAt:new Date(), error:null});
      return true;
    } catch(error) { this.#fail(error,generation,true); return false; }
  }
}
