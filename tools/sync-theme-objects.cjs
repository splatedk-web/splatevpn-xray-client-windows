// Extract layer coordinates, masks and transforms without changing the source project.
const fs=require('node:fs'),path=require('node:path'),assert=require('node:assert/strict');
const android=path.resolve(process.argv[2]||'../SplateClient');
const source=fs.readFileSync(path.join(android,'app/src/main/java/com/danieldk/splatevpn/ui/components/ThemeLayeredObjects.kt'),'utf8');
function calls(text,name){const result=[];let from=0;while(true){const start=text.indexOf(name+'(',from);if(start<0)break;let depth=1,i=start+name.length+1;for(;depth&&i<text.length;i++){if(text[i]==='(')depth++;if(text[i]===')')depth--;}assert.equal(depth,0);result.push(text.slice(start+name.length+1,i-1));from=i;}return result;}
const result={};
for(const [style,file] of Object.entries({SAKURA:'sakura',GRID:'cyberpunk',OCEAN:'ocean',SUNSET:'sunset',AURORA:'aurora',RINGS:'red'})){
 const marker=style==='RINGS'?'else -> LayerArt':'BackdropStyle.'+style+' -> LayerArt';
 const begin=source.indexOf(marker),end=source.indexOf('\n    BackdropStyle.',begin+1),otherwise=source.indexOf('\n    else -> LayerArt',begin+1);
 const finish=Math.min(...[end,otherwise,source.indexOf('\n}\n',begin)].filter(v=>v>=0));const text=source.slice(begin,finish);
 const aspect=Number(text.match(/LayerArt\(R.drawable.\w+,\s*([\d.]+)f/)[1]);
 const number=v=>Number(v.trim().replace(/f$/,''));
 const parts=calls(text,'part').map(p=>({rect:p.split(',').slice(0,4).map(number),mask:calls(p,'Offset').map(pair=>pair.split(',').map(number))}));
 const layers=calls(text,'ArtLayer').map(p=>{const args=p.split(',');const [part,x,y,width,height]=args.splice(0,5).map(number);const layer={part,x,y,width,height,anchorX:.5,anchorY:1,pivotX:.5,pivotY:1,moveX:0,moveY:0,turn:0,cycles:1,shift:0,alpha:.75,pulse:0,breathe:0};args.forEach(a=>{const [key,value]=a.trim().split('=');layer[key]=number(value);});return layer;});
 assert(parts.length>=3&&layers.length>=2,style);result[style]={file:'object-'+file+'.png',aspect,parts,layers};
 fs.copyFileSync(path.join(android,'app/src/main/res/drawable-nodpi/theme_'+file+'_object.png'),path.resolve('wwwroot/assets/themes/object-'+file+'.png'));
}
fs.writeFileSync('wwwroot/js/objects-data.json',JSON.stringify(result,null,2)+'\n');
fs.writeFileSync('wwwroot/js/objects-data.js','window.SplateObjectData='+JSON.stringify(result)+';\n');
console.log('Synced six original theme atlases and exact layer transforms');
