<script setup lang="ts">
import {useRoute,useRouter} from 'vue-router';
import {DataAnalysis,Document,MagicStick,Setting,Operation,Cpu,Moon,Sunny,TrendCharts} from '@element-plus/icons-vue';
import {useTheme} from '../composables/useTheme';

const route=useRoute();
const router=useRouter();
const {mode,accent,toggle,setAccent}=useTheme();

const menus=[
  {path:'/dashboard',label:'智能总览',icon:DataAnalysis},
  {path:'/novels',label:'小说资产',icon:Document},
  {path:'/jobs',label:'任务中枢',icon:Operation},
  {path:'/styles',label:'写作风格',icon:MagicStick},
  {path:'/writing',label:'AI 创作舱',icon:MagicStick},
  {path:'/tokens',label:'Token统计',icon:TrendCharts},
  {path:'/settings',label:'模型设置',icon:Setting}
];

const accents=['#43e8ff','#7c6cff','#ff4fd8','#21d19f','#ff9f43'];
</script>

<template>
<div class="app-shell">
  <div class="ambient-grid"></div>
  <aside class="sidebar glass-panel">
    <div class="brand">
      <div class="brand-orb"><el-icon><Cpu/></el-icon></div>
      <div><strong>NOVEL<span>AI</span></strong><small>STORY INTELLIGENCE</small></div>
    </div>
    <nav>
      <button v-for="item in menus" :key="item.path" class="nav-item" :class="{active:route.path.startsWith(item.path)}" @click="router.push(item.path)">
        <el-icon><component :is="item.icon"/></el-icon><span>{{item.label}}</span><i></i>
      </button>
    </nav>
    <div class="sidebar-foot">
      <span class="pulse-dot"></span>
      <div><b>LOCAL AI ONLINE</b><small>Private · Offline · Secure</small></div>
    </div>
  </aside>

  <section class="workspace">
    <header class="topbar glass-panel">
      <div><span class="eyebrow">NOVEL INTELLIGENCE SYSTEM</span><h2>本地智能小说工作站</h2></div>
      <div class="topbar-right">
        <div class="accent-dots">
          <button v-for="color in accents" :key="color" :class="{active:accent===color}" :style="{background:color}" @click="setAccent(color)"></button>
        </div>
        <el-button circle class="theme-toggle" @click="toggle">
          <el-icon><Sunny v-if="mode==='dark'"/><Moon v-else/></el-icon>
        </el-button>
        <div class="top-status"><span>LLAMA.CPP</span><span>QWEN3-TTS</span><span>FFMPEG</span></div>
      </div>
    </header>
    <main class="page-container"><router-view/></main>
  </section>
</div>
</template>