(function (root) {
  'use strict';
  const presets = new Set(['preset_white_cidr','preset_harvester','preset_white_universal']);
  const personal = n => !n.subscriptionId || ['custom','builtin'].includes(n.subscriptionId);
  const preset = n => presets.has(n.subscriptionId);
  const nameCompare = (a,b) => String(a.displayName || a.address || '').localeCompare(String(b.displayName || b.address || ''),'ru',{numeric:true,sensitivity:'base'});
  function filter(nodes, {filterType='all', source='Все', searchQuery='', sortOrder='default'}={}) {
    const query = searchQuery.trim().toLowerCase();
    const rows = nodes.filter(n => {
      if (filterType === 'all' && n.enabled === false) return false;
      if (filterType === 'preset' && !preset(n)) return false;
      if (filterType === 'personal' && !personal(n)) return false;
      if (filterType === 'subscriptions' && (personal(n) || preset(n))) return false;
      if (filterType === 'favorites' && !n.isFavorite) return false;
      if (source !== 'Все' && source !== n.subscriptionId) return false;
      return !query || [n.displayName,n.address,n.protocol,n.transport,n.subscriptionGroup].some(v=>String(v||'').toLowerCase().includes(query));
    });
    if (sortOrder === 'ping') rows.sort((a,b) => {
      const pa=Number.isFinite(a.ping)&&a.ping>0?a.ping:Infinity, pb=Number.isFinite(b.ping)&&b.ping>0?b.ping:Infinity;
      return (pa === pb ? 0 : pa < pb ? -1 : 1) || nameCompare(a,b);
    });
    else if (sortOrder === 'name' || query) rows.sort(nameCompare);
    else if (sortOrder === 'favorites') rows.sort((a,b)=>Number(Boolean(b.isFavorite))-Number(Boolean(a.isFavorite)) || nameCompare(a,b));
    return rows;
  }
  const api={filter,personal,preset,nameCompare};
  if (typeof module !== 'undefined') module.exports=api;
  else root.SplateNodes=api;
})(typeof window !== 'undefined' ? window : globalThis);
