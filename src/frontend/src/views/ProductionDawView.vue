<script setup lang="ts">
import {computed,nextTick,onMounted,onUnmounted,ref} from 'vue';
import {ElMessage,ElMessageBox} from 'element-plus';
import {Headset,Refresh,Upload,Plus,Delete,VideoPlay,Download,Operation} from '@element-plus/icons-vue';
import {novelApi} from '../api/novels';
import {productionApi} from '../api/production';
import {audioApi} from '../api/audio';
import PageHeader from '../components/PageHeader.vue';

const novels=ref<any[]>([]),novelId=ref<number>();
const activeTab=ref('sync'),loading=ref(false);
const chapters=ref<any[]>([]),chapterId=ref<number>();
const chapterData=ref<any>(null),currentMs=ref(0),activeScriptId=ref<number>();
const masterAudio=ref<HTMLAudioElement>(),sourcePanel=ref<HTMLElement>();
let autoChapterLoading=false;

const trackData=ref<any>({voiceTrack:{endMs:0},tracks:[],mixedExists:false});
const trackWindowStartMs=ref(0),trackWindowDurationMs=ref(10*60*1000);
const trackDialog=ref(false),trackForm=ref<any>({id:null,name:'',type:'Bgm',volume:1,isMuted:false});
const clipDialog=ref(false),clipTrack=ref<any>(null),clipFile=ref<File>(),clipForm=ref<any>({name:'',startMs:0,volume:1,fadeInMs:0,fadeOutMs:0});
const clipEditDialog=ref(false),clipEdit=ref<any>(null);

const selectedNovel=computed(()=>novels.value.find(x=>x.id===novelId.value));
const activeScript=computed(()=>chapterData.value?.scripts?.find((x:any)=>x.id===activeScriptId.value));
const sourceSegments=computed(()=>{
  const data=chapterData.value;if(!data)return [];
  const text=String(data.sourceText||'');
  const scripts=(data.scripts||[]).filter((x:any)=>x.localSourceStart>=0&&x.localSourceEnd>x.localSourceStart).sort((a:any,b:any)=>a.localSourceStart-b.localSourceStart);
  const result:any[]=[];let cursor=0;
  for(const s of scripts){
    const start=Math.max(cursor,Math.min(text.length,s.localSourceStart));
    const end=Math.max(start,Math.min(text.length,s.localSourceEnd));
    if(start>cursor)result.push({type:'plain',text:text.slice(cursor,start),key:'p'+cursor});
    result.push({type:'script',text:text.slice(start,end)||s.text,script:s,key:'s'+s.id});
    cursor=end;
  }
  if(cursor<text.length)result.push({type:'plain',text:text.slice(cursor),key:'p'+cursor});
  return result;
});
const totalTrackDuration=computed(()=>{
  const clipEnd=Math.max(0,...(trackData.value.tracks||[]).flatMap((t:any)=>(t.clips||[]).map((c:any)=>Number(c.endMs||0))));
  return Math.max(Number(trackData.value.voiceTrack?.endMs||0),clipEnd,1);
});
const visibleTracks=computed(()=>trackData.value.tracks||[]);
const voiceBlocks=computed(()=>{
  const all=chapterData.value?.scripts||[];
  return all.filter((x:any)=>x.audioStartMs!=null&&x.audioEndMs!=null&&x.audioEndMs>=trackWindowStartMs.value&&x.audioStartMs<=trackWindowStartMs.value+trackWindowDurationMs.value);
});

async function loadNovels(){const r=await novelApi.list({page:1,pageSize:100});novels.value=r.items||r;if(!novelId.value&&novels.value.length)novelId.value=novels.value[0].id;}
async function loadSyncChapters(){if(!novelId.value)return;chapters.value=await productionApi.syncChapters(novelId.value);if(!chapterId.value||!chapters.value.some(x=>x.id===chapterId.value))chapterId.value=chapters.value[0]?.id;if(chapterId.value)await loadChapter();}
async function loadChapter(){if(!novelId.value||!chapterId.value)return;chapterData.value=await productionApi.syncChapter(novelId.value,chapterId.value);const first=chapterData.value.scripts?.find((x:any)=>x.audioStartMs!=null);if(!activeScriptId.value&&first)activeScriptId.value=first.id;}
async function novelChanged(){chapterId.value=undefined;currentMs.value=0;activeScriptId.value=undefined;await Promise.all([loadSyncChapters(),loadTracks()]);}
async function chapterChanged(){await loadChapter();const first=chapterData.value?.scripts?.find((x:any)=>x.audioStartMs!=null);if(first)seekScript(first,false);}

function onTimeUpdate(){
  const ms=Math.round((masterAudio.value?.currentTime||0)*1000);currentMs.value=ms;
  const current=chapterData.value?.scripts?.find((x:any)=>x.audioStartMs!=null&&x.audioEndMs!=null&&ms>=x.audioStartMs&&ms<x.audioEndMs);
  if(current&&current.id!==activeScriptId.value){activeScriptId.value=current.id;nextTick(scrollActiveSource);}
  const chapter=chapters.value.find(x=>x.audioStartMs!=null&&x.audioEndMs!=null&&ms>=x.audioStartMs&&ms<x.audioEndMs);
  if(chapter&&chapter.id!==chapterId.value&&!autoChapterLoading){autoChapterLoading=true;chapterId.value=chapter.id;loadChapter().finally(()=>{autoChapterLoading=false;nextTick(scrollActiveSource);});}
}
function seekScript(script:any,play=true){if(script.audioStartMs==null){ElMessage.warning('该脚本尚未建立音频时间轴');return;}activeScriptId.value=script.id;currentMs.value=script.audioStartMs;if(masterAudio.value){masterAudio.value.currentTime=script.audioStartMs/1000;if(play)masterAudio.value.play().catch(()=>{});}nextTick(scrollActiveSource);}
function scrollActiveSource(){sourcePanel.value?.querySelector('.sync-source-script.active')?.scrollIntoView({block:'center',behavior:'smooth'});}
function formatTime(ms:number|null|undefined){if(ms==null)return '—';const total=Math.floor(ms/1000),h=Math.floor(total/3600),m=Math.floor(total%3600/60),s=total%60;return h>0?`${String(h).padStart(2,'0')}:${String(m).padStart(2,'0')}:${String(s).padStart(2,'0')}`:`${String(m).padStart(2,'0')}:${String(s).padStart(2,'0')}`;}

async function loadTracks(){if(!novelId.value)return;trackData.value=await productionApi.tracks(novelId.value);if(!(trackData.value.tracks||[]).length){await productionApi.ensureDefaultTracks(novelId.value);trackData.value=await productionApi.tracks(novelId.value);}if(trackWindowStartMs.value>totalTrackDuration.value)trackWindowStartMs.value=0;}
function openAddTrack(){trackForm.value={id:null,name:'',type:'Bgm',volume:1,isMuted:false};trackDialog.value=true;}
function openEditTrack(t:any){trackForm.value={id:t.id,name:t.name,type:t.type,volume:t.volume,isMuted:t.isMuted};trackDialog.value=true;}
async function saveTrack(){const f=trackForm.value;if(f.id)await productionApi.updateTrack(f.id,f);else await productionApi.createTrack(novelId.value!,f);trackDialog.value=false;await loadTracks();}
async function removeTrack(t:any){await ElMessageBox.confirm('删除轨道会同时删除该轨全部素材，确认继续？','删除轨道',{type:'warning'});await productionApi.removeTrack(t.id);await loadTracks();}
async function toggleMute(t:any){await productionApi.updateTrack(t.id,{name:t.name,type:t.type,volume:t.volume,isMuted:!t.isMuted});await loadTracks();}

function openUpload(track:any){clipTrack.value=track;clipFile.value=undefined;clipForm.value={name:'',startMs:currentMs.value,volume:1,fadeInMs:500,fadeOutMs:500};clipDialog.value=true;}
function fileChanged(e:Event){clipFile.value=(e.target as HTMLInputElement).files?.[0];if(clipFile.value&&!clipForm.value.name)clipForm.value.name=clipFile.value.name.replace(/\.[^.]+$/,'');}
async function uploadClip(){if(!clipTrack.value||!clipFile.value){ElMessage.warning('请选择音频文件');return;}const fd=new FormData();fd.append('file',clipFile.value);for(const k of ['name','startMs','volume','fadeInMs','fadeOutMs'])fd.append(k,String(clipForm.value[k]??''));loading.value=true;try{await productionApi.uploadClip(clipTrack.value.id,fd);ElMessage.success('音频素材已加入轨道');clipDialog.value=false;await loadTracks();}finally{loading.value=false;}}
function openClipEdit(c:any){clipEdit.value={...c};clipEditDialog.value=true;}
async function saveClip(){await productionApi.updateClip(clipEdit.value.id,{name:clipEdit.value.name,startMs:clipEdit.value.startMs,volume:clipEdit.value.volume,fadeInMs:clipEdit.value.fadeInMs,fadeOutMs:clipEdit.value.fadeOutMs});clipEditDialog.value=false;await loadTracks();}
async function removeClip(c:any){await ElMessageBox.confirm('确认删除素材“'+c.name+'”？','删除素材',{type:'warning'});await productionApi.removeClip(c.id);await loadTracks();}
function clipStyle(c:any){const start=trackWindowStartMs.value,end=start+trackWindowDurationMs.value;const left=(Math.max(start,c.startMs)-start)/trackWindowDurationMs.value*100;const right=(Math.min(end,c.endMs)-start)/trackWindowDurationMs.value*100;return {left:Math.max(0,left)+'%',width:Math.max(.6,right-left)+'%'};}
function voiceStyle(s:any){return clipStyle({startMs:s.audioStartMs,endMs:s.audioEndMs});}
function inWindow(c:any){return c.endMs>=trackWindowStartMs.value&&c.startMs<=trackWindowStartMs.value+trackWindowDurationMs.value;}
function centerWindowAt(ms:number){trackWindowStartMs.value=Math.max(0,Math.round(ms-trackWindowDurationMs.value/2));}
function windowLabel(){return `${formatTime(trackWindowStartMs.value)} — ${formatTime(trackWindowStartMs.value+trackWindowDurationMs.value)}`;}
async function mixTracks(){loading.value=true;try{await productionApi.mixTracks(novelId.value!);ElMessage.success('多轨混音版 MP3 已生成');await loadTracks();}finally{loading.value=false;}}

onMounted(async()=>{await loadNovels();if(novelId.value)await Promise.all([loadSyncChapters(),loadTracks()]);});
onUnmounted(()=>{masterAudio.value?.pause();});
</script>

<template>
<div class="page-fill daw-page">
  <PageHeader eyebrow="READ · LISTEN · MIX" title="读听同步与多轨制作" description="第三阶段：正文、脚本、整书音频联动定位，并在对白主轨之上编排 BGM 与环境音效。">
    <el-select v-model="novelId" filterable class="daw-novel-select" placeholder="选择小说" @change="novelChanged"><el-option v-for="n in novels" :key="n.id" :label="n.title" :value="n.id"/></el-select>
    <el-button class="ghost-button" :loading="loading" @click="activeTab==='sync'?loadSyncChapters():loadTracks()"><el-icon><Refresh/></el-icon>刷新</el-button>
  </PageHeader>

  <section class="glass-panel content-card daw-shell" v-loading="loading">
    <el-tabs v-model="activeTab" class="daw-tabs">
      <el-tab-pane name="sync" label="读听同步">
        <div class="sync-workbench">
          <div class="sync-toolbar">
            <el-select v-model="chapterId" filterable class="sync-chapter-select" placeholder="选择章节" @change="chapterChanged"><el-option v-for="c in chapters" :key="c.id" :label="c.chapterOrder+'. '+c.title" :value="c.id"/></el-select>
            <div class="sync-now"><span>当前播放</span><b>{{formatTime(currentMs)}}</b><small v-if="activeScript">{{activeScript.speaker}} · #{{activeScript.order}}</small></div>
            <audio v-if="novelId&&chapterData?.mergedExists" ref="masterAudio" controls preload="metadata" class="sync-master-player" :src="audioApi.novelPlayUrl(novelId)" @timeupdate="onTimeUpdate" @seeked="onTimeUpdate"></audio>
            <span v-else class="sync-no-audio">请先合并完整有声书 MP3</span>
          </div>

          <div class="sync-columns">
            <article ref="sourcePanel" class="sync-source-panel">
              <header><span>小说原文</span><b>{{chapterData?.chapter?.title||'—'}}</b></header>
              <div class="sync-source-text">
                <template v-for="seg in sourceSegments" :key="seg.key">
                  <span v-if="seg.type==='plain'">{{seg.text}}</span>
                  <mark v-else class="sync-source-script" :class="{active:seg.script.id===activeScriptId}" @click="seekScript(seg.script)">{{seg.text}}</mark>
                </template>
              </div>
            </article>

            <aside class="sync-script-panel">
              <header><span>TTS 脚本</span><b>{{chapterData?.scripts?.length||0}} 段</b></header>
              <div class="sync-script-list">
                <button v-for="s in chapterData?.scripts||[]" :key="s.id" :class="{active:s.id===activeScriptId}" @click="seekScript(s)">
                  <div><b>#{{s.order}} · {{s.speaker}}</b><span>{{formatTime(s.audioStartMs)}} → {{formatTime(s.audioEndMs)}}</span></div>
                  <p>{{s.text}}</p>
                </button>
              </div>
            </aside>
          </div>
        </div>
      </el-tab-pane>

      <el-tab-pane name="tracks" label="多轨制作">
        <div class="track-workbench">
          <div class="track-toolbar">
            <div><span class="eyebrow">MULTI-TRACK</span><h3>{{selectedNovel?.title||'当前项目'}}</h3><small>{{windowLabel()}}</small></div>
            <div class="track-toolbar-actions">
              <el-select v-model="trackWindowDurationMs" style="width:130px"><el-option label="1分钟窗口" :value="60000"/><el-option label="5分钟窗口" :value="300000"/><el-option label="10分钟窗口" :value="600000"/><el-option label="30分钟窗口" :value="1800000"/></el-select>
              <el-button class="ghost-button" @click="centerWindowAt(currentMs)">定位播放器</el-button>
              <el-button class="ghost-button" @click="openAddTrack"><el-icon><Plus/></el-icon>新增轨道</el-button>
              <el-button class="neon-button" @click="mixTracks"><el-icon><Operation/></el-icon>混音导出</el-button>
            </div>
          </div>

          <div class="track-ruler"><span v-for="i in 6" :key="i" :style="{left:((i-1)*20)+'%'}">{{formatTime(trackWindowStartMs+(i-1)*trackWindowDurationMs/5)}}</span></div>
          <div class="track-scroll">
            <div class="track-row voice-track-row">
              <div class="track-head"><b>对白主轨</b><span>VOICE</span></div>
              <div class="track-lane">
                <button v-for="s in voiceBlocks" :key="s.id" class="voice-block" :class="{active:s.id===activeScriptId}" :style="voiceStyle(s)" @click="seekScript(s);centerWindowAt(s.audioStartMs)">{{s.speaker}}</button>
              </div>
            </div>

            <div v-for="t in visibleTracks" :key="t.id" class="track-row" :class="{muted:t.isMuted}">
              <div class="track-head">
                <div><b>{{t.name}}</b><span>{{t.type==='Bgm'?'BGM':'SFX'}} · {{Math.round(t.volume*100)}}%</span></div>
                <div><el-button text @click="toggleMute(t)">{{t.isMuted?'取消静音':'静音'}}</el-button><el-button text @click="openEditTrack(t)">设置</el-button><el-button text type="danger" @click="removeTrack(t)"><el-icon><Delete/></el-icon></el-button></div>
              </div>
              <div class="track-lane" @dblclick="openUpload(t)">
                <button v-for="c in (t.clips||[]).filter(inWindow)" :key="c.id" class="track-clip" :class="t.type==='Bgm'?'bgm':'sfx'" :style="clipStyle(c)" @click.stop="openClipEdit(c)">
                  <b>{{c.name}}</b><small>{{formatTime(c.startMs)}} · {{Math.round(c.volume*100)}}%</small>
                </button>
                <button class="track-add-clip" @click.stop="openUpload(t)"><el-icon><Upload/></el-icon>添加素材</button>
              </div>
            </div>
          </div>

          <div class="track-footer">
            <el-slider v-model="trackWindowStartMs" :min="0" :max="Math.max(0,totalTrackDuration-trackWindowDurationMs)" :step="1000" :show-tooltip="false"/>
            <audio v-if="trackData.mixedExists&&novelId" controls preload="metadata" :src="productionApi.mixedPlayUrl(novelId)"></audio>
            <el-link v-if="trackData.mixedExists&&novelId" :href="productionApi.mixedDownloadUrl(novelId)" type="primary"><el-icon><Download/></el-icon>下载混音版</el-link>
          </div>
        </div>
      </el-tab-pane>
    </el-tabs>
  </section>

  <el-dialog v-model="trackDialog" title="轨道设置" width="520px" class="theme-dialog"><el-form label-position="top"><el-form-item label="轨道名称"><el-input v-model="trackForm.name"/></el-form-item><div class="voice-form-grid"><el-form-item label="类型"><el-select v-model="trackForm.type"><el-option label="背景音乐 BGM" value="Bgm"/><el-option label="环境/音效 SFX" value="Sfx"/></el-select></el-form-item><el-form-item label="轨道音量"><el-slider v-model="trackForm.volume" :min="0" :max="2" :step="0.05"/></el-form-item></div><el-form-item><el-checkbox v-model="trackForm.isMuted">静音该轨道</el-checkbox></el-form-item></el-form><template #footer><el-button @click="trackDialog=false">取消</el-button><el-button class="neon-button" @click="saveTrack">保存</el-button></template></el-dialog>

  <el-dialog v-model="clipDialog" title="添加音频素材" width="620px" class="theme-dialog"><el-form label-position="top"><el-form-item label="音频文件"><input type="file" accept="audio/*,.wav,.mp3,.m4a,.aac,.flac,.ogg" @change="fileChanged"/></el-form-item><el-form-item label="素材名称"><el-input v-model="clipForm.name"/></el-form-item><div class="voice-form-grid"><el-form-item label="开始时间（毫秒）"><el-input-number v-model="clipForm.startMs" :min="0" class="full-width"/></el-form-item><el-form-item label="素材音量"><el-slider v-model="clipForm.volume" :min="0" :max="2" :step="0.05"/></el-form-item><el-form-item label="淡入 ms"><el-input-number v-model="clipForm.fadeInMs" :min="0"/></el-form-item><el-form-item label="淡出 ms"><el-input-number v-model="clipForm.fadeOutMs" :min="0"/></el-form-item></div></el-form><template #footer><el-button @click="clipDialog=false">取消</el-button><el-button class="neon-button" :loading="loading" @click="uploadClip">上传并加入轨道</el-button></template></el-dialog>

  <el-dialog v-model="clipEditDialog" title="编辑轨道素材" width="620px" class="theme-dialog"><el-form v-if="clipEdit" label-position="top"><el-form-item label="素材名称"><el-input v-model="clipEdit.name"/></el-form-item><audio controls preload="metadata" class="clip-edit-player" :src="productionApi.clipPlayUrl(clipEdit.id)"></audio><div class="voice-form-grid"><el-form-item label="开始时间（毫秒）"><el-input-number v-model="clipEdit.startMs" :min="0"/></el-form-item><el-form-item label="素材音量"><el-slider v-model="clipEdit.volume" :min="0" :max="2" :step="0.05"/></el-form-item><el-form-item label="淡入 ms"><el-input-number v-model="clipEdit.fadeInMs" :min="0"/></el-form-item><el-form-item label="淡出 ms"><el-input-number v-model="clipEdit.fadeOutMs" :min="0"/></el-form-item></div></el-form><template #footer><el-button type="danger" text @click="removeClip(clipEdit);clipEditDialog=false">删除素材</el-button><el-button @click="clipEditDialog=false">取消</el-button><el-button class="neon-button" @click="saveClip">保存</el-button></template></el-dialog>
</div>
</template>
