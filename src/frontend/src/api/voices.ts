import {http} from './http';
export const voiceApi={
  list:(params:any={})=>http.get('/voice-profiles/',{params}).then(r=>r.data),
  options:()=>http.get('/voice-profiles/options').then(r=>r.data),
  create:(payload:any)=>http.post('/voice-profiles/',payload).then(r=>r.data),
  batchCreate:(payload:any)=>http.post('/voice-profiles/batch',payload).then(r=>r.data),
  update:(id:number,payload:any)=>http.put('/voice-profiles/'+id,payload).then(r=>r.data),
  buildPrompt:(id:number)=>http.post('/voice-profiles/'+id+'/prompt').then(r=>r.data),
  remove:(id:number)=>http.delete('/voice-profiles/'+id)
};