<script setup lang="ts">
import {onMounted,ref} from 'vue';
import {useRouter} from 'vue-router';
import {ElMessage,ElMessageBox} from 'element-plus';
import {UploadFilled,MagicStick,Edit,Delete,Search,Refresh} from '@element-plus/icons-vue';
import {novelApi} from '../api/novels';import {writingApi} from '../api/writing';
import PageHeader from '../components/PageHeader.vue';import StatusBadge from '../components/StatusBadge.vue';import EmptyState from '../components/EmptyState.vue';import ListPager from '../components/ListPager.vue';
const router=useRouter();const novels=ref<any[]>([]),total=ref(0),uploading=ref(false),editorVisible=ref(false),editorLoading=ref(false);
const query=ref({page:1,pageSize:12,keyword:'',status:''});const editorForm=ref({id:0,title:'',content:''});
async function load(){const r=await novelApi.list(query.value);novels.value=r.items;total.value=r.total;}
function search(){query.value.page=1;load();}function reset(){query.value={page:1,pageSize:12,keyword:'',status:''};load();}
async function upload(o:any){uploading.value=true;try{await novelApi.upload(o.file);o.onSuccess();ElMessage.success('小说导入成功');query.value.page=1;await load();}catch(e){o.onError(e);}finally{uploading.value=false;}}
async function learn(row:any){await writingApi.learn(row.id);ElMessage.success('写法学习任务已创建');router.push('/styles');}
async function openEditor(row:any){editorLoading.value=true;editorVisible.value=true;try{const d=await novelApi.detail(row.id);editorForm.value={id:row.id,title:d.novel.title,content:d.novel.content};}finally{editorLoading.value=false;}}
async function saveNovel(){await novelApi.update(editorForm.value.id,{title:editorForm.value.title,content:editorForm.value.content});ElMessage.success('小说资产已保存');editorVisible.value=false;await load();}
async function removeNovel(row:any){await ElMessageBox.confirm('删除小说“'+row.title+'”后，将同时删除人物、脚本等解析数据。','删除小说资产',{type:'warning'});await novelApi.remove(row.id);ElMessage.success('小说资产已删除');await load();}
onMounted(load);
</script>
<template><div>
<PageHeader eyebrow="NOVEL ASSETS" title="小说资产库" description="导入、检索和管理小说资产。"><el-upload :http-request="upload" :show-file-list="false" accept=".txt"><el-button class="neon-button" :loading="uploading"><el-icon><UploadFilled/></el-icon>导入小说</el-button></el-upload></PageHeader>
<section class="glass-panel content-card">
<div class="list-filter-bar"><el-input v-model="query.keyword" clearable placeholder="搜索小说标题 / 来源文件" @keyup.enter="search"><template #prefix><el-icon><Search/></el-icon></template></el-input><el-select v-model="query.status" clearable placeholder="全部状态"><el-option label="已上传" value="Uploaded"/><el-option label="解析中" value="Analyzing"/><el-option label="已解析" value="Analyzed"/></el-select><el-button class="neon-button" @click="search">查询</el-button><el-button class="ghost-button" @click="reset"><el-icon><Refresh/></el-icon>重置</el-button></div>
<div v-if="novels.length" class="novel-grid"><article v-for="novel in novels" :key="novel.id" class="novel-card" @click="router.push('/novels/'+novel.id)"><div class="book-holo"><span>N</span><i></i></div><div class="novel-info"><StatusBadge :status="novel.status"/><h3>{{novel.title}}</h3><p>{{novel.sourceFile}}</p><small>{{new Date(novel.createdAt).toLocaleString()}}</small></div><div class="novel-actions"><el-button text @click.stop="openEditor(novel)"><el-icon><Edit/></el-icon>编辑</el-button><el-button text @click.stop="learn(novel)"><el-icon><MagicStick/></el-icon>学习写法</el-button><el-button text type="danger" @click.stop="removeNovel(novel)"><el-icon><Delete/></el-icon>删除</el-button></div></article></div>
<EmptyState v-else title="没有匹配的小说资产" description="调整筛选条件，或导入新的 TXT 小说。"/>
<ListPager v-model:page="query.page" v-model:page-size="query.pageSize" :total="total" @change="load"/>
</section>
<el-dialog v-model="editorVisible" title="编辑小说资产" width="78%" class="theme-dialog" destroy-on-close><div v-loading="editorLoading"><el-form label-position="top"><el-form-item label="小说标题"><el-input v-model="editorForm.title"/></el-form-item><el-form-item label="小说正文"><el-input v-model="editorForm.content" type="textarea" :rows="26" resize="vertical"/></el-form-item></el-form></div><template #footer><el-button class="ghost-button" @click="editorVisible=false">取消</el-button><el-button class="neon-button" @click="saveNovel">保存修改</el-button></template></el-dialog>
</div></template>