import {http} from './http';

export const tokenUsageApi={
  list:(params:any={})=>http.get('/token-usage/',{params}).then(r=>r.data),
  summary:(params:any={})=>http.get('/token-usage/summary',{params}).then(r=>r.data)
};