import {http} from './http';
export const writingApi={
styles:()=>http.get('/writing/styles').then(r=>r.data),
generated:()=>http.get('/writing/generated').then(r=>r.data),
learn:(novelId:number)=>http.post('/writing/learn/'+novelId).then(r=>r.data),
generate:(payload:any)=>http.post('/writing/generate',payload).then(r=>r.data),
publish:(id:number)=>http.post('/writing/generated/'+id+'/publish').then(r=>r.data),
downloadUrl:(id:number)=>http.defaults.baseURL+'/writing/generated/'+id+'/download'
};