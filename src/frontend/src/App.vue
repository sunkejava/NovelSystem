<script setup lang="ts">
import {ref,onMounted} from 'vue';
import axios from 'axios';
const api=axios.create({baseURL:import.meta.env.VITE_API_URL||'http://localhost:5080/api'});
const tab=ref('novels'),novels=ref<any[]>([]),jobs=ref<any[]>([]),settings=ref<any>({}),voices=ref<any[]>([]),selected=ref<any>(null),styles=ref<any[]>([]),generated=ref<any[]>([]);
const gen=ref({title:'新小说',styleId:null as any,sourceNovelId:null as any,prompt:'基于学习到的写作方法，生成一部全新的中文小说第一章。'});
async function load(){novels.value=(await api.get('/novels')).data;jobs.value=(await api.get('/jobs')).data;settings.value=(await api.get('/settings')).data;voices.value=(await api.get('/settings/voices')).data;styles.value=(await api.get('/writing/styles')).data;generated.value=(await api.get('/writing/generated')).data;}
onMounted(load);
async function upload(o:any){const f=new FormData();f.append('file',o.file);await api.post('/novels/upload',f);o.onSuccess();await load();}
async function detail(n:any){selected.value=(await api.get('/novels/'+n.id)).data;tab.value='detail';}
async function analyze(){await api.post('/novels/'+selected.value.novel.id+'/analyze');tab.value='jobs';await load();}
async function audio(){await api.post('/novels/'+selected.value.novel.id+'/audio');tab.value='jobs';await load();}
async function saveChar(c:any){await api.put('/characters/'+c.id,c);}
async function saveSettings(){await api.put('/settings',settings.value);await load();}
async function learn(n:any){await api.post('/writing/learn/'+n.id);await load();tab.value='writing';}
async function generate(){await api.post('/writing/generate',gen.value);await load();}
async function publishGenerated(row:any){await api.post('/writing/generated/'+row.id+'/publish');await load();tab.value='novels';}
</script>
<template>
<div class="layout">
  <aside>
    <h2>NovelSystem</h2>
    <el-menu :default-active="tab" @select="tab=$event">
      <el-menu-item index="novels">小说库</el-menu-item>
      <el-menu-item index="jobs">任务中心</el-menu-item>
      <el-menu-item index="writing">AI 创作</el-menu-item>
      <el-menu-item index="settings">系统设置</el-menu-item>
    </el-menu>
  </aside>
  <main>
    <section class="panel" v-if="tab==='novels'">
      <div class="head"><h1>小说库</h1><el-upload :http-request="upload" :show-file-list="false"><el-button type="primary">上传 TXT 小说</el-button></el-upload></div>
      <el-table :data="novels">
        <el-table-column prop="title" label="标题"/>
        <el-table-column prop="status" label="状态" width="120"/>
        <el-table-column prop="createdAt" label="创建时间" width="220"/>
        <el-table-column label="操作" width="260"><template #default="{row}"><el-button @click="detail(row)">查看</el-button><el-button @click="learn(row)">学习写法</el-button></template></el-table-column>
      </el-table>
    </section>

    <section class="panel" v-if="tab==='detail' && selected">
      <div class="head"><h1>{{selected.novel.title}}</h1><div><el-button @click="analyze">AI解析人物/脚本</el-button><el-button type="success" @click="audio">生成整书音频</el-button></div></div>
      <h3>人物与音色</h3>
      <el-table :data="selected.characters">
        <el-table-column prop="name" label="人物" width="140"/>
        <el-table-column prop="gender" label="性别" width="100"/>
        <el-table-column prop="personality" label="性格"/>
        <el-table-column label="音色" width="260"><template #default="{row}"><el-select class="full" v-model="row.voiceFile" clearable @change="saveChar(row)"><el-option v-for="v in voices" :key="v.path" :label="v.name" :value="v.path"/></el-select></template></el-table-column>
      </el-table>
      <h3 class="mt">脚本</h3>
      <el-table :data="selected.scripts" height="500">
        <el-table-column prop="order" label="#" width="70"/>
        <el-table-column prop="speaker" label="人物" width="140"/>
        <el-table-column prop="text" label="内容"/>
        <el-table-column prop="emotion" label="情绪" width="120"/>
        <el-table-column prop="status" label="状态" width="110"/>
      </el-table>
    </section>

    <section class="panel" v-if="tab==='jobs'">
      <div class="head"><h1>任务中心</h1><el-button @click="load">刷新</el-button></div>
      <el-table :data="jobs">
        <el-table-column prop="id" label="ID" width="80"/>
        <el-table-column prop="type" label="任务" width="180"/>
        <el-table-column prop="status" label="状态" width="120"/>
        <el-table-column label="进度"><template #default="{row}"><el-progress :percentage="row.progress"/></template></el-table-column>
        <el-table-column prop="result" label="结果"/>
        <el-table-column prop="error" label="错误"/>
      </el-table>
    </section>

    <section class="panel" v-if="tab==='writing'">
      <h1>AI 写作</h1>
      <el-form label-width="110px">
        <el-form-item label="新小说标题"><el-input v-model="gen.title"/></el-form-item>
        <el-form-item label="写作风格"><el-select class="full" v-model="gen.styleId" clearable><el-option v-for="s in styles" :key="s.id" :label="s.name" :value="s.id"/></el-select></el-form-item>
        <el-form-item label="创作要求"><el-input type="textarea" :rows="6" v-model="gen.prompt"/></el-form-item>
        <el-button type="primary" @click="generate">生成小说</el-button>
      </el-form>
      <h3 class="mt">已生成小说</h3>
      <el-table :data="generated">
        <el-table-column prop="title" label="标题"/>
        <el-table-column prop="createdAt" label="时间" width="220"/>
        <el-table-column label="下载" width="120"><template #default="{row}"><el-link type="primary" :href="api.defaults.baseURL+'/writing/generated/'+row.id+'/download'">TXT</el-link></template></el-table-column>
      </el-table>
    </section>

    <section class="panel" v-if="tab==='settings'">
      <h1>系统设置</h1>
      <el-form label-width="180px">
        <el-form-item label="llama.cpp API 地址"><el-input v-model="settings.AiBaseUrl"/></el-form-item>
        <el-form-item label="模型名"><el-input v-model="settings.AiModel"/></el-form-item>
        <el-form-item label="Qwen3-TTS 地址"><el-input v-model="settings.TtsBaseUrl"/></el-form-item>
        <el-form-item label="音色 WAV 目录"><el-input v-model="settings.VoiceDirectory"/></el-form-item>
        <el-form-item label="FFmpeg 路径"><el-input v-model="settings.FfmpegPath"/></el-form-item>
        <el-button type="primary" @click="saveSettings">保存设置</el-button>
      </el-form>
    </section>
  </main>
</div>
</template>