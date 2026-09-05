import {http} from './http';

export const productionApi={
  chapters:(novelId:number|string)=>http.get('/production/novels/'+novelId+'/chapters').then(r=>r.data),
  rebuildChapters:(novelId:number|string)=>http.post('/production/novels/'+novelId+'/chapters/rebuild').then(r=>r.data),
  timeline:(novelId:number|string,params:any={})=>http.get('/production/novels/'+novelId+'/timeline',{params}).then(r=>r.data),
  updateTimeline:(scriptId:number|string,payload:any)=>http.put('/production/timeline/'+scriptId,payload).then(r=>r.data),
  pronunciations:(novelId:number|string)=>http.get('/production/novels/'+novelId+'/pronunciations').then(r=>r.data),
  createPronunciation:(novelId:number|string,payload:any)=>http.post('/production/novels/'+novelId+'/pronunciations',payload).then(r=>r.data),
  updatePronunciation:(id:number|string,payload:any)=>http.put('/production/pronunciations/'+id,payload).then(r=>r.data),
  removePronunciation:(id:number|string)=>http.delete('/production/pronunciations/'+id),
  qa:(novelId:number|string,params:any={})=>http.get('/production/novels/'+novelId+'/qa',{params}).then(r=>r.data),
  runQa:(novelId:number|string)=>http.post('/production/novels/'+novelId+'/qa/run').then(r=>r.data),
  resolveQa:(id:number|string,resolved:boolean)=>http.put('/production/qa/'+id+'/resolve',{},{params:{resolved}}).then(r=>r.data)
};
