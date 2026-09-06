<script setup lang="ts">
import {computed,onMounted,ref} from 'vue';
import {ElMessage,ElMessageBox} from 'element-plus';
import {MagicStick,Refresh,Check,EditPen,Upload,Delete,VideoPlay} from '@element-plus/icons-vue';
import {novelApi} from '../api/novels';
import {productionApi} from '../api/production';
import PageHeader from '../components/PageHeader.vue';

const novels=ref<any[]>([]),novelId=ref<number>();
const chapters=ref<any[]>([]),chapterId=ref<number>();
const cues=ref<any[]>([]),tracks=ref<any[]>([]);
const loading=ref(false),analyzingAll=ref(false),analysisIndex=ref(0),analysisTotal=ref(0);
const filter=ref({type:'',approved:undefined as boolean|undefined,applied:undefined as boolean|undefined});
const editVisible=ref(false),editCue=ref<any>(null);
const uploadVisible=ref(false),uploadCue=ref<any>(null),uploadFile=ref<File>();
const uploadForm=ref({name:'',volume:1,fadeInMs:500,fadeOutMs:500});

const selectedNovel=computed(()=>novels.value.find(x=>x.id===novelId.value));
const selectedChapter=computed(()=>chapters.value.find(x=>x.id===chapterId.value));
const visibleCues=computed(()=>cues.value.filter(x=>(!filter.value.type||x.type===filter.value.type)&&(filter.value.approved===undefined||x.approved===filter.value.approved)&&(filter.value.applied===undefined||x.applied===filter.value.applied)));
const summary=computed(()=>({
  total:cues.value.length,
  bgm:cues.value.filter(x=>x.type==='Bgm').length,
  sfx:cues.value.filter(x=>x.type==='Sfx').length,
  approved:cues.value.filter(x=>x.approved).length,
  applied:cues.value.filter(x=>x.applied).length
}));
const analysisPercent=computed(()=>analysisTotal.value?Math.round(analysisIndex.value*100/analysisTotal.value):0);

async function loadNovels(){const r=await novelApi.list({page:1,pageSize:100});novels.value=r.items||r;if(!novelId.value&&novels.value.length)novelId.value=novels.value[0].id;}
async function loadChapters(){if(!novelId.value)return;chapters.value=await productionApi.chapters(novelId.value);if(!chapterId.value||!chapters.value.some(x=>x.id===chapterId.value))chapterId.value=chapters.value[0]?.id;}
async function loadCues(){if(!novelId.value)return;cues.value=await productionApi.soundDesign(novelId.value,chapterId.value?{chapterId:chapterId.value}:{});}
async function loadTracks(){if(!novelId.value)return;let data=await productionApi.tracks(novelId.value);if(!(data.tracks||[]).length){await productionApi.ensureDefaultTracks(novelId.value);data=await productionApi.tracks(novelId.value);}tracks.value=data.tracks||[];}
async function novelChanged(){chapterId.value=undefined;await loadChapters();await Promise.all([loadCues(),loadTracks()]);}
async function chapterChanged(){await loadCues();}

async function analyzeCurrent(){if(!novelId.value||!chapterId.value)return;loading.value=true;try{const r=await productionApi.analyzeSoundDesignChapter(novelId.value,chapterId.value);ElMessage.success('本章声音设计完成，生成 '+r.created+' 条建议');await loadCues();}finally{loading.value=false;}}
async function analyzeAll(){if(!novelId.value||!chapters.value.length)return;await ElMessageBox.confirm('将逐章调用 AI 分析全书声音设计。已人工批准的 Cue 会保留；未批准建议会被当前章节的新分析替换。','全书声音设计',{type:'warning'});analyzingAll.value=true;analysisTotal.value=chapters.value.length;analysisIndex.value=0;try{for(let i=0;i<chapters.value.length;i++){analysisIndex.value=i;const c=chapters.value[i];chapterId.value=c.id;await productionApi.analyzeSoundDesignChapter(novelId.value,c.id);analysisIndex.value=i+1;}ElMessage.success('全书声音设计分析完成');await loadCues();}finally{analyzingAll.value=false;}}

function openEdit(row:any){editCue.value={...row};editVisible.value=true;}
async function saveCue(){const c=editCue.value;await productionApi.updateSoundCue(c.id,{type:c.type,scene:c.scene,mood:c.mood,intensity:c.intensity,keywords:c.keywords,description:c.description,startMs:c.startMs,endMs:c.endMs,approved:c.approved});ElMessage.success('声音设计建议已保存');editVisible.value=false;await loadCues();}
async function toggleApprove(row:any){await productionApi.approveSoundCue(row.id,!row.approved);await loadCues();}
async function approveAll(){if(!novelId.value)return;const r=await productionApi.approveAllSoundCues(novelId.value,chapterId.value);ElMessage.success('已批准 '+r.approved+' 条建议');await loadCues();}
async function applyApproved(){if(!novelId.value)return;const r=await productionApi.applySoundCues(novelId.value,chapterId.value);ElMessage.success('已将 '+r.applied+' 条建议应用为多轨编排 Cue');await Promise.all([loadCues(),loadTracks()]);}
async function removeCue(row:any){await ElMessageBox.confirm('确认删除该声音设计建议？','删除 Cue',{type:'warning'});await productionApi.removeSoundCue(row.id);await loadCues();}

function openUpload(row:any){if(!row.approved){ElMessage.warning('请先审核批准该 Cue');return;}uploadCue.value=row;uploadFile.value=undefined;uploadForm.value={name:row.keywords||row.scene||'声音素材',volume:Math.max(.2,Math.min(1,row.intensity/100)),fadeInMs:row.type==='Bgm'?1200:100,fadeOutMs:row.type==='Bgm'?1800:200};uploadVisible.value=true;}
function fileChanged(e:Event){uploadFile.value=(e.target as HTMLInputElement).files?.[0];if(uploadFile.value&&!uploadForm.value.name)uploadForm.value.name=uploadFile.value.name.replace(/\.[^.]+$/,'');}
async function uploadToCue(){if(!uploadCue.value||!uploadFile.value){ElMessage.warning('请选择音频素材');return;}await loadTracks();const track=tracks.value.find(x=>x.type===uploadCue.value.type);if(!track){ElMessage.error('未找到对应 BGM/SFX 轨道');return;}const fd=new FormData();fd.append('file',uploadFile.value);fd.append('name',uploadForm.value.name);fd.append('startMs',String(uploadCue.value.startMs));fd.append('volume',String(uploadForm.value.volume));fd.append('fadeInMs',String(uploadForm.value.fadeInMs));fd.append('fadeOutMs',String(uploadForm.value.fadeOutMs));loading.value=true;try{await productionApi.uploadClip(track.id,fd);if(!uploadCue.value.applied)await productionApi.applySoundCues(novelId.value!,chapterId.value);ElMessage.success('素材已按 Cue 时间点落入 '+(uploadCue.value.type==='Bgm'?'背景音乐':'环境音效')+'轨');uploadVisible.value=false;await loadCues();}finally{loading.value=false;}}

function formatTime(ms:number){const total=Math.max(0,Math.floor(Number(ms||0)/1000)),m=Math.floor(total/60),s=total%60;return `${String(m).padStart(2,'0')}:${String(s).padStart(2,'0')}`;}
function cueWidth(row:any){const duration=Math.max(1000,row.endMs-row.startMs);return Math.max(8,Math.min(100,duration/60000*100));}
function typeLabel(v:string){return v==='Bgm'?'背景音乐':'环境音效';}

onMounted(async()=>{await loadNovels();if(novelId.value){await loadChapters();await Promise.all([loadCues(),loadTracks()]);}});
</script>

<template>
<div class="page-fill sound-director-page">
  <PageHeader eyebrow="AI SOUND DIRECTOR" title="AI 声音导演" description="按章节分析场景、情绪与声音事件，生成可审核的 BGM / SFX 编排方案，再把真实素材落到多轨时间轴。">
    <el-select v-model="novelId" filterable class="director-novel-select" placeholder="选择小说" @change="novelChanged"><el-option v-for="n in novels" :key="n.id" :label="n.title" :value="n.id"/></el-select>
    <el-button class="ghost-button" @click="loadCues"><el-icon><Refresh/></el-icon>刷新</el-button>
  </PageHeader>

  <section class="glass-panel content-card director-shell" v-loading="loading">
    <div class="director-toolbar">
      <div class="director-project"><span>当前项目</span><b>{{selectedNovel?.title||'—'}}</b><small>{{selectedChapter?.title||'请选择章节'}}</small></div>
      <el-select v-model="chapterId" filterable class="director-chapter-select" placeholder="选择章节" @change="chapterChanged"><el-option v-for="c in chapters" :key="c.id" :label="c.chapterOrder+'. '+c.title" :value="c.id"/></el-select>
      <el-button class="ghost-button" :disabled="analyzingAll" @click="analyzeCurrent"><el-icon><MagicStick/></el-icon>分析当前章节</el-button>
      <el-button class="neon-button" :loading="analyzingAll" @click="analyzeAll"><el-icon><MagicStick/></el-icon>逐章分析全书</el-button>
    </div>

    <div v-if="analyzingAll" class="director-analysis-progress">
      <div><span>全书声音设计分析</span><b>{{analysisIndex}} / {{analysisTotal}}</b></div>
      <el-progress :percentage="analysisPercent" :stroke-width="9"/>
    </div>

    <div class="director-metrics">
      <div><span>本章建议</span><b>{{summary.total}}</b></div><div><span>BGM</span><b>{{summary.bgm}}</b></div><div><span>SFX</span><b>{{summary.sfx}}</b></div><div><span>已批准</span><b>{{summary.approved}}</b></div><div><span>已应用</span><b>{{summary.applied}}</b></div>
    </div>

    <div class="director-actions">
      <div class="director-filters">
        <el-select v-model="filter.type" clearable placeholder="全部类型"><el-option label="背景音乐" value="Bgm"/><el-option label="环境音效" value="Sfx"/></el-select>
        <el-select v-model="filter.approved" clearable placeholder="全部审核状态"><el-option label="已批准" :value="true"/><el-option label="待审核" :value="false"/></el-select>
        <el-select v-model="filter.applied" clearable placeholder="全部应用状态"><el-option label="已应用" :value="true"/><el-option label="未应用" :value="false"/></el-select>
      </div>
      <div><el-button class="ghost-button" @click="approveAll"><el-icon><Check/></el-icon>批准本章全部</el-button><el-button class="neon-button" @click="applyApproved">应用已批准 Cue</el-button></div>
    </div>

    <div class="director-cue-region">
      <article v-for="cue in visibleCues" :key="cue.id" class="director-cue-card" :class="[cue.type.toLowerCase(),{approved:cue.approved,applied:cue.applied}]">
        <div class="cue-type"><span>{{typeLabel(cue.type)}}</span><b>#{{cue.scriptOrder||'—'}}</b></div>
        <div class="cue-main">
          <div class="cue-title"><h3>{{cue.scene||'场景声音设计'}}</h3><span>{{cue.mood||'未指定情绪'}}</span></div>
          <p>{{cue.description}}</p>
          <div class="cue-tags"><i v-for="k in String(cue.keywords||'').split(/[,，]/).filter(Boolean)" :key="k">{{k}}</i></div>
          <div class="cue-time"><b>{{formatTime(cue.startMs)}} → {{formatTime(cue.endMs)}}</b><div><i :style="{width:cueWidth(cue)+'%'}"></i></div><span>强度 {{cue.intensity}}%</span></div>
        </div>
        <div class="cue-status"><span :class="cue.approved?'ok':'pending'">{{cue.approved?'已审核':'待审核'}}</span><span v-if="cue.applied" class="applied-mark">已应用</span></div>
        <div class="cue-ops">
          <el-button text type="primary" @click="toggleApprove(cue)"><el-icon><Check/></el-icon>{{cue.approved?'取消批准':'批准'}}</el-button>
          <el-button text @click="openEdit(cue)"><el-icon><EditPen/></el-icon>编辑</el-button>
          <el-button text @click="openUpload(cue)"><el-icon><Upload/></el-icon>上传素材</el-button>
          <el-button text type="danger" @click="removeCue(cue)"><el-icon><Delete/></el-icon>删除</el-button>
        </div>
      </article>
      <div v-if="!visibleCues.length" class="director-empty"><el-icon><VideoPlay/></el-icon><b>当前章节还没有声音设计方案</b><span>点击“分析当前章节”，AI 会根据脚本顺序、场景和情绪生成中文 BGM / SFX 编排建议。</span></div>
    </div>
  </section>

  <el-dialog v-model="editVisible" title="编辑声音设计 Cue" width="620px" class="theme-dialog">
    <el-form v-if="editCue" label-position="top">
      <div class="director-form-grid"><el-form-item label="类型"><el-select v-model="editCue.type"><el-option label="背景音乐" value="Bgm"/><el-option label="环境音效" value="Sfx"/></el-select></el-form-item><el-form-item label="强度"><el-slider v-model="editCue.intensity" :min="0" :max="100"/></el-form-item></div>
      <div class="director-form-grid"><el-form-item label="场景"><el-input v-model="editCue.scene"/></el-form-item><el-form-item label="情绪"><el-input v-model="editCue.mood"/></el-form-item></div>
      <el-form-item label="素材关键词"><el-input v-model="editCue.keywords"/></el-form-item>
      <el-form-item label="制作说明"><el-input v-model="editCue.description" type="textarea" :rows="4"/></el-form-item>
      <div class="director-form-grid"><el-form-item label="开始毫秒"><el-input-number v-model="editCue.startMs" :min="0" class="full-width"/></el-form-item><el-form-item label="结束毫秒"><el-input-number v-model="editCue.endMs" :min="0" class="full-width"/></el-form-item></div>
      <el-form-item><el-checkbox v-model="editCue.approved">保存后标记为已审核批准</el-checkbox></el-form-item>
    </el-form>
    <template #footer><el-button @click="editVisible=false">取消</el-button><el-button class="neon-button" @click="saveCue">保存 Cue</el-button></template>
  </el-dialog>

  <el-dialog v-model="uploadVisible" title="按声音设计 Cue 上传素材" width="620px" class="theme-dialog">
    <div v-if="uploadCue" class="cue-upload-summary"><span>{{typeLabel(uploadCue.type)}}</span><b>{{uploadCue.scene}} · {{uploadCue.mood}}</b><small>落轨时间：{{formatTime(uploadCue.startMs)}} · 建议关键词：{{uploadCue.keywords}}</small></div>
    <el-form label-position="top">
      <el-form-item label="音频文件"><input type="file" accept="audio/*" @change="fileChanged"/></el-form-item>
      <el-form-item label="素材名称"><el-input v-model="uploadForm.name"/></el-form-item>
      <div class="director-form-grid"><el-form-item label="音量"><el-input-number v-model="uploadForm.volume" :min="0" :max="4" :step="0.05" class="full-width"/></el-form-item><el-form-item label="淡入(ms)"><el-input-number v-model="uploadForm.fadeInMs" :min="0" class="full-width"/></el-form-item></div>
      <el-form-item label="淡出(ms)"><el-input-number v-model="uploadForm.fadeOutMs" :min="0" class="full-width"/></el-form-item>
    </el-form>
    <template #footer><el-button @click="uploadVisible=false">取消</el-button><el-button class="neon-button" @click="uploadToCue"><el-icon><Upload/></el-icon>上传并落轨</el-button></template>
  </el-dialog>
</div>
</template>
