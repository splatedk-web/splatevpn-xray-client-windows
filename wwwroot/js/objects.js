(function(){
 'use strict';
 const data=window.SplateObjectData,images={};
 const rgba=(hex,a)=>{const h=hex.slice(1,7);return `rgba(${parseInt(h.slice(0,2),16)},${parseInt(h.slice(2,4),16)},${parseInt(h.slice(4,6),16)},${Math.max(0,Math.min(1,a))})`;};
 for(const [key,art] of Object.entries(data)){const image=new Image();image.src='assets/themes/'+art.file;image.onload=()=>{window.dispatchEvent(new Event('themeArtworkLoaded'));if(window.SplateTheme)SplateTheme.apply(SplateTheme.key);};images[key]=image;}
 function draw(c,w,h,p,time,preview){
  const art=data[p.backdrop],image=images[p.backdrop],u=Math.min(w,h),phase=preview?.18:time/28000,a=phase*Math.PI*2;
  const fill=c.createLinearGradient(0,0,0,h);fill.addColorStop(0,p['bg-dark']);fill.addColorStop(1,p['bg-deep']);c.fillStyle=fill;c.fillRect(0,0,w,h);
  const circle=(x,y,r,color)=>{c.beginPath();c.arc(x,y,r,0,Math.PI*2);c.fillStyle=color;c.fill();};
  const glow=(x,y,r,tone,alpha)=>{const g=c.createRadialGradient(x,y,0,x,y,r);g.addColorStop(0,rgba(tone,alpha));g.addColorStop(1,rgba(tone,0));circle(x,y,r,g);};
  const primary={SAKURA:'#F29BC2',SUNSET:'#FFA169',RINGS:'#EF2248',AURORA:'#42D9AD'}[p.backdrop]||'#62DDF3',secondary={GRID:'#BE48E9',SUNSET:'#BC6BC7',RINGS:'#6A1420'}[p.backdrop]||p['ambient-b'],gain=p.backdrop==='RINGS'?.55:1;
  glow(w*.85+Math.sin(a)*u*.10,h*.24+Math.cos(a)*u*.035,u*1.3,primary,.17*gain);
  c.save();c.translate(w*.14+Math.cos(a)*u*.08,h*.67+Math.sin(a)*u*.025);c.scale(1.3,.4);glow(0,0,u*1.15,secondary,.15*gain);c.restore();
  if(p.backdrop==='SAKURA'){glow(w*.77,h*.20,u*.25,'#EBD9FF',.22);glow(w*.77,h*.20,u*.052,'#FFE9EE',.63);}
  if(p.backdrop==='OCEAN')for(let i=0;i<5;i++){const dx=Math.sin(a+i)*u*.08;c.beginPath();c.moveTo(w*(.35+i*.17)+dx,-h*.08);c.lineTo(w*(.39+i*.17)+dx,-h*.08);c.lineTo(w*(-.35+i*.32)+dx,h*.88);c.lineTo(w*(-.65+i*.32)+dx,h*.88);c.closePath();const g=c.createLinearGradient(0,0,0,h*.88);g.addColorStop(0,rgba('#62DDF3',.11));g.addColorStop(1,rgba('#62DDF3',0));c.fillStyle=g;c.fill();}
  if(!['GRID','RINGS'].includes(p.backdrop))for(let i=0;i<3;i++){const y=h*(.20+i*.15)+Math.cos(a+i)*u*.025,dx=Math.sin(a+i)*u*.07;c.beginPath();c.moveTo(-w*.3+dx,y+h*.09);c.bezierCurveTo(w*.18+dx,y-h*.12,w*.48+dx,y+h*.13,w*1.3+dx,y-h*.16);const g=c.createLinearGradient(0,0,w,0);g.addColorStop(0,rgba(primary,0));g.addColorStop(.5,rgba(i%2?secondary:primary,p.backdrop==='AURORA'?.10:.035));g.addColorStop(1,rgba(primary,0));c.strokeStyle=g;c.lineWidth=u*.08;c.stroke();}
  // Preserve source rectangles, polygon masks and pivot points from the original atlases.
  if(image.complete&&image.naturalWidth){
   const sw=w*(preview?.76:.68),sh=preview?h*.98:Math.min(h*.74,sw*art.aspect),left=w-sw,top=h-sh-(preview?0:74);
   for(const layer of art.layers){const part=art.parts[layer.part],[l,t,r,b]=part.rect,sx=Math.round(l*image.naturalWidth),sy=Math.round(t*image.naturalHeight),iw=Math.round((r-l)*image.naturalWidth),ih=Math.round((b-t)*image.naturalHeight),fit=Math.min(sw*layer.width/iw,sh*layer.height/ih),dw=iw*fit,dh=ih*fit,angle=a*layer.cycles+layer.shift,wave=preview?0:Math.sin(angle),cross=preview?0:Math.sin(angle+.9),px=dw*layer.pivotX,py=dh*layer.pivotY;
    c.save();c.globalAlpha=preview?.95:Math.max(0,Math.min(1,layer.alpha+layer.pulse*wave));c.translate(left+layer.x*sw-dw*layer.anchorX+wave*layer.moveX*sw,top+layer.y*sh-dh*layer.anchorY+cross*layer.moveY*sh);c.translate(px,py);c.rotate(wave*layer.turn*Math.PI/180);c.scale(1+wave*layer.breathe,1-wave*layer.breathe*.5);c.translate(-px,-py);
    if(part.mask.length){c.beginPath();part.mask.forEach(([x,y],i)=>{const xx=(x-l)/(r-l)*dw,yy=(y-t)/(b-t)*dh;if(i===0)c.moveTo(xx,yy);else c.lineTo(xx,yy);});c.closePath();c.clip();}
    c.drawImage(image,sx,sy,iw,ih,0,0,dw,dh);c.restore();
   }
  }
  let seed=817;const random=()=>{seed=(seed*1664525+1013904223)>>>0;return seed/4294967296;};
  for(let i=0;i<(preview?12:64);i++){const x=random(),y=random(),depth=.4+i%4*.2;
   if(p.backdrop==='GRID'){const yy=((y+phase*(3+i%3))%1)*h,xx=((x-phase)%1+1)%1*w;c.beginPath();c.moveTo(xx,yy);c.lineTo(xx-u*.012*depth,yy+u*.04*depth);c.strokeStyle=rgba('#62DDF3',.055+depth*.055);c.lineWidth=u*.001;c.stroke();}
   else{const yy=((y-phase*(1+i%2))%1+1)%1*h,xx=x*w+Math.sin(a+i)*u*.02,fade=(.5+.5*Math.sin(a*2+i))*Math.sin(yy/h*Math.PI);circle(xx,yy,u*(.001+depth*.0012),rgba(primary,fade*.25));}
  }
  for(let i=0;i<(preview?6:22);i++){const x=random(),y=random(),fall=p.backdrop==='SAKURA',yy=((y+(fall?phase:-phase))%1+1)%1,xx=(x+Math.sin(a+i)*.035)*w,fade=Math.sin(yy*Math.PI);
   if(fall){const r=u*(.004+i%3*.002);c.save();c.translate(xx,yy*h);c.rotate(a+i*31*Math.PI/180);c.beginPath();c.moveTo(0,-r);c.bezierCurveTo(r*1.25,-r*.8,r,r*.7,0,r);c.bezierCurveTo(-r*.7,r*.4,-r*.8,-r*.5,0,-r);c.fillStyle=rgba(p['accent-secondary'],fade*.45);c.fill();c.restore();}
   else if(p.backdrop==='OCEAN'){c.beginPath();c.arc(xx,yy*h,u*(.003+i%4*.002),0,Math.PI*2);c.strokeStyle=rgba(p['accent-cyan'],fade*.22);c.lineWidth=u*.0012;c.stroke();}
   else circle(xx,yy*h,u*(.0015+i%3*.0007),rgba(p['accent-secondary'],fade*.28));
  }
 }
 window.SplateObjects={has:key=>!!data[key],draw,images};
})();
