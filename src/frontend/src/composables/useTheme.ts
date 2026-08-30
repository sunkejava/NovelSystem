import {ref} from 'vue';

export type ThemeMode='dark'|'light';

const mode=ref<ThemeMode>((localStorage.getItem('novelsystem-theme') as ThemeMode)||'dark');
const accent=ref(localStorage.getItem('novelsystem-accent')||'#43e8ff');

function apply(){
  document.documentElement.dataset.theme=mode.value;
  document.documentElement.style.setProperty('--accent',accent.value);
  localStorage.setItem('novelsystem-theme',mode.value);
  localStorage.setItem('novelsystem-accent',accent.value);
}

apply();

export function useTheme(){
  const setMode=(value:ThemeMode)=>{mode.value=value;apply();};
  const setAccent=(value:string)=>{accent.value=value;apply();};
  const toggle=()=>setMode(mode.value==='dark'?'light':'dark');
  return {mode,accent,setMode,setAccent,toggle,apply};
}