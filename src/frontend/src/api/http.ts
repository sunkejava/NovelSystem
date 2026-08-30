import axios from 'axios';
export const http=axios.create({baseURL:import.meta.env.VITE_API_URL||'http://localhost:5080/api',timeout:120000});
http.interceptors.response.use(response=>response,error=>{console.error('[NovelSystem API]',error);return Promise.reject(error);});