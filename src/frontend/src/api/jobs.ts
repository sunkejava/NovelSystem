import {http} from './http';
export const jobApi={list:()=>http.get('/jobs/').then(r=>r.data),downloadUrl:(id:number)=>http.defaults.baseURL+'/jobs/'+id+'/download'};