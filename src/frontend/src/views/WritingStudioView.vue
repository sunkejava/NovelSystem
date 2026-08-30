<script setup lang="ts">
import {onMounted,ref} from 'vue';import {ElMessage} from 'element-plus';import {useRouter} from 'vue-router';import {Search} from '@element-plus/icons-vue';
import {writingApi} from '../api/writing';import PageHeader from '../components/PageHeader.vue';import EmptyState from '../components/EmptyState.vue';import ListPager from '../components/ListPager.vue';
const router=useRouter();const styles=ref<any[]>([]),generated=ref<any[]>([]),total=ref(0),generating=ref(false);const query=ref({page:1,pageSize:12,keyword:''});
const form=ref({title:'未命名新世界',styleId:undefined as number|undefined,sourceNovelId:undefined as number|undefined,prompt:'使用选定写作风格，创作一部长篇中文小说。请先输出有吸引力的开篇章节，建立核心人物、冲突和世界观。'});
async function load(){const [s,g]=await Promise.all([writingApi.styleOptions(),writingApi.generated(query.value)]);styles.value=s;generated.value=g.items;total.value=g.total;}
async function loadGenerated(){const g=await writingApi.generated(query.value);generated.value=g.items;total.value=g.total;}
function search(){query.value.page=1;loadGenerated();}
async function generate(){generating.value=true;try{await writingApi.generate(form.value);ElMessage.success('新小说已生成并保存');query.value.page=1;await loadGenerated();}finally{generating.value=false;}}
async function publish(row:any){const novel=await writingApi.publish(row.id);ElMessage.success('已加入小说资产库');router.push('/novels/'+novel.id);}
onMounted(load);
</script>
<template><div>
<PageHeader eyebrow="GENERATIVE STUDIO" title="AI 创作舱" description="选择写作风格生成小说，并分页检索历史生成作品。"/>
<div class="writing-layout">
<section class="glass-panel content-card generator-panel"><div class="section-title"><span>AI</span><div><h3>生成任务</h3><p>PROMPT-DRIVEN STORY SYNTHESIS</p></div></div>
<el-form label-position="top"><el-form-item label="新小说标题"><el-input v-model="form.title"/></el-form-item><el-form-item label="写作风格模型"><el-select v-model="form.styleId" placeholder="选择已学习的风格" clearable class="full-width"><el-option v-for="style in styles" :key="style.id" :label="style.name" :value="style.id"/></el-select></el-form-item><el-form-item label="创作指令"><el-input v-model="form.prompt" type="textarea" :rows="9"/></el-form-item><el-button class="neon-button full-width" :loading="generating" @click="generate">启动生成引擎</el-button></el-form>
</section>
<section class="glass-panel content-card"><div class="card-head"><div><span class="eyebrow">STYLE OPTIONS</span><h3>可用写作风格</h3></div><span>{{styles.length}} OPTIONS</span></div><div v-if="styles.length" class="style-list"><article v-for="style in styles" :key="style.id" class="style-item"><div class="style-index">{{String(style.id).padStart(2,'0')}}</div><div><h4>{{style.name}}</h4></div></article></div><EmptyState v-else title="尚未训练写作风格" description="在小说资产库中选择“学习写法”。"/></section>
</div>
<section class="glass-panel content-card generated-panel">
<div class="card-head"><div><span class="eyebrow">GENERATED STORIES</span><h3>生成作品</h3></div><span>{{total}} STORIES</span></div>
<div class="list-filter-bar compact-filter"><el-input v-model="query.keyword" clearable placeholder="搜索标题 / 创作指令 / 内容" @keyup.enter="search"><template #prefix><el-icon><Search/></el-icon></template></el-input><el-button class="neon-button" @click="search">查询</el-button></div>
<div v-if="generated.length" class="generated-grid"><article v-for="item in generated" :key="item.id" class="generated-card"><span class="generated-id">GEN-{{item.id}}</span><h3>{{item.title}}</h3><p>{{item.content.slice(0,180)}}...</p><div><el-link :href="writingApi.downloadUrl(item.id)" type="primary">下载 TXT</el-link><el-button text @click="publish(item)">进入小说工作台</el-button></div></article></div><EmptyState v-else title="没有匹配的生成作品" description="调整查询条件或创建新的小说。"/>
<ListPager v-model:page="query.page" v-model:page-size="query.pageSize" :total="total" @change="loadGenerated"/>
</section>
</div></template>