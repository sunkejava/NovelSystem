<script setup lang="ts">
import {onMounted,onUnmounted,ref} from 'vue';
import {ElMessage,ElMessageBox} from 'element-plus';
import {VideoPause,VideoPlay,Delete,Refresh,RefreshRight,WarningFilled,Search} from '@element-plus/icons-vue';
import {jobApi} from '../api/jobs';
import PageHeader from '../components/PageHeader.vue';
import StatusBadge from '../components/StatusBadge.vue';
import ListPager from '../components/ListPager.vue';

const jobs=ref<any[]>([]);
const total=ref(0);
const summary=ref<any>({running:0,completed:0,failed:0});
const query=ref({page:1,pageSize:20,keyword:'',type:'',status:''});
const errorVisible=ref(false);
const currentError=ref('');
const now=ref(Date.now());
let timer:number|undefined;
let clockTimer:number|undefined;

const taskTypes=['AnalyzeNovel','GenerateAudio','GenerateAudioSegment','MergeAudio','LearnWritingStyle'];
const statuses=['Queued','Running','Stopping','Stopped','Completed','Failed'];

async function load(){
  const result=await jobApi.list(query.value);
  jobs.value=result.items;
  total.value=result.total;
  summary.value=result.summary||{};
}
function search(){query.value.page=1;load();}
function reset(){query.value={page:1,pageSize:20,keyword:'',type:'',status:''};load();}

async function stop(row:any){await jobApi.stop(row.id);ElMessage.success('已提交停止请求');await load();}
async function resume(row:any){await jobApi.continue(row.id);ElMessage.success('任务已重新进入队列');await load();}
async function retry(row:any){await jobApi.retry(row.id);ElMessage.success('任务已从断点重新进入队列');await load();}
async function remove(row:any){await ElMessageBox.confirm('确认删除任务 #'+row.id+'？','删除任务',{type:'warning'});await jobApi.remove(row.id);ElMessage.success('任务记录已删除');await load();}
function showError(row:any){currentError.value=row.error||'未记录异常信息';errorVisible.value=true;}
function formatTime(value?:string){if(!value)return '—';const d=new Date(value);return Number.isNaN(d.getTime())?'—':d.toLocaleString();}
function formatDuration(ms?:number){if(!ms||ms<=0)return '—';const s=Math.floor(ms/1000),d=Math.floor(s/86400),h=Math.floor((s%86400)/3600),m=Math.floor((s%3600)/60),sec=s%60;if(d)return d+'天 '+h+'小时 '+m+'分';if(h)return h+'小时 '+m+'分 '+sec+'秒';if(m)return m+'分 '+sec+'秒';return sec+'秒';}
function liveElapsed(row:any){if(!row.startedAt)return row.elapsedMilliseconds||0;if(['Completed','Failed','Stopped'].includes(row.status))return row.elapsedMilliseconds||0;return Math.max(row.elapsedMilliseconds||0,now.value-new Date(row.startedAt).getTime());}
function remaining(row:any){if(!row.estimatedCompletionAt||!['Running','Queued','Stopping'].includes(row.status))return '—';const diff=new Date(row.estimatedCompletionAt).getTime()-now.value;return diff<=0?'即将完成':formatDuration(diff);}

onMounted(async()=>{await load();timer=window.setInterval(load,2500);clockTimer=window.setInterval(()=>now.value=Date.now(),1000);});
onUnmounted(()=>{if(timer)clearInterval(timer);if(clockTimer)clearInterval(clockTimer);});
</script>

<template>
<div>
  <PageHeader eyebrow="AI PIPELINE" title="任务中枢" description="分页检索任务，实时查看进度、耗时、ETA、失败原因及断点状态。">
    <el-button class="ghost-button" @click="load"><el-icon><Refresh/></el-icon>刷新</el-button>
  </PageHeader>

  <div class="task-summary-grid">
    <div class="mini-stat"><span>ACTIVE</span><b>{{summary.running||0}}</b></div>
    <div class="mini-stat"><span>COMPLETED</span><b>{{summary.completed||0}}</b></div>
    <div class="mini-stat"><span>FAILED</span><b>{{summary.failed||0}}</b></div>
  </div>

  <section class="glass-panel content-card">
    <div class="list-filter-bar">
      <el-input v-model="query.keyword" clearable placeholder="搜索任务类型 / 异常信息" @keyup.enter="search"><template #prefix><el-icon><Search/></el-icon></template></el-input>
      <el-select v-model="query.type" clearable placeholder="全部任务类型"><el-option v-for="item in taskTypes" :key="item" :label="item" :value="item"/></el-select>
      <el-select v-model="query.status" clearable placeholder="全部状态"><el-option v-for="item in statuses" :key="item" :label="item" :value="item"/></el-select>
      <el-button class="neon-button" @click="search">查询</el-button>
      <el-button class="ghost-button" @click="reset">重置</el-button>
    </div>

    <el-table :data="jobs" class="cyber-table">
      <el-table-column prop="id" label="ID" width="72"/>
      <el-table-column prop="type" label="任务类型" width="170"/>
      <el-table-column label="状态" width="120"><template #default="{row}"><StatusBadge :status="row.status"/></template></el-table-column>
      <el-table-column label="执行进度" min-width="220"><template #default="{row}"><div class="progress-cell"><el-progress :percentage="row.progress" :stroke-width="6"/><span>{{row.progress}}%</span></div><div class="checkpoint-text" v-if="row.totalSteps">步骤 {{row.checkpoint}} / {{row.totalSteps}} · 重试 {{row.retryCount||0}} 次</div></template></el-table-column>
      <el-table-column label="时间统计" min-width="315"><template #default="{row}"><div class="job-time-grid"><span>创建</span><b>{{formatTime(row.createdAt)}}</b><span>开始</span><b>{{formatTime(row.startedAt)}}</b><span>完成</span><b>{{formatTime(row.finishedAt)}}</b><span>已耗时</span><b class="time-emphasis">{{formatDuration(liveElapsed(row))}}</b></div></template></el-table-column>
      <el-table-column label="预计完成" min-width="245"><template #default="{row}"><div class="eta-panel" :class="{active:row.estimatedCompletionAt&&row.status==='Running'}"><template v-if="row.estimatedCompletionAt"><strong>{{formatTime(row.estimatedCompletionAt)}}</strong><small>预计剩余 {{remaining(row)}}</small><small v-if="row.averageStepMilliseconds">平均每步 {{formatDuration(row.averageStepMilliseconds)}}</small></template><template v-else><strong>{{row.status==='Completed'?'已完成':'计算中'}}</strong><small v-if="row.status==='Running'">至少完成 1 个步骤后生成 ETA</small><small v-else-if="row.status==='Queued'">等待任务开始</small><small v-else>—</small></template></div></template></el-table-column>
      <el-table-column label="失败原因" min-width="190"><template #default="{row}"><button v-if="row.status==='Failed'" class="error-link" @click="showError(row)"><el-icon><WarningFilled/></el-icon><span>{{(row.error||'未知异常').split('\n')[0]}}</span></button><span v-else class="muted-text">—</span></template></el-table-column>
      <el-table-column label="产物" width="110"><template #default="{row}"><el-link v-if="row.type==='GenerateAudio'&&row.status==='Completed'" :href="jobApi.downloadUrl(row.id)" type="primary">下载 MP3</el-link><span v-else>—</span></template></el-table-column>
      <el-table-column label="管理" width="285" fixed="right"><template #default="{row}">
        <el-button v-if="row.status==='Running'||row.status==='Queued'" text class="action-button warn" @click="stop(row)"><el-icon><VideoPause/></el-icon>停止</el-button>
        <el-button v-if="row.status==='Stopped'" text class="action-button success" @click="resume(row)"><el-icon><VideoPlay/></el-icon>继续</el-button>
        <el-button v-if="row.status==='Failed'" text class="action-button success" @click="retry(row)"><el-icon><RefreshRight/></el-icon>断点重试</el-button>
        <el-button v-if="!['Running','Queued','Stopping'].includes(row.status)" text class="action-button danger" @click="remove(row)"><el-icon><Delete/></el-icon>删除</el-button>
      </template></el-table-column>
    </el-table>
    <ListPager v-model:page="query.page" v-model:page-size="query.pageSize" :total="total" @change="load"/>
  </section>

  <el-dialog v-model="errorVisible" title="任务失败详情" width="720px" class="theme-dialog"><pre class="error-detail">{{currentError}}</pre><template #footer><el-button class="ghost-button" @click="errorVisible=false">关闭</el-button></template></el-dialog>
</div>
</template>