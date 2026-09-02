<script setup lang="ts">
import {computed,onMounted,onUnmounted,ref} from 'vue';
import {useRoute,useRouter} from 'vue-router';
import {ElMessage,ElMessageBox} from 'element-plus';
import {RefreshRight,Delete,Download,Connection,Search} from '@element-plus/icons-vue';
import {novelApi} from '../api/novels';
import {voiceApi} from '../api/voices';
import {audioApi} from '../api/audio';
import PageHeader from '../components/PageHeader.vue';
import StatusBadge from '../components/StatusBadge.vue';
import ListPager from '../components/ListPager.vue';

const route=useRoute(),router=useRouter(),id=Number(route.params.id);
const detail=ref<any>(null),voiceProfiles=ref<any[]>([]);
const activeTab=ref('overview'),working=ref(false),matchingVoices=ref(false),deduplicating=ref(false);

const characters=ref<any[]>([]),characterTotal=ref(0),charactersLoaded=ref(false),voicesLoaded=ref(false);
const characterQuery=ref({page:1,pageSize:24,keyword:''});

const scripts=ref<any[]>([]),scriptTotal=ref(0),scriptSpeakers=ref<string[]>([]),scriptsLoaded=ref(false);
const scriptQuery=ref({page:1,pageSize:20,keyword:'',speaker:'',status:''});

const audioInfo=ref<any>({segments:[],total:0,completed:0,filteredTotal:0,mergedExists:false,speakers:[]});
const audioQuery=ref({page:1,pageSize:20,keyword:'',speaker:'',status:''});
const audioLoaded=ref(false);
let audioTimer:number|undefined;

const characterCount=computed(()=>detail.value?.characterCount||0);
const scriptCount=computed(()=>detail.value?.scriptCount||0);

async function loadBase(){
  detail.value=await novelApi.detail(id);
  if(!audioLoaded.value){
    audioInfo.value.total=detail.value.scriptCount||0;
    audioInfo.value.completed=detail.value.audioCompleted||0;
  }
}
async function ensureVoices(){
  if(voicesLoaded.value)return;
  voiceProfiles.value=await voiceApi.options();
  voicesLoaded.value=true;
}
async function loadCharacters(){
  await ensureVoices();
  const r=await novelApi.characters(id,characterQuery.value);
  characters.value=r.items;characterTotal.value=r.total;charactersLoaded.value=true;
}
async function loadScripts(){
  const r=await novelApi.scripts(id,scriptQuery.value);
  scripts.value=r.items;scriptTotal.value=r.total;scriptSpeakers.value=r.speakers||[];scriptsLoaded.value=true;
}
async function loadAudio(){
  audioInfo.value=await audioApi.list(id,audioQuery.value);audioLoaded.value=true;
}
async function switchTab(tab:string){
  activeTab.value=tab;
  if(tab==='characters'&&!charactersLoaded.value)await loadCharacters();
  if(tab==='scripts'&&!scriptsLoaded.value)await loadScripts();
  if(tab==='audio'&&!audioLoaded.value)await loadAudio();
}
function searchCharacters(){characterQuery.value.page=1;loadCharacters();}
function resetCharacters(){characterQuery.value={page:1,pageSize:24,keyword:''};loadCharacters();}
function searchScripts(){scriptQuery.value.page=1;loadScripts();}
function resetScripts(){scriptQuery.value={page:1,pageSize:20,keyword:'',speaker:'',status:''};loadScripts();}
function searchAudio(){audioQuery.value.page=1;loadAudio();}
function resetAudio(){audioQuery.value={page:1,pageSize:20,keyword:'',speaker:'',status:''};loadAudio();}

async function analyze(){working.value=true;try{await novelApi.analyze(id);ElMessage.success('解析任务已进入任务中枢等待执行');router.push('/jobs');}finally{working.value=false;}}
async function generateAllAudio(){working.value=true;try{await novelApi.generateAudio(id);ElMessage.success('整本有声书任务已进入任务队列');await switchTab('audio');}finally{working.value=false;}}
async function saveCharacter(row:any){await novelApi.updateCharacter(row.id,row);ElMessage.success(row.name+' 的音色档案已保存');}
async function saveNarratorVoice(){await novelApi.updateNarratorVoice(id,detail.value.novel.narratorVoiceProfileId??null);ElMessage.success('旁白音色已保存');}
async function autoMatchVoices(){
  matchingVoices.value=true;
  try{
    const r=await novelApi.autoMatchVoices(id);
    ElMessage.success('已自动匹配 '+r.matched+' 个角色音色');
    await Promise.all([loadBase(),loadCharacters()]);
  }finally{matchingVoices.value=false;}
}
async function deduplicateCharacters(){
  await ElMessageBox.confirm('AI 将识别同一人物的本名、昵称和称谓，并同步修正全部脚本角色。建议在解析完成后执行。','人物角色去重',{type:'warning'});
  deduplicating.value=true;
  try{
    const r=await novelApi.deduplicateCharacters(id);
    ElMessage.success('已合并 '+r.mergedGroups+' 组重复人物，移除 '+r.removedCharacters+' 个别名角色');
    await Promise.all([loadBase(),loadCharacters()]);
    if(scriptsLoaded.value)await loadScripts();
    if(audioLoaded.value)await loadAudio();
  }finally{deduplicating.value=false;}
}
async function generateSegment(row:any){await audioApi.generateSegment(row.id);ElMessage.success('片段生成任务已创建并进入队列');await loadAudio();}
async function removeSegment(row:any){await ElMessageBox.confirm('确认删除第 '+row.order+' 条已生成音频？脚本文本不会删除。','删除音频片段',{type:'warning'});await audioApi.removeSegment(row.id);ElMessage.success('音频片段已删除');await loadAudio();}
async function mergeAudio(){await audioApi.merge(id);ElMessage.success('音频合并任务已进入任务队列');}

onMounted(async()=>{await loadBase();audioTimer=window.setInterval(()=>{if(activeTab.value==='audio'&&audioLoaded.value)loadAudio();},4000);});
onUnmounted(()=>audioTimer&&clearInterval(audioTimer));
</script>

<template>
<div v-if="detail" class="page-fill novel-workspace-page">
  <PageHeader eyebrow="NOVEL WORKSPACE" :title="detail.novel.title" description="工作台采用按需加载：角色、脚本与音频只在进入对应功能页时加载。">
    <el-button class="ghost-button" :loading="working" @click="analyze">AI 重新解析</el-button>
    <el-button class="neon-button" :loading="working" @click="generateAllAudio">生成完整音频</el-button>
  </PageHeader>

  <div class="workspace-stats">
    <div class="mini-stat"><span>STATUS</span><StatusBadge :status="detail.novel.status"/></div>
    <div class="mini-stat"><span>CHARACTERS</span><b>{{characterCount}}</b></div>
    <div class="mini-stat"><span>SCRIPT LINES</span><b>{{scriptCount}}</b></div>
    <div class="mini-stat"><span>AUDIO</span><b>{{audioInfo.completed}} / {{audioInfo.total}}</b></div>
  </div>

  <section class="glass-panel content-card workspace-main-card">
    <div class="segmented">
      <button :class="{active:activeTab==='overview'}" @click="switchTab('overview')">小说概览</button>
      <button :class="{active:activeTab==='characters'}" @click="switchTab('characters')">角色声纹矩阵</button>
      <button :class="{active:activeTab==='scripts'}" @click="switchTab('scripts')">解析脚本流</button>
      <button :class="{active:activeTab==='audio'}" @click="switchTab('audio')">音频资产</button>
    </div>

    <div v-if="activeTab==='overview'" class="workspace-overview">
      <div class="overview-metric"><span>小说文件</span><b>{{detail.novel.sourceFile}}</b></div>
      <div class="overview-metric"><span>正文字符数</span><b>{{Number(detail.novel.contentLength||0).toLocaleString()}}</b></div>
      <div class="overview-metric"><span>识别角色</span><b>{{characterCount.toLocaleString()}}</b></div>
      <div class="overview-metric"><span>脚本片段</span><b>{{scriptCount.toLocaleString()}}</b></div>
      <div class="overview-note">角色、脚本和音频数据不会在进入工作台时一次性加载。选择上方功能页后才按分页读取对应数据，大型小说首屏无需传输完整正文。</div>
    </div>

    <div v-else-if="activeTab==='characters'" class="workspace-tab-fill">
      <div class="character-toolbar">
        <div><span class="eyebrow">VOICE CAST</span><b>{{characterCount}} 个角色 + 旁白</b></div>
        <div>
          <el-button class="ghost-button" :loading="deduplicating" @click="deduplicateCharacters">AI 人物去重</el-button>
          <el-button class="neon-button" :loading="matchingVoices" @click="autoMatchVoices">自动匹配音色</el-button>
        </div>
      </div>
      <div class="list-filter-bar compact-filter">
        <el-input v-model="characterQuery.keyword" clearable placeholder="搜索角色 / 性格 / 描述" @keyup.enter="searchCharacters"><template #prefix><el-icon><Search/></el-icon></template></el-input>
        <el-button class="neon-button" @click="searchCharacters">查询</el-button>
        <el-button class="ghost-button" @click="resetCharacters">重置</el-button>
      </div>
      <div class="character-grid">
        <article class="character-card narrator-card">
          <div class="avatar-holo narrator-avatar">旁</div>
          <div class="character-main">
            <h3>旁白</h3><span>小说级默认声线</span><p>用于当前小说全部旁白脚本。</p>
            <el-select v-model="detail.novel.narratorVoiceProfileId" placeholder="绑定旁白音色档案" clearable filterable @change="saveNarratorVoice">
              <el-option v-for="voice in voiceProfiles" :key="voice.id" :label="voice.name+' · '+voice.language" :value="voice.id"/>
            </el-select>
          </div>
        </article>
        <article v-for="character in characters" :key="character.id" class="character-card">
          <div class="avatar-holo">{{character.name.slice(0,1)}}</div>
          <div class="character-main">
            <h3>{{character.name}}</h3><span>{{character.gender||'未知'}} · {{character.personality||'待分析性格'}}</span><p>{{character.description||'暂无人物描述'}}</p>
            <el-select v-model="character.voiceProfileId" placeholder="绑定音色档案" clearable filterable @change="saveCharacter(character)">
              <el-option v-for="voice in voiceProfiles" :key="voice.id" :label="voice.name+' · '+voice.language" :value="voice.id"/>
            </el-select>
          </div>
        </article>
      </div>
      <ListPager v-model:page="characterQuery.page" v-model:page-size="characterQuery.pageSize" :total="characterTotal" @change="loadCharacters"/>
    </div>

    <div v-else-if="activeTab==='scripts'" class="workspace-tab-fill">
      <div class="list-filter-bar">
        <el-input v-model="scriptQuery.keyword" clearable placeholder="搜索脚本文本 / 角色 / 情绪" @keyup.enter="searchScripts"><template #prefix><el-icon><Search/></el-icon></template></el-input>
        <el-select v-model="scriptQuery.speaker" clearable filterable placeholder="全部角色"><el-option v-for="s in scriptSpeakers" :key="s" :label="s" :value="s"/></el-select>
        <el-select v-model="scriptQuery.status" clearable placeholder="全部状态"><el-option v-for="s in ['Pending','Generating','Completed','Failed']" :key="s" :label="s" :value="s"/></el-select>
        <el-button class="neon-button" @click="searchScripts">查询</el-button><el-button class="ghost-button" @click="resetScripts">重置</el-button>
      </div>
      <div class="table-flex-region"><el-table :data="scripts" height="100%" class="cyber-table">
        <el-table-column prop="order" label="#" width="70"/><el-table-column prop="speaker" label="角色" width="140"/><el-table-column prop="text" label="脚本文本" min-width="520"/><el-table-column prop="emotion" label="情绪" width="130"/><el-table-column label="状态" width="130"><template #default="{row}"><StatusBadge :status="row.status"/></template></el-table-column>
      </el-table></div>
      <ListPager v-model:page="scriptQuery.page" v-model:page-size="scriptQuery.pageSize" :total="scriptTotal" @change="loadScripts"/>
    </div>

    <div v-else class="workspace-tab-fill">
      <div class="audio-toolbar">
        <div><span class="eyebrow">AUDIO ASSETS</span><h3>{{audioInfo.completed}} / {{audioInfo.total}} 个片段已生成</h3></div>
        <div class="audio-toolbar-actions"><el-button class="ghost-button" @click="loadAudio"><el-icon><RefreshRight/></el-icon>刷新</el-button><el-button class="neon-button" @click="mergeAudio"><el-icon><Connection/></el-icon>合并 MP3</el-button></div>
      </div>
      <div v-if="audioInfo.mergedExists" class="merged-audio-card">
        <div><span class="engine-led"></span><div><small>MERGED AUDIO</small><b>完整有声书 MP3 已生成</b></div></div>
        <audio controls preload="metadata" :src="audioApi.novelPlayUrl(id)"></audio>
        <el-link :href="audioApi.novelDownloadUrl(id)" type="primary"><el-icon><Download/></el-icon>下载完整 MP3</el-link>
      </div>
      <div class="list-filter-bar">
        <el-input v-model="audioQuery.keyword" clearable placeholder="搜索脚本文本 / 角色 / 情绪" @keyup.enter="searchAudio"><template #prefix><el-icon><Search/></el-icon></template></el-input>
        <el-select v-model="audioQuery.speaker" clearable filterable placeholder="全部角色"><el-option v-for="s in audioInfo.speakers||[]" :key="s" :label="s" :value="s"/></el-select>
        <el-select v-model="audioQuery.status" clearable placeholder="全部状态"><el-option v-for="s in ['Pending','Generating','Completed','Failed']" :key="s" :label="s" :value="s"/></el-select>
        <el-button class="neon-button" @click="searchAudio">查询</el-button><el-button class="ghost-button" @click="resetAudio">重置</el-button>
      </div>
      <div class="table-flex-region"><el-table :data="audioInfo.segments" height="100%" class="cyber-table audio-table">
        <el-table-column prop="order" label="#" width="70"/><el-table-column prop="speaker" label="角色" width="120"/><el-table-column prop="text" label="脚本文本" min-width="330" show-overflow-tooltip/>
        <el-table-column label="状态" width="120"><template #default="{row}"><StatusBadge :status="row.status"/></template></el-table-column>
        <el-table-column label="试听" min-width="250"><template #default="{row}"><audio v-if="row.exists" controls preload="none" class="segment-player" :src="audioApi.segmentPlayUrl(row.id)"></audio><span v-else class="muted-text">尚未生成</span></template></el-table-column>
        <el-table-column label="操作" width="250" fixed="right"><template #default="{row}"><el-button text type="primary" @click="generateSegment(row)">{{row.exists?'重新生成':'生成'}}</el-button><el-link v-if="row.exists" :href="audioApi.segmentDownloadUrl(row.id)" class="audio-download-link"><el-icon><Download/></el-icon>下载</el-link><el-button v-if="row.exists" text type="danger" @click="removeSegment(row)"><el-icon><Delete/></el-icon>删除</el-button></template></el-table-column>
      </el-table></div>
      <ListPager v-model:page="audioQuery.page" v-model:page-size="audioQuery.pageSize" :total="audioInfo.filteredTotal||0" @change="loadAudio"/>
    </div>
  </section>
</div>
</template>