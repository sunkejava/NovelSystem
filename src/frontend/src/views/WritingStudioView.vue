<script setup lang="ts">
import {computed,onMounted,onUnmounted,ref} from 'vue';
import {ElMessage} from 'element-plus';
import {useRouter} from 'vue-router';
import {Search,EditPen,Collection,Document} from '@element-plus/icons-vue';
import {writingApi} from '../api/writing';
import {jobApi} from '../api/jobs';
import PageHeader from '../components/PageHeader.vue';
import EmptyState from '../components/EmptyState.vue';
import ListPager from '../components/ListPager.vue';

const router=useRouter();
const activeTab=ref<'create'|'styles'|'stories'>('create');
const styles=ref<any[]>([]);
const generated=ref<any[]>([]);
const total=ref(0);
const generating=ref(false);
const generationJob=ref<any>(null);
const query=ref({page:1,pageSize:12,keyword:''});
let pollTimer:number|undefined;

const form=ref({
  title:'未命名新世界',
  styleId:undefined as number|undefined,
  sourceNovelId:undefined as number|undefined,
  genre:'都市异能',
  targetWords:4000,
  chapterCount:1,
  pointOfView:'第三人称有限视角',
  tone:'紧张、有悬念、节奏明快',
  prompt:'创作全新的故事，建立清晰主角目标、核心冲突、人物关系和持续推进的悬念。'
});

const selectedStyle=computed(()=>styles.value.find(x=>x.id===form.value.styleId));
const generationStage=computed(()=>{
  const job=generationJob.value;
  if(!job)return '';
  if(job.status==='Queued')return '等待生成任务调度';
  if(job.status==='Completed')return '小说生成完成';
  if(job.status==='Failed')return '生成失败';
  if(job.status==='Stopped')return '生成已停止';
  if(job.checkpoint<=0)return '正在生成全书创作大纲';
  const totalChapters=Math.max(1,(job.totalSteps||1)-1);
  const currentChapter=Math.min(totalChapters,Math.max(1,job.checkpoint));
  return `正在生成第 ${currentChapter.toLocaleString()} / ${totalChapters.toLocaleString()} 章`;
});
const completedChapters=computed(()=>{
  const job=generationJob.value;
  if(!job)return 0;
  return Math.max(0,Math.min((job.totalSteps||1)-1,(job.checkpoint||0)-1));
});
const generationElapsed=computed(()=>formatDuration(generationJob.value?.elapsedMilliseconds||0));

async function load(){
  const [s,g]=await Promise.all([writingApi.styleOptions(),writingApi.generated(query.value)]);
  styles.value=s;generated.value=g.items;total.value=g.total;
}
async function loadGenerated(){
  const g=await writingApi.generated(query.value);
  generated.value=g.items;total.value=g.total;
}
function search(){query.value.page=1;loadGenerated();}
function chooseStyle(id:number){
  form.value.styleId=id;
  ElMessage.success('已选择该写作风格');
}
function formatDuration(ms:number){
  if(!ms)return '0 秒';
  const sec=Math.floor(ms/1000);
  if(sec<60)return sec+' 秒';
  const min=Math.floor(sec/60);
  if(min<60)return min+' 分 '+sec%60+' 秒';
  return Math.floor(min/60)+' 小时 '+min%60+' 分';
}
function formatEta(value?:string){return value?new Date(value).toLocaleString():'计算中';}
async function pollGeneration(jobId:number){
  try{
    const job=await jobApi.get(jobId);
    generationJob.value=job;
    generating.value=['Queued','Running','Stopping'].includes(job.status);
    if(generating.value){
      pollTimer=window.setTimeout(()=>pollGeneration(jobId),1200);
      return;
    }
    localStorage.removeItem('NovelSystem.GenerationJobId');
    if(job.status==='Completed'){
      ElMessage.success('新小说已生成并保存');
      query.value.page=1;
      await loadGenerated();
      activeTab.value='stories';
    }else if(job.status==='Failed'){
      ElMessage.error('小说生成失败，可在任务中枢查看错误并断点重试');
    }
  }catch{
    generating.value=false;
  }
}
async function generate(){
  if(!form.value.title.trim()){ElMessage.warning('请输入小说标题');return;}
  if(!form.value.targetWords||form.value.targetWords<=0){ElMessage.warning('目标字数必须大于 0');return;}
  if(!form.value.chapterCount||form.value.chapterCount<=0){ElMessage.warning('章节数必须大于 0');return;}
  generating.value=true;
  generationJob.value={status:'Queued',progress:0,checkpoint:0,totalSteps:form.value.chapterCount+1,elapsedMilliseconds:0};
  try{
    const result=await writingApi.generate(form.value);
    localStorage.setItem('NovelSystem.GenerationJobId',String(result.jobId));
    await pollGeneration(result.jobId);
  }catch{
    generating.value=false;
  }
}
async function publish(row:any){
  const novel=await writingApi.publish(row.id);
  ElMessage.success('已加入小说资产库');
  router.push('/novels/'+novel.id);
}
onMounted(async()=>{
  await load();
  const saved=Number(localStorage.getItem('NovelSystem.GenerationJobId'));
  if(Number.isFinite(saved)&&saved>0)await pollGeneration(saved);
});
onUnmounted(()=>{if(pollTimer)window.clearTimeout(pollTimer);});
</script>

<template>
<div class="writing-studio-page page-fill">
  <PageHeader eyebrow="AI 创作中心" title="AI 创作舱" description="创作配置、风格选择和生成作品分区展示，长篇小说采用大纲 + 逐章后台生成。"/>

  <section class="glass-panel content-card writing-tabs-shell">
    <el-tabs v-model="activeTab" class="writing-studio-tabs">
      <el-tab-pane name="create">
        <template #label><span class="writing-tab-label"><el-icon><EditPen/></el-icon>创作任务</span></template>
        <div class="writing-tab-scroll">
          <div class="creation-workbench">
            <div class="creation-main-panel">
              <div class="section-title">
                <span>创</span>
                <div><h3>小说生成任务</h3><p>所有小说正文强制使用中文生成</p></div>
              </div>

              <el-form label-position="top" class="creation-form">
                <el-form-item label="新小说标题"><el-input v-model="form.title"/></el-form-item>
                <div class="creation-grid">
                  <el-form-item label="题材"><el-input v-model="form.genre" placeholder="都市、玄幻、悬疑、科幻..."/></el-form-item>
                  <el-form-item label="目标字数"><el-input-number v-model="form.targetWords" :min="1" :step="1000" controls-position="right" class="full-width"/></el-form-item>
                  <el-form-item label="章节数"><el-input-number v-model="form.chapterCount" :min="1" :step="1" controls-position="right" class="full-width"/></el-form-item>
                  <el-form-item label="叙事视角">
                    <el-select v-model="form.pointOfView" class="full-width">
                      <el-option label="第三人称有限视角" value="第三人称有限视角"/>
                      <el-option label="第一人称" value="第一人称"/>
                      <el-option label="第三人称全知" value="第三人称全知"/>
                    </el-select>
                  </el-form-item>
                </div>
                <el-form-item label="整体基调"><el-input v-model="form.tone" placeholder="例如：轻松治愈 / 黑暗压抑 / 热血爽快"/></el-form-item>
                <el-form-item label="写作风格模型">
                  <el-select v-model="form.styleId" placeholder="选择已学习的风格，可不选" clearable class="full-width">
                    <el-option v-for="style in styles" :key="style.id" :label="style.name" :value="style.id"/>
                  </el-select>
                </el-form-item>
                <div v-if="selectedStyle" class="selected-style-tip">
                  <span>当前风格</span><b>{{selectedStyle.name}}</b>
                  <el-button text type="primary" @click="activeTab='styles'">切换风格</el-button>
                </div>
                <el-form-item label="创作指令"><el-input v-model="form.prompt" type="textarea" :rows="8"/></el-form-item>

                <div v-if="generationJob" class="generation-progress-panel" :class="{failed:generationJob.status==='Failed'}">
                  <div class="generation-progress-head">
                    <div><span class="eyebrow">生成进度</span><strong>{{generationStage}}</strong></div>
                    <b>{{generationJob.progress||0}}%</b>
                  </div>
                  <el-progress :percentage="generationJob.progress||0" :stroke-width="10"/>
                  <div class="generation-progress-meta">
                    <span>已完成章节 <b>{{completedChapters.toLocaleString()}}</b></span>
                    <span>总步骤 <b>{{generationJob.checkpoint||0}} / {{generationJob.totalSteps||0}}</b></span>
                    <span>已耗时 <b>{{generationElapsed}}</b></span>
                    <span>预计完成 <b>{{formatEta(generationJob.estimatedCompletionAt)}}</b></span>
                  </div>
                  <pre v-if="generationJob.status==='Failed'&&generationJob.error" class="generation-error">{{generationJob.error}}</pre>
                </div>

                <el-button class="neon-button full-width generation-button" :loading="generating" :disabled="generating" @click="generate">
                  {{generating?'生成任务执行中':'启动生成引擎'}}
                </el-button>
              </el-form>
            </div>

            <aside class="creation-guide-panel">
              <span class="eyebrow">创作说明</span>
              <h3>长篇生成流程</h3>
              <div class="creation-steps">
                <div><b>01</b><span>结合所选中文风格生成全书创作大纲</span></div>
                <div><b>02</b><span>根据章节数量逐章生成中文正文</span></div>
                <div><b>03</b><span>每章完成即落库，可实时查看与断点续作</span></div>
                <div><b>04</b><span>完成后可查看全文、下载或进入小说工作台</span></div>
              </div>
            </aside>
          </div>
        </div>
      </el-tab-pane>

      <el-tab-pane name="styles">
        <template #label><span class="writing-tab-label"><el-icon><Collection/></el-icon>写作风格</span></template>
        <div class="writing-tab-scroll">
          <div class="tab-section-head">
            <div><span class="eyebrow">风格模型</span><h3>选择已学习的中文写作风格</h3><p>风格模型只用于学习写法规律，不复制原小说人物与剧情。</p></div>
            <el-button class="ghost-button" @click="router.push('/styles')">管理写作风格</el-button>
          </div>
          <div v-if="styles.length" class="studio-style-grid">
            <article v-for="style in styles" :key="style.id" class="studio-style-card" :class="{active:form.styleId===style.id}" @click="chooseStyle(style.id)">
              <div class="style-index">{{String(style.id).padStart(2,'0')}}</div>
              <div><h4>{{style.name}}</h4><small>{{form.styleId===style.id?'当前已选择':'点击使用此风格'}}</small></div>
              <span v-if="form.styleId===style.id" class="selected-mark">已选</span>
            </article>
          </div>
          <EmptyState v-else title="尚未训练写作风格" description="请先从小说资产或写作风格页面创建风格模型。"/>
        </div>
      </el-tab-pane>

      <el-tab-pane name="stories">
        <template #label><span class="writing-tab-label"><el-icon><Document/></el-icon>生成作品 <i v-if="total">{{total}}</i></span></template>
        <div class="writing-stories-tab">
          <div class="tab-section-head stories-head">
            <div><span class="eyebrow">生成作品</span><h3>AI 小说作品库</h3></div>
            <div class="stories-search">
              <el-input v-model="query.keyword" clearable placeholder="搜索标题 / 创作指令 / 内容" @keyup.enter="search">
                <template #prefix><el-icon><Search/></el-icon></template>
              </el-input>
              <el-button class="neon-button" @click="search">查询</el-button>
            </div>
          </div>
          <div class="generated-results-region">
            <div v-if="generated.length" class="generated-grid writing-generated-grid">
              <article v-for="item in generated" :key="item.id" class="generated-card">
                <span class="generated-id">作品 {{item.id}}</span>
                <h3>{{item.title}}</h3>
                <small>{{item.genre||'未分类'}} · {{Number(item.chapterCount||1).toLocaleString()}} 章 · 目标 {{Number(item.targetWords||0).toLocaleString()}} 字</small>
                <p>{{item.content?item.content.slice(0,180)+'...':'生成中，正文将在每章完成后持续写入。'}}</p>
                <div class="generated-card-actions">
                  <el-button class="neon-button generated-view-button" @click="router.push('/writing/generated/'+item.id)">查看小说</el-button>
                  <el-link :href="writingApi.downloadUrl(item.id)" type="primary" :disabled="!item.content">下载 TXT</el-link>
                  <el-button class="ghost-button" :disabled="!item.content" @click="publish(item)">进入小说工作台</el-button>
                </div>
              </article>
            </div>
            <EmptyState v-else title="没有匹配的生成作品" description="调整查询条件或创建新的小说。"/>
          </div>
          <ListPager v-model:page="query.page" v-model:page-size="query.pageSize" :total="total" @change="loadGenerated"/>
        </div>
      </el-tab-pane>
    </el-tabs>
  </section>
</div>
</template>