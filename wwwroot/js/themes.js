(function () {
  'use strict';
  const palettes=window.SplatePalettes;
  let key='dark',glass=0,blurEnabled=true,canvas,ctx,frame=0,last=0;
  const reduced=matchMedia('(prefers-reduced-motion: reduce)');
  const images={};
  ['pony','winx'].forEach(name=>{const img=new Image();img.src='assets/themes/'+name+'.png';img.onload=()=>{draw();window.dispatchEvent(new Event('themeArtworkLoaded'));};images[name]=img;});
  const resolved=()=>key==='system'?(matchMedia('(prefers-color-scheme: dark)').matches?'dark':'light'):key;
  const alpha=(hex,a)=>{const s=hex.slice(1,7);return `rgba(${parseInt(s.slice(0,2),16)},${parseInt(s.slice(2,4),16)},${parseInt(s.slice(4,6),16)},${a})`;};
  const luminance=hex=>{const c=[0,2,4].map(i=>parseInt(hex.slice(i+1,i+3),16)/255).map(v=>v<=.04045?v/12.92:((v+.055)/1.055)**2.4);return c[0]*.2126+c[1]*.7152+c[2]*.0722;};
  const foreground=hex=>luminance(hex)>.36?'#071018':'#FFFFFF';
  function apply(next,transparency,blur) {
    key=palettes[next]||next==='system'?next:'dark';
    if(transparency!==undefined)glass=Number.isFinite(Number(transparency))?Math.max(0,Math.min(1,Number(transparency))):0;
    if(blur!==undefined)blurEnabled=!!blur;
    const palette=palettes[resolved()]||palettes.dark,style=document.documentElement.style;
    document.documentElement.dataset.theme=resolved();document.documentElement.dataset.light=String(!palette.isDark);
    style.colorScheme=palette.isDark?'dark':'light';
    Object.entries(palette).filter(([n])=>n.includes('-')).forEach(([n,value])=>style.setProperty('--'+n,value));
    const set=(n,v)=>style.setProperty('--'+n,v),opacity=1-glass*.68;
    set('surface-card',alpha(palette['surface-dark'],palette.glassAlpha*opacity));
    set('glass-bg',alpha(palette['surface-dark'],Math.min(.96,palette.glassAlpha+.05)*opacity));
    set('glass-border',palette['border-subtle']);set('border-focus',palette['accent-cyan']);
    ['cyan','emerald','crimson','connected'].forEach(tone=>{set('accent-'+tone+'-dim',alpha(palette['accent-'+tone],.14));set('accent-'+tone+'-glow','0 0 24px '+alpha(palette['accent-'+tone],.32));});
    set('on-success',foreground(palette['accent-emerald']));set('on-error',foreground(palette['accent-crimson']));
    set('glass-blur',blurEnabled?`blur(${14+glass*14}px)`:'none');
    document.documentElement.dataset.glassBlur=String(blurEnabled);
    try{localStorage.setItem('splate-theme',key);localStorage.setItem('splate-glass',glass);localStorage.setItem('splate-blur',String(blurEnabled));}catch(_){}
    draw();window.dispatchEvent(new CustomEvent('splateTheme',{detail:{key,glass,blurEnabled,palette}}));
  }
  function scene(c,w,h,p,time=0,preview=false) {
    if(window.SplateObjects?.has(p.backdrop)){c.clearRect(0,0,w,h);SplateObjects.draw(c,w,h,p,time,preview);return;}
    const u=Math.min(w,h),a=p['accent-cyan'],b=p['ambient-b'],d=p['ambient-c'],phase=time/28000,angle=phase*Math.PI*2;
    c.clearRect(0,0,w,h);c.fillStyle=p['bg-dark'];c.fillRect(0,0,w,h);
    const circle=(x,y,r,color)=>{c.beginPath();c.arc(x,y,r,0,Math.PI*2);c.fillStyle=color;c.fill();};
    const line=(x,y,x2,y2,color,width=1)=>{c.strokeStyle=color;c.lineWidth=width;c.beginPath();c.moveTo(x,y);c.lineTo(x2,y2);c.stroke();};
    const glow=(x,y,r,color,opacity)=>{const g=c.createRadialGradient(x,y,0,x,y,r);g.addColorStop(0,alpha(color,opacity));g.addColorStop(1,alpha(color,0));circle(x,y,r,g);};
    const drift=Math.sin(angle),dx=drift*u*.075,dy=Math.cos(angle)*u*.04;
    glow(w*.86+dx,h*.13+dy,u*1.05,a,p.backdrop==='STARS'?.035:.12);glow(w*.04+dx,h*.86+dy,u*.95,b,p.isDark?.13:.09);
    let seed=4217;const random=()=>{seed=(seed*1664525+1013904223)>>>0;return seed/4294967296;};
    const points=Array.from({length:p.backdrop==='STARS'?90:32},()=>[random(),random()]);
    const waves=(opacity)=>{for(let i=0;i<5;i++){const y=h*(.38+i*.12)+Math.sin(angle+i)*u*.025;c.beginPath();c.moveTo(-w*.15,y);c.bezierCurveTo(w*.22,y-h*.15,w*.6,y+h*.12,w*1.15,y-h*.07);c.lineTo(w*1.15,h*1.15);c.lineTo(-w*.15,h*1.15);c.closePath();const g=c.createLinearGradient(0,y-h*.15,0,h);g.addColorStop(0,alpha(i%2?a:b,opacity));g.addColorStop(1,alpha(p['bg-deep'],.03));c.fillStyle=g;c.fill();}};
    switch(p.backdrop){
      case 'STARS':
        points.forEach(([x,y],i)=>circle(x*w+drift*u*.025,y*h+Math.cos(angle)*u*.025,(i%3+1)*u*.0011,alpha(p['text-primary'],.12+.25*(.5+.5*Math.sin(angle*(1+i%3)+i)))));
        for(let i=0;i<12;i+=2)line(points[i][0]*w,points[i][1]*h,points[i][0]*w+(points[i+1][0]-.5)*u*.2,points[i][1]*h+(points[i+1][1]-.5)*u*.2,alpha(a,.1));break;
      case 'GRID':
        glow(w*.66,h*.61,u*.65,b,.14);
        for(let i=0;i<15;i++)line(w*.66,h*.61,w*(-1.3+i*.25),h,alpha(d,.17),u*.0018);
        for(let j=0;j<11;j++){const t=(j/11+phase)%1,y=h*.61+h*.39*t*t;line(0,y,w,y,alpha(b,.22*Math.sin(t*Math.PI)),u*.0018);}
        for(let i=0;i<24;i++){const height=(.018+random()*.065)*h,x=i*w/24,y=h*.61-height;c.fillStyle=alpha(p['bg-deep'],.8);c.fillRect(x,y,w/24*.78,height);line(x,y,x+w/24*.78,y,alpha(b,.35));if(i%3===0)line(x+w/48,y+height*.2,x+w/48,y+height*.7,alpha(d,.24));}break;
      case 'RINGS':
      case 'SOFT':
        for(let i=0;i<4;i++){const rings=p.backdrop==='RINGS',r=u*(.38+i*.2+drift*.025);c.strokeStyle=alpha(i%2?b:a,rings?.18:.09);c.lineWidth=u*.0017;c.beginPath();c.arc(w*.96,h*.28,r,(rings?angle+i*.9:0)+.6+drift*.1,(rings?angle+i*.9:0)+5.3);c.stroke();if(rings)circle(w*.96+Math.cos(angle+i*2.1)*r,h*.28+Math.sin(angle+i*2.1)*r,u*.005,alpha(a,.4));}
        if(p.backdrop==='SOFT')silk();break;
      case 'AURORA':
        for(let i=0;i<3;i++){const y=h*(.12+i*.12)+Math.sin(angle+i*1.2)*u*.045;c.save();c.translate(Math.cos(angle+i)*u*.045,0);c.beginPath();c.moveTo(-w*.16,y+h*.18);c.bezierCurveTo(w*.18,y-h*.14,w*.42,y+h*.30,w*.68,y+h*.04);c.bezierCurveTo(w*.86,y-h*.12,w*1.02,y+h*.02,w*1.16,y-h*.16);c.lineTo(w*1.16,y+h*.12);c.bezierCurveTo(w*.88,y+h*.22,w*.84,y+h*.06,w*.68,y+h*.25);c.bezierCurveTo(w*.42,y+h*.46,w*.16,y+h*.08,-w*.16,y+h*.38);c.closePath();const g=c.createLinearGradient(0,y-h*.1,0,y+h*.5),tone=[a,d,b][i];g.addColorStop(0,alpha(tone,0));g.addColorStop(.45,alpha(tone,.18));g.addColorStop(1,alpha(tone,0));c.fillStyle=g;c.fill();c.restore();}
        for(let i=0;i<18;i++){const y=h*(.17+i*.018)+Math.sin(angle+i*.1)*u*.04;c.beginPath();c.moveTo(-w*.1,y+h*.13);c.bezierCurveTo(w*.2,y-h*.12,w*.44,y+h*.24,w*.7,y);c.bezierCurveTo(w*.9,y-h*.13,w,y-h*.02,w*1.1,y-h*.15);c.lineWidth=u*.0016;c.strokeStyle=alpha(i%3? a:b,.07);c.stroke();}break;
      case 'SUNSET': {
        const x=w*.72,y=h*.69-u*.17,r=u*.18;glow(x,y,r*2.7,p['accent-secondary'],.18);
        const g=c.createLinearGradient(x,y-r,x,y+r);g.addColorStop(0,alpha('#FFDF94',.6));g.addColorStop(1,alpha(a,.35));circle(x,y,r,g);
        c.save();c.beginPath();c.arc(x,y,r,0,Math.PI*2);c.clip();for(let i=0;i<8;i++){c.fillStyle=p['bg-dark'];c.fillRect(x-r,y+i*r*.13,r*2,r*.025+i*.3);}c.restore();
        for(let j=0;j<2;j++){c.beginPath();const top=h*(.69+j*.025);c.moveTo(0,top);for(let i=0;i<=8;i++)c.lineTo(i*w/8,top-Math.sin(i*1.9+j)*h*.035);c.lineTo(w,h);c.lineTo(0,h);c.fillStyle=alpha(p['bg-deep'],.5+j*.18);c.fill();}
        for(let i=0;i<20;i++){const t=(i+1)/21,yy=h*.735+h*.265*t*t,amplitude=u*(.001+t*t*.007);const gradient=c.createLinearGradient(0,0,w,0);gradient.addColorStop(0,alpha(b,0));gradient.addColorStop(.28,alpha(b,.12));gradient.addColorStop(.54,alpha(a,.04));gradient.addColorStop(.72,alpha(p['accent-secondary'],.48-t*.26));gradient.addColorStop(1,alpha(b,0));c.beginPath();for(let j=0;j<=80;j++){const xx=j/80,yWave=yy+amplitude*(Math.sin(xx*Math.PI*2*(1.4+t)-angle*2+i*.65)+.35*Math.sin(xx*Math.PI*6+angle+i));if(j===0)c.moveTo(xx*w,yWave);else c.lineTo(xx*w,yWave);}c.strokeStyle=gradient;c.lineWidth=u*(.001+t*.0018);c.stroke();}
        for(let i=0;i<3;i++)glow(w*(.12+i*.36)+Math.sin(angle+i)*u*.065,h*(.18+i*.055),u*.4,b,.025);break;}
      case 'OCEAN':
        for(let i=0;i<4;i++){c.beginPath();c.moveTo(w*(.52+i*.15),-h*.08);c.lineTo(w*(.6+i*.15),-h*.08);c.lineTo(w*(.12+i*.26)+Math.sin(angle+i)*u*.03,h*.8);c.lineTo(w*(-.3+i*.26),h*.8);c.closePath();const g=c.createLinearGradient(0,0,0,h*.8);g.addColorStop(0,alpha(a,.12));g.addColorStop(1,alpha(a,0));c.fillStyle=g;c.fill();}waves(.08);
        points.forEach(([x,y],i)=>{const yy=((y-phase)%1+1)%1;c.strokeStyle=alpha(a,Math.sin(yy*Math.PI)*.28);c.lineWidth=1;c.beginPath();c.arc(x*w+Math.sin(angle+i)*u*.025,yy*h,u*(.003+i%4*.0015),0,Math.PI*2);c.stroke();});break;
      case 'SAKURA':
        glow(w*.79,h*.23,u*.4,p['accent-secondary'],.17);circle(w*.79,h*.23,u*.11,alpha(p['accent-secondary'],.13));
        for(let i=0;i<3;i++){c.strokeStyle=alpha(p['accent-secondary'],.2);c.lineWidth=u*.004;c.beginPath();c.moveTo(w*1.05,h*(.55+i*.1));c.bezierCurveTo(w*.9,h*.38,w*.92,h*.2,w*(.65-i*.06),h*(.12+i*.04));c.stroke();}
        [[.68,.14],[.77,.18],[.88,.24],[.70,.28],[.58,.235],[.97,.17],[.31,.73],[.20,.76],[.06,.77]].forEach(([x,y],i)=>{for(let j=0;j<5;j++){const t=j*Math.PI*2/5+i;circle(x*w+Math.cos(t)*u*.01,y*h+Math.sin(t)*u*.01,u*.01,alpha(a,.25));}circle(x*w,y*h,u*.0028,alpha(p['accent-amber'],.5));});
        points.forEach(([x,y],i)=>{const life=(y+phase)%1;c.save();c.translate(x*w+Math.sin(angle+i*.8)*u*.07,life*h);c.rotate(angle+i);c.fillStyle=alpha(i%3?a:p['accent-secondary'],Math.sin(life*Math.PI)*(.25+i%3*.1));c.beginPath();c.moveTo(0,-u*.012);c.bezierCurveTo(u*.020,-u*.010,u*.018,u*.009,0,u*.014);c.bezierCurveTo(-u*.010,u*.006,-u*.014,-u*.005,0,-u*.012);c.fill();c.restore();});break;
      case 'PASTEL':waves(.06);silk();break;
      case 'PONY':case 'BLOOM': {
        const image=images[p.backdrop==='PONY'?'pony':'winx'];
        if(image?.complete&&image.naturalWidth){const maxW=preview?w*.98:w*.55,maxH=preview?h*.96:h*.55,scale=Math.min(maxW/image.naturalWidth,maxH/image.naturalHeight),iw=image.naturalWidth*scale,ih=image.naturalHeight*scale;c.save();c.globalAlpha=preview?.95:.82;c.translate(w-iw*.52,h-ih*.5-(preview?0:70)+Math.sin(angle*2)*4);c.rotate(Math.sin(angle)*.006);c.drawImage(image,-iw*.5,-ih*.5,iw,ih);c.restore();}
        points.forEach(([x,y],i)=>{const xx=x*w,yy=((y-phase*.06+10)%1)*h,r=u*(.002+i%3*.001);const opacity=.13+.07*Math.sin(angle+i);line(xx-r,yy,xx+r,yy,alpha(i%2?a:p['accent-secondary'],opacity));line(xx,yy-r,xx,yy+r,alpha(a,opacity));});
        for(let i=0;i<46;i++){const px=random(),py=random(),speed=random(),size=random(),life=(phase*(2+speed*2)+py)%1,fade=Math.sin(life*Math.PI),shimmer=.55+.45*Math.sin(angle*(6+size*3)+i),x=w*(.04+px*.92)+Math.sin(angle*2+i)*u*.024,y=h*(.98-life*.46),tone=[a,p['accent-secondary'],d][i%3],r=u*(.0028+size*.004);if(i%5===0){glow(x,y,u*.022,tone,fade*shimmer*.19);c.beginPath();c.moveTo(x,y-r*2.4);c.lineTo(x+r*.4,y-r*.4);c.lineTo(x+r*2,y);c.lineTo(x+r*.4,y+r*.4);c.lineTo(x,y+r*2.4);c.lineTo(x-r*.4,y+r*.4);c.lineTo(x-r*2,y);c.lineTo(x-r*.4,y-r*.4);c.closePath();c.fillStyle=alpha(tone,fade*(.25+shimmer*.45));c.fill();}else circle(x,y,r*1.8,alpha(tone,fade*(.25+shimmer*.45)*.55));circle(x,y,r*.45,alpha('#FFFFFF',fade*(.35+shimmer*.5)));}
        const g=c.createLinearGradient(0,0,w,0);g.addColorStop(0,alpha(d,0));g.addColorStop(.4,alpha(d,.22));g.addColorStop(.7,alpha(p['accent-secondary'],.18));g.addColorStop(1,alpha(d,0));const sparks=[];c.beginPath();for(let i=0;i<=60;i++){const t=i/60,x=w*(.1+t*.83),y=h*(p.backdrop==='PONY'?.84:.71)-Math.sin(t*Math.PI+angle*.65)*u*.1+Math.cos(angle+t*5)*u*.018;if(i===0)c.moveTo(x,y);else c.lineTo(x,y);if(i%10===0)sparks.push([x,y,i]);}c.strokeStyle=g;c.lineWidth=u*.002;c.stroke();sparks.forEach(([x,y,i])=>circle(x,y,u*.003,alpha(a,.25+.15*Math.sin(angle*4+i))));break;}
    }
    function silk(){const g=c.createLinearGradient(0,0,w,0);g.addColorStop(0,alpha(p['accent-secondary'],0));g.addColorStop(.5,alpha(p['accent-secondary'],.06));g.addColorStop(1,alpha(p['accent-secondary'],0));c.beginPath();c.moveTo(-w*.2,h*.26+drift*u*.05);c.bezierCurveTo(w*.24,h*.54+drift*u*.05,w*.44,h*.12+drift*u*.05,w*1.2,h*.5+drift*u*.05);c.strokeStyle=g;c.lineWidth=u*.12;c.stroke();c.lineWidth=u*.0015;c.stroke();}
  }
  function draw(time=0){if(!ctx)return;const r=devicePixelRatio||1,w=innerWidth,h=innerHeight;if(canvas.width!==w*r||canvas.height!==h*r){canvas.width=w*r;canvas.height=h*r;}ctx.setTransform(r,0,0,r,0,0);scene(ctx,w,h,palettes[resolved()]||palettes.dark,time);}
  function animate(time){frame=0;if(document.hidden||reduced.matches)return;if(time-last>=33){draw(time);last=time;}frame=requestAnimationFrame(animate);}
  function schedule(){cancelAnimationFrame(frame);frame=0;draw();if(!document.hidden&&!reduced.matches)frame=requestAnimationFrame(animate);}
  window.SplateTheme={apply,palettes,preview:(target,name)=>{const c=target.getContext('2d');scene(c,target.width,target.height,palettes[name==='system'?'dark':name],0,true);},get key(){return key;},get glass(){return glass;}};
  try{apply(localStorage.getItem('splate-theme')||'dark',Number(localStorage.getItem('splate-glass'))||0,localStorage.getItem('splate-blur')!=='false');}catch(_){apply('dark',0,true);}
  document.addEventListener('DOMContentLoaded',()=>{canvas=document.createElement('canvas');canvas.id='themeBackdrop';canvas.setAttribute('aria-hidden','true');document.body.prepend(canvas);ctx=canvas.getContext('2d');schedule();});
  addEventListener('resize',()=>draw());document.addEventListener('visibilitychange',schedule);reduced.addEventListener('change',schedule);
  matchMedia('(prefers-color-scheme: dark)').addEventListener('change',()=>{if(key==='system')apply(key);});
})();
