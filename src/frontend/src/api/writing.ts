import {http} from './http';
export const writingApi={
  styles:(params:any={})=>http.get('/writing/styles',{params}).then(r=>r.data),
  styleOptions:()=>http.get('/writing/styles/options').then(r=>r.data),
  learn:(novelId:number)=>http.post('/writing/learn/'+novelId).then(r=>r.data),
  updateStyle:(id:number,payload:any)=>http.put('/writing/styles/'+id,payload).then(r=>r.data),
  removeStyle:(id:number)=>http.delete('/writing/styles/'+id),
  generated:(params:any={})=>http.get('/writing/generated',{params}).then(r=>r.data),
  generatedDetail:(id:number|string)=>http.get('/writing/generated/'+id).then(r=>r.data),
  generate:(payload:any)=>http.post('/writing/generate',payload).then(r=>r.data),
  publish:(id:number)=>http.post('/writing/generated/'+id+'/publish').then(r=>r.data),
  downloadUrl:(id:number)=>http.defaults.baseURL+'/writing/generated/'+id+'/download'
};