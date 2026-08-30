<script setup lang="ts">
import {onMounted,ref} from 'vue';
import {Search,Refresh} from '@element-plus/icons-vue';
import {tokenUsageApi} from '../api/tokenUsage';
import PageHeader from '../components/PageHeader.vue';
import ListPager from '../components/ListPager.vue';

const rows=ref<any[]>([]);
const total=ref(0);
const summary=ref<any>({totals:{},byOperation:[],byNovel:[],operations:[]});
const query=ref({page:1,pageSize:20,novelId:undefined as number|undefined,jobId:undefined as number|undefined,operation:'',from:'',to:''});

function params(){
  return {
    page:query.value.page,
    pageSize:query.value.pageSize,
    novelId:query.value.novelId||undefined,
    jobId:query.value.jobId||undefined,
    operation:query.value.operation||undefined,
    from:query.value.from||undefined,
    to:query.value.to||undefined
  };
}
async function load(){
  const [list,s]=await Promise.all([tokenUsageApi.list(params()),tokenUsageApi.summary(params())]);
  rows.value=list.items;total.value=list.total;summary.value=s;
}
function search(){query.value.page=1;load();}
function reset(){query.value={page:1,pageSize:20,novelId:undefined,jobId:undefined,operation:'',from:'',to:''};load();}
function token(v:number){return Number(v||0).toLocaleString();}
function duration(ms:number){if(!ms)return '0 秒';const s=Math.round(ms/1000);if(s<60)return s+' 秒';const m=Math.floor(s/60);const r=s%60;return m+' 分 '+r+' 秒';}
function time(v:string){return v?new Date(v).toLocaleString():'—';}
onMounted(load);
</script>

<template><div class="page-fill token-page">
<PageHeader eyebrow="LLM TELEMETRY" title="Token 统计中心" description="统计本地 llama.cpp 在人物脚本解析、写法学习、AI 创作等操作中的 Token 消耗与性能。">
  <el-button class="ghost-button" @click="load"><el-icon><Refresh/></el-icon>刷新</el-button>
</PageHeader>

<div class="token-metric-grid">
  <div class="mini-stat"><span>INPUT TOKENS</span><b>{{token(summary.totals?.promptTokens)}}</b></div>
  <div class="mini-stat"><span>OUTPUT TOKENS</span><b>{{token(summary.totals?.completionTokens)}}</b></div>
  <div class="mini-stat"><span>TOTAL TOKENS</span><b>{{token(summary.totals?.totalTokens)}}</b></div>
  <div class="mini-stat"><span>CALLS</span><b>{{token(summary.totals?.calls)}}</b></div>
  <div class="mini-stat"><span>AVG LATENCY</span><b>{{duration(summary.totals?.averageElapsedMilliseconds)}}</b></div>
  <div class="mini-stat"><span>AVG OUTPUT T/S</span><b>{{Number(summary.totals?.averageCompletionTokensPerSecond||0).toFixed(1)}}</b></div>
</div>

<section class="glass-panel content-card token-summary-card">
  <div class="list-filter-bar">
    <el-input v-model.number="query.novelId" clearable placeholder="小说 ID"/>
    <el-input v-model.number="query.jobId" clearable placeholder="任务 ID"/>
    <el-select v-model="query.operation" clearable placeholder="全部操作"><el-option v-for="op in summary.operations||[]" :key="op" :label="op" :value="op"/></el-select>
    <el-button class="neon-button" @click="search"><el-icon><Search/></el-icon>查询</el-button>
    <el-button class="ghost-button" @click="reset">重置</el-button>
  </div>

  <div class="token-summary-grid">
    <div>
      <div class="card-head"><div><span class="eyebrow">BY NOVEL</span><h3>按小说汇总</h3></div></div>
      <el-table :data="summary.byNovel||[]" class="cyber-table" height="320">
        <el-table-column prop="novelTitle" label="小说" min-width="180"/>
        <el-table-column prop="calls" label="调用" width="80"/>
        <el-table-column label="输入" width="110"><template #default="{row}">{{token(row.promptTokens)}}</template></el-table-column>
        <el-table-column label="输出" width="110"><template #default="{row}">{{token(row.completionTokens)}}</template></el-table-column>
        <el-table-column label="总 Token" width="120"><template #default="{row}">{{token(row.totalTokens)}}</template></el-table-column>
      </el-table>
    </div>
    <div>
      <div class="card-head"><div><span class="eyebrow">BY OPERATION</span><h3>按操作汇总</h3></div></div>
      <el-table :data="summary.byOperation||[]" class="cyber-table" height="320">
        <el-table-column prop="operation" label="操作" min-width="180"/>
        <el-table-column prop="calls" label="调用" width="80"/>
        <el-table-column label="总 Token" width="120"><template #default="{row}">{{token(row.totalTokens)}}</template></el-table-column>
        <el-table-column label="耗时" width="120"><template #default="{row}">{{duration(row.elapsedMilliseconds)}}</template></el-table-column>
        <el-table-column label="输出 T/S" width="100"><template #default="{row}">{{Number(row.averageCompletionTokensPerSecond||0).toFixed(1)}}</template></el-table-column>
      </el-table>
    </div>
  </div>
</section>

<section class="glass-panel content-card table-page-card token-detail-card">
  <div class="card-head"><div><span class="eyebrow">CALL DETAILS</span><h3>单次调用明细</h3></div><span>{{total}} RECORDS</span></div>
  <div class="table-flex-region">
  <el-table :data="rows" class="cyber-table" height="100%">
    <el-table-column prop="id" label="ID" width="70"/>
    <el-table-column prop="novelTitle" label="小说" min-width="150"/>
    <el-table-column prop="jobId" label="任务" width="85"/>
    <el-table-column prop="operation" label="操作" min-width="170"/>
    <el-table-column label="分块" width="90"><template #default="{row}">{{row.chunkIndex||'—'}} / {{row.chunkTotal||'—'}}</template></el-table-column>
    <el-table-column prop="model" label="模型" min-width="140"/>
    <el-table-column label="输入 Token" width="110"><template #default="{row}">{{token(row.promptTokens)}}</template></el-table-column>
    <el-table-column label="输出 Token" width="110"><template #default="{row}">{{token(row.completionTokens)}}</template></el-table-column>
    <el-table-column label="总 Token" width="110"><template #default="{row}"><b class="token-accent">{{token(row.totalTokens)}}</b></template></el-table-column>
    <el-table-column label="缓存" width="95"><template #default="{row}">{{token(row.cachedPromptTokens)}}</template></el-table-column>
    <el-table-column label="耗时" width="105"><template #default="{row}">{{duration(row.elapsedMilliseconds)}}</template></el-table-column>
    <el-table-column label="输出 T/S" width="100"><template #default="{row}">{{Number(row.completionTokensPerSecond||0).toFixed(1)}}</template></el-table-column>
    <el-table-column label="状态" width="80"><template #default="{row}"><span :class="row.success?'token-ok':'danger-text'">{{row.success?'成功':'失败'}}</span></template></el-table-column>
    <el-table-column label="时间" width="180"><template #default="{row}">{{time(row.createdAt)}}</template></el-table-column>
  </el-table>
  </div>
  <ListPager v-model:page="query.page" v-model:page-size="query.pageSize" :total="total" @change="load"/>
</section>
</div></template>