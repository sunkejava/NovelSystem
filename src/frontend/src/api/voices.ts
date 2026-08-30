import {http} from './http';

export const voiceApi={
  list:()=>http.get('/voice-profiles/').then(r=>r.data),
  create:(payload:any)=>http.post('/voice-profiles/',payload).then(r=>r.data),
  update:(id:number,payload:any)=>http.put('/voice-profiles/'+id,payload).then(r=>r.data),
  buildPrompt:(id:number)=>http.post('/voice-profiles/'+id+'/prompt').then(r=>r.data),
  remove:(id:number)=>http.delete('/voice-profiles/'+id)
};