<script setup lang="ts">
import {computed,onMounted,ref} from 'vue';
import {ElMessage,ElMessageBox} from 'element-plus';
import {settingApi} from '../api/settings';
import {voiceApi} from '../api/voices';
import {useTheme,type ThemeMode} from '../composables/useTheme';
import PageHeader from '../components/PageHeader.vue';
import StatusBadge from '../components/StatusBadge.vue';

const form=ref<Record<string,string>>({});
const wavFiles=ref<any[]>([]);
const profiles=ref<any[]>([]);
const editingId=ref<number|null>(null);
const savingVoice=ref(false);
const {mode,accent,setMode,setAccent}=useTheme();

const accents=['#43e8ff','#5979ff','#7c6cff','#a855f7','#ff4fd8','#21d19f','#ff9f43'];
const languages=['Auto','Chinese','English','German','Italian','Portuguese','Spanish','Japanese','Korean','French','Russian'];

const voiceForm=ref({
  name:'',
  referenceAudioFile:'',
  referenceText:'',
  useXVector:false,
  language:'Chinese'
});

const isEditing=computed(()=>editingId.value!==null);

async function load(){
  [form.value,wavFiles.value,profiles.value]=await Promise.all([
    settingApi.get(),
    settingApi.voices(),
    voiceApi.list()
  ]);
}

async function save(){
  await settingApi.save(form.value);
  ElMessage.success('模型与运行参数已保存');
  await load();
}

function resetVoice(){
  editingId.value=null;
  voiceForm.value={name:'',referenceAudioFile:'',referenceText:'',useXVector:false,language:'Chinese'};
}

function editVoice(row:any){
  editingId.value=row.id;
  voiceForm.value={
    name:row.name,
    referenceAudioFile:row.referenceAudioFile,
    referenceText:row.referenceText,
    useXVector:row.useXVector,
    language:row.language||'Chinese'
  };
}

async function saveVoice(){
  if(!voiceForm.value.name||!voiceForm.value.referenceAudioFile){
    ElMessage.warning('请填写音色名称并选择参考 WAV');
    return;
  }
  if(!voiceForm.value.useXVector&&!voiceForm.value.referenceText.trim()){
    ElMessage.warning('未启用 x-vector 时必须填写参考音频文本');
    return;
  }

  savingVoice.value=true;
  try{
    if(editingId.value) await voiceApi.update(editingId.value,voiceForm.value);
    else await voiceApi.create(voiceForm.value);
    ElMessage.success(isEditing.value?'音色档案已更新':'音色档案已创建');
    resetVoice();
    await load();
  }finally{savingVoice.value=false;}
}

async function buildPrompt(row:any){
  ElMessage.info('正在调用 Qwen3-TTS 生成 Prompt...');
  await voiceApi.buildPrompt(row.id);
  ElMessage.success('Prompt 缓存生成完成');
  await load();
}

async function removeVoice(row:any){
  await ElMessageBox.confirm('确认删除音色“'+row.name+'”？','删除音色',{type:'warning'});
  await voiceApi.remove(row.id);
  ElMessage.success('音色已删除');
  await load();
}

onMounted(load);
</script>

<template>
<div>
  <PageHeader eyebrow="MODEL MATRIX" title="系统、模型与主题设置" description="集中管理本地 AI、Qwen3-TTS Gradio、音色档案、FFmpeg 与界面主题。">
    <el-button class="neon-button" @click="save">保存运行配置</el-button>
  </PageHeader>

  <div class="settings-grid">
    <section class="glass-panel content-card">
      <div class="section-title"><span>01</span><div><h3>LLM 推理引擎</h3><p>OPENAI-COMPATIBLE LLAMA.CPP</p></div></div>
      <el-form label-position="top">
        <el-form-item label="API Base URL"><el-input v-model="form.AiBaseUrl"/></el-form-item>
        <el-form-item label="Model / Alias"><el-input v-model="form.AiModel"/></el-form-item>
      </el-form>
    </section>

    <section class="glass-panel content-card">
      <div class="section-title"><span>02</span><div><h3>界面主题</h3><p>LIGHT / DARK · ACCENT COLOR</p></div></div>
      <div class="theme-mode-grid">
        <button class="theme-mode-card" :class="{active:mode==='dark'}" @click="setMode('dark' as ThemeMode)">
          <div class="theme-preview dark-preview"></div><b>暗色模式</b><small>深空 AI 科技风</small>
        </button>
        <button class="theme-mode-card" :class="{active:mode==='light'}" @click="setMode('light' as ThemeMode)">
          <div class="theme-preview light-preview"></div><b>亮色模式</b><small>明亮科技工作台</small>
        </button>
      </div>
      <div class="accent-picker"><span>主题强调色</span><div><button v-for="color in accents" :key="color" :class="{active:accent===color}" :style="{background:color}" @click="setAccent(color)"></button></div></div>
    </section>

    <section class="glass-panel content-card qwen-settings">
      <div class="section-title"><span>03</span><div><h3>Qwen3-TTS Gradio 引擎</h3><p>UPLOAD · EVENT · SSE RESULT</p></div></div>
      <el-form label-position="top" class="endpoint-grid">
        <el-form-item label="TTS Base URL"><el-input v-model="form.TtsBaseUrl" placeholder="http://127.0.0.1:8000"/></el-form-item>
        <el-form-item label="单句超时（秒）"><el-input v-model="form.TtsTimeoutSeconds"/></el-form-item>
        <el-form-item label="上传接口"><el-input v-model="form.TtsUploadEndpoint"/></el-form-item>
        <el-form-item label="默认语言"><el-select v-model="form.TtsDefaultLanguage" class="full-width"><el-option v-for="lang in languages" :key="lang" :label="lang" :value="lang"/></el-select></el-form-item>
        <el-form-item label="Voice Clone 提交"><el-input v-model="form.TtsVoiceCloneSubmitEndpoint"/></el-form-item>
        <el-form-item label="Voice Clone 结果"><el-input v-model="form.TtsVoiceCloneResultEndpoint"/></el-form-item>
        <el-form-item label="Save Prompt 提交"><el-input v-model="form.TtsSavePromptSubmitEndpoint"/></el-form-item>
        <el-form-item label="Save Prompt 结果"><el-input v-model="form.TtsSavePromptResultEndpoint"/></el-form-item>
        <el-form-item label="Prompt Generate 提交"><el-input v-model="form.TtsPromptGenSubmitEndpoint"/></el-form-item>
        <el-form-item label="Prompt Generate 结果"><el-input v-model="form.TtsPromptGenResultEndpoint"/></el-form-item>
      </el-form>
    </section>

    <section class="glass-panel content-card">
      <div class="section-title"><span>04</span><div><h3>本地资源</h3><p>VOICE ASSETS · PROMPT CACHE · FFMPEG</p></div></div>
      <el-form label-position="top">
        <el-form-item label="WAV 音色目录"><el-input v-model="form.VoiceDirectory"/></el-form-item>
        <el-form-item label="Prompt 缓存目录"><el-input v-model="form.PromptDirectory"/></el-form-item>
        <el-form-item label="FFmpeg 路径"><el-input v-model="form.FfmpegPath"/></el-form-item>
      </el-form>
      <div class="voice-count">{{wavFiles.length}} <small>REFERENCE WAV FILES DETECTED</small></div>
    </section>
  </div>

  <section class="glass-panel content-card voice-manager">
    <div class="card-head"><div><span class="eyebrow">VOICE PROFILES</span><h3>Qwen3-TTS 音色档案</h3></div><span>{{profiles.length}} PROFILES</span></div>

    <div class="voice-editor">
      <el-form label-position="top">
        <div class="voice-form-grid">
          <el-form-item label="音色名称"><el-input v-model="voiceForm.name" placeholder="例如：温柔女声"/></el-form-item>
          <el-form-item label="参考 WAV">
            <el-select v-model="voiceForm.referenceAudioFile" filterable class="full-width" placeholder="从音色目录选择">
              <el-option v-for="wav in wavFiles" :key="wav.path" :label="wav.name" :value="wav.path"/>
            </el-select>
          </el-form-item>
          <el-form-item label="语言"><el-select v-model="voiceForm.language" class="full-width"><el-option v-for="lang in languages" :key="lang" :label="lang" :value="lang"/></el-select></el-form-item>
          <el-form-item label="仅使用 x-vector"><el-switch v-model="voiceForm.useXVector"/></el-form-item>
        </div>
        <el-form-item label="参考音频对应文本">
          <el-input v-model="voiceForm.referenceText" type="textarea" :rows="3" placeholder="必须尽量与参考 WAV 中实际说话内容完全一致"/>
        </el-form-item>
        <div class="voice-editor-actions">
          <el-button v-if="isEditing" class="ghost-button" @click="resetVoice">取消编辑</el-button>
          <el-button class="neon-button" :loading="savingVoice" @click="saveVoice">{{isEditing?'保存音色':'新增音色档案'}}</el-button>
        </div>
      </el-form>
    </div>

    <el-table :data="profiles" class="cyber-table" style="margin-top:18px">
      <el-table-column prop="name" label="音色" min-width="150"/>
      <el-table-column prop="language" label="语言" width="100"/>
      <el-table-column label="模式" width="120"><template #default="{row}">{{row.useXVector?'X-Vector':'参考文本'}}</template></el-table-column>
      <el-table-column prop="referenceAudioFile" label="参考 WAV" min-width="250"/>
      <el-table-column label="Prompt" min-width="180"><template #default="{row}"><span class="path-text">{{row.promptFile||'尚未生成'}}</span></template></el-table-column>
      <el-table-column label="状态" width="130"><template #default="{row}"><StatusBadge :status="row.status"/></template></el-table-column>
      <el-table-column label="操作" width="260" fixed="right">
        <template #default="{row}">
          <el-button text @click="editVoice(row)">编辑</el-button>
          <el-button text type="primary" @click="buildPrompt(row)">生成 Prompt</el-button>
          <el-button text type="danger" @click="removeVoice(row)">删除</el-button>
        </template>
      </el-table-column>
    </el-table>
  </section>
</div>
</template>