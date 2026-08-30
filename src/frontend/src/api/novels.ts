import {http} from './http';
export const novelApi={
list:()=>http.get('/novels/').then(r=>r.data),
detail:(id:number|string)=>http.get('/novels/'+id).then(r=>r.data),
upload:(file:File)=>{const form=new FormData();form.append('file',file);return http.post('/novels/upload',form).then(r=>r.data);},
analyze:(id:number|string)=>http.post('/novels/'+id+'/analyze').then(r=>r.data),
generateAudio:(id:number|string)=>http.post('/novels/'+id+'/audio').then(r=>r.data),
updateCharacter:(id:number,payload:any)=>http.put('/characters/'+id,payload).then(r=>r.data)
};