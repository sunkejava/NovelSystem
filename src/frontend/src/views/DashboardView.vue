<script setup lang="ts">
import {computed,onMounted,onUnmounted,ref} from 'vue';
import {novelApi} from '../api/novels';
import {jobApi} from '../api/jobs';
import {settingApi} from '../api/settings';
import PageHeader from '../components/PageHeader.vue';
import MetricCard from '../components/MetricCard.vue';
import StatusBadge from '../components/StatusBadge.vue';

const novels=ref<any[]>([]);
const jobs=ref<any[]>([]);
const aiStatus=ref<any>({llm:{online:false,name:'LLM',latencyMs:0},tts:{online:false,name:'Qwen3-TTS',latencyMs:0}});
let timer:number|undefined;

const running=computed(()=>jobs.value.filter(x=>['Running','Queued','Stopping'].includes(x.status)).length);
const completed=computed(()=>jobs.value.filter(x=>x.status==='Completed').length);

async function load(){
  const [n,j,s]=await Promise.all([novelApi.list(),jobApi.list(),settingApi.aiStatus()]);
  novels.value=n;jobs.value=j;aiStatus.value=s;
}

onMounted(async()=>{await load();timer=window.setInterval(async()=>{aiStatus.value=await settingApi.aiStatus();},10000)});
onUnmounted(()=>timer&&clearInterval(timer));
</script>

<template>
<div>
  <PageHeader eyebrow="SYSTEM OVERVIEW" title="智能创作控制台" description="在一个工作台中管理小说理解、角色声音、AI 创作与音频生产流水线。"/>

  <div class="metric-grid">
    <MetricCard label="小说资产" :value="novels.length" hint="已入库文本资产"/>
    <MetricCard label="活跃任务" :value="running" hint="解析 / TTS 处理中" accent="var(--neon-violet)"/>
    <MetricCard label="完成任务" :value="completed" hint="可继续加工输出" accent="var(--neon-blue)"/>
    <MetricCard
      label="LLM 推理引擎"
      :value="aiStatus.llm?.online?'ONLINE':'OFFLINE'"
      :hint="(aiStatus.llm?.name||'LLM')+' · '+(aiStatus.llm?.latencyMs||0)+'ms'"
      :accent="aiStatus.llm?.online?'var(--neon-green)':'var(--danger)'"
    />
  </div>

  <div class="engine-status-grid">
    <div class="engine-status-card glass-panel" :class="{online:aiStatus.llm?.online}">
      <span class="engine-led"></span>
      <div><small>LLAMA.CPP / LLM</small><b>{{aiStatus.llm?.online?'在线':'离线'}}</b><p>{{aiStatus.llm?.name}} · {{aiStatus.llm?.latencyMs}} ms</p></div>
    </div>
    <div class="engine-status-card glass-panel" :class="{online:aiStatus.tts?.online}">
      <span class="engine-led"></span>
      <div><small>QWEN3-TTS</small><b>{{aiStatus.tts?.online?'在线':'离线'}}</b><p>{{aiStatus.tts?.latencyMs}} ms</p></div>
    </div>
  </div>

  <div class="dashboard-grid">
    <section class="glass-panel content-card">
      <div class="card-head"><div><span class="eyebrow">PIPELINE</span><h3>最近任务流</h3></div></div>
      <div class="timeline-list">
        <div v-for="job in jobs.slice(0,6)" :key="job.id" class="timeline-item">
          <div class="timeline-node"></div>
          <div><b>{{job.type}}</b><small>#{{job.id}} · {{job.createdAt}}</small></div>
          <StatusBadge :status="job.status"/>
          <el-progress :percentage="job.progress" :stroke-width="5"/>
        </div>
      </div>
    </section>

    <section class="glass-panel content-card neural-card">
      <div class="neural-ring r1"></div><div class="neural-ring r2"></div><div class="neural-core">AI</div>
      <h3>本地智能引擎</h3>
      <p>小说理解、写作风格学习、多角色脚本拆解、声音合成全部在你的本地模型链路中完成。</p>
      <div class="engine-tags"><span>LLM</span><span>TTS</span><span>SQLITE</span><span>FFMPEG</span></div>
    </section>
  </div>
</div>
</template>