import {http} from './http';

export const jobApi={
  list:()=>http.get('/jobs/').then(r=>r.data),
  stop:(id:number)=>http.post('/jobs/'+id+'/stop').then(r=>r.data),
  continue:(id:number)=>http.post('/jobs/'+id+'/continue').then(r=>r.data),
  retry:(id:number)=>http.post('/jobs/'+id+'/retry').then(r=>r.data),
  remove:(id:number)=>http.delete('/jobs/'+id),
  downloadUrl:(id:number)=>http.defaults.baseURL+'/jobs/'+id+'/download'
};