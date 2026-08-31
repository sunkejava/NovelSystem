import {http} from './http';
export const analysisErrorApi={
  list:(params:any={})=>http.get('/analysis-errors/',{params}).then(r=>r.data)
};