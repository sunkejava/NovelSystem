<script setup lang="ts">
import {computed,onMounted,ref} from 'vue';
import {ElMessage,ElMessageBox} from 'element-plus';
import {Search} from '@element-plus/icons-vue';
import {settingApi} from '../api/settings';
import {voiceApi} from '../api/voices';
import {useTheme,type ThemeMode} from '../composables/useTheme';
import PageHeader from '../components/PageHeader.vue';
import StatusBadge from '../components/StatusBadge.vue';
import ListPager from '../components/ListPager.vue';

const form=ref<Record<string,string>>({});
const wavFiles=ref<any[]>([]);
const profiles=ref<any[]>([]);
const voiceTotal=ref(0);
const voiceQuery=ref({page:1,pageSize:12,keyword:'',language:'',status:''});
const editingId=ref<number|null>(null);
const savingVoice=ref(false);
const batchCreating=ref(false);
const testingAi=ref(false);
const testingTts=ref(false);
const testingFfmpeg=ref(false);
const aiTest=ref<any>(null);
const ttsTest=ref<any>(null);
const ffmpegTest=ref<any>(null);
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

const batchVoiceForm=ref({
  referenceText:'',
  useXVector:true,
  language:'Chinese',
  skipExisting:true,
  buildPrompt:false
});

const isEditing=computed(()=>editingId.value!==null);

async function load(){
  const [settings,wavs,voicePage]=await Promise.all([
    settingApi.get(),
    settingApi.voices(),
    voiceApi.list(voiceQuery.value)
  ]);
  form.value=settings;
  wavFiles.value=wavs;
  profiles.value=voicePage.items;
  voiceTotal.value=voicePage.total;
}
async function loadVoices(){
  const result=await voiceApi.list(voiceQuery.value);
  profiles.value=result.items;
  voiceTotal.value=result.total;
}
function searchVoices(){voiceQuery.value.page=1;loadVoices();}
function resetVoiceQuery(){voiceQuery.value={page:1,pageSize:12,keyword:'',language:'',status:''};loadVoices();}

async function save(){
  await settingApi.save(form.value);
  ElMessage.success('模型与运行参数已保存');
  await load();
}

async function testAi(){
  testingAi.value=true;
  try{
    await settingApi.save(form.value);
    aiTest.value=await settingApi.testAi();
    if(aiTest.value.online) ElMessage.success('LLM 连接成功，响应 '+aiTest.value.latencyMs+' ms');
    else ElMessage.error('LLM 连接失败：'+(aiTest.value.error||'未知错误'));
  }finally{testingAi.value=false;}
}

async function testFfmpeg(){
  testingFfmpeg.value=true;
  try{
    await settingApi.save(form.value);
    ffmpegTest.value=await settingApi.testFfmpeg();
    if(ffmpegTest.value.online) ElMessage.success('FFmpeg 可用：'+ffmpegTest.value.version);
    else ElMessage.error('FFmpeg 检测失败：'+(ffmpegTest.value.error||'未知错误'));
  }finally{testingFfmpeg.value=false;}
}

async function testTts(){
  testingTts.value=true;
  try{
    await settingApi.save(form.value);
    ttsTest.value=await settingApi.testTts();
    if(ttsTest.value.online) ElMessage.success('Qwen3-TTS 服务在线，响应 '+ttsTest.value.latencyMs+' ms');
    else ElMessage.error('Qwen3-TTS 连接失败：'+(ttsTest.value.error||'未知错误'));
  }finally{testingTts.value=false;}
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

async function batchCreateVoices(){
  batchCreating.value=true;
  try{
    const result=await voiceApi.batchCreate(batchVoiceForm.value);
    ElMessage.success('扫描 '+result.scanned+' 个 WAV，新建 '+result.created+' 个，跳过 '+result.skipped+' 个');
    if(result.promptErrors?.length) ElMessage.warning('其中 '+result.promptErrors.length+' 个 Prompt 生成失败，可稍后单独重试');
    await load();
  }finally{batchCreating.value=false;}
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
        <div class="endpoint-grid">
          <el-form-item label="请求超时（秒）">
            <el-input v-model="form.AiTimeoutSeconds" type="number" placeholder="120"/>
          </el-form-item>
          <el-form-item label="小说自动拆分长度（字符）">
            <el-input v-model="form.AiChunkSize" type="number" placeholder="12000"/>
          </el-form-item>
          <el-form-item label="解析最大输出 Token">
            <el-input v-model="form.AiAnalysisMaxTokens" type="number" placeholder="16384"/>
          </el-form-item>
          <el-form-item label="普通 AI Prompt Cache">
            <el-select v-model="form.AiCachePrompt" class="full-width">
              <el-option label="启用（推荐）" value="true"/>
              <el-option label="禁用" value="false"/>
            </el-select>
          </el-form-item>
          <el-form-item label="小说分块解析 Prompt Cache">
            <el-select v-model="form.AiAnalysisCachePrompt" class="full-width">
              <el-option label="关闭（推荐，分块完全隔离）" value="false"/>
              <el-option label="启用" value="true"/>
            </el-select>
          </el-form-item>
          <el-form-item label="Qwen Thinking / Reasoning">
            <el-select v-model="form.AiEnableThinking" class="full-width">
              <el-option label="关闭（解析推荐，速度更快）" value="false"/>
              <el-option label="开启" value="true"/>
            </el-select>
          </el-form-item>
          <el-form-item label="JSON 约束解码">
            <el-select v-model="form.AiUseJsonResponseFormat" class="full-width">
              <el-option label="关闭（速度优先）" value="false"/>
              <el-option label="开启（格式优先）" value="true"/>
            </el-select>
          </el-form-item>
        </div>
      </el-form>
      <div class="model-test-row">
        <el-button class="ghost-button" :loading="testingAi" @click="testAi">测试 LLM 连接</el-button>
        <div v-if="aiTest" class="test-result" :class="{online:aiTest.online}">
          <span class="engine-led"></span>
          <b>{{aiTest.online?'连接正常':'连接失败'}}</b>
          <small>{{aiTest.model}} · {{aiTest.latencyMs}} ms</small>
        </div>
      </div>
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
      <div class="model-test-row">
        <el-button class="ghost-button" :loading="testingTts" @click="testTts">测试 Qwen3-TTS</el-button>
        <div v-if="ttsTest" class="test-result" :class="{online:ttsTest.online}">
          <span class="engine-led"></span>
          <b>{{ttsTest.online?'服务在线':'服务离线'}}</b>
          <small>{{ttsTest.latencyMs}} ms</small>
        </div>
      </div>
    </section>

    <section class="glass-panel content-card">
      <div class="section-title"><span>04</span><div><h3>本地资源</h3><p>VOICE ASSETS · PROMPT CACHE · FFMPEG</p></div></div>
      <el-form label-position="top">
        <el-form-item label="WAV 音色目录"><el-input v-model="form.VoiceDirectory"/></el-form-item>
        <el-form-item label="Prompt 缓存目录"><el-input v-model="form.PromptDirectory"/></el-form-item>
        <el-form-item label="FFmpeg 路径"><el-input v-model="form.FfmpegPath"/></el-form-item>
      </el-form>
      <div class="model-test-row">
        <el-button class="ghost-button" :loading="testingFfmpeg" @click="testFfmpeg">检测 FFmpeg</el-button>
        <div v-if="ffmpegTest" class="test-result" :class="{online:ffmpegTest.online}">
          <span class="engine-led"></span>
          <b>{{ffmpegTest.online?'FFmpeg 可用':'FFmpeg 不可用'}}</b>
          <small>{{ffmpegTest.online?ffmpegTest.version:(ffmpegTest.error||'检测失败')}}</small>
        </div>
      </div>
      <div class="voice-count">{{wavFiles.length}} <small>REFERENCE WAV FILES DETECTED</small></div>
    </section>
  </div>

  <section class="glass-panel content-card voice-manager">
    <div class="card-head"><div><span class="eyebrow">VOICE PROFILES</span><h3>Qwen3-TTS 音色档案</h3></div><span>{{voiceTotal}} PROFILES</span></div>

    <div class="batch-voice-panel">
      <div class="section-title compact"><span>批</span><div><h3>从本地 WAV 批量建档</h3><p>名称自动取文件名，其他字段使用下方统一默认值</p></div></div>
      <el-form label-position="top">
        <div class="voice-form-grid">
          <el-form-item label="默认语言"><el-select v-model="batchVoiceForm.language" class="full-width"><el-option v-for="lang in languages" :key="lang" :label="lang" :value="lang"/></el-select></el-form-item>
          <el-form-item label="仅使用 x-vector"><el-switch v-model="batchVoiceForm.useXVector"/></el-form-item>
          <el-form-item label="跳过已存在档案"><el-switch v-model="batchVoiceForm.skipExisting"/></el-form-item>
          <el-form-item label="建档后生成 Prompt"><el-switch v-model="batchVoiceForm.buildPrompt"/></el-form-item>
        </div>
        <el-form-item label="统一参考文本（仅在未启用 x-vector 时需要）">
          <el-input v-model="batchVoiceForm.referenceText" type="textarea" :rows="2" placeholder="如果每个音频内容不同，建议保持 x-vector 模式，建档后再逐个补充准确参考文本"/>
        </el-form-item>
        <div class="voice-editor-actions"><el-button class="neon-button" :loading="batchCreating" @click="batchCreateVoices">扫描目录并批量建档</el-button></div>
      </el-form>
    </div>

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

    <div class="list-filter-bar compact-filter" style="margin-top:18px">
      <el-input v-model="voiceQuery.keyword" clearable placeholder="搜索音色名称 / WAV / 参考文本" @keyup.enter="searchVoices"><template #prefix><el-icon><Search/></el-icon></template></el-input>
      <el-select v-model="voiceQuery.language" clearable placeholder="全部语言"><el-option v-for="lang in languages" :key="lang" :label="lang" :value="lang"/></el-select>
      <el-select v-model="voiceQuery.status" clearable placeholder="全部状态"><el-option v-for="s in ['Ready','PromptReady','BuildingPrompt','Failed']" :key="s" :label="s" :value="s"/></el-select>
      <el-button class="neon-button" @click="searchVoices">查询</el-button>
      <el-button class="ghost-button" @click="resetVoiceQuery">重置</el-button>
    </div>
    <el-table :data="profiles" class="cyber-table">
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
    <ListPager v-model:page="voiceQuery.page" v-model:page-size="voiceQuery.pageSize" :total="voiceTotal" @change="loadVoices"/>
  </section>
</div>
</template>