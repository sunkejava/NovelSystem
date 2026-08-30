<script setup lang="ts">
import {computed,onMounted,onUnmounted,ref} from 'vue';
import {useRoute,useRouter} from 'vue-router';
import {ElMessage,ElMessageBox} from 'element-plus';
import {RefreshRight,Delete,Download,Connection} from '@element-plus/icons-vue';
import {novelApi} from '../api/novels';
import {voiceApi} from '../api/voices';
import {audioApi} from '../api/audio';
import PageHeader from '../components/PageHeader.vue';
import StatusBadge from '../components/StatusBadge.vue';

const route=useRoute();
const router=useRouter();
const id=Number(route.params.id);

const detail=ref<any>(null);
const voiceProfiles=ref<any[]>([]);
const audioInfo=ref<any>({segments:[],total:0,completed:0,mergedExists:false});
const activeTab=ref('characters');
const working=ref(false);
let audioTimer:number|undefined;

const characterCount=computed(()=>detail.value?.characters?.length||0);
const scriptCount=computed(()=>detail.value?.scripts?.length||0);

async function load(){
  [detail.value,voiceProfiles.value,audioInfo.value]=await Promise.all([
    novelApi.detail(id),
    voiceApi.list(),
    audioApi.list(id)
  ]);
}

async function refreshAudio(){
  audioInfo.value=await audioApi.list(id);
}

async function analyze(){
  working.value=true;
  try{
    await novelApi.analyze(id);
    ElMessage.success('解析任务已进入任务中枢');
    router.push('/jobs');
  }finally{working.value=false;}
}

async function generateAllAudio(){
  working.value=true;
  try{
    await novelApi.generateAudio(id);
    ElMessage.success('整本有声书任务已进入任务中枢');
    activeTab.value='audio';
  }finally{working.value=false;}
}

async function saveCharacter(row:any){
  await novelApi.updateCharacter(row.id,row);
  ElMessage.success(row.name+' 的音色档案已保存');
}

async function generateSegment(row:any){
  await audioApi.generateSegment(row.id);
  ElMessage.success('片段重生成任务已创建');
  await refreshAudio();
}

async function removeSegment(row:any){
  await ElMessageBox.confirm('确认删除第 '+row.order+' 条已生成音频？脚本文本不会删除。','删除音频片段',{type:'warning'});
  await audioApi.removeSegment(row.id);
  ElMessage.success('音频片段已删除');
  await refreshAudio();
}

async function mergeAudio(){
  await audioApi.merge(id);
  ElMessage.success('音频合并任务已进入任务中枢');
}

onMounted(async()=>{
  await load();
  audioTimer=window.setInterval(()=>{
    if(activeTab.value==='audio') refreshAudio();
  },4000);
});
onUnmounted(()=>audioTimer&&clearInterval(audioTimer));
</script>

<template>
<div v-if="detail">
  <PageHeader eyebrow="NOVEL WORKSPACE" :title="detail.novel.title" description="人物理解、脚本拆解、音色档案绑定、音频资产管理与有声书生产的统一工作台。">
    <el-button class="ghost-button" :loading="working" @click="analyze">AI 重新解析</el-button>
    <el-button class="neon-button" :loading="working" @click="generateAllAudio">生成完整音频</el-button>
  </PageHeader>

  <div class="workspace-stats">
    <div class="mini-stat"><span>STATUS</span><StatusBadge :status="detail.novel.status"/></div>
    <div class="mini-stat"><span>CHARACTERS</span><b>{{characterCount}}</b></div>
    <div class="mini-stat"><span>SCRIPT LINES</span><b>{{scriptCount}}</b></div>
    <div class="mini-stat"><span>AUDIO</span><b>{{audioInfo.completed}} / {{audioInfo.total}}</b></div>
  </div>

  <section class="glass-panel content-card">
    <div class="segmented">
      <button :class="{active:activeTab==='characters'}" @click="activeTab='characters'">角色声纹矩阵</button>
      <button :class="{active:activeTab==='scripts'}" @click="activeTab='scripts'">解析脚本流</button>
      <button :class="{active:activeTab==='audio'}" @click="activeTab='audio'">音频资产</button>
    </div>

    <div v-if="activeTab==='characters'" class="character-grid">
      <article v-for="character in detail.characters" :key="character.id" class="character-card">
        <div class="avatar-holo">{{character.name.slice(0,1)}}</div>
        <div class="character-main">
          <h3>{{character.name}}</h3>
          <span>{{character.gender||'未知'}} · {{character.personality||'待分析性格'}}</span>
          <p>{{character.description||'暂无人物描述'}}</p>
          <el-select v-model="character.voiceProfileId" placeholder="绑定音色档案" clearable filterable @change="saveCharacter(character)">
            <el-option v-for="voice in voiceProfiles" :key="voice.id" :label="voice.name+' · '+voice.language" :value="voice.id">
              <span>{{voice.name}}</span>
              <span style="float:right;color:var(--muted);font-size:11px">{{voice.status}}</span>
            </el-option>
          </el-select>
        </div>
      </article>
    </div>

    <el-table v-else-if="activeTab==='scripts'" :data="detail.scripts" height="620" class="cyber-table">
      <el-table-column prop="order" label="#" width="70"/>
      <el-table-column prop="speaker" label="角色" width="140"/>
      <el-table-column prop="text" label="脚本文本" min-width="520"/>
      <el-table-column prop="emotion" label="情绪" width="130"/>
      <el-table-column label="状态" width="130"><template #default="{row}"><StatusBadge :status="row.status"/></template></el-table-column>
    </el-table>

    <div v-else>
      <div class="audio-toolbar">
        <div>
          <span class="eyebrow">AUDIO ASSETS</span>
          <h3>{{audioInfo.completed}} / {{audioInfo.total}} 个片段已生成</h3>
        </div>
        <div class="audio-toolbar-actions">
          <el-button class="ghost-button" @click="refreshAudio"><el-icon><RefreshRight/></el-icon>刷新</el-button>
          <el-button class="neon-button" @click="mergeAudio"><el-icon><Connection/></el-icon>合并 MP3</el-button>
        </div>
      </div>

      <div v-if="audioInfo.mergedExists" class="merged-audio-card">
        <div><span class="engine-led"></span><div><small>MERGED AUDIO</small><b>完整有声书 MP3 已生成</b></div></div>
        <audio controls preload="metadata" :src="audioApi.novelPlayUrl(id)"></audio>
        <el-link :href="audioApi.novelDownloadUrl(id)" type="primary"><el-icon><Download/></el-icon>下载完整 MP3</el-link>
      </div>

      <el-table :data="audioInfo.segments" height="620" class="cyber-table audio-table">
        <el-table-column prop="order" label="#" width="70"/>
        <el-table-column prop="speaker" label="角色" width="120"/>
        <el-table-column prop="text" label="脚本文本" min-width="330" show-overflow-tooltip/>
        <el-table-column label="状态" width="120">
          <template #default="{row}"><StatusBadge :status="row.status"/></template>
        </el-table-column>
        <el-table-column label="试听" min-width="250">
          <template #default="{row}">
            <audio v-if="row.exists" controls preload="none" class="segment-player" :src="audioApi.segmentPlayUrl(row.id)"></audio>
            <span v-else class="muted-text">尚未生成</span>
          </template>
        </el-table-column>
        <el-table-column label="操作" width="250" fixed="right">
          <template #default="{row}">
            <el-button text type="primary" @click="generateSegment(row)">{{row.exists?'重新生成':'生成'}}</el-button>
            <el-link v-if="row.exists" :href="audioApi.segmentDownloadUrl(row.id)" class="audio-download-link"><el-icon><Download/></el-icon>下载</el-link>
            <el-button v-if="row.exists" text type="danger" @click="removeSegment(row)"><el-icon><Delete/></el-icon>删除</el-button>
          </template>
        </el-table-column>
      </el-table>
    </div>
  </section>
</div>
</template>