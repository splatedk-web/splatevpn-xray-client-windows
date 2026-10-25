const assert=require('node:assert/strict');
const {filter}=require('../wwwroot/js/nodes.js');
const nodes=[
 {id:'preset',subscriptionId:'preset_white_cidr',displayName:'RU 10',ping:50,subscriptionGroup:'Белый список'},
 {id:'personal',displayName:'RU 2',ping:50},
 {id:'sub',subscriptionId:'user',displayName:'Alpha',ping:0,isFavorite:true},
 {id:'disabled',subscriptionId:'off',displayName:'Disabled',ping:1,enabled:false},
 {id:'failed',displayName:'Failed',ping:-1},
 {id:'unknown',displayName:'Unknown'}
];
const ids=opts=>filter(nodes,opts).map(n=>n.id);
assert.deepEqual(ids({filterType:'preset'}),['preset']);
assert.deepEqual(ids({filterType:'personal'}),['personal','failed','unknown']);
assert.deepEqual(ids({filterType:'subscriptions'}),['sub','disabled']);
assert.deepEqual(ids({filterType:'favorites'}),['sub']);
assert(!ids({}).includes('disabled'));
assert.deepEqual(ids({source:'user'}),['sub']);
assert.deepEqual(ids({searchQuery:'БЕЛЫЙ'}),['preset']);
assert.deepEqual(ids({sortOrder:'ping'}),['personal','preset','sub','failed','unknown']);
assert.deepEqual(ids({sortOrder:'name'}),['sub','failed','personal','preset','unknown']);
assert.equal(ids({sortOrder:'favorites'})[0],'sub');
assert.equal(nodes[0].id,'preset'); // filtering never mutates host order
assert.deepEqual(filter(nodes.filter(n=>n.ping<=0),{sortOrder:'ping'}).map(n=>n.id),['sub','failed']);
console.log('PASS node tabs, disabled subscriptions, source IDs, search, stable sorting, unknown/failed ping, favorites, immutability');
