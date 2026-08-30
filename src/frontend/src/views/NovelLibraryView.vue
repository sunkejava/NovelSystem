<script setup lang="ts">
import {onMounted,ref} from 'vue';import {useRouter} from 'vue-router';import {UploadFilled,MagicStick} from '@element-plus/icons-vue';
import {novelApi} from '../api/novels';import {writingApi} from '../api/writing';import PageHeader from '../components/PageHeader.vue';import StatusBadge from '../components/StatusBadge.vue';import EmptyState from '../components/EmptyState.vue';
const router=useRouter();const novels=ref<any[]>([]),uploading=ref(false);
async function load(){novels.value=await novelApi.list();}
async function upload(o:any){uploading.value=true;try{await novelApi.upload(o.file);o.onSuccess();await load();}finally{uploading.value=false;}}
async function learn(row:any){await writingApi.learn(row.id);router.push('/writing');}
onMounted(load);
</script>
<template><div><PageHeader eyebrow="NOVEL ASSETS" title="小说资产库" description="导入原始小说，建立可持续解析、配音、学习与再创作的知识资产。">
<el-upload :http-request="upload" :show-file-list="false" accept=".txt"><el-button class="neon-button" :loading="uploading"><el-icon><UploadFilled/></el-icon>导入小说</el-button></el-upload></PageHeader>
<section class="glass-panel content-card" v-if="novels.length"><div class="novel-grid"><article v-for="novel in novels" :key="novel.id" class="novel-card" @click="router.push('/novels/'+novel.id)"><div class="book-holo"><span>N</span><i></i></div><div class="novel-info"><StatusBadge :status="novel.status"/><h3>{{novel.title}}</h3><p>{{novel.sourceFile}}</p><small>{{novel.createdAt}}</small></div><div class="novel-actions"><el-button text @click.stop="learn(novel)"><el-icon><MagicStick/></el-icon>学习写法</el-button></div></article></div></section>
<EmptyState v-else title="等待第一部小说" description="上传 TXT 小说后，系统会将全文入库并开放人物解析、脚本生成与有声书生产能力。"/></div></template>