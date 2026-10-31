// Shared local/native reading layout. Subtrees own horizontal intervals;
// branches widen the drawing instead of routing through unrelated nodes.
(() => {
  'use strict';
  const secondary=n=>n.kind==='collect'||['ending','reference'].includes(n.kind)&&['after','cg','collect'].includes(n.endingType);
  globalThis.guidePrimaryEdges=(nodes,edges)=>{
    const outgoing=new Set(edges.map(e=>e.from)),keep=new Set(nodes.filter(n=>!secondary(n)&&(!outgoing.has(n.id)||n.kind==='ending')).map(n=>n.id));
    for(let pass=0;pass<nodes.length;pass++){let changed=false;for(const edge of edges)if(keep.has(edge.to)&&!keep.has(edge.from)){keep.add(edge.from);changed=true;}if(!changed)break;}
    return edges.filter(e=>keep.has(e.to));
  };
  const weight=n=>n.kind==='collect'?0:({heroine:3,true:3,good:3,normal:1,bad:1,after:0,cg:0,collect:0}[n.endingType]??2);
  const saveNumber=n=>n.kind==='save'&&/^存档\s*[0-9]{1,4}$/.test(n.label||'')?Number(n.label.match(/[0-9]+/)[0]):Infinity;
  globalThis.guidePlaceForest=(graph,edges)=>{
    const ids=graph.nodes(),children=new Map(ids.map(id=>[id,[]])),incoming=new Map();
    for(const edge of edges){if(incoming.has(edge.to))throw new Error('Joined route requires a named reference terminal');incoming.set(edge.to,edge);children.get(edge.from).push(edge);}
    const roots=ids.filter(id=>!incoming.has(id)),order=[],seen=new Set(),queue=[...roots];
    while(queue.length){const id=queue.pop();if(seen.has(id))throw new Error('Cyclic reading layout');seen.add(id);order.push(id);for(const edge of children.get(id))queue.push(edge.to);}
    if(order.length!==ids.length)throw new Error('Cyclic reading layout');
    const measured=new Map(),gap=32;
    for(const id of [...order].reverse()){
      const node=graph.node(id),links=children.get(id);
      if(node.kind!=='save'&&links.length>1)throw new Error('Non-save continuation requires its own reference root');
      links.sort((a,b)=>{const x=measured.get(a.to),y=measured.get(b.to);return x.firstSave-y.firstSave||y.priority-x.priority||a.id.localeCompare(b.id,undefined,{numeric:true});});
      // Compare occupied contours at the same row, including the outgoing
      // horizontal spans and the intervening choice labels. Deep empty space
      // can then be reused without interleaving branches or adding any bends.
      const merged=[],items=[];
      const merge=(profile,at,offset=0)=>profile.forEach((p,i)=>{const j=i+at,value={left:p.left+offset,right:p.right+offset};if(!merged[j])merged[j]=value;else{merged[j].left=Math.min(merged[j].left,value.left);merged[j].right=Math.max(merged[j].right,value.right);}});
      for(const edge of links){const tree=measured.get(edge.to);let offset=0;if(items.length)for(let i=0;i<Math.min(merged.length,tree.profile.length);i++)offset=Math.max(offset,merged[i].right-tree.profile[i].left+gap);items.push({edge,tree,offset});merge(tree.profile,0,offset);}
      const half=Math.floor(items.length/2),center=items.length===0?0:items.length%2?items[half].offset:(items[half-1].offset+items[half].offset)/2;
      for(const item of items)item.offset-=center;
      const inEdge=incoming.get(id),labelWidth=inEdge?graph.edge({v:inEdge.from,w:id,name:inEdge.id}).width:0;
      const own=Math.max(node.width+16,labelWidth+24),profile=[{left:Math.min(-own/2,...items.map(i=>i.offset-8)),right:Math.max(own/2,...items.map(i=>i.offset+8))}];
      if(items.length){profile[1]={left:Math.min(...items.map(i=>i.offset-graph.edge({v:id,w:i.edge.to,name:i.edge.id}).width/2-12)),right:Math.max(...items.map(i=>i.offset+graph.edge({v:id,w:i.edge.to,name:i.edge.id}).width/2+12))};
        merged.forEach((p,i)=>profile[i+2]={left:p.left-center,right:p.right-center});}
      measured.set(id,{id,profile,left:Math.min(...profile.map(p=>p.left)),right:Math.max(...profile.map(p=>p.right)),items,firstSave:Math.min(saveNumber(node),...items.map(i=>i.tree.firstSave)),priority:Math.max(weight(node),...items.map(i=>i.tree.priority))});
    }
    roots.sort((a,b)=>{const x=measured.get(a),y=measured.get(b);return x.firstSave-y.firstSave||a.localeCompare(b,undefined,{numeric:true});});
    let width=24,height=1;const rootY=24+Math.max(0,...roots.map(id=>graph.node(id).height))/2;
    for(const root of roots){const tree=measured.get(root),tasks=[{tree,x:width-tree.left,y:rootY}];
      while(tasks.length){const at=tasks.pop(),node=graph.node(at.tree.id);node.x=at.x;node.y=at.y;height=Math.max(height,node.y+node.height/2+24);
        for(const item of at.tree.items)tasks.push({tree:item.tree,x:at.x+item.offset,y:at.y+112});}
      width+=tree.right-tree.left+64;
    }
    return {width:Math.max(1,width-40),height};
  };
  globalThis.guideExitPorts=(graph,edges)=>{
    const result={},groups=new Map();for(const edge of edges){const list=groups.get(edge.from)||[];list.push(edge);groups.set(edge.from,list);}
    for(const [id,list] of groups){if(graph.node(id).kind!=='save')continue;
      list.sort((a,b)=>graph.node(a.to).x-graph.node(b.to).x||a.id.localeCompare(b.id,undefined,{numeric:true}));const half=Math.floor(list.length/2);
      list.forEach((edge,i)=>result[edge.id]=list.length===1?'bottom':i<half?'left':list.length%2&&i===half?'bottom':'right');}
    return result;
  };
  globalThis.guideShapeRoute=(_,from,to,port)=>{
    const target={x:to.x,y:to.y-to.height/2},side=from.kind==='save'&&(port==='left'||port==='right');
    const start=side?{x:from.x+(port==='left'?-1:1)*from.width/2,y:from.y}:{x:from.x,y:from.y+from.height/2};
    if(Math.abs(start.x-target.x)<.001)return [start,target];
    if(!side)throw new Error('Vertical continuation lost alignment');
    return [start,{x:target.x,y:start.y},target];
  };
  globalThis.guideCurveControls=points=>{
    const clean=[];for(const p of points)if(!clean.length||Math.hypot(p.x-clean.at(-1).x,p.y-clean.at(-1).y)>.0001)clean.push(p);
    const out=[clean[0]],line=to=>{const a=out.at(-1);if(Math.hypot(to.x-a.x,to.y-a.y)<.0001)return;out.push({x:a.x+(to.x-a.x)/3,y:a.y+(to.y-a.y)/3},{x:a.x+2*(to.x-a.x)/3,y:a.y+2*(to.y-a.y)/3},to);};
    for(let i=1;i+1<clean.length;i++){const a=clean[i-1],b=clean[i],c=clean[i+1],before=Math.hypot(b.x-a.x,b.y-a.y),after=Math.hypot(c.x-b.x,c.y-b.y),r=Math.min(14,before*.45,after*.45),k=.5522847498307936;
      const u={x:(b.x-a.x)/before,y:(b.y-a.y)/before},v={x:(c.x-b.x)/after,y:(c.y-b.y)/after},entry={x:b.x-u.x*r,y:b.y-u.y*r},exit={x:b.x+v.x*r,y:b.y+v.y*r};line(entry);out.push({x:entry.x+u.x*r*k,y:entry.y+u.y*r*k},{x:exit.x-v.x*r*k,y:exit.y-v.y*r*k},exit);}
    line(clean.at(-1));return out;
  };
  globalThis.guideLabelPoint=(points,from,to)=>({x:points.at(-1).x,y:from&&to?(from.y+to.y)/2:(points.at(-2).y+points.at(-1).y)/2});
  globalThis.guidePlaceCompact=(graph,edges)=>{
    if(graph.nodeCount()>128||edges.length>192)return guidePlaceForest(graph,edges);
    dagre.layout(graph);
    const ids=graph.nodes(),children=new Map(ids.map(id=>[id,[]])),incoming=new Map();
    for(const edge of edges){if(incoming.has(edge.to))return guidePlaceForest(graph,edges);incoming.set(edge.to,edge);children.get(edge.from).push(edge);}
    const roots=ids.filter(id=>!incoming.has(id)),order=[],queue=[...roots];
    while(queue.length){const id=queue.pop();order.push(id);if(order.length>ids.length)return guidePlaceForest(graph,edges);for(const edge of children.get(id))queue.push(edge.to);}
    if(order.length!==ids.length)return guidePlaceForest(graph,edges);
    const subtree=id=>{const result=[],todo=[id];while(todo.length){const next=todo.pop();result.push(graph.node(next));for(const edge of children.get(next))todo.push(edge.to);}return result;};
    const shift=(id,dx)=>{for(const node of subtree(id))node.x+=dx;};
    // Keep the compact optimizer's staggered positions. Mirror a branch only
    // when numbered save siblings read backwards; END/collection siblings are
    // free to occupy the useful spaces beside the main continuation.
    for(const id of order){const node=graph.node(id),links=children.get(id),saves=links.filter(e=>Number.isFinite(saveNumber(graph.node(e.to)))).sort((a,b)=>graph.node(a.to).x-graph.node(b.to).x);
      if(saves.length>1&&saveNumber(graph.node(saves[0].to))>saveNumber(graph.node(saves.at(-1).to)))for(const child of subtree(id))child.x=2*node.x-child.x;
      const sorted=[...links].sort((a,b)=>graph.node(a.to).x-graph.node(b.to).x),numbered=sorted.filter(e=>Number.isFinite(saveNumber(graph.node(e.to))));
      if(numbered.some((e,i)=>i&&saveNumber(graph.node(e.to))<saveNumber(graph.node(numbered[i-1].to))))return guidePlaceForest(graph,edges);
      const straight=links.length===1?links[0]:node.kind==='save'&&links.length%2?sorted[Math.floor(links.length/2)]:null;
      if(straight)shift(straight.to,node.x-graph.node(straight.to).x);
    }
    let ports=guideExitPorts(graph,edges);
    const routeFor=edge=>guideShapeRoute(null,graph.node(edge.from),graph.node(edge.to),ports[edge.id]);
    for(const edge of edges){const from=graph.node(edge.from),to=graph.node(edge.to),port=ports[edge.id];if(port==='left'&&to.x>=from.x-from.width/2-2)shift(edge.to,from.x-from.width/2-16-to.x);else if(port==='right'&&to.x<=from.x+from.width/2+2)shift(edge.to,from.x+from.width/2+16-to.x);}
    // Short terminal collection branches stay local when that space is free.
    for(const edge of edges){const from=graph.node(edge.from),to=graph.node(edge.to),port=ports[edge.id];if(from.kind!=='save'||to.kind!=='collect'||children.get(edge.to).length||!['left','right'].includes(port))continue;
      const sign=port==='left'?-1:1,reach=Math.max(from.width/2+16,graph.edge({v:edge.from,w:edge.to,name:edge.id}).width/2+28),wanted=from.x+sign*reach;
      if(Math.abs(to.x-from.x)<=reach+24)continue;
      if(!ids.some(id=>{const n=graph.node(id);return n!==from&&n!==to&&Math.abs(n.y-to.y)<(n.height+to.height)/2+18&&Math.abs(n.x-wanted)<(n.width+to.width)/2+20;}))to.x=wanted;
    }
    const conflicts=()=>{const found=[];ports=guideExitPorts(graph,edges);
      for(const edge of edges){const a=graph.node(edge.from),b=graph.node(edge.to),route=guideShapeRoute(null,a,b,ports[edge.id]);
        for(const id of ids){const n=graph.node(id);if(n===a||n===b)continue;const box={left:n.x-n.width/2-8,right:n.x+n.width/2+8,top:n.y-n.height/2-8,bottom:n.y+n.height/2+8};
          for(let i=1;i<route.length;i++){const p=route[i-1],q=route[i];if(Math.abs(p.x-q.x)<.001?p.x>box.left&&p.x<box.right&&Math.max(p.y,q.y)>box.top&&Math.min(p.y,q.y)<box.bottom:p.y>box.top&&p.y<box.bottom&&Math.max(p.x,q.x)>box.left&&Math.min(p.x,q.x)<box.right)found.push({edge,id,left:Math.min(p.x,q.x),right:Math.max(p.x,q.x)});}}
      }return found;};
    // Keep rank Y coordinates. Resolve a crossing by shifting an independent
    // branch horizontally, preferring a collection-only branch. A continuous
    // chain moves together; moving a terminal off its assigned port is rejected.
    const initialPorts=guideExitPorts(graph,edges),minimum=new Map(edges.map(e=>[e.id,['left','right'].includes(initialPorts[e.id])?1:0]));
    const overlaps=()=>ids.some((id,i)=>ids.slice(i+1).some(other=>{const a=graph.node(id),b=graph.node(other);return Math.abs(a.x-b.x)<(a.width+b.width)/2+8&&Math.abs(a.y-b.y)<(a.height+b.height)/2+8;}));
    const validPorts=()=>{const current=guideExitPorts(graph,edges);for(const edge of edges){const a=graph.node(edge.from),b=graph.node(edge.to),p=current[edge.id];if(['left','right'].includes(p)){if((b.x-a.x)*(p==='left'?-1:1)-a.width/2<minimum.get(edge.id)-.001)return false;}else if(Math.abs(a.x-b.x)>.001)return false;}
      for(const links of children.values()){const numbered=links.filter(e=>Number.isFinite(saveNumber(graph.node(e.to)))).sort((a,b)=>graph.node(a.to).x-graph.node(b.to).x);if(numbered.some((e,i)=>i&&saveNumber(graph.node(e.to))<saveNumber(graph.node(numbered[i-1].to))))return false;}return true;};
    const ancestors=id=>{const result=[];while(id){result.push(id);id=incoming.get(id)?.from;}return result;};
    for(let pass=0;pass<12;pass++){const found=conflicts();if(!found.length)break;let changed=false;
      for(const hit of found){const n=graph.node(hit.id),left=n.x-n.width/2-8,right=n.x+n.width/2+8,candidates=[];
        for(const root of ancestors(hit.edge.to)){const branch=subtree(root);if(branch.includes(n))continue;for(const dx of [left-hit.right-1,right-hit.left+1])candidates.push({root,branch,dx});}
        for(const root of ancestors(hit.id)){const branch=subtree(root);if(branch.includes(graph.node(hit.edge.from))||branch.includes(graph.node(hit.edge.to)))continue;for(const dx of [hit.left-right-1,hit.right-left+1])candidates.push({root,branch,dx});}
        for(const candidate of candidates)candidate.quiet=candidate.branch.every(node=>secondary(node)||!['ending','reference'].includes(node.kind)&&children.get(ids.find(id=>graph.node(id)===node)).length);
        candidates.sort((a,b)=>Number(b.quiet)-Number(a.quiet)||Math.abs(a.dx)-Math.abs(b.dx)||a.branch.length-b.branch.length);
        for(const candidate of candidates){for(const node of candidate.branch)node.x+=candidate.dx;
          if(validPorts()&&!overlaps()&&conflicts().length<found.length){changed=true;break;}
          for(const node of candidate.branch)node.x-=candidate.dx;
        }
        if(changed)break;
      }if(!changed)return guidePlaceForest(graph,edges);
    }
    if(conflicts().length)return guidePlaceForest(graph,edges);
    for(let i=0;i<ids.length;i++)for(let j=i+1;j<ids.length;j++){const a=graph.node(ids[i]),b=graph.node(ids[j]);if(Math.abs(a.x-b.x)<(a.width+b.width)/2+8&&Math.abs(a.y-b.y)<(a.height+b.height)/2+8)return guidePlaceForest(graph,edges);}
    let left=Infinity,top=Infinity,right=-Infinity,bottom=-Infinity;
    for(const id of ids){const n=graph.node(id);left=Math.min(left,n.x-n.width/2-24);top=Math.min(top,n.y-n.height/2-24);right=Math.max(right,n.x+n.width/2+24);bottom=Math.max(bottom,n.y+n.height/2+24);}
    for(const edge of edges){const center=guideLabelPoint(routeFor(edge),graph.node(edge.from),graph.node(edge.to)),label=graph.edge({v:edge.from,w:edge.to,name:edge.id});left=Math.min(left,center.x-label.width/2-24);right=Math.max(right,center.x+label.width/2+24);}
    for(const id of ids){const n=graph.node(id);n.x-=left;n.y-=top;}
    return {width:Math.max(1,right-left),height:Math.max(1,bottom-top)};
  };
  globalThis.guideBuildRoutes=(graph,edges)=>{
    const bounds=graph.nodeCount()?guidePlaceCompact(graph,edges):{width:1,height:1},ports=guideExitPorts(graph,edges);
    return {...bounds,edges:edges.map(edge=>{const from=graph.node(edge.from),to=graph.node(edge.to),layout=graph.edge({v:edge.from,w:edge.to,name:edge.id}),route=guideShapeRoute(null,from,to,ports[edge.id]);return {id:edge.id,points:guideCurveControls(route),label:guideLabelPoint(route,from,to),width:layout.width,height:layout.height};})};
  };
})();
