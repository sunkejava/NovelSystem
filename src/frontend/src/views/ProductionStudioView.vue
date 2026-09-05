<script setup lang="ts">
import {computed,nextTick,onMounted,ref} from 'vue';
import {ElMessage,ElMessageBox} from 'element-plus';
import {Refresh,Search,EditPen,Headset,Check,Plus,Delete,VideoPlay,MagicStick,Clock,Files} from '@element-plus/icons-vue';
import {novelApi} from '../api/novels';
import {productionApi} from '../api/production';
import {audioApi} from '../api/audio';
import PageHeader from '../components/PageHeader.vue';
import ListPager from '../components/ListPager.vue';
import StatusBadge from '../components/StatusBadge.vue';

const novels=ref<any[]>([]);
const novelId=ref<number>();
const activeTab=ref('chapters');
const loading=ref(false);

const chapters=ref<any[]>([]);
const timeline=ref<any[]>([]),timelineTotal=ref(0),timelineTotalDuration=ref(0);
const timelineQuery=ref({page:1,pageSize:50,chapterId:undefined as number|undefined,keyword:''});
const editVisible=ref(false),editRow=ref<any>(null);

const waveformVisible=ref(false),waveformRow=ref<any>(null),waveformCanvas=ref<HTMLCanvasElement>();
const waveformLoading=ref(false);
const versionsVisible=ref(false),versionRow=ref<any>(null),versions=ref<any[]>([]),versionLoading=ref(false);

const qaRows=ref<any[]>([]),qaFilter=ref({resolved:false as boolean|undefined,severity:''});
const qaSummary=computed(()=>({total:qaRows.value.length,errors:qaRows.value.filter(x=>x.severity==='Error'&&!x.resolved).length,warnings:qaRows.value.filter(x=>x.severity==='Warning'&&!x.resolved).length}));

const pronunciations=ref<any[]>([]);
const pronunciationVisible=ref(false),pronunciationForm=ref<any>({id:null,pattern:'',replacement:'',note:'',isEnabled:true});
const selectedNovel=computed(()=>novels.value.find(x=>x.id===novelId.value));
const maxPageDuration=computed(()=>Math.max(1,...timeline.value.map(x=>Number(x.durationMs||0))));

async function loadNovels(){const r=await novelApi.list({page:1,pageSize:100});novels.value=r.items||r;if(!novelId.value&&novels.value.length)novelId.value=novels.value[0].id;}
async function ensureNovel(){return !!novelId.value;}
async function loadChapters(){if(await ensureNovel())chapters.value=await productionApi.chapters(novelId.value!);}
async function rebuildChapters(){if(!await ensureNovel())return;loading.value=true;try{const r=await productionApi.rebuildChapters(novelId.value!);ElMessage.success('章节结构已重建，共识别 '+r.chapters+' 个章节，并已回填脚本原文位置');await Promise.all([loadChapters(),loadTimeline()]);}finally{loading.value=false;}}
async function loadTimeline(){if(!await ensureNovel())return;const r=await productionApi.timeline(novelId.value!,timelineQuery.value);timeline.value=r.items;timelineTotal.value=r.total;timelineTotalDuration.value=r.totalDurationMs||0;}
async function recalculateTimeline(){if(!await ensureNovel())return;loading.value=true;try{const r=await productionApi.recalculateTimeline(novelId.value!);ElMessage.success('音频时间轴已重建：'+r.located+' 个片段，整书时长 '+formatDuration(r.totalDurationMs));await loadTimeline();}finally{loading.value=false;}}
function searchTimeline(){timelineQuery.value.page=1;loadTimeline();}
function openEdit(row:any){editRow.value={...row};editVisible.value=true;}
async function saveTimeline(){if(!editRow.value)return;await productionApi.updateTimeline(editRow.value.id,{speaker:editRow.value.speaker,text:editRow.value.text,emotion:editRow.value.emotion});ElMessage.success('脚本已保存；如内容发生变化，旧音频已失效');editVisible.value=false;await loadTimeline();}
async function regenerate(row:any){await audioApi.generateSegment(row.id);ElMessage.success('该片段已进入音频生成队列');}

async function openWaveform(row:any){if(row.status!=='Completed'||!row.audioFile){ElMessage.warning('当前脚本尚无可试听音频');return;}waveformRow.value=row;waveformVisible.value=true;waveformLoading.value=true;await nextTick();try{await drawWaveform(audioApi.segmentPlayUrl(row.id));}catch{ElMessage.warning('波形解析失败，但仍可使用播放器试听');}finally{waveformLoading.value=false;}}
async function drawWaveform(url:string){const response=await fetch(url);if(!response.ok)throw new Error('audio fetch failed');const buffer=await response.arrayBuffer();const context=new AudioContext();try{const audioBuffer=await context.decodeAudioData(buffer.slice(0));const data=audioBuffer.getChannelData(0);const canvas=waveformCanvas.value;if(!canvas)return;const ratio=window.devicePixelRatio||1;const width=Math.max(600,canvas.clientWidth||900),height=180;canvas.width=width*ratio;canvas.height=height*ratio;const ctx=canvas.getContext('2d');if(!ctx)return;ctx.scale(ratio,ratio);ctx.clearRect(0,0,width,height);const bars=180,block=Math.max(1,Math.floor(data.length/bars));ctx.fillStyle=getComputedStyle(document.documentElement).getPropertyValue('--accent').trim()||'#43e8ff';for(let i=0;i<bars;i++){let peak=0;const start=i*block,end=Math.min(data.length,start+block);for(let j=start;j<end;j++)peak=Math.max(peak,Math.abs(data[j]));const barH=Math.max(2,peak*(height-20));ctx.globalAlpha=.35+Math.min(.65,peak);ctx.fillRect(i*(width/bars),height/2-barH/2,Math.max(1,width/bars-1),barH);}ctx.globalAlpha=1;}finally{await context.close();}}

async function openVersions(row:any){versionRow.value=row;versionsVisible.value=true;await loadVersions();}
async function loadVersions(){if(versionRow.value)versions.value=await productionApi.versions(versionRow.value.id);}
async function generateVersion(){if(!versionRow.value)return;versionLoading.value=true;try{await productionApi.generateVersion(versionRow.value.id);ElMessage.success('新的 A/B 音频版本已生成');await loadVersions();}finally{versionLoading.value=false;}}
async function selectVersion(row:any){await productionApi.selectVersion(row.id);ElMessage.success('已采用版本 V'+row.versionNo);await Promise.all([loadVersions(),loadTimeline()]);}
async function removeVersion(row:any){await ElMessageBox.confirm('确认删除音频版本 V'+row.versionNo+'？','删除音频版本',{type:'warning'});await productionApi.removeVersion(row.id);await loadVersions();}

async function loadQa(){if(await ensureNovel())qaRows.value=await productionApi.qa(novelId.value!,qaFilter.value);}
async function runQa(){if(!await ensureNovel())return;loading.value=true;try{const r=await productionApi.runQa(novelId.value!);ElMessage.success('质量检测完成：'+r.errors+' 个错误，'+r.warnings+' 个警告');await loadQa();}finally{loading.value=false;}}
async function autoFixQa(){if(!await ensureNovel())return;await ElMessageBox.confirm('自动修复会重新定位原文、修正脚本顺序、重建音频时间轴，并把缺失/失败音频加入生成队列。无法自动判断的问题仍保留人工处理。','QA 自动修复',{type:'warning'});loading.value=true;try{const r=await productionApi.autoFixQa(novelId.value!);ElMessage.success('自动修复完成：排队重生 '+r.queuedAudio+' 条音频');await Promise.all([loadQa(),loadTimeline(),loadChapters()]);}finally{loading.value=false;}}
async function resolveQa(row:any){await productionApi.resolveQa(row.id,!row.resolved);await loadQa();}

async function loadPronunciations(){if(await ensureNovel())pronunciations.value=await productionApi.pronunciations(novelId.value!);}
function addPronunciation(){pronunciationForm.value={id:null,pattern:'',replacement:'',note:'',isEnabled:true};pronunciationVisible.value=true;}
function editPronunciation(row:any){pronunciationForm.value={id:row.id,pattern:row.pattern,replacement:row.replacement,note:row.note||'',isEnabled:row.isEnabled};pronunciationVisible.value=true;}
async function savePronunciation(){const f=pronunciationForm.value;if(!f.pattern?.trim()||!f.replacement?.trim()){ElMessage.warning('请填写原词和发音替换');return;}f.id?await productionApi.updatePronunciation(f.id,f):await productionApi.createPronunciation(novelId.value!,f);ElMessage.success('发音词典已保存');pronunciationVisible.value=false;await loadPronunciations();}
async function removePronunciation(row:any){await ElMessageBox.confirm('确认删除“'+row.pattern+'”的发音规则？','删除发音规则',{type:'warning'});await productionApi.removePronunciation(row.id);await loadPronunciations();}

async function switchTab(name:string|number){activeTab.value=String(name);if(activeTab.value==='chapters')await loadChapters();if(activeTab.value==='timeline')await Promise.all([loadChapters(),loadTimeline()]);if(activeTab.value==='qa')await loadQa();if(activeTab.value==='pronunciation')await loadPronunciations();}
async function novelChanged(){timelineQuery.value={page:1,pageSize:50,chapterId:undefined,keyword:''};await switchTab(activeTab.value);}
function offsetText(row:any){return row.sourceStart>=0?row.sourceStart.toLocaleString()+' ~ '+row.sourceEnd.toLocaleString():'未定位';}
function formatDuration(ms:number){const total=Math.max(0,Math.floor(Number(ms||0)/1000));const h=Math.floor(total/3600),m=Math.floor(total%3600/60),s=total%60;return h>0?`${h}时${m}分${s}秒`:`${m}分${s}秒`;}
function formatTimecode(ms:number|null|undefined){if(ms==null)return '—';const total=Math.max(0,Math.floor(ms/1000)),m=Math.floor(total/60),s=total%60,cs=Math.floor(ms%1000/10);return `${String(m).padStart(2,'0')}:${String(s).padStart(2,'0')}.${String(cs).padStart(2,'0')}`;}
function durationPercent(row:any){return Math.max(3,Math.round(Number(row.durationMs||0)*100/maxPageDuration.value));}

onMounted(async()=>{await loadNovels();if(novelId.value){await loadChapters();if(chapters.value.length===0)await rebuildChapters();}});
</script>

<template>
<div class="page-fill production-page">
  <PageHeader eyebrow="PRODUCTION STUDIO" title="专业制作中心" description="第二阶段：精确音频时间轴、真实波形、A/B 选片、QA 自动修复、章节与发音词典统一工作区。">
    <el-select v-model="novelId" filterable placeholder="选择小说" class="production-novel-select" @change="novelChanged"><el-option v-for="n in novels" :key="n.id" :label="n.title" :value="n.id"/></el-select>
    <el-button class="ghost-button" :loading="loading" @click="switchTab(activeTab)"><el-icon><Refresh/></el-icon>刷新</el-button>
  </PageHeader>

  <section class="glass-panel content-card production-shell" v-loading="loading">
    <div v-if="selectedNovel" class="production-current"><span>当前项目</span><b>{{selectedNovel.title}}</b><small v-if="timelineTotalDuration">整书已定位音频 {{formatDuration(timelineTotalDuration)}}</small></div>
    <el-tabs v-model="activeTab" class="production-tabs" @tab-change="switchTab">
      <el-tab-pane name="chapters" label="章节结构"><div class="production-tab-fill"><div class="production-toolbar"><div><span class="eyebrow">CHAPTER MAP</span><h3>卷 / 章节结构</h3><p>按小说标题行自动识别，重建时同步回填 Script Source Offset 和章节归属。</p></div><el-button class="neon-button" @click="rebuildChapters">重新识别章节</el-button></div><div class="table-flex-region"><el-table :data="chapters" height="100%" class="cyber-table"><el-table-column prop="chapterOrder" label="#" width="70"/><el-table-column prop="volumeTitle" label="卷" min-width="150"><template #default="{row}">{{row.volumeTitle||'—'}}</template></el-table-column><el-table-column prop="title" label="章节标题" min-width="260"/><el-table-column prop="scriptCount" label="脚本数" width="100"/><el-table-column label="原文区间" width="210"><template #default="{row}">{{row.sourceStart.toLocaleString()}} ~ {{row.sourceEnd.toLocaleString()}}</template></el-table-column></el-table></div></div></el-tab-pane>

      <el-tab-pane name="timeline" label="音频时间轴"><div class="production-tab-fill"><div class="production-toolbar timeline-summary-toolbar"><div><span class="eyebrow">AUDIO TIMELINE</span><h3>脚本 · 原文 · 音频时间轴</h3><p>FFprobe 读取真实 WAV 时长；修改/选片后可一键重建整书累计时间。</p></div><div class="timeline-summary-actions"><span><el-icon><Clock/></el-icon>{{formatDuration(timelineTotalDuration)}}</span><el-button class="ghost-button" @click="recalculateTimeline">重建音频时间轴</el-button></div></div><div class="list-filter-bar"><el-select v-model="timelineQuery.chapterId" clearable filterable placeholder="全部章节"><el-option v-for="c in chapters" :key="c.id" :label="c.chapterOrder+'. '+c.title" :value="c.id"/></el-select><el-input v-model="timelineQuery.keyword" clearable placeholder="搜索角色 / 文本" @keyup.enter="searchTimeline"><template #prefix><el-icon><Search/></el-icon></template></el-input><el-button class="neon-button" @click="searchTimeline">查询</el-button></div><div class="table-flex-region"><el-table :data="timeline" height="100%" class="cyber-table timeline-table"><el-table-column prop="order" label="#" width="65"/><el-table-column prop="chapterTitle" label="章节" min-width="140" show-overflow-tooltip/><el-table-column prop="speaker" label="角色" width="105"/><el-table-column prop="text" label="脚本文本" min-width="300" show-overflow-tooltip/><el-table-column label="音频时间" width="190"><template #default="{row}"><div class="timecode-cell"><b>{{formatTimecode(row.audioStartMs)}} → {{formatTimecode(row.audioEndMs)}}</b><small>{{row.durationMs?formatDuration(row.durationMs):'未定位时长'}}</small><div class="duration-bar"><i :style="{width:durationPercent(row)+'%'}"></i></div></div></template></el-table-column><el-table-column label="原文定位" width="150"><template #default="{row}"><span :class="row.sourceStart>=0?'offset-ok':'offset-missing'">{{offsetText(row)}}</span></template></el-table-column><el-table-column label="状态" width="100"><template #default="{row}"><StatusBadge :status="row.status"/></template></el-table-column><el-table-column label="版本" width="75"><template #default="{row}">{{row.versionCount||0}}</template></el-table-column><el-table-column label="制作" width="285" fixed="right"><template #default="{row}"><el-button text type="primary" @click="openWaveform(row)"><el-icon><VideoPlay/></el-icon>波形</el-button><el-button text @click="openVersions(row)"><el-icon><Files/></el-icon>A/B</el-button><el-button text @click="openEdit(row)"><el-icon><EditPen/></el-icon>编辑</el-button><el-button text @click="regenerate(row)"><el-icon><Headset/></el-icon>重生</el-button></template></el-table-column></el-table></div><ListPager v-model:page="timelineQuery.page" v-model:page-size="timelineQuery.pageSize" :total="timelineTotal" @change="loadTimeline"/></div></el-tab-pane>

      <el-tab-pane name="qa" label="质量检测 QA"><div class="production-tab-fill"><div class="production-toolbar qa-toolbar"><div><span class="eyebrow">QUALITY ASSURANCE</span><h3>有声书质量检测</h3><p>检测缺音色、Offset、时间轴、顺序、重复、超长、音频失败和文件丢失。</p></div><div class="qa-actions"><el-select v-model="qaFilter.severity" clearable placeholder="全部级别" @change="loadQa"><el-option label="错误" value="Error"/><el-option label="警告" value="Warning"/></el-select><el-button class="ghost-button" @click="autoFixQa"><el-icon><MagicStick/></el-icon>自动修复</el-button><el-button class="neon-button" @click="runQa">执行检测</el-button></div></div><div class="qa-metrics"><div class="mini-stat"><span>问题总数</span><b>{{qaSummary.total}}</b></div><div class="mini-stat"><span>错误</span><b>{{qaSummary.errors}}</b></div><div class="mini-stat"><span>警告</span><b>{{qaSummary.warnings}}</b></div></div><div class="table-flex-region"><el-table :data="qaRows" height="100%" class="cyber-table"><el-table-column label="级别" width="90"><template #default="{row}"><span :class="row.severity==='Error'?'qa-error':'qa-warning'">{{row.severity==='Error'?'错误':'警告'}}</span></template></el-table-column><el-table-column prop="type" label="类型" width="190"/><el-table-column prop="scriptLineId" label="脚本ID" width="95"><template #default="{row}">{{row.scriptLineId||'—'}}</template></el-table-column><el-table-column prop="message" label="问题说明" min-width="420"/><el-table-column label="状态" width="100"><template #default="{row}">{{row.resolved?'已处理':'待处理'}}</template></el-table-column><el-table-column label="操作" width="100"><template #default="{row}"><el-button text type="primary" @click="resolveQa(row)"><el-icon><Check/></el-icon>{{row.resolved?'重新打开':'标记处理'}}</el-button></template></el-table-column></el-table></div></div></el-tab-pane>

      <el-tab-pane name="pronunciation" label="发音词典"><div class="production-tab-fill"><div class="production-toolbar"><div><span class="eyebrow">PRONUNCIATION</span><h3>小说发音词典</h3><p>原文和脚本保持不变，仅在 TTS 生成前替换读音文本。</p></div><el-button class="neon-button" @click="addPronunciation"><el-icon><Plus/></el-icon>新增规则</el-button></div><div class="table-flex-region"><el-table :data="pronunciations" height="100%" class="cyber-table"><el-table-column prop="pattern" label="原词" min-width="180"/><el-table-column prop="replacement" label="TTS 发音替换" min-width="220"/><el-table-column prop="note" label="备注" min-width="260"/><el-table-column label="启用" width="90"><template #default="{row}">{{row.isEnabled?'是':'否'}}</template></el-table-column><el-table-column label="操作" width="150"><template #default="{row}"><el-button text type="primary" @click="editPronunciation(row)">编辑</el-button><el-button text type="danger" @click="removePronunciation(row)"><el-icon><Delete/></el-icon>删除</el-button></template></el-table-column></el-table></div></div></el-tab-pane>
    </el-tabs>
  </section>

  <el-dialog v-model="editVisible" title="编辑时间轴脚本" width="720px" class="theme-dialog"><el-form v-if="editRow" label-position="top"><div class="voice-form-grid"><el-form-item label="角色"><el-input v-model="editRow.speaker"/></el-form-item><el-form-item label="情绪"><el-input v-model="editRow.emotion"/></el-form-item></div><el-form-item label="朗读文本"><el-input v-model="editRow.text" type="textarea" :rows="8"/></el-form-item><div class="timeline-edit-meta">原文位置：{{offsetText(editRow)}} · 修改文本后旧音频会自动失效。</div></el-form><template #footer><el-button @click="editVisible=false">取消</el-button><el-button class="neon-button" @click="saveTimeline">保存脚本</el-button></template></el-dialog>

  <el-dialog v-model="waveformVisible" :title="'真实音频波形 · '+(waveformRow?.speaker||'')" width="900px" class="theme-dialog waveform-dialog"><div v-if="waveformRow" class="waveform-panel" v-loading="waveformLoading"><div class="waveform-meta"><span>#{{waveformRow.order}}</span><b>{{waveformRow.text}}</b><small>{{formatTimecode(waveformRow.audioStartMs)}} → {{formatTimecode(waveformRow.audioEndMs)}} · {{formatDuration(waveformRow.durationMs)}}</small></div><canvas ref="waveformCanvas" class="waveform-canvas"></canvas><audio controls preload="metadata" :src="audioApi.segmentPlayUrl(waveformRow.id)" class="waveform-player"></audio></div></el-dialog>

  <el-dialog v-model="versionsVisible" :title="'A/B 音频版本 · '+(versionRow?.speaker||'')" width="880px" class="theme-dialog"><div class="version-dialog-head"><div><span class="eyebrow">VOICE TAKES</span><b>{{versionRow?.text}}</b></div><el-button class="neon-button" :loading="versionLoading" @click="generateVersion">生成新版本</el-button></div><div v-if="versions.length" class="audio-version-list"><article v-for="v in versions" :key="v.id" class="audio-version-card" :class="{selected:v.isSelected}"><div><span>V{{v.versionNo}}</span><b>{{formatDuration(v.durationMs)}}</b><small>{{v.isSelected?'当前采用版本':'候选版本'}}</small></div><audio controls preload="metadata" :src="productionApi.versionPlayUrl(v.id)"></audio><div><el-button v-if="!v.isSelected" text type="primary" @click="selectVersion(v)">设为采用版本</el-button><el-button v-if="!v.isSelected" text type="danger" @click="removeVersion(v)">删除</el-button></div></article></div><div v-else class="reader-empty">尚未生成 A/B 候选版本，点击“生成新版本”创建第一个候选音频。</div></el-dialog>

  <el-dialog v-model="pronunciationVisible" :title="pronunciationForm.id?'编辑发音规则':'新增发音规则'" width="620px" class="theme-dialog"><el-form label-position="top"><div class="voice-form-grid"><el-form-item label="原词"><el-input v-model="pronunciationForm.pattern" placeholder="例如：单于"/></el-form-item><el-form-item label="TTS 发音替换"><el-input v-model="pronunciationForm.replacement" placeholder="例如：蝉于"/></el-form-item></div><el-form-item label="备注"><el-input v-model="pronunciationForm.note" placeholder="人物名 / 地名 / 生僻字说明"/></el-form-item><el-form-item label="启用"><el-switch v-model="pronunciationForm.isEnabled"/></el-form-item></el-form><template #footer><el-button @click="pronunciationVisible=false">取消</el-button><el-button class="neon-button" @click="savePronunciation">保存规则</el-button></template></el-dialog>
</div>
</template>
