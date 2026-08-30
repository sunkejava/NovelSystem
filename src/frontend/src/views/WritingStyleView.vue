<script setup lang="ts">
import {computed,onMounted,onUnmounted,ref} from 'vue';
import {ElMessage,ElMessageBox} from 'element-plus';
import {Edit,Delete,Refresh} from '@element-plus/icons-vue';
import {writingApi} from '../api/writing';
import {jobApi} from '../api/jobs';
import PageHeader from '../components/PageHeader.vue';
import StatusBadge from '../components/StatusBadge.vue';
import EmptyState from '../components/EmptyState.vue';

const styles=ref<any[]>([]);
const jobs=ref<any[]>([]);
const editorVisible=ref(false);
const form=ref({id:0,name:'',summary:'',promptTemplate:''});
let timer:number|undefined;

const learningJobs=computed(()=>jobs.value.filter(x=>x.type==='LearnWritingStyle'));

async function load(){
  [styles.value,jobs.value]=await Promise.all([writingApi.styles(),jobApi.list()]);
}
function edit(row:any){
  form.value={id:row.id,name:row.name,summary:row.summary,promptTemplate:row.promptTemplate};
  editorVisible.value=true;
}
async function save(){
  await writingApi.updateStyle(form.value.id,{name:form.value.name,summary:form.value.summary,promptTemplate:form.value.promptTemplate});
  ElMessage.success('写作风格已保存');
  editorVisible.value=false;
  await load();
}
async function remove(row:any){
  await ElMessageBox.confirm('确认删除写作风格“'+row.name+'”？','删除写作风格',{type:'warning'});
  await writingApi.removeStyle(row.id);
  ElMessage.success('写作风格已删除');
  await load();
}

onMounted(async()=>{await load();timer=window.setInterval(load,3000)});
onUnmounted(()=>timer&&clearInterval(timer));
</script>

<template>
<div>
  <PageHeader eyebrow="STYLE INTELLIGENCE" title="写作风格管理" description="查看小说写法学习进度，管理已沉淀的风格模型及其可复用提示词。">
    <el-button class="ghost-button" @click="load"><el-icon><Refresh/></el-icon>刷新</el-button>
  </PageHeader>

  <section class="glass-panel content-card style-job-panel">
    <div class="card-head"><div><span class="eyebrow">LEARNING PIPELINE</span><h3>写法学习任务</h3></div><span>{{learningJobs.length}} TASKS</span></div>
    <el-table :data="learningJobs" class="cyber-table">
      <el-table-column prop="id" label="任务" width="90"/>
      <el-table-column label="状态" width="130"><template #default="{row}"><StatusBadge :status="row.status"/></template></el-table-column>
      <el-table-column label="进度" min-width="280">
        <template #default="{row}">
          <el-progress :percentage="row.progress" :stroke-width="6"/>
          <div class="checkpoint-text">步骤 {{row.checkpoint||0}} / {{row.totalSteps||0}}</div>
        </template>
      </el-table-column>
      <el-table-column prop="createdAt" label="创建时间" width="200"/>
      <el-table-column label="异常" min-width="260">
        <template #default="{row}">
          <span v-if="row.status==='Failed'" class="danger-text">{{(row.error||'未知异常').split('\n')[0]}}</span>
          <span v-else class="muted-text">—</span>
        </template>
      </el-table-column>
    </el-table>
  </section>

  <section class="glass-panel content-card" style="margin-top:16px">
    <div class="card-head"><div><span class="eyebrow">STYLE MODELS</span><h3>已学习风格模型</h3></div><span>{{styles.length}} MODELS</span></div>
    <div v-if="styles.length" class="style-management-grid">
      <article v-for="style in styles" :key="style.id" class="style-management-card">
        <div class="style-card-top"><span class="style-index">{{String(style.id).padStart(2,'0')}}</span><div><small>{{style.novelTitle||'独立风格'}}</small><h3>{{style.name}}</h3></div></div>
        <p>{{style.summary}}</p>
        <div class="style-card-actions">
          <el-button text @click="edit(style)"><el-icon><Edit/></el-icon>编辑</el-button>
          <el-button text type="danger" @click="remove(style)"><el-icon><Delete/></el-icon>删除</el-button>
        </div>
      </article>
    </div>
    <EmptyState v-else title="尚未生成写作风格" description="在小说资产库中点击“学习写法”，系统会创建后台任务并逐块学习整部小说。"/>
  </section>

  <el-dialog v-model="editorVisible" title="编辑写作风格" width="760px" class="theme-dialog">
    <el-form label-position="top">
      <el-form-item label="风格名称"><el-input v-model="form.name"/></el-form-item>
      <el-form-item label="风格摘要"><el-input v-model="form.summary" type="textarea" :rows="8"/></el-form-item>
      <el-form-item label="生成提示词模板"><el-input v-model="form.promptTemplate" type="textarea" :rows="12"/></el-form-item>
    </el-form>
    <template #footer>
      <el-button class="ghost-button" @click="editorVisible=false">取消</el-button>
      <el-button class="neon-button" @click="save">保存</el-button>
    </template>
  </el-dialog>
</div>
</template>