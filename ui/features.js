'use strict';
let activeView='live',activeRange='24h',historyRequestAt=0;
function switchView(view){
 activeView=view;document.querySelector('.layout').hidden=view!=='live';
 el('historyPanel').hidden=view!=='history';el('diagnosticsPanel').hidden=view!=='diagnostics';
 for(const [id,v] of [['tabLive','live'],['tabHistory','history'],['tabDiagnostics','diagnostics']])el(id).classList.toggle('selected',view===v);
 if(view==='history'){send('history:'+activeRange);historyRequestAt=Date.now();}
}
el('tabLive').onclick=()=>switchView('live');
el('tabHistory').onclick=()=>send('view:history');
el('tabDiagnostics').onclick=()=>send('view:diagnostics');
el('historyRange').onchange=()=>{activeRange=el('historyRange').value;send('history:'+activeRange);};
el('exportHistory').onclick=()=>send('export');el('runDiagnostics').onclick=()=>send('diagnostics');
el('openReport').onclick=()=>send('report');el('openReports').onclick=()=>send('open-reports');el('openData').onclick=()=>send('open-data');
function drawHistory(data){
 if(data.range!==activeRange)return;
 text('historySummary',data.count?number(data.count)+' muestras · Primera lectura del periodo: '+new Date(data.first).toLocaleString('es-MX'):'Aún no hay muestras en este periodo. El registro empieza al abrir esta versión de Pulse.');
 const series=[['cpu','CPU','#c2b4fc','%'],['ram','RAM','#87d9c6','%'],['gpu','GPU','#f2b39f','%'],['disk','SSD activo','#99c6fa','%'],['battery','Carga de batería','#c5ec9c','%'],['cpuTemp','Temperatura CPU','#efcf87','°C']];
 const fmt=t=>new Date(t).toLocaleString('es-MX',{month:'short',day:'numeric',hour:'2-digit',minute:'2-digit',hour12:false});
 const points=data.points||[];
 const start=points.length?points[0].ts:0,end=points.length?Math.max(start+data.bucketMs,points.at(-1).ts):1;
 el('historyCharts').innerHTML=series.map(([key,label,color,unit])=>{
  const values=points.filter(p=>p[key]!=null);let chartHtml='<div class="history-empty">Sin lecturas disponibles</div>';
  if(values.length){
   const top=unit==='°C'?Math.max(100,...values.map(p=>p[key])):100;let groups=[],g=[],previous=null;
   for(const p of points){if(p[key]==null||(previous!=null&&p.ts-previous>data.bucketMs*1.6)){if(g.length)groups.push(g);g=[];}if(p[key]!=null){g.push({x:((p.ts-start)/(end-start)*300).toFixed(1),y:(98-Math.max(0,Math.min(top,p[key]))/top*96).toFixed(1)});}previous=p.ts;}if(g.length)groups.push(g);
   chartHtml='<svg class="history-plot" viewBox="0 0 300 100" preserveAspectRatio="none" role="img" aria-label="Historial de '+label+'">'+groups.map(g=>g.length===1?'<circle cx="'+g[0].x+'" cy="'+g[0].y+'" fill="'+color+'"/>':'<polyline stroke="'+color+'" points="'+g.map(p=>p.x+','+p.y).join(' ')+'"/>').join('')+'</svg><div class="plot-labels"><span>'+escape(fmt(start))+'</span><span>'+escape(fmt(points.at(-1).ts))+'</span></div>';
  }
  const avg=values.length?values.reduce((a,p)=>a+p[key],0)/values.length:null,max=values.length?Math.max(...values.map(p=>p[key])):null;
  return '<div class="history-card"><div class="label"><span>'+label+'</span><strong>'+number(avg,1)+' '+unit+' promedio</strong></div>'+chartHtml+'<div class="history-caption">Máximo de los puntos mostrados: '+number(max,1)+' '+unit+'. '+(unit==='°C'?'Cada punto conserva el máximo del intervalo.':'Cada punto es el promedio de su intervalo.')+'</div></div>';
 }).join('');
 const daily=data.batteryDaily||[];
 el('batteryTrend').innerHTML=daily.length?'<table class="trend-table"><thead><tr><th>FECHA</th><th>SALUD</th><th>CAPACIDAD</th><th>CICLOS</th></tr></thead><tbody>'+daily.slice(-30).reverse().map(d=>'<tr><td>'+escape(d.day)+'</td><td class="'+(d.health<80?'worn':'good')+'">'+number(d.health,1)+' %</td><td>'+number(d.fullWh,2)+' / '+number(d.designWh,2)+' Wh</td><td>'+number(d.cycles)+'</td></tr>').join('')+'</tbody></table><p class="panel-note">Se muestran hasta 30 días registrados. El archivo local conserva hasta un año.</p>':'<p class="panel-note">La evolución aparecerá al registrar la primera lectura de capacidad.</p>';
}
function renderDiagnostic(r){
 text('diagnosticStatus','Último diagnóstico: '+new Date(r.time).toLocaleString('es-MX')+' · Reporte guardado');
 el('runDiagnostics').disabled=false;text('runDiagnostics','Correr de nuevo');el('openReport').disabled=false;
 const q=r.battery||{};
 const stat=(title,value,sub)=>'<div class="quality-stat"><small>'+escape(title)+'</small><strong>'+escape(value)+'</strong><p>'+escape(sub)+'</p></div>';
 let html='<div class="quality-hero">'+stat('CAPACIDAD CONSERVADA',number(q.healthPercent,1)+' %',q.assessment||'Sin datos')+stat('CAPACIDAD COMPLETA',number(q.fullWh,2)+' Wh','De fábrica: '+number(q.designWh,2)+' Wh')+stat('CICLOS INFORMADOS',number(q.cycles),'Desgaste estimado: '+number(q.wearPercent,1)+' %')+'</div><div class="battery-quality-note">Salud = capacidad de carga completa ÷ capacidad de fábrica. Es una estimación que puede variar según calibración y temperatura. Las etiquetas de Pulse son orientativas: menos de 80 % se marca como desgaste; menos de 60 %, desgaste alto.</div>';
 html+=(r.checks||[]).map(c=>'<section class="check-item"><div class="check-top"><h3>'+escape(c.name)+'</h3><span class="check-status '+(['atención','revisar','parcial'].includes(c.status)?'attention':c.status==='no disponible'?'unknown':'')+'">'+escape(c.status)+'</span></div><p>'+escape(c.detail)+'</p></section>').join('');
 el('diagnosticResults').innerHTML=html;
}
window.chrome.webview.addEventListener('message',event=>{
 const m=event.data;
 if(m.type==='view')switchView(m.view);
 if(m.type==='sample'){
  const q=m.data.batteryQuality;if(q){text('batteryHealth','Salud '+number(q.healthPercent,1)+' % · '+number(q.cycles)+' ciclos');el('batteryHealth').classList.toggle('worn',q.healthPercent!=null&&q.healthPercent<80);}
  if(activeView==='history'&&Date.now()-historyRequestAt>30000){historyRequestAt=Date.now();send('history:'+activeRange);}
 }
 if(m.type==='history')drawHistory(m.data);
 if(m.type==='historyPending')text('historySummary','Iniciando el registro local. La primera muestra aparecerá en unos segundos.');
 if(m.type==='diagnostic')renderDiagnostic(m.data);
 if(m.type==='diagnosticRunning'){el('runDiagnostics').disabled=true;text('runDiagnostics','Revisando…');text('diagnosticStatus','Consultando Windows y generando reportes. Puedes seguir usando el equipo.');}
 if(m.type==='diagnosticError'){el('runDiagnostics').disabled=false;text('runDiagnostics','Reintentar');text('diagnosticStatus',m.message);}
 if(m.type==='storageError'){text('historySummary',m.message);el('historySummary').classList.add('storage-warning');el('toast').hidden=false;text('toast',m.message);}
 if(m.type==='notice'){el('toast').hidden=false;text('toast',m.message);}
 if(m.type==='exported'){text('historySummary','CSV guardado. Se abrió la carpeta de exportaciones.');}
});
