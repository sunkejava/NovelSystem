<script setup lang="ts">
import {computed,onMounted,ref} from 'vue';
import {novelApi} from '../api/novels';import {jobApi} from '../api/jobs';
import PageHeader from '../components/PageHeader.vue';import MetricCard from '../components/MetricCard.vue';import StatusBadge from '../components/StatusBadge.vue';
const novels=ref<any[]>([]),jobs=ref<any[]>([]);
const running=computed(()=>jobs.value.filter(x=>x.status==='Running'||x.status==='Queued').length);
const completed=computed(()=>jobs.value.filter(x=>x.status==='Completed').length);
onMounted(async()=>{[novels.value,jobs.value]=await Promise.all([novelApi.list(),jobApi.list()]);});
</script>
<template><div>
<PageHeader eyebrow="SYSTEM OVERVIEW" title="智能创作控制台" description="在一个工作台中管理小说理解、角色声音、AI 创作与音频生产流水线。"/>
<div class="metric-grid"><MetricCard label="小说资产" :value="novels.length" hint="已入库文本资产"/><MetricCard label="活跃任务" :value="running" hint="解析 / TTS 处理中" accent="var(--neon-violet)"/><MetricCard label="完成任务" :value="completed" hint="可继续加工输出" accent="var(--neon-blue)"/><MetricCard label="本地模型" value="ONLINE" hint="llama.cpp + Qwen3-TTS" accent="var(--neon-green)"/></div>
<div class="dashboard-grid">
<section class="glass-panel content-card"><div class="card-head"><div><span class="eyebrow">PIPELINE</span><h3>最近任务流</h3></div></div><div class="timeline-list"><div v-for="job in jobs.slice(0,6)" :key="job.id" class="timeline-item"><div class="timeline-node"></div><div><b>{{job.type}}</b><small>#{{job.id}} · {{job.createdAt}}</small></div><StatusBadge :status="job.status"/><el-progress :percentage="job.progress" :stroke-width="5"/></div></div></section>
<section class="glass-panel content-card neural-card"><div class="neural-ring r1"></div><div class="neural-ring r2"></div><div class="neural-core">AI</div><h3>本地智能引擎</h3><p>小说理解、写作风格学习、多角色脚本拆解、声音合成全部在你的本地模型链路中完成。</p><div class="engine-tags"><span>LLM</span><span>TTS</span><span>SQLITE</span><span>FFMPEG</span></div></section>
</div></div></template>