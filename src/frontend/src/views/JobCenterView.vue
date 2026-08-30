<script setup lang="ts">
import {onMounted,onUnmounted,ref} from 'vue';
import {ElMessage,ElMessageBox} from 'element-plus';
import {VideoPause,VideoPlay,Delete,Refresh,RefreshRight,WarningFilled} from '@element-plus/icons-vue';
import {jobApi} from '../api/jobs';
import PageHeader from '../components/PageHeader.vue';
import StatusBadge from '../components/StatusBadge.vue';

const jobs=ref<any[]>([]);
const errorVisible=ref(false);
const currentError=ref('');
const now=ref(Date.now());
let timer:number|undefined;
let clockTimer:number|undefined;

async function load(){jobs.value=await jobApi.list();}

async function stop(row:any){
  await jobApi.stop(row.id);
  ElMessage.success('已提交停止请求，当前步骤结束后任务会停止');
  await load();
}

async function resume(row:any){
  await jobApi.continue(row.id);
  ElMessage.success('任务已重新进入执行队列');
  await load();
}

async function retry(row:any){
  await jobApi.retry(row.id);
  ElMessage.success('任务已从断点重新进入执行队列');
  await load();
}

function showError(row:any){
  currentError.value=row.error||'未记录异常信息';
  errorVisible.value=true;
}

async function remove(row:any){
  await ElMessageBox.confirm('确认删除任务 #'+row.id+' 的历史记录？','删除任务',{type:'warning'});
  await jobApi.remove(row.id);
  ElMessage.success('任务记录已删除');
  await load();
}

function formatTime(value?:string){
  if(!value) return '—';
  const date=new Date(value);
  return Number.isNaN(date.getTime())?'—':date.toLocaleString();
}

function formatDuration(ms?:number){
  if(!ms||ms<=0) return '—';
  const total=Math.floor(ms/1000);
  const days=Math.floor(total/86400);
  const hours=Math.floor((total%86400)/3600);
  const minutes=Math.floor((total%3600)/60);
  const seconds=total%60;
  if(days>0) return days+'天 '+hours+'小时 '+minutes+'分';
  if(hours>0) return hours+'小时 '+minutes+'分 '+seconds+'秒';
  if(minutes>0) return minutes+'分 '+seconds+'秒';
  return seconds+'秒';
}

function liveElapsed(row:any){
  if(!row.startedAt) return row.elapsedMilliseconds||0;
  if(['Completed','Failed','Stopped'].includes(row.status)) return row.elapsedMilliseconds||0;
  return Math.max(row.elapsedMilliseconds||0,now.value-new Date(row.startedAt).getTime());
}

function remaining(row:any){
  if(!row.estimatedCompletionAt||!['Running','Queued','Stopping'].includes(row.status)) return '—';
  const diff=new Date(row.estimatedCompletionAt).getTime()-now.value;
  if(diff<=0) return '即将完成';
  return formatDuration(diff);
}

onMounted(async()=>{
  await load();
  timer=window.setInterval(load,2500);
  clockTimer=window.setInterval(()=>now.value=Date.now(),1000);
});
onUnmounted(()=>{
  if(timer) clearInterval(timer);
  if(clockTimer) clearInterval(clockTimer);
});
</script>

<template>
<div>
  <PageHeader eyebrow="AI PIPELINE" title="任务中枢" description="实时查看任务开始、耗时与预计完成时间，并支持停止、继续、断点重试、删除和结果下载。">
    <el-button class="ghost-button" @click="load"><el-icon><Refresh/></el-icon>立即刷新</el-button>
  </PageHeader>

  <section class="glass-panel content-card">
    <el-table :data="jobs" class="cyber-table">
      <el-table-column prop="id" label="ID" width="72"/>
      <el-table-column prop="type" label="任务类型" width="170"/>
      <el-table-column label="状态" width="120"><template #default="{row}"><StatusBadge :status="row.status"/></template></el-table-column>

      <el-table-column label="执行进度" min-width="220">
        <template #default="{row}">
          <div class="progress-cell">
            <el-progress :percentage="row.progress" :stroke-width="6"/>
            <span>{{row.progress}}%</span>
          </div>
          <div class="checkpoint-text" v-if="row.totalSteps">
            步骤 {{row.checkpoint}} / {{row.totalSteps}} · 重试 {{row.retryCount||0}} 次
          </div>
        </template>
      </el-table-column>

      <el-table-column label="时间统计" min-width="315">
        <template #default="{row}">
          <div class="job-time-grid">
            <span>创建</span><b>{{formatTime(row.createdAt)}}</b>
            <span>开始</span><b>{{formatTime(row.startedAt)}}</b>
            <span>完成</span><b>{{formatTime(row.finishedAt)}}</b>
            <span>已耗时</span><b class="time-emphasis">{{formatDuration(liveElapsed(row))}}</b>
          </div>
        </template>
      </el-table-column>

      <el-table-column label="预计完成" min-width="245">
        <template #default="{row}">
          <div class="eta-panel" :class="{active:row.estimatedCompletionAt&&row.status==='Running'}">
            <template v-if="row.estimatedCompletionAt">
              <strong>{{formatTime(row.estimatedCompletionAt)}}</strong>
              <small>预计剩余 {{remaining(row)}}</small>
              <small v-if="row.averageStepMilliseconds">平均每步 {{formatDuration(row.averageStepMilliseconds)}}</small>
            </template>
            <template v-else>
              <strong>{{row.status==='Completed'?'已完成':'计算中'}}</strong>
              <small v-if="row.status==='Running'">至少完成 1 个步骤后生成 ETA</small>
              <small v-else-if="row.status==='Queued'">等待任务开始</small>
              <small v-else>—</small>
            </template>
          </div>
        </template>
      </el-table-column>

      <el-table-column label="失败原因" min-width="190">
        <template #default="{row}">
          <button v-if="row.status==='Failed'" class="error-link" @click="showError(row)">
            <el-icon><WarningFilled/></el-icon>
            <span>{{(row.error||'未知异常').split('\n')[0]}}</span>
          </button>
          <span v-else class="muted-text">—</span>
        </template>
      </el-table-column>

      <el-table-column label="产物" width="110">
        <template #default="{row}">
          <el-link v-if="row.type==='GenerateAudio'&&row.status==='Completed'" :href="jobApi.downloadUrl(row.id)" type="primary">下载 MP3</el-link>
          <span v-else>—</span>
        </template>
      </el-table-column>

      <el-table-column label="管理" width="285" fixed="right">
        <template #default="{row}">
          <el-button v-if="row.status==='Running'||row.status==='Queued'" text class="action-button warn" @click="stop(row)">
            <el-icon><VideoPause/></el-icon>停止
          </el-button>
          <el-button v-if="row.status==='Stopped'" text class="action-button success" @click="resume(row)">
            <el-icon><VideoPlay/></el-icon>继续
          </el-button>
          <el-button v-if="row.status==='Failed'" text class="action-button success" @click="retry(row)">
            <el-icon><RefreshRight/></el-icon>断点重试
          </el-button>
          <el-button v-if="!['Running','Queued','Stopping'].includes(row.status)" text class="action-button danger" @click="remove(row)">
            <el-icon><Delete/></el-icon>删除
          </el-button>
        </template>
      </el-table-column>
    </el-table>
  </section>

  <el-dialog v-model="errorVisible" title="任务失败详情" width="720px" class="theme-dialog">
    <pre class="error-detail">{{currentError}}</pre>
    <template #footer><el-button class="ghost-button" @click="errorVisible=false">关闭</el-button></template>
  </el-dialog>
</div>
</template>