'use strict';
const el=id=>document.getElementById(id);
const send=command=>window.chrome.webview.postMessage(command);
let sample=null,prefs={},lastAt=0;
const history={cpu:[],gpu:[]};
const number=(v,d=0)=>v==null||!Number.isFinite(v)?'—':v.toLocaleString('es-MX',{maximumFractionDigits:d,minimumFractionDigits:d});
const gib=v=>v==null?'—':number(v/1073741824,1);
const rate=v=>v==null?'—':v>=1048576?number(v/1048576,1)+' MB/s':number(v/1024,0)+' KB/s';
const escape=s=>String(s).replace(/[&<>"']/g,c=>({'&':'&amp;','<':'&lt;','>':'&gt;','"':'&quot;',"'":'&#39;'}[c]));
const text=(id,v)=>el(id).textContent=v;
const temperatures=s=>(s.sensors||[]).filter(x=>x.kind==='Temperature'&&x.value!=null&&!/Distance to TjMax|Warning Temperature|Critical Temperature|Threshold/i.test(x.name));
function tempFor(s,fragment){const a=temperatures(s).filter(x=>fragment==='/intelcpu/'?/\/(intelcpu|amdcpu)\//.test(x.id):x.id.includes(fragment));return a.length?Math.max(...a.map(x=>x.value)):null;}
function chart(key,value){const a=history[key];a.push(value);if(a.length>60)a.shift();const pts=a.map((v,i)=>v==null?null:((i+60-a.length)/59*180).toFixed(1)+','+(29-Math.max(0,Math.min(100,v))*.28).toFixed(1));let groups=[],group=[];for(const p of pts){if(p)group.push(p);else if(group.length){groups.push(group);group=[];}}if(group.length)groups.push(group);el(key+'Chart').innerHTML=groups.map(g=>'<polyline points="'+g.join(' ')+'"/>').join('');}
function update(s){
 sample=s;lastAt=Date.now();document.body.classList.remove('stale');text('connection','En vivo');
 text('cpu',number(s.cpu));text('ram',s.memoryTotal?number(100*s.memoryUsed/s.memoryTotal):'—');text('ramSub',gib(s.memoryUsed)+' / '+gib(s.memoryTotal)+' GB');
 const ramPercent=s.memoryTotal?100*s.memoryUsed/s.memoryTotal:0;el('ramSegments').innerHTML=Array.from({length:23},(_,i)=>'<i class="'+(i/23<ramPercent/100?'filled':'')+'"></i>').join('');
 text('cpuSub',s.cpuName.replace(/\(R\)|\(TM\)/g,'').replace('11th Gen ',''));text('ssd',number(s.diskActive));text('diskSub',s.diskTotal?gib(s.diskUsed)+' / '+gib(s.diskTotal)+' GB ocupados':'Espacio no disponible');text('read','↓ '+rate(s.diskRead));text('write','↑ '+rate(s.diskWrite));
 text('machineName',s.machine?[s.machine.manufacturer,s.machine.model].filter(Boolean).join(' '):'Equipo Windows');text('machineSpec',gib(s.memoryTotal)+' GB · '+number(s.machine?.threads)+' hilos');
 text('gpu',number(s.gpu));text('gpuSub',s.gpuName==='GPU'?'Nombre de GPU no disponible':s.gpuName.replace(/\(R\)|\(TM\)/g,''));
 text('battery',number(s.battery));text('batteryTag',s.plugged?'CONECTADA':'EN BATERÍA');el('batteryMeter').style.width=(s.battery??0)+'%';
 text('batterySub',s.plugged?(s.battery>=100?'Carga completa · conectada':'Alimentación conectada'):s.batterySeconds!=null?Math.floor(s.batterySeconds/3600)+' h '+Math.floor(s.batterySeconds%3600/60)+' min estimados':'Autonomía no disponible');
 const ct=tempFor(s,'/intelcpu/'),dt=tempFor(s,'/nvme/'),gt=tempFor(s,'/gpu');
 text('cpuTemp',ct!=null?number(ct)+' °C':'Sin sensor');text('diskTemp',dt!=null?number(dt)+' °C':'Sin sensor');text('gpuTemp',gt!=null?number(gt)+' °C':'Sin sensor');
 const ts=temperatures(s);const max=ts.length?ts.reduce((a,b)=>a.value>b.value?a:b):null;
 text('temp',max?number(max.value):'—');text('tempSub',max?max.name:'Sensores no disponibles');text('tempStatus',max?'Máxima entre '+ts.length+' sensores':s.admin?'El equipo no expone esta lectura':'Activar sensores avanzados ↗');
 text('updated','Actualizado '+new Date(s.time).toLocaleTimeString('es-MX',{hour12:false}));
 el('admin').hidden=s.admin&&s.pawnIoInstalled;text('adminHelp',s.admin&&s.pawnIoInstalled?'Sensores avanzados activados. Los valores disponibles dependen del hardware.':s.pawnIoInstalled?'Algunos sensores necesitan permisos de administrador. Windows te pedirá confirmación.':'Las temperaturas internas necesitan el controlador firmado PawnIO y permisos de administrador. Windows solicitará confirmación para instalarlos.');
 chart('cpu',s.cpu);chart('gpu',s.gpu);if(prefs.expanded)renderDetails(s);
}
function renderDetails(s){
 const row=(a,b)=>'<div class="reading"><span>'+escape(a)+'</span><strong>'+escape(b)+'</strong></div>';
 const group=(title,hw,html)=>'<section class="detail-group"><h3>'+title+'</h3>'+(hw?'<div class="hw">'+escape(hw)+'</div>':'')+html+'</section>';
 let html=group('Procesador',s.cpuName,row('Uso total',number(s.cpu)+' %')+row('Núcleos / hilos',number(s.machine?.cores)+' / '+number(s.machine?.threads)));
 const clocks=s.sensors.filter(x=>x.kind==='Clock'&&/\/(intelcpu|amdcpu)\//.test(x.id)&&x.name.includes('Core')&&x.value!=null);
 if(clocks.length)html+=group('Frecuencia de CPU','',row('Promedio de núcleos',number(clocks.reduce((a,b)=>a+b.value,0)/clocks.length)+' MHz'));
 html+=group('Memoria','RAM compartida también con la GPU integrada',row('En uso',gib(s.memoryUsed)+' GB')+row('Disponible',gib(s.memoryTotal-s.memoryUsed)+' GB'));
 html+=group('Almacenamiento',s.diskName,row('Lectura',rate(s.diskRead))+row('Escritura',rate(s.diskWrite))+row('Espacio libre',gib(s.diskTotal!=null&&s.diskUsed!=null?s.diskTotal-s.diskUsed:null)+' GB'));
 const shared=s.sensors.find(x=>x.name==='D3D Shared Memory Used'&&x.value!=null);
 const sharedTotal=s.sensors.find(x=>x.name==='D3D Shared Memory Total'&&x.value!=null);
 html+=group('Gráficos',s.gpuName==='GPU'?'Nombre no disponible':s.gpuName,row('Motor más ocupado',number(s.gpu)+' %')+(shared?row('RAM compartida en uso',number(shared.value/1024,2)+' GB'):'')+(sharedTotal?row('Límite de memoria compartida',number(sharedTotal.value/1024,1)+' GB'):''));
 const wear=s.sensors.find(x=>x.name==='Degradation Level'&&x.value!=null);
 const capacity=s.sensors.find(x=>x.name==='Fully-Charged Capacity'&&x.value!=null);
 html+=group('Batería','',row('Carga actual',number(s.battery)+' %')+row('Alimentación',s.plugged?'Conectada':'Batería')+(wear?row('Salud estimada',number(Math.max(0,100-wear.value),1)+' %'):'')+(capacity?row('Capacidad de carga completa',number(capacity.value/1000,1)+' Wh'):''));
 const temps=temperatures(s);html+=group('Temperaturas disponibles','',temps.length?temps.map(t=>row(t.name,number(t.value,1)+' °C')).join(''):'<div class="sensor-note">'+(s.admin?'No se detectaron sensores accesibles.':'Activa los sensores avanzados desde ⚙. Algunas lecturas requieren permisos de administrador.')+'</div>');
 if(!s.admin)html+='<p class="muted">Modo estándar. CPU, RAM, actividad, GPU y batería se consultan sin elevación.</p>';
 text('detailContent','');el('detailContent').innerHTML=html;
}
for(const id of ['pin','hide','quit','expand','startup','admin'])el(id).addEventListener('click',()=>send(id));
el('drag').addEventListener('mousedown',e=>{if(e.button===0&&!e.target.closest('button'))send('drag');});
el('settings').onclick=()=>el('options').hidden=!el('options').hidden;
el('closeOptions').onclick=()=>el('options').hidden=true;
document.addEventListener('keydown',e=>{if(e.key==='Escape'){el('options').hidden=true;el('toast').hidden=true;}});
document.querySelectorAll('.card').forEach(card=>card.addEventListener('click',()=>{if(card.dataset.key==='temp'&&sample&&!sample.admin&&temperatures(sample).length===0){el('options').hidden=false;return;}if(!prefs.expanded)send('expand');}));
window.chrome.webview.addEventListener('message',event=>{const m=event.data;if(m.type==='sample')update(m.data);if(m.type==='prefs'){prefs=m;document.body.classList.toggle('expanded',m.expanded);el('pin').classList.toggle('active',m.pinned);el('pin').setAttribute('aria-pressed',String(m.pinned));text('expand',m.expanded?'Compactar ↙':'Ampliar ↗');text('startupState',m.startup?'Activado':'Desactivado');if(sample&&m.expanded)renderDetails(sample);}if(m.type==='error'){el('toast').hidden=false;text('toast',m.message);}});
setInterval(()=>{if(lastAt&&Date.now()-lastAt>8000){document.body.classList.add('stale');text('connection','Sin actualizar');text('updated','Última lectura hace '+Math.floor((Date.now()-lastAt)/1000)+' s');}},2000);
send('ready');
