<script setup lang="ts">
import {onMounted,onUnmounted,ref} from 'vue';
import {ElMessage,ElMessageBox} from 'element-plus';
import {Edit,Delete,Refresh,Search} from '@element-plus/icons-vue';
import {writingApi} from '../api/writing';
import {jobApi} from '../api/jobs';
import PageHeader from '../components/PageHeader.vue';
import StatusBadge from '../components/StatusBadge.vue';
import EmptyState from '../components/EmptyState.vue';
import ListPager from '../components/ListPager.vue';

const styles=ref<any[]>([]);
const styleTotal=ref(0);
const styleQuery=ref({page:1,pageSize:12,keyword:''});
const jobs=ref<any[]>([]);
const jobTotal=ref(0);
const jobQuery=ref({page:1,pageSize:10,type:'LearnWritingStyle',status:'',keyword:''});
const editorVisible=ref(false);
const form=ref({id:0,name:'',summary:'',promptTemplate:''});
let timer:number|undefined;

async function loadStyles(){const r=await writingApi.styles(styleQuery.value);styles.value=r.items;styleTotal.value=r.total;}
async function loadJobs(){const r=await jobApi.list(jobQuery.value);jobs.value=r.items;jobTotal.value=r.total;}
async function load(){await Promise.all([loadStyles(),loadJobs()]);}
function searchStyles(){styleQuery.value.page=1;loadStyles();}
function resetStyles(){styleQuery.value={page:1,pageSize:12,keyword:''};loadStyles();}
function searchJobs(){jobQuery.value.page=1;loadJobs();}
function edit(row:any){form.value={id:row.id,name:row.name,summary:row.summary,promptTemplate:row.promptTemplate};editorVisible.value=true;}
async function save(){await writingApi.updateStyle(form.value.id,{name:form.value.name,summary:form.value.summary,promptTemplate:form.value.promptTemplate});ElMessage.success('写作风格已保存');editorVisible.value=false;await loadStyles();}
async function remove(row:any){await ElMessageBox.confirm('确认删除写作风格“'+row.name+'”？','删除写作风格',{type:'warning'});await writingApi.removeStyle(row.id);ElMessage.success('写作风格已删除');await loadStyles();}

onMounted(async()=>{await load();timer=window.setInterval(loadJobs,3000);});
onUnmounted(()=>timer&&clearInterval(timer));
</script>

<template><div class="page-fill writing-style-page">
<PageHeader eyebrow="STYLE INTELLIGENCE" title="写作风格管理" description="分页查看学习任务与风格模型，支持关键字和状态筛选。"><el-button class="ghost-button" @click="load"><el-icon><Refresh/></el-icon>刷新</el-button></PageHeader>

<div class="writing-style-content"><section class="glass-panel content-card style-job-panel table-page-card">
  <div class="card-head"><div><span class="eyebrow">LEARNING PIPELINE</span><h3>写法学习任务</h3></div><span>{{jobTotal}} TASKS</span></div>
  <div class="list-filter-bar compact-filter">
    <el-input v-model="jobQuery.keyword" clearable placeholder="搜索任务 / 异常" @keyup.enter="searchJobs"><template #prefix><el-icon><Search/></el-icon></template></el-input>
    <el-select v-model="jobQuery.status" clearable placeholder="全部状态"><el-option v-for="s in ['Queued','Running','Stopping','Stopped','Completed','Failed']" :key="s" :label="s" :value="s"/></el-select>
    <el-button class="neon-button" @click="searchJobs">查询</el-button>
  </div>
  <div class="table-flex-region"><el-table :data="jobs" class="cyber-table" height="100%">
    <el-table-column prop="id" label="任务" width="90"/>
    <el-table-column label="状态" width="130"><template #default="{row}"><StatusBadge :status="row.status"/></template></el-table-column>
    <el-table-column label="进度" min-width="240"><template #default="{row}"><el-progress :percentage="row.progress" :stroke-width="6"/><div class="checkpoint-text">步骤 {{row.checkpoint||0}} / {{row.totalSteps||0}}</div></template></el-table-column>
    <el-table-column label="开始时间" width="190"><template #default="{row}">{{row.startedAt?new Date(row.startedAt).toLocaleString():'—'}}</template></el-table-column>
    <el-table-column label="预计完成" width="190"><template #default="{row}">{{row.estimatedCompletionAt?new Date(row.estimatedCompletionAt).toLocaleString():(row.status==='Running'?'计算中':'—')}}</template></el-table-column>
    <el-table-column label="异常" min-width="260"><template #default="{row}"><span v-if="row.status==='Failed'" class="danger-text">{{(row.error||'未知异常').split('\n')[0]}}</span><span v-else class="muted-text">—</span></template></el-table-column>
  </el-table></div>
  <ListPager v-model:page="jobQuery.page" v-model:page-size="jobQuery.pageSize" :total="jobTotal" @change="loadJobs"/>
</section>

<section class="glass-panel content-card style-model-panel">
  <div class="card-head"><div><span class="eyebrow">STYLE MODELS</span><h3>已学习风格模型</h3></div><span>{{styleTotal}} MODELS</span></div>
  <div class="list-filter-bar compact-filter">
    <el-input v-model="styleQuery.keyword" clearable placeholder="搜索风格名称 / 来源小说 / 摘要" @keyup.enter="searchStyles"><template #prefix><el-icon><Search/></el-icon></template></el-input>
    <el-button class="neon-button" @click="searchStyles">查询</el-button>
    <el-button class="ghost-button" @click="resetStyles">重置</el-button>
  </div>
  <div v-if="styles.length" class="style-management-grid">
    <article v-for="style in styles" :key="style.id" class="style-management-card">
      <div class="style-card-top"><span class="style-index">{{String(style.id).padStart(2,'0')}}</span><div><small>{{style.novelTitle||'独立风格'}}</small><h3>{{style.name}}</h3></div></div>
      <p>{{style.summary}}</p>
      <div class="style-card-actions"><el-button text @click="edit(style)"><el-icon><Edit/></el-icon>编辑</el-button><el-button text type="danger" @click="remove(style)"><el-icon><Delete/></el-icon>删除</el-button></div>
    </article>
  </div>
  <EmptyState v-else title="没有匹配的写作风格" description="调整查询条件，或从小说资产发起写法学习。"/>
  <ListPager v-model:page="styleQuery.page" v-model:page-size="styleQuery.pageSize" :total="styleTotal" @change="loadStyles"/>
</section></div>

<el-dialog v-model="editorVisible" title="编辑写作风格" width="760px" class="theme-dialog">
  <el-form label-position="top"><el-form-item label="风格名称"><el-input v-model="form.name"/></el-form-item><el-form-item label="风格摘要"><el-input v-model="form.summary" type="textarea" :rows="8"/></el-form-item><el-form-item label="生成提示词模板"><el-input v-model="form.promptTemplate" type="textarea" :rows="12"/></el-form-item></el-form>
  <template #footer><el-button class="ghost-button" @click="editorVisible=false">取消</el-button><el-button class="neon-button" @click="save">保存</el-button></template>
</el-dialog>
</div></template>