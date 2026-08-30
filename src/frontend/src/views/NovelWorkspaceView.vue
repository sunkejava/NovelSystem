<script setup lang="ts">
import {computed,onMounted,ref} from 'vue';
import {useRoute,useRouter} from 'vue-router';
import {ElMessage} from 'element-plus';
import {novelApi} from '../api/novels';
import {settingApi} from '../api/settings';
import PageHeader from '../components/PageHeader.vue';
import StatusBadge from '../components/StatusBadge.vue';

const route=useRoute();const router=useRouter();const id=Number(route.params.id);
const detail=ref<any>(null);const voices=ref<any[]>([]);const activeTab=ref('characters');const working=ref(false);
const characterCount=computed(()=>detail.value?.characters?.length||0);
const scriptCount=computed(()=>detail.value?.scripts?.length||0);

async function load(){detail.value=await novelApi.detail(id);voices.value=await settingApi.voices();}
async function analyze(){working.value=true;try{await novelApi.analyze(id);ElMessage.success('解析任务已进入任务中枢');router.push('/jobs');}finally{working.value=false;}}
async function audio(){working.value=true;try{await novelApi.generateAudio(id);ElMessage.success('有声书任务已进入任务中枢');router.push('/jobs');}finally{working.value=false;}}
async function saveCharacter(row:any){await novelApi.updateCharacter(row.id,row);ElMessage.success(row.name+' 的声音配置已保存');}
onMounted(load);
</script>
<template>
<div v-if="detail">
<PageHeader eyebrow="NOVEL WORKSPACE" :title="detail.novel.title" description="人物理解、脚本拆解、音色绑定与有声书生产的统一工作台。">
<el-button class="ghost-button" :loading="working" @click="analyze">AI 重新解析</el-button><el-button class="neon-button" :loading="working" @click="audio">生成完整音频</el-button>
</PageHeader>
<div class="workspace-stats">
<div class="mini-stat"><span>STATUS</span><StatusBadge :status="detail.novel.status"/></div><div class="mini-stat"><span>CHARACTERS</span><b>{{characterCount}}</b></div><div class="mini-stat"><span>SCRIPT LINES</span><b>{{scriptCount}}</b></div><div class="mini-stat"><span>SOURCE</span><b>{{detail.novel.sourceFile}}</b></div>
</div>
<section class="glass-panel content-card">
<div class="segmented"><button :class="{active:activeTab==='characters'}" @click="activeTab='characters'">角色声纹矩阵</button><button :class="{active:activeTab==='scripts'}" @click="activeTab='scripts'">解析脚本流</button></div>
<div v-if="activeTab==='characters'" class="character-grid">
<article v-for="character in detail.characters" :key="character.id" class="character-card">
<div class="avatar-holo">{{character.name.slice(0,1)}}</div>
<div class="character-main"><h3>{{character.name}}</h3><span>{{character.gender||'未知'}} · {{character.personality||'待分析性格'}}</span><p>{{character.description||'暂无人物描述'}}</p>
<el-select v-model="character.voiceFile" placeholder="选择 WAV 音色" clearable filterable @change="saveCharacter(character)"><el-option v-for="voice in voices" :key="voice.path" :label="voice.name" :value="voice.path"/></el-select></div>
</article></div>
<el-table v-else :data="detail.scripts" height="620" class="cyber-table">
<el-table-column prop="order" label="#" width="70"/><el-table-column prop="speaker" label="角色" width="140"/><el-table-column prop="text" label="脚本文本" min-width="520"/><el-table-column prop="emotion" label="情绪" width="130"/><el-table-column label="状态" width="130"><template #default="{row}"><StatusBadge :status="row.status"/></template></el-table-column>
</el-table>
</section>
</div>
</template>