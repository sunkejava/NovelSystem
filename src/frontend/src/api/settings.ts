import {http} from './http';

export const settingApi={
  get:()=>http.get('/settings/').then(r=>r.data),
  save:(payload:Record<string,string>)=>http.put('/settings/',payload),
  voices:()=>http.get('/settings/voices').then(r=>r.data),
  testAi:()=>http.post('/settings/test-ai').then(r=>r.data),
  testTts:()=>http.post('/settings/test-tts').then(r=>r.data),
  aiStatus:()=>http.get('/settings/ai-status').then(r=>r.data)
};