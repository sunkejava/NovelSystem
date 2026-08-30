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
let timer:number|undefined;

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

onMounted(async()=>{await load();timer=window.setInterval(load,2500)});
onUnmounted(()=>timer&&clearInterval(timer));
</script>

<template>
<div>
  <PageHeader eyebrow="AI PIPELINE" title="任务中枢" description="管理小说解析、TTS 生成与媒体合并任务，支持停止、继续、失败断点重试、删除和结果下载。">
    <el-button class="ghost-button" @click="load"><el-icon><Refresh/></el-icon>立即刷新</el-button>
  </PageHeader>

  <section class="glass-panel content-card">
    <el-table :data="jobs" class="cyber-table">
      <el-table-column prop="id" label="ID" width="80"/>
      <el-table-column prop="type" label="任务类型" width="170"/>
      <el-table-column label="状态" width="130"><template #default="{row}"><StatusBadge :status="row.status"/></template></el-table-column>
      <el-table-column label="执行进度" min-width="240">
        <template #default="{row}">
          <div class="progress-cell">
            <el-progress :percentage="row.progress" :stroke-width="6"/>
            <span>{{row.progress}}%</span>
          </div>
          <div class="checkpoint-text" v-if="row.totalSteps">断点 {{row.checkpoint}} / {{row.totalSteps}} · 重试 {{row.retryCount||0}} 次</div>
        </template>
      </el-table-column>
      <el-table-column label="失败原因" min-width="220">
        <template #default="{row}">
          <button v-if="row.status==='Failed'" class="error-link" @click="showError(row)">
            <el-icon><WarningFilled/></el-icon>
            <span>{{(row.error||'未知异常').split('\n')[0]}}</span>
          </button>
          <span v-else class="muted-text">—</span>
        </template>
      </el-table-column>
      <el-table-column prop="createdAt" label="创建时间" width="190"/>
      <el-table-column label="产物" width="120">
        <template #default="{row}">
          <el-link v-if="row.type==='GenerateAudio'&&row.status==='Completed'" :href="jobApi.downloadUrl(row.id)" type="primary">下载 MP3</el-link>
          <span v-else>—</span>
        </template>
      </el-table-column>
      <el-table-column label="管理" width="300" fixed="right">
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