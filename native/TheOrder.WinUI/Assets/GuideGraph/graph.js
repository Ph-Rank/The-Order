/* The Order's local guide renderer. API text is assigned only through textContent. */
(() => {
  'use strict';
  const $ = id => document.getElementById(id), NS = 'http://www.w3.org/2000/svg';
  const stage = $('stage'), viewport = $('viewport'), main = document.querySelector('main');
  let payload, revision = 0, view = {x:0,y:0,k:1}, bounds = {width:1,height:1};
  let activeEdges = [], activeNodes = [], selectedEdge, autoFit = true, drag, rendered = false;
  const post = value => window.chrome?.webview?.postMessage(value);
  function routePicker(open = false, focus = false) {
    const select = $('section'), list = $('routeOptions');
    $('routeValue').textContent = select.selectedOptions[0]?.textContent || '全部路线';
    $('routeButton').setAttribute('aria-expanded', String(open)); list.hidden = !open;
    list.replaceChildren();
    for (const option of select.options) {
      const item = document.createElement('button'); item.type = 'button'; item.role = 'option';
      item.textContent = option.textContent; item.dataset.value = option.value;
      item.setAttribute('aria-selected', String(option.value === select.value)); item.tabIndex = option.value === select.value ? 0 : -1;
      item.addEventListener('click', () => { select.value = option.value; render(); routePicker(false); $('routeButton').focus(); }); list.append(item);
    }
    if (open && focus) (list.querySelector('[aria-selected=true]') || list.firstElementChild)?.focus();
  }
  $('routeButton').addEventListener('click', () => routePicker($('routeOptions').hidden, true));
  $('routeButton').addEventListener('keydown', e => { if (e.key === 'ArrowDown' || e.key === 'ArrowUp') { e.preventDefault(); routePicker(true, true); } });
  $('routeOptions').addEventListener('keydown', e => {
    const items = [...$('routeOptions').children], index = items.indexOf(document.activeElement);
    if (e.key === 'Escape') { e.preventDefault(); routePicker(false); $('routeButton').focus(); }
    else if (['ArrowDown','ArrowUp','Home','End'].includes(e.key)) { e.preventDefault(); items[e.key === 'Home' ? 0 : e.key === 'End' ? items.length - 1 : (index + (e.key === 'ArrowDown' ? 1 : -1) + items.length) % items.length]?.focus(); }
    else if (e.key === 'Tab') { e.preventDefault(); routePicker(false); (e.shiftKey ? $('routeButton') : $('hideCollections')).focus(); }
  });
  document.addEventListener('pointerdown', e => { if (!e.target.closest('.route-picker') && !$('routeOptions').hidden) routePicker(false); });
  const svg = (name, attrs = {}, text) => {
    const item = document.createElementNS(NS,name);
    for (const [key,value] of Object.entries(attrs)) item.setAttribute(key,String(value));
    if (text !== undefined) item.textContent = text;
    return item;
  };
  const el = (name, text, className) => {
    const item = document.createElement(name); if (text !== undefined) item.textContent = text;
    if (className) item.className = className; return item;
  };
  const palette = () => document.body.dataset.theme === 'light'
    ? ['#00799f','#7655a0','#347b65','#96702c','#a24f69','#526fa6']
    : ['#55b5db','#a18bc8','#75b59c','#c5a166','#cd8eab','#8fa9da'];
  const css = name => getComputedStyle(document.body).getPropertyValue(name).trim();
  const sectionTitle = key => payload.sections.find(s=>s.key===key)?.title || '路线';
  const edgeColor = edge => edge.kind === 'unlock' ? css('--muted') : palette()[Math.max(0,payload.sections.findIndex(s=>s.key===edge.section)) % palette().length];
  const edgeCaption = edge => edge.kind === 'unlock' ? '通关后解锁' : edge.choices ? `${edge.choices} 个选项` : edge.count ? `${edge.count} 个步骤` : '继续';
  const endingWeight = node => node.kind==='collect'?0:node.kind!=='ending'?2:({heroine:3,true:3,good:3,normal:1,bad:1,after:0,cg:0,collect:0}[node.endingType]??2);
  const nodeHeight = node => node.kind==='save'?48:node.kind==='ending'||node.kind==='collect'?[24,34,38,46][endingWeight(node)]:34;
  const nodeCaption = node => node.label;
  const measure = document.createElement('canvas').getContext('2d');
  measure.font = '13px "Segoe UI","Microsoft YaHei UI",sans-serif';
  const widthOf = text => measure.measureText(text).width;
  const shorten = (text, width, scale=1) => {
    if (widthOf(text)*scale <= width) return text;
    const chars = Array.from(text);let low=0,high=chars.length;
    while(low<high){const mid=Math.ceil((low+high)/2);if(widthOf(chars.slice(0,mid).join('')+'…')*scale<=width)low=mid;else high=mid-1;}
    return chars.slice(0,low).join('')+'…';
  };
  function transform() { viewport.setAttribute('transform',`translate(${view.x} ${view.y}) scale(${view.k})`); }
  function fit(mode='width') {
    const w=stage.clientWidth,h=stage.clientHeight;
    view.k=Math.max(.005,Math.min(1.25,(w-48)/bounds.width,mode==='all'?(h-54)/bounds.height:2.5));
    view.x=(w-bounds.width*view.k)/2;view.y=mode==='all'?(h-bounds.height*view.k)/2:24;transform();
  }
  function zoom(factor,x=stage.clientWidth/2,y=stage.clientHeight/2) {
    const old=view.k;view.k=Math.max(.005,Math.min(2.5,old*factor));
    view.x=x-(x-view.x)*view.k/old;view.y=y-(y-view.y)*view.k/old;autoFit=false;transform();
  }
  function select(title) {
    $('selectionTitle').textContent=title;$('selectionBody').replaceChildren();main.classList.add('inspecting');
  }
  function button(text,click,primary=false) {
    const b=el('button',text,primary?'primary':undefined);b.type='button';b.addEventListener('click',click);return b;
  }
  function highlight(edgeId) {
    for(const item of viewport.querySelectorAll('[data-edge]'))item.classList.toggle('highlight',item.dataset.edge===edgeId);
  }
  function inspectEdge(edge) {
    selectedEdge=edge.id;highlight(edge.id);select(edge.kind==='unlock'?'解锁条件':sectionTitle(edge.section));
    const nodes=payload.graph.nodes,from=nodes.find(n=>n.id===edge.from),to=nodes.find(n=>n.id===edge.to);
    $('selectionBody').append(el('p',`${from?.label||''} → ${to?.label||''}`));
    if(edge.kind==='unlock') {
      $('selectionBody').append(el('p','先完成这一结局，再从对应起点开始。','muted'));
      $('selectionBody').append(button('查看对应路线正文',()=>post({kind:'read',revision,edgeId:edge.id})));return;
    }
    $('selectionBody').append(el('p','正在读取这一段的步骤…','muted'));
    post({kind:'inspect',revision,edgeId:edge.id});
  }
  function inspectNode(node) {
    selectedEdge=undefined;highlight('');select(nodeCaption(node));
    if(node.kind!=='save'&&node.original!==node.label)$('selectionBody').append(el('p',node.original,'original'));
    if(node.requirement)$('selectionBody').append(el('p','解锁条件：先完成 '+node.requirement,'muted'));
    const endingType={heroine:'角色路线结局',true:'真结局',good:'Good End',normal:'普通结局',bad:'BE',after:'路线后续结束',cg:'CG 回收',collect:'回收后结束'}[node.endingType];
    if(endingType)$('selectionBody').append(el('p',endingType,'muted'));
    for(const [label,edges] of [['到达这里',activeEdges.filter(e=>e.to===node.id)],['从这里继续',activeEdges.filter(e=>e.from===node.id)]]) {
      if(!edges.length)continue;$('selectionBody').append(el('p',label,'muted'));
      for(const edge of edges) {
        const other=payload.graph.nodes.find(n=>n.id===(edge.to===node.id?edge.from:edge.to));
        $('selectionBody').append(button(`${sectionTitle(edge.section)} · ${edgeCaption(edge)}\n${other?.label||''}`,()=>inspectEdge(edge)));
      }
    }
  }
  function inspection(message) {
    if(message.edgeId!==selectedEdge)return;
    const body=$('selectionBody');body.replaceChildren(el('p',`${message.from} → ${message.to}`));
    if(message.condition)body.append(el('p',message.condition,'muted'));
    body.append(button('连续阅读  →',()=>post({kind:'read',revision,edgeId:message.edgeId}),true));
    const list=el('ol',undefined,'steps'),kinds={choice:'选择',save:'存档',load:'读档',action:'操作',note:'提示'};
    for(const step of message.steps) {
      const item=el('li');item.append(el('span',`${String(step.index+1).padStart(2,'0')}  ${kinds[step.kind]||'提示'}${step.achievement?' · 成就相关':''}`,'step-kind'));
      item.append(el('span',step.display||step.original));
      if(step.original!==step.display)item.append(el('div',step.original,'original'));
      for(const alternative of step.alternatives)item.append(el('div','其他选项 · '+alternative,'alternative'));
      list.append(item);
    }
    body.append(list);
    if(message.more)body.append(el('p','其余内容请打开正文阅读。','muted'));
    if(!message.steps.length)body.append(el('p','此处为路线接续。','muted'));
  }
  // Orthogonal spans and small corner fillets share the native renderer's geometry.
  function path(points) {
    if(points.length<2)return '';
    let d=`M ${points[0].x} ${points[0].y}`;
    for(let i=1;i+2<points.length;i+=3) {
      const a=points[i],b=points[i+1],c=points[i+2];
      d+=` C ${a.x} ${a.y}, ${b.x} ${b.y}, ${c.x} ${c.y}`;
    }
    return d;
  }
  function interactive(item,action,name) {
    item.setAttribute('role','button');item.setAttribute('tabindex','0');item.setAttribute('aria-label',name);
    item.addEventListener('click',action);item.addEventListener('keydown',e=>{if(e.key==='Enter'||e.key===' '){e.preventDefault();action();}});
  }
  function render() {
    routePicker(false);
    rendered=false;viewport.replaceChildren();$('defs').replaceChildren();selectedEdge=undefined;
    main.classList.remove('inspecting');$('selectionTitle').textContent='路线与步骤';
    $('selectionBody').replaceChildren(el('p','点击存档或结局查看去向；点击连线上的选项数，阅读这一段的步骤。','muted'));
    const filter=$('section').value,ids=new Set();
    const available=$('hideCollections').checked?guidePrimaryEdges(payload.graph.nodes,payload.graph.edges):payload.graph.edges;
    activeEdges=available.filter(e=>!filter||e.section===filter);
    // Preserve upstream context, including saves declared in another route.
    if(filter) {
      const included=new Set(activeEdges.map(e=>e.id));for(const e of activeEdges){ids.add(e.from);ids.add(e.to);}
      for(let i=0;i<payload.graph.nodes.length;i++) {
        let added=false;for(const edge of available)if(ids.has(edge.to)&&!included.has(edge.id)) {
          included.add(edge.id);ids.add(edge.from);activeEdges.push(edge);added=true;
        }
        if(!added)break;
      }
    }
    for(const edge of activeEdges){ids.add(edge.from);ids.add(edge.to);}
    activeNodes=payload.graph.nodes.filter(n=>(!filter&&!$('hideCollections').checked)||ids.has(n.id));
    const g=new dagre.graphlib.Graph({multigraph:true}).setGraph({rankdir:'TB',nodesep:28,ranksep:36,edgesep:12,marginx:24,marginy:24}).setDefaultEdgeLabel(()=>({}));
    for(const node of activeNodes)g.setNode(node.id,{kind:node.kind,label:node.label,endingType:node.endingType,width:node.kind==='save'?Math.max(104,Math.min(360,(widthOf(nodeCaption(node))+16)/.66)):endingWeight(node)===0?Math.max(76,Math.min(160,widthOf(nodeCaption(node))*.82+22)):Math.max(endingWeight(node)===3?116:90,Math.min(230,widthOf(nodeCaption(node))+30)),height:nodeHeight(node)});
    for(const edge of activeEdges)g.setEdge(edge.from,edge.to,{width:Math.max(edge.choices?90:72,widthOf(edgeCaption(edge))+28),height:edge.choices?28:24,labelpos:'c',labeloffset:0},edge.id);
    const routedGraph=guideBuildRoutes(g,activeEdges),routes=new Map(routedGraph.edges.map(e=>[e.id,e]));bounds={width:routedGraph.width,height:routedGraph.height};
    const edgeLayer=svg('g'),nodeLayer=svg('g');viewport.append(edgeLayer,nodeLayer);
    for(const edge of activeEdges) {
      const layout=g.edge({v:edge.from,w:edge.to,name:edge.id}),color=edgeColor(edge);
      const marker=svg('marker',{id:'arrow-'+edge.id,viewBox:'0 0 10 10',refX:9,refY:5,markerWidth:6,markerHeight:6,orient:'auto-start-reverse'});
      marker.append(svg('path',{d:'M 0 1 L 9 5 L 0 9 z',fill:color}));$('defs').append(marker);
      const group=svg('g',{'data-edge':edge.id,class:filter&&edge.section!==filter?'dimmed':''});
      const route=routes.get(edge.id);
      const d=path(route.points),line=svg('path',{d,class:'edge'+(edge.kind==='unlock'?' unlock':''),stroke:color,'marker-end':`url(#arrow-${edge.id})`});group.append(line);
      const hit=svg('path',{d,class:'edge-hit'});hit.addEventListener('click',()=>inspectEdge(edge));group.append(hit);
      edgeLayer.append(group);
      const midpoint=route.label;
      if(edge.kind!=='unlock')group.append(svg('path',{class:'choice-arrow',d:`M ${midpoint.x} ${midpoint.y-(edge.choices?14:12)-1} l -3.2 -7 h 6.4 z`,fill:color,'pointer-events':'none'}));
      const label=svg('g',{class:'edge-label',transform:`translate(${midpoint.x} ${midpoint.y})`});
      if(edge.choices)label.append(svg('rect',{x:-layout.width/2,y:-14,width:layout.width,height:28,rx:2,stroke:color,'data-shape':'decision'}));
      else label.append(svg('rect',{x:-layout.width/2,y:-12,width:layout.width,height:24,rx:6,stroke:color}));
      label.append(svg('text',{'text-anchor':'middle',dy:'0.35em'},edgeCaption(edge)));
      interactive(label,()=>inspectEdge(edge),`${sectionTitle(edge.section)}，${edgeCaption(edge)}，查看步骤`);group.append(label);
    }
    for(const node of activeNodes) {
      const layout=g.node(node.id),out=activeEdges.find(e=>e.from===node.id&&e.kind!=='unlock'),incoming=activeEdges.find(e=>e.to===node.id&&e.kind!=='unlock');
      const color=node.kind==='save'?css('--muted'):node.kind==='entry'||node.kind==='start'?palette()[2]:edgeColor(out||incoming||{section:'',kind:'route'});
      const emphasis=endingWeight(node),group=svg('g',{class:`node emphasis-${emphasis}`,'data-node':node.id,'data-ending-type':node.endingType||'',transform:`translate(${layout.x} ${layout.y})`});
      if(node.kind==='save'){
        group.append(svg('path',{d:`M 0 ${-layout.height/2} L ${layout.width/2} 0 L 0 ${layout.height/2} L ${-layout.width/2} 0 Z`,fill:css('--canvas'),stroke:color,'stroke-linejoin':'round','data-shape':'storage'}));
      }else{
        group.append(svg('rect',{x:-layout.width/2,y:-layout.height/2,width:layout.width,height:layout.height,rx:layout.height/2,fill:css('--canvas'),stroke:color,'data-shape':node.kind==='ending'||node.kind==='collect'?'terminal':'entry'}));
        group.append(svg('rect',{x:-layout.width/2,y:-layout.height/2,width:layout.width,height:layout.height,rx:layout.height/2,fill:color,opacity:[.04,.08,.12,.20][emphasis],'pointer-events':'none'}));
      }
      group.append(svg('text',{'text-anchor':'middle',dy:'.35em'},shorten(nodeCaption(node),node.kind==='save'?layout.width*.66-8:layout.width-(emphasis===0?16:24),[10.5/13,12/13,1,14/13][emphasis])));group.append(svg('title',{},nodeCaption(node)));
      interactive(group,()=>inspectNode(node),nodeCaption(node)+'，查看关联路线');nodeLayer.append(group);
    }
    $('count').textContent=`${activeNodes.length} 个节点 · ${activeEdges.filter(e=>e.kind!=='unlock').length} 段流程`;
    $('notice').hidden=!payload.graph.truncated&&activeNodes.length>0;$('notice').textContent=activeNodes.length===0?'暂无主路线，取消隐藏可查看回收内容。':'流程过大，仅绘制前 512 个节点／768 段连接；正文保留全部内容。';
    autoFit=true;fit('width');rendered=true;
    post({kind:'rendered',revision,nodes:activeNodes.length,edges:activeEdges.length});
  }
  window.chrome?.webview?.addEventListener('message',event=>{
    const message=event.data;
    if(message.kind==='graph') {
      const previousFilter=payload?.guideId===message.guideId?$('section').value:null;
      payload=message;revision=message.revision;document.body.dataset.theme=message.theme;
      if(typeof message.hideCollections==='boolean')$('hideCollections').checked=message.hideCollections;
      $('section').replaceChildren(el('option','全部路线'));$('section').firstChild.value='';
      for(const section of message.sections){const option=el('option',section.title);option.value=section.key;$('section').append(option);}
      if(previousFilter!==null&&(!previousFilter||message.sections.some(s=>s.key===previousFilter)))$('section').value=previousFilter;
      else if(stage.clientWidth<560&&message.sections.some(s=>s.key===message.initialSection))$('section').value=message.initialSection;
      render();
    } else if(message.revision===revision&&message.kind==='inspection')inspection(message);
  });
  $('section').addEventListener('change',render);
  $('hideCollections').addEventListener('change',()=>{render();post({kind:'display',revision,hideCollections:$('hideCollections').checked});});
  $('zoomIn').addEventListener('click',()=>zoom(1.25));$('zoomOut').addEventListener('click',()=>zoom(.8));
  $('fit').addEventListener('click',()=>{autoFit=false;fit('all');});$('width').addEventListener('click',()=>{autoFit=true;fit('width');});
  $('reset').addEventListener('click',()=>{autoFit=false;view={x:(stage.clientWidth-bounds.width)/2,y:24,k:1};transform();});
  $('closeInspector').addEventListener('click',()=>main.classList.remove('inspecting'));
  stage.addEventListener('pointerdown',e=>{
    if(e.button!==0&&e.button!==1)return;if(e.button!==1&&e.target.closest('.node,.edge-label,.edge-hit'))return;
    drag={x:e.clientX,y:e.clientY,vx:view.x,vy:view.y,pointerId:e.pointerId,buttons:e.button===1?4:1};autoFit=false;stage.setPointerCapture(e.pointerId);stage.classList.add('dragging');e.preventDefault();
  });
  const end=()=>{const id=drag?.pointerId;drag=null;stage.classList.remove('dragging');if(id!==undefined&&stage.hasPointerCapture(id))stage.releasePointerCapture(id);};
  stage.addEventListener('pointermove',e=>{if(!drag||e.pointerId!==drag.pointerId)return;if(!(e.buttons&drag.buttons)){end();return;}view.x=drag.vx+e.clientX-drag.x;view.y=drag.vy+e.clientY-drag.y;transform();});
  stage.addEventListener('pointerup',end);stage.addEventListener('pointercancel',end);stage.addEventListener('lostpointercapture',end);
  window.addEventListener('pointerup',end);window.addEventListener('blur',end);document.addEventListener('visibilitychange',()=>{if(document.hidden)end();});
  stage.addEventListener('wheel',e=>{
    e.preventDefault();const rect=stage.getBoundingClientRect(),delta=e.deltaY*(e.deltaMode===1?16:1);
    zoom(Math.pow(1.15,Math.max(-4,Math.min(4,-delta/120))),e.clientX-rect.left,e.clientY-rect.top);
  },{passive:false});
  stage.addEventListener('keydown',e=>{
    if(e.target!==stage)return;let handled=true;
    if(e.key==='+'||e.key==='=')zoom(1.25);else if(e.key==='-')zoom(.8);else if(e.key==='0')fit('all');
    else if(e.key==='ArrowDown')view.y-=70;else if(e.key==='ArrowUp')view.y+=70;else if(e.key==='ArrowLeft')view.x+=70;else if(e.key==='ArrowRight')view.x-=70;else handled=false;
    if(handled){autoFit=false;transform();e.preventDefault();}
  });
  new ResizeObserver(()=>{if(rendered&&autoFit)fit('width');}).observe(stage);
  window.guideGraphEvidence=()=>({rendered,revision,nodes:activeNodes.length,edges:activeEdges.length,filter:$('section').value,hideCollections:$('hideCollections').checked,selectedEdge,bounds,view,stage:{width:stage.clientWidth,height:stage.clientHeight},inspector:main.classList.contains('inspecting'),text:$('selectionBody').textContent,picker:{open:!$('routeOptions').hidden,background:getComputedStyle($('routeOptions')).backgroundColor,color:getComputedStyle($('routeOptions')).color,options:$('routeOptions').children.length},networkFree:!document.querySelector('iframe,img,video')});
  // Only the native Test controller calls these fixed operations; no evaluation.
  window.guideGraphTest=command=>{
    if(command.kind==='inspect'){const edge=activeEdges.find(e=>e.id===command.edgeId);if(edge)inspectEdge(edge);}
    else if(command.kind==='read'){if(activeEdges.some(e=>e.id===command.edgeId))post({kind:'read',revision,edgeId:command.edgeId});}
    else if(command.kind==='filter'){if(payload.sections.some(s=>s.key===command.key)||command.key===''){$('section').value=command.key;render();}}
    else if(command.kind==='fit')fit(command.all?'all':'width');
    else if(command.kind==='picker')routePicker(command.key==='1',true);
    else if(command.kind==='collections'){$('hideCollections').checked=!!command.hidden;render();post({kind:'display',revision,hideCollections:$('hideCollections').checked});}
    return window.guideGraphEvidence();
  };
  post({kind:'ready'});
})();
