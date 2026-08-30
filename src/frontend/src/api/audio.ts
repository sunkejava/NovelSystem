import {http} from './http';
export const audioApi={
  list:(novelId:number|string,params:any={})=>http.get('/audio/novels/'+novelId,{params}).then(r=>r.data),
  generateSegment:(scriptLineId:number)=>http.post('/audio/segments/'+scriptLineId+'/generate').then(r=>r.data),
  removeSegment:(scriptLineId:number)=>http.delete('/audio/segments/'+scriptLineId),
  merge:(novelId:number|string)=>http.post('/audio/novels/'+novelId+'/merge').then(r=>r.data),
  segmentPlayUrl:(id:number)=>http.defaults.baseURL+'/audio/segments/'+id+'/play',
  segmentDownloadUrl:(id:number)=>http.defaults.baseURL+'/audio/segments/'+id+'/download',
  novelPlayUrl:(id:number|string)=>http.defaults.baseURL+'/audio/novels/'+id+'/play',
  novelDownloadUrl:(id:number|string)=>http.defaults.baseURL+'/audio/novels/'+id+'/download'
};