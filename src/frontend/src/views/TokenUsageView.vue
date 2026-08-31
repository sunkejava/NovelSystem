<script setup lang="ts">
import {onMounted,ref} from 'vue';
import {Search,Refresh,WarningFilled} from '@element-plus/icons-vue';
import {tokenUsageApi} from '../api/tokenUsage';
import {analysisErrorApi} from '../api/analysisErrors';
import PageHeader from '../components/PageHeader.vue';
import ListPager from '../components/ListPager.vue';

const rows=ref<any[]>([]);
const total=ref(0);
const summary=ref<any>({totals:{},byOperation:[],byNovel:[],operations:[]});
const query=ref({page:1,pageSize:20,novelId:undefined as number|undefined,jobId:undefined as number|undefined,operation:'',from:'',to:''});
const errorVisible=ref(false);
const errorRows=ref<any[]>([]);
const errorTotal=ref(0);
const errorQuery=ref({page:1,pageSize:20,novelId:undefined as number|undefined,jobId:undefined as number|undefined,recovered:undefined as boolean|undefined});
const errorDetailVisible=ref(false);
const currentError=ref<any>(null);

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
async function loadErrors(){
  const r=await analysisErrorApi.list(errorQuery.value);
  errorRows.value=r.items;errorTotal.value=r.total;
}
async function openErrors(){errorVisible.value=true;await loadErrors();}
function showErrorDetail(row:any){currentError.value=row;errorDetailVisible.value=true;}
onMounted(load);
</script>

<template><div class="page-fill token-page">
<PageHeader eyebrow="LLM TELEMETRY" title="Token 统计中心" description="统计本地/第三方 LLM 的真实 Token、AI 创作消耗，以及 Qwen3-TTS 文本 Token 估算与性能；同时保留 JSON 异常诊断样本。">
  <el-button class="ghost-button" @click="openErrors"><el-icon><WarningFilled/></el-icon>JSON异常样本</el-button>
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
        <el-table-column label="总 Token" width="120"><template #default="{row}">{{token(row.totalTokens)}}<small v-if="row.estimatedCalls" class="estimated-chip">含估算</small></template></el-table-column>
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
    <el-table-column label="总 Token" width="125"><template #default="{row}"><b class="token-accent">{{token(row.totalTokens)}}</b><small v-if="row.isEstimated" class="estimated-chip">估算</small></template></el-table-column>
    <el-table-column label="缓存" width="95"><template #default="{row}">{{token(row.cachedPromptTokens)}}</template></el-table-column>
    <el-table-column label="耗时" width="105"><template #default="{row}">{{duration(row.elapsedMilliseconds)}}</template></el-table-column>
    <el-table-column label="输出 T/S" width="100"><template #default="{row}">{{Number(row.completionTokensPerSecond||0).toFixed(1)}}</template></el-table-column>
    <el-table-column label="状态" width="80"><template #default="{row}"><span :class="row.success?'token-ok':'danger-text'">{{row.success?'成功':'失败'}}</span></template></el-table-column>
    <el-table-column label="时间" width="180"><template #default="{row}">{{time(row.createdAt)}}</template></el-table-column>
  </el-table>
  </div>
  <ListPager v-model:page="query.page" v-model:page-size="query.pageSize" :total="total" @change="load"/>
</section>

<el-dialog v-model="errorVisible" title="LLM JSON 异常样本" width="92%" class="theme-dialog diagnostic-dialog">
  <div class="list-filter-bar compact-filter">
    <el-input v-model.number="errorQuery.novelId" clearable placeholder="小说 ID"/>
    <el-input v-model.number="errorQuery.jobId" clearable placeholder="任务 ID"/>
    <el-select v-model="errorQuery.recovered" clearable placeholder="全部恢复状态">
      <el-option label="已自动恢复" :value="true"/>
      <el-option label="未恢复" :value="false"/>
    </el-select>
    <el-button class="neon-button" @click="loadErrors">查询</el-button>
  </div>
  <el-table :data="errorRows" height="520" class="cyber-table">
    <el-table-column prop="id" label="ID" width="70"/>
    <el-table-column prop="novelId" label="小说" width="80"/>
    <el-table-column prop="jobId" label="任务" width="80"/>
    <el-table-column label="分块" width="100"><template #default="{row}">{{row.chunkIndex}} / {{row.chunkTotal}}</template></el-table-column>
    <el-table-column prop="retryDepth" label="深度" width="75"/>
    <el-table-column prop="stage" label="异常阶段" width="140"/>
    <el-table-column label="源文本" min-width="260"><template #default="{row}">{{row.sourceText?.slice(0,100)}}...</template></el-table-column>
    <el-table-column label="状态" width="110"><template #default="{row}"><span :class="row.recovered?'token-ok':'danger-text'">{{row.recovered?'已恢复':'未恢复'}}</span></template></el-table-column>
    <el-table-column label="时间" width="180"><template #default="{row}">{{time(row.createdAt)}}</template></el-table-column>
    <el-table-column label="操作" width="90" fixed="right"><template #default="{row}"><el-button text @click="showErrorDetail(row)">详情</el-button></template></el-table-column>
  </el-table>
  <ListPager v-model:page="errorQuery.page" v-model:page-size="errorQuery.pageSize" :total="errorTotal" @change="loadErrors"/>
</el-dialog>

<el-dialog v-model="errorDetailVisible" title="JSON 异常完整详情" width="82%" class="theme-dialog">
  <div v-if="currentError" class="diagnostic-detail">
    <h4>错误源文本</h4><pre>{{currentError.sourceText}}</pre>
    <h4>LLM 原始输出</h4><pre>{{currentError.rawResponse||'未获得模型输出'}}</pre>
    <h4>异常堆栈</h4><pre>{{currentError.error}}</pre>
  </div>
</el-dialog>

</div></template>