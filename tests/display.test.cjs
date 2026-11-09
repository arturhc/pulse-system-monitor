const {test}=require('node:test');
const assert=require('node:assert/strict');
const vm=require('node:vm');
const fs=require('node:fs');
const path=require('node:path');

function screen(){
 const elements=new Map();
 const element=id=>{if(!elements.has(id))elements.set(id,{textContent:'',innerHTML:'',hidden:false,style:{},classList:{toggle(){},add(){},remove(){}},addEventListener(){},setAttribute(){}});return elements.get(id);};
 let receive,tick;
 const context={console,Date,document:{getElementById:element,body:element('body'),querySelectorAll:()=>[],addEventListener(){}},window:{chrome:{webview:{postMessage(){},addEventListener:(_,handler)=>receive=handler}}},setInterval:fn=>tick=fn};
 vm.runInNewContext(fs.readFileSync(path.join(__dirname,'../ui/app.js'),'utf8'),context);
 return {element,send:data=>receive({data}),tick:()=>tick(),context};
}
function fixture(overrides={}){return {time:new Date().toISOString(),cpu:24,memoryUsed:8*1073741824,memoryTotal:16*1073741824,diskActive:5,diskRead:1048576,diskWrite:0,diskUsed:100*1073741824,diskTotal:512*1073741824,gpu:12,battery:63,plugged:true,batterySeconds:null,admin:false,cpuName:'Intel CPU',gpuName:'GPU',diskName:'SSD',sensors:[],...overrides};}

test('missing sensors and unavailable utilization never appear as zero',()=>{
 const s=screen();s.send({type:'sample',data:fixture({cpu:null,gpu:null,diskActive:null})});
 for(const id of ['cpu','gpu','ssd','temp'])assert.equal(s.element(id).textContent,'—');
 assert.equal(s.element('cpuTemp').textContent,'Sin sensor');
});
test('memory percentage, transfer units and battery state are rendered consistently',()=>{
 const s=screen();s.send({type:'sample',data:fixture()});
 assert.equal(s.element('ram').textContent,'50');assert.equal(s.element('read').textContent,'↓ 1.0 MB/s');
 assert.equal(s.element('batteryTag').textContent,'CONECTADA');assert.equal(s.element('batteryMeter').style.width,'63%');
});
test('real sensor values are selected by hardware and escaped in details',()=>{
 const s=screen();s.send({type:'prefs',expanded:true});s.send({type:'sample',data:fixture({cpuName:'<unsafe>',sensors:[{id:'/intelcpu/0/temperature/0',name:'CPU <Package>',kind:'Temperature',value:61},{id:'/nvme/0/temperature/0',name:'SSD',kind:'Temperature',value:43}]})});
 assert.equal(s.element('cpuTemp').textContent,'61 °C');assert.equal(s.element('diskTemp').textContent,'43 °C');assert.equal(s.element('temp').textContent,'61');
 assert.ok(s.element('detailContent').innerHTML.includes('&lt;unsafe&gt;'));assert.ok(!s.element('detailContent').innerHTML.includes('<unsafe>'));
});
test('stale samples are labelled instead of pretending to be live',()=>{
 const s=screen();s.send({type:'sample',data:fixture()});vm.runInNewContext('lastAt=Date.now()-12000',s.context);s.tick();assert.equal(s.element('connection').textContent,'Sin actualizar');
});
test('NVMe warning limits and CPU distance-to-limit are not measured temperatures',()=>{
 const s=screen();s.send({type:'sample',data:fixture({sensors:[{id:'/nvme/0/temperature/0',name:'Composite Temperature',kind:'Temperature',value:54},{id:'/nvme/0/temperature/11',name:'Critical Temperature',kind:'Temperature',value:83},{id:'/intelcpu/0/temperature/7',name:'CPU Core #1 Distance to TjMax',kind:'Temperature',value:90}]})});
 assert.equal(s.element('diskTemp').textContent,'54 °C');assert.equal(s.element('temp').textContent,'54');assert.equal(s.element('cpuTemp').textContent,'Sin sensor');
});
test('machine identity and core count come from the sample rather than one laptop',()=>{
 const s=screen();s.send({type:'prefs',expanded:true});s.send({type:'sample',data:fixture({machine:{manufacturer:'Example',model:'Workstation',cores:8,threads:16}})});
 assert.equal(s.element('machineName').textContent,'Example Workstation');assert.ok(s.element('machineSpec').textContent.includes('16 hilos'));assert.ok(s.element('detailContent').innerHTML.includes('8 / 16'));
});
