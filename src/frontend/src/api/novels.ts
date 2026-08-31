import {http} from './http';
export const novelApi={
  list:(params:any={})=>http.get('/novels/',{params}).then(r=>r.data),
  detail:(id:number|string)=>http.get('/novels/'+id).then(r=>r.data),
  scripts:(id:number|string,params:any={})=>http.get('/novels/'+id+'/scripts',{params}).then(r=>r.data),
  upload:(file:File)=>{const form=new FormData();form.append('file',file);return http.post('/novels/upload',form).then(r=>r.data);},
  update:(id:number,payload:{title:string;content:string})=>http.put('/novels/'+id,payload).then(r=>r.data),
  remove:(id:number)=>http.delete('/novels/'+id),
  analyze:(id:number|string)=>http.post('/novels/'+id+'/analyze').then(r=>r.data),
  generateAudio:(id:number|string)=>http.post('/novels/'+id+'/audio').then(r=>r.data),
  updateCharacter:(id:number,payload:any)=>http.put('/characters/'+id,payload).then(r=>r.data),
  updateNarratorVoice:(id:number|string,voiceProfileId:number|null)=>http.put('/novels/'+id+'/narrator-voice',{voiceProfileId}).then(r=>r.data)
};