import {http} from './http';

export const productionApi={
  chapters:(novelId:number|string)=>http.get('/production/novels/'+novelId+'/chapters').then(r=>r.data),
  rebuildChapters:(novelId:number|string)=>http.post('/production/novels/'+novelId+'/chapters/rebuild').then(r=>r.data),
  timeline:(novelId:number|string,params:any={})=>http.get('/production/novels/'+novelId+'/timeline',{params}).then(r=>r.data),
  recalculateTimeline:(novelId:number|string)=>http.post('/production/novels/'+novelId+'/timeline/recalculate').then(r=>r.data),
  updateTimeline:(scriptId:number|string,payload:any)=>http.put('/production/timeline/'+scriptId,payload).then(r=>r.data),
  versions:(scriptId:number|string)=>http.get('/production/timeline/'+scriptId+'/versions').then(r=>r.data),
  generateVersion:(scriptId:number|string)=>http.post('/production/timeline/'+scriptId+'/versions/generate').then(r=>r.data),
  selectVersion:(versionId:number|string)=>http.put('/production/timeline/versions/'+versionId+'/select').then(r=>r.data),
  removeVersion:(versionId:number|string)=>http.delete('/production/timeline/versions/'+versionId),
  versionPlayUrl:(versionId:number|string)=>'/api/production/timeline/versions/'+versionId+'/play',

  syncChapters:(novelId:number|string)=>http.get('/production/novels/'+novelId+'/sync/chapters').then(r=>r.data),
  syncChapter:(novelId:number|string,chapterId:number|string)=>http.get('/production/novels/'+novelId+'/sync/chapter/'+chapterId).then(r=>r.data),

  tracks:(novelId:number|string)=>http.get('/production/novels/'+novelId+'/tracks').then(r=>r.data),
  ensureDefaultTracks:(novelId:number|string)=>http.post('/production/novels/'+novelId+'/tracks/defaults').then(r=>r.data),
  createTrack:(novelId:number|string,payload:any)=>http.post('/production/novels/'+novelId+'/tracks',payload).then(r=>r.data),
  updateTrack:(trackId:number|string,payload:any)=>http.put('/production/tracks/'+trackId,payload).then(r=>r.data),
  removeTrack:(trackId:number|string)=>http.delete('/production/tracks/'+trackId),
  uploadClip:(trackId:number|string,form:FormData)=>http.post('/production/tracks/'+trackId+'/clips/upload',form,{headers:{'Content-Type':'multipart/form-data'}}).then(r=>r.data),
  updateClip:(clipId:number|string,payload:any)=>http.put('/production/tracks/clips/'+clipId,payload).then(r=>r.data),
  removeClip:(clipId:number|string)=>http.delete('/production/tracks/clips/'+clipId),
  clipPlayUrl:(clipId:number|string)=>'/api/production/tracks/clips/'+clipId+'/play',
  mixTracks:(novelId:number|string)=>http.post('/production/novels/'+novelId+'/tracks/mix').then(r=>r.data),
  mixedPlayUrl:(novelId:number|string)=>'/api/production/novels/'+novelId+'/tracks/mixed/play',
  mixedDownloadUrl:(novelId:number|string)=>'/api/production/novels/'+novelId+'/tracks/mixed/download',

  pronunciations:(novelId:number|string)=>http.get('/production/novels/'+novelId+'/pronunciations').then(r=>r.data),
  createPronunciation:(novelId:number|string,payload:any)=>http.post('/production/novels/'+novelId+'/pronunciations',payload).then(r=>r.data),
  updatePronunciation:(id:number|string,payload:any)=>http.put('/production/pronunciations/'+id,payload).then(r=>r.data),
  removePronunciation:(id:number|string)=>http.delete('/production/pronunciations/'+id),
  qa:(novelId:number|string,params:any={})=>http.get('/production/novels/'+novelId+'/qa',{params}).then(r=>r.data),
  runQa:(novelId:number|string)=>http.post('/production/novels/'+novelId+'/qa/run').then(r=>r.data),
  autoFixQa:(novelId:number|string)=>http.post('/production/novels/'+novelId+'/qa/auto-fix').then(r=>r.data),
  resolveQa:(id:number|string,resolved:boolean)=>http.put('/production/qa/'+id+'/resolve',{},{params:{resolved}}).then(r=>r.data)
};
