<script setup lang="ts">
import {onMounted,ref} from 'vue';
import {Search,Refresh} from '@element-plus/icons-vue';
import {tokenUsageApi} from '../api/tokenUsage';
import {analysisErrorApi} from '../api/analysisErrors';
import PageHeader from '../components/PageHeader.vue';
import ListPager from '../components/ListPager.vue';

const activeTab=ref('summary');
const rows=ref<any[]>([]);
const total=ref(0);
const summary=ref<any>({totals:{},byOperation:[],byNovel:[],operations:[]});
const query=ref({page:1,pageSize:20,novelId:undefined as number|undefined,jobId:undefined as number|undefined,operation:'',from:'',to:''});
const errorRows=ref<any[]>([]);
const errorTotal=ref(0);
const errorQuery=ref({page:1,pageSize:20,novelId:undefined as number|undefined,jobId:undefined as number|undefined,recovered:undefined as boolean|undefined});
const errorDetailVisible=ref(false);
const currentError=ref<any>(null);

function commonParams(){
  return {
    novelId:query.value.novelId||undefined,
    jobId:query.value.jobId||undefined,
    operation:query.value.operation||undefined,
    from:query.value.from||undefined,
    to:query.value.to||undefined
  };
}
async function loadSummary(){summary.value=await tokenUsageApi.summary(commonParams());}
async function loadDetails(){
  const list=await tokenUsageApi.list({...commonParams(),page:query.value.page,pageSize:query.value.pageSize});
  rows.value=list.items;total.value=list.total;
}
async function loadErrors(){
  const r=await analysisErrorApi.list(errorQuery.value);
  errorRows.value=r.items;errorTotal.value=r.total;
}
async function loadCurrent(){
  if(activeTab.value==='summary')await loadSummary();
  else if(activeTab.value==='details')await Promise.all([loadDetails(),loadSummary()]);
  else await loadErrors();
}
async function tabChanged(name:string|number){
  activeTab.value=String(name);
  await loadCurrent();
}
async function search(){
  query.value.page=1;
  errorQuery.value.page=1;
  errorQuery.value.novelId=query.value.novelId;
  errorQuery.value.jobId=query.value.jobId;
  await loadCurrent();
}
async function reset(){
  query.value={page:1,pageSize:20,novelId:undefined,jobId:undefined,operation:'',from:'',to:''};
  errorQuery.value={page:1,pageSize:20,novelId:undefined,jobId:undefined,recovered:undefined};
  await loadCurrent();
}
function token(v:number){return Number(v||0).toLocaleString();}
function duration(ms:number){if(!ms)return '0 秒';const s=Math.round(ms/1000);if(s<60)return s+' 秒';const m=Math.floor(s/60);return m+' 分 '+s%60+' 秒';}
function time(v:string){return v?new Date(v).toLocaleString():'—';}
function showErrorDetail(row:any){currentError.value=row;errorDetailVisible.value=true;}
onMounted(loadSummary);
</script>

<template>
<div class="page-fill token-page">
  <PageHeader eyebrow="LLM TELEMETRY" title="Token 统计中心" description="汇总、调用明细和 JSON 异常诊断分 Tab 展示，避免同屏内容过多导致表格显示不全。">
    <el-button class="ghost-button" @click="loadCurrent"><el-icon><Refresh/></el-icon>刷新</el-button>
  </PageHeader>

  <section class="glass-panel content-card token-tabs-shell">
    <el-tabs v-model="activeTab" class="token-tabs" @tab-change="tabChanged">
      <el-tab-pane label="汇总分析" name="summary">
        <div class="token-tab-fill token-summary-tab">
          <div class="list-filter-bar">
            <el-input v-model.number="query.novelId" clearable placeholder="小说 ID"/>
            <el-input v-model.number="query.jobId" clearable placeholder="任务 ID"/>
            <el-select v-model="query.operation" clearable placeholder="全部操作"><el-option v-for="op in summary.operations||[]" :key="op" :label="op" :value="op"/></el-select>
            <el-button class="neon-button" @click="search"><el-icon><Search/></el-icon>查询</el-button>
            <el-button class="ghost-button" @click="reset">重置</el-button>
          </div>

          <div class="token-metric-grid token-metric-grid-tab">
            <div class="mini-stat"><span>输入 Token</span><b>{{token(summary.totals?.promptTokens)}}</b></div>
            <div class="mini-stat"><span>输出 Token</span><b>{{token(summary.totals?.completionTokens)}}</b></div>
            <div class="mini-stat"><span>总 Token</span><b>{{token(summary.totals?.totalTokens)}}</b></div>
            <div class="mini-stat"><span>调用次数</span><b>{{token(summary.totals?.calls)}}</b></div>
            <div class="mini-stat"><span>平均耗时</span><b>{{duration(summary.totals?.averageElapsedMilliseconds)}}</b></div>
            <div class="mini-stat"><span>平均输出 T/S</span><b>{{Number(summary.totals?.averageCompletionTokensPerSecond||0).toFixed(1)}}</b></div>
          </div>

          <div class="token-summary-grid token-summary-grid-fill">
            <div class="token-summary-pane">
              <div class="card-head"><div><span class="eyebrow">按小说</span><h3>小说 Token 汇总</h3></div></div>
              <div class="table-flex-region">
                <el-table :data="summary.byNovel||[]" class="cyber-table" height="100%">
                  <el-table-column prop="novelTitle" label="小说" min-width="180"/>
                  <el-table-column prop="calls" label="调用" width="80"/>
                  <el-table-column label="输入" width="120"><template #default="{row}">{{token(row.promptTokens)}}</template></el-table-column>
                  <el-table-column label="输出" width="120"><template #default="{row}">{{token(row.completionTokens)}}</template></el-table-column>
                  <el-table-column label="总 Token" width="130"><template #default="{row}">{{token(row.totalTokens)}}</template></el-table-column>
                </el-table>
              </div>
            </div>
            <div class="token-summary-pane">
              <div class="card-head"><div><span class="eyebrow">按操作</span><h3>操作 Token 汇总</h3></div></div>
              <div class="table-flex-region">
                <el-table :data="summary.byOperation||[]" class="cyber-table" height="100%">
                  <el-table-column prop="operation" label="操作" min-width="190"/>
                  <el-table-column prop="calls" label="调用" width="80"/>
                  <el-table-column label="总 Token" width="135"><template #default="{row}">{{token(row.totalTokens)}}<small v-if="row.estimatedCalls" class="estimated-chip">含估算</small></template></el-table-column>
                  <el-table-column label="耗时" width="125"><template #default="{row}">{{duration(row.elapsedMilliseconds)}}</template></el-table-column>
                  <el-table-column label="输出 T/S" width="105"><template #default="{row}">{{Number(row.averageCompletionTokensPerSecond||0).toFixed(1)}}</template></el-table-column>
                </el-table>
              </div>
            </div>
          </div>
        </div>
      </el-tab-pane>

      <el-tab-pane label="调用明细" name="details">
        <div class="token-tab-fill">
          <div class="list-filter-bar">
            <el-input v-model.number="query.novelId" clearable placeholder="小说 ID"/>
            <el-input v-model.number="query.jobId" clearable placeholder="任务 ID"/>
            <el-select v-model="query.operation" clearable placeholder="全部操作"><el-option v-for="op in summary.operations||[]" :key="op" :label="op" :value="op"/></el-select>
            <el-button class="neon-button" @click="search"><el-icon><Search/></el-icon>查询</el-button>
            <el-button class="ghost-button" @click="reset">重置</el-button>
          </div>
          <div class="table-flex-region">
            <el-table :data="rows" class="cyber-table" height="100%">
              <el-table-column prop="id" label="ID" width="70"/>
              <el-table-column prop="novelTitle" label="小说" min-width="150"/>
              <el-table-column prop="jobId" label="任务" width="85"/>
              <el-table-column prop="operation" label="操作" min-width="190"/>
              <el-table-column label="分块" width="90"><template #default="{row}">{{row.chunkIndex||'—'}} / {{row.chunkTotal||'—'}}</template></el-table-column>
              <el-table-column prop="model" label="模型" min-width="150"/>
              <el-table-column label="输入 Token" width="115"><template #default="{row}">{{token(row.promptTokens)}}</template></el-table-column>
              <el-table-column label="输出 Token" width="115"><template #default="{row}">{{token(row.completionTokens)}}</template></el-table-column>
              <el-table-column label="总 Token" width="130"><template #default="{row}"><b class="token-accent">{{token(row.totalTokens)}}</b><small v-if="row.isEstimated" class="estimated-chip">估算</small></template></el-table-column>
              <el-table-column label="缓存" width="95"><template #default="{row}">{{token(row.cachedPromptTokens)}}</template></el-table-column>
              <el-table-column label="耗时" width="110"><template #default="{row}">{{duration(row.elapsedMilliseconds)}}</template></el-table-column>
              <el-table-column label="输出 T/S" width="105"><template #default="{row}">{{Number(row.completionTokensPerSecond||0).toFixed(1)}}</template></el-table-column>
              <el-table-column label="状态" width="80"><template #default="{row}"><span :class="row.success?'token-ok':'danger-text'">{{row.success?'成功':'失败'}}</span></template></el-table-column>
              <el-table-column label="时间" width="180"><template #default="{row}">{{time(row.createdAt)}}</template></el-table-column>
            </el-table>
          </div>
          <ListPager v-model:page="query.page" v-model:page-size="query.pageSize" :total="total" @change="loadDetails"/>
        </div>
      </el-tab-pane>

      <el-tab-pane label="JSON 异常" name="errors">
        <div class="token-tab-fill">
          <div class="list-filter-bar">
            <el-input v-model.number="errorQuery.novelId" clearable placeholder="小说 ID"/>
            <el-input v-model.number="errorQuery.jobId" clearable placeholder="任务 ID"/>
            <el-select v-model="errorQuery.recovered" clearable placeholder="全部恢复状态"><el-option label="已自动恢复" :value="true"/><el-option label="未恢复" :value="false"/></el-select>
            <el-button class="neon-button" @click="loadErrors">查询</el-button>
          </div>
          <div class="table-flex-region">
            <el-table :data="errorRows" height="100%" class="cyber-table">
              <el-table-column prop="id" label="ID" width="70"/><el-table-column prop="novelId" label="小说" width="80"/><el-table-column prop="jobId" label="任务" width="80"/>
              <el-table-column label="分块" width="100"><template #default="{row}">{{row.chunkIndex}} / {{row.chunkTotal}}</template></el-table-column>
              <el-table-column prop="retryDepth" label="深度" width="75"/><el-table-column prop="stage" label="异常阶段" width="150"/>
              <el-table-column label="源文本" min-width="340"><template #default="{row}">{{row.sourceText?.slice(0,140)}}...</template></el-table-column>
              <el-table-column label="状态" width="110"><template #default="{row}"><span :class="row.recovered?'token-ok':'danger-text'">{{row.recovered?'已恢复':'未恢复'}}</span></template></el-table-column>
              <el-table-column label="时间" width="180"><template #default="{row}">{{time(row.createdAt)}}</template></el-table-column>
              <el-table-column label="操作" width="90" fixed="right"><template #default="{row}"><el-button text @click="showErrorDetail(row)">详情</el-button></template></el-table-column>
            </el-table>
          </div>
          <ListPager v-model:page="errorQuery.page" v-model:page-size="errorQuery.pageSize" :total="errorTotal" @change="loadErrors"/>
        </div>
      </el-tab-pane>
    </el-tabs>
  </section>

  <el-dialog v-model="errorDetailVisible" title="JSON 异常完整详情" width="82%" class="theme-dialog">
    <div v-if="currentError" class="diagnostic-detail">
      <h4>错误源文本</h4><pre>{{currentError.sourceText}}</pre>
      <h4>LLM 原始输出</h4><pre>{{currentError.rawResponse||'未获得模型输出'}}</pre>
      <h4>异常堆栈</h4><pre>{{currentError.error}}</pre>
    </div>
  </el-dialog>
</div>
</template>