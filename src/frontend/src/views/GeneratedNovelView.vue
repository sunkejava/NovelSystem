<script setup lang="ts">
import {computed,onMounted,onUnmounted,ref} from 'vue';
import {ElMessage} from 'element-plus';
import {useRoute,useRouter} from 'vue-router';
import {ArrowLeft,Download,Promotion,Refresh} from '@element-plus/icons-vue';
import {writingApi} from '../api/writing';
import PageHeader from '../components/PageHeader.vue';

const route=useRoute();
const router=useRouter();
const id=String(route.params.id);
const detail=ref<any>(null);
const loading=ref(false);
let timer:number|undefined;

const isGenerating=computed(()=>['Queued','Running','Stopping'].includes(detail.value?.job?.status));
const completedChapters=computed(()=>{
  const job=detail.value?.job;
  if(!job)return 0;
  return Math.max(0,Math.min((job.totalSteps||1)-1,(job.checkpoint||0)-1));
});
const wordCount=computed(()=>detail.value?.content?.length||0);

function statusText(status?:string){
  return ({Queued:'等待生成',Running:'生成中',Stopping:'停止中',Completed:'已完成',Failed:'失败',Stopped:'已停止'} as any)[status||'']||'未知';
}
function time(v?:string){return v?new Date(v).toLocaleString():'—';}
async function load(){
  loading.value=true;
  try{
    detail.value=await writingApi.generatedDetail(id);
  }finally{
    loading.value=false;
  }
}
async function poll(){
  await load();
  if(isGenerating.value)timer=window.setTimeout(poll,1500);
}
async function publish(){
  const novel=await writingApi.publish(Number(id));
  ElMessage.success('已加入小说资产库');
  router.push('/novels/'+novel.id);
}
onMounted(poll);
onUnmounted(()=>{if(timer)window.clearTimeout(timer);});
</script>

<template>
<div class="page-fill generated-reader-page" v-loading="loading">
  <PageHeader
    eyebrow="GENERATED NOVEL"
    :title="detail?.title||'生成小说'"
    description="查看 AI 创作大纲、生成状态和完整正文。"
  >
    <el-button class="ghost-button" @click="router.push('/writing')"><el-icon><ArrowLeft/></el-icon>返回创作舱</el-button>
    <el-button class="ghost-button" @click="load"><el-icon><Refresh/></el-icon>刷新</el-button>
    <el-link v-if="detail?.content" :href="writingApi.downloadUrl(Number(id))" class="reader-download">
      <el-button class="ghost-button"><el-icon><Download/></el-icon>下载 TXT</el-button>
    </el-link>
    <el-button v-if="detail?.content" class="neon-button" @click="publish"><el-icon><Promotion/></el-icon>进入小说工作台</el-button>
  </PageHeader>

  <div v-if="detail" class="generated-reader-layout">
    <aside class="glass-panel content-card generated-meta-panel">
      <div class="card-head"><div><span class="eyebrow">CREATION PROFILE</span><h3>创作信息</h3></div></div>
      <div class="generated-meta-list">
        <div><span>题材</span><b>{{detail.genre||'未设置'}}</b></div>
        <div><span>目标字数</span><b>{{Number(detail.targetWords||0).toLocaleString()}}</b></div>
        <div><span>章节数</span><b>{{Number(detail.chapterCount||0).toLocaleString()}}</b></div>
        <div><span>叙事视角</span><b>{{detail.pointOfView||'未设置'}}</b></div>
        <div><span>整体基调</span><b>{{detail.tone||'未设置'}}</b></div>
        <div><span>当前正文字数</span><b>{{wordCount.toLocaleString()}}</b></div>
        <div><span>创建时间</span><b>{{time(detail.createdAt)}}</b></div>
      </div>

      <div v-if="detail.job" class="reader-job-card">
        <div class="reader-job-head">
          <span>生成状态</span><b>{{statusText(detail.job.status)}}</b>
        </div>
        <el-progress :percentage="detail.job.progress||0" :stroke-width="9"/>
        <div class="reader-job-meta">
          <span>已完成章节 <b>{{completedChapters}}</b></span>
          <span>步骤 <b>{{detail.job.checkpoint||0}} / {{detail.job.totalSteps||0}}</b></span>
        </div>
        <pre v-if="detail.job.error" class="generation-error">{{detail.job.error}}</pre>
      </div>

      <div class="reader-prompt-block">
        <span>创作指令</span>
        <p>{{detail.prompt||'—'}}</p>
      </div>
    </aside>

    <main class="generated-reader-main">
      <section class="glass-panel content-card generated-outline-panel">
        <div class="card-head"><div><span class="eyebrow">OUTLINE</span><h3>创作大纲</h3></div></div>
        <pre>{{detail.outline||'大纲尚未生成完成。'}}</pre>
      </section>

      <section class="glass-panel content-card generated-content-panel">
        <div class="card-head">
          <div><span class="eyebrow">FULL STORY</span><h3>小说正文</h3></div>
          <span>{{wordCount.toLocaleString()}} CHARS</span>
        </div>
        <article v-if="detail.content" class="novel-reader-content">{{detail.content}}</article>
        <div v-else class="reader-empty">
          {{isGenerating?'正文将在章节生成完成后持续显示在这里。':'当前还没有正文内容。'}}
        </div>
      </section>
    </main>
  </div>
</div>
</template>