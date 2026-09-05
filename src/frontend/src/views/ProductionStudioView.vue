<script setup lang="ts">
import {computed,onMounted,ref} from 'vue';
import {ElMessage,ElMessageBox} from 'element-plus';
import {Refresh,Search,EditPen,Headset,Check,WarningFilled,Plus,Delete} from '@element-plus/icons-vue';
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
const timeline=ref<any[]>([]),timelineTotal=ref(0);
const timelineQuery=ref({page:1,pageSize:50,chapterId:undefined as number|undefined,keyword:''});
const editVisible=ref(false),editRow=ref<any>(null);

const qaRows=ref<any[]>([]),qaFilter=ref({resolved:false as boolean|undefined,severity:''});
const qaSummary=computed(()=>({
  total:qaRows.value.length,
  errors:qaRows.value.filter(x=>x.severity==='Error'&&!x.resolved).length,
  warnings:qaRows.value.filter(x=>x.severity==='Warning'&&!x.resolved).length
}));

const pronunciations=ref<any[]>([]);
const pronunciationVisible=ref(false),pronunciationForm=ref<any>({id:null,pattern:'',replacement:'',note:'',isEnabled:true});
const selectedNovel=computed(()=>novels.value.find(x=>x.id===novelId.value));

async function loadNovels(){
  const r=await novelApi.list({page:1,pageSize:100});
  novels.value=r.items||r;
  if(!novelId.value&&novels.value.length)novelId.value=novels.value[0].id;
}
async function ensureNovel(){
  if(!novelId.value)return false;
  return true;
}
async function loadChapters(){
  if(!await ensureNovel())return;
  chapters.value=await productionApi.chapters(novelId.value!);
}
async function rebuildChapters(){
  if(!await ensureNovel())return;
  loading.value=true;
  try{
    const r=await productionApi.rebuildChapters(novelId.value!);
    ElMessage.success('章节结构已重建，共识别 '+r.chapters+' 个章节，并已回填脚本原文位置');
    await Promise.all([loadChapters(),loadTimeline()]);
  }finally{loading.value=false;}
}
async function loadTimeline(){
  if(!await ensureNovel())return;
  const r=await productionApi.timeline(novelId.value!,timelineQuery.value);
  timeline.value=r.items;timelineTotal.value=r.total;
}
function searchTimeline(){timelineQuery.value.page=1;loadTimeline();}
function openEdit(row:any){editRow.value={...row};editVisible.value=true;}
async function saveTimeline(){
  if(!editRow.value)return;
  await productionApi.updateTimeline(editRow.value.id,{speaker:editRow.value.speaker,text:editRow.value.text,emotion:editRow.value.emotion});
  ElMessage.success('脚本已保存；如内容发生变化，旧音频已自动失效');
  editVisible.value=false;
  await loadTimeline();
}
async function regenerate(row:any){
  await audioApi.generateSegment(row.id);
  ElMessage.success('该片段已进入音频生成队列');
}
async function loadQa(){
  if(!await ensureNovel())return;
  qaRows.value=await productionApi.qa(novelId.value!,qaFilter.value);
}
async function runQa(){
  if(!await ensureNovel())return;
  loading.value=true;
  try{
    const r=await productionApi.runQa(novelId.value!);
    ElMessage.success('质量检测完成：'+r.errors+' 个错误，'+r.warnings+' 个警告');
    await loadQa();
  }finally{loading.value=false;}
}
async function resolveQa(row:any){await productionApi.resolveQa(row.id,!row.resolved);await loadQa();}
async function loadPronunciations(){if(await ensureNovel())pronunciations.value=await productionApi.pronunciations(novelId.value!);}
function addPronunciation(){pronunciationForm.value={id:null,pattern:'',replacement:'',note:'',isEnabled:true};pronunciationVisible.value=true;}
function editPronunciation(row:any){pronunciationForm.value={id:row.id,pattern:row.pattern,replacement:row.replacement,note:row.note||'',isEnabled:row.isEnabled};pronunciationVisible.value=true;}
async function savePronunciation(){
  const f=pronunciationForm.value;
  if(!f.pattern?.trim()||!f.replacement?.trim()){ElMessage.warning('请填写原词和发音替换');return;}
  f.id?await productionApi.updatePronunciation(f.id,f):await productionApi.createPronunciation(novelId.value!,f);
  ElMessage.success('发音词典已保存');pronunciationVisible.value=false;await loadPronunciations();
}
async function removePronunciation(row:any){
  await ElMessageBox.confirm('确认删除“'+row.pattern+'”的发音规则？','删除发音规则',{type:'warning'});
  await productionApi.removePronunciation(row.id);await loadPronunciations();
}
async function switchTab(name:string|number){
  activeTab.value=String(name);
  if(activeTab.value==='chapters')await loadChapters();
  if(activeTab.value==='timeline')await Promise.all([loadChapters(),loadTimeline()]);
  if(activeTab.value==='qa')await loadQa();
  if(activeTab.value==='pronunciation')await loadPronunciations();
}
async function novelChanged(){timelineQuery.value={page:1,pageSize:50,chapterId:undefined,keyword:''};await switchTab(activeTab.value);}
function offsetText(row:any){return row.sourceStart>=0?row.sourceStart.toLocaleString()+' ~ '+row.sourceEnd.toLocaleString():'未定位';}

onMounted(async()=>{await loadNovels();if(novelId.value){await loadChapters();if(chapters.value.length===0)await rebuildChapters();}});
</script>

<template>
<div class="page-fill production-page">
  <PageHeader eyebrow="PRODUCTION STUDIO" title="专业制作中心" description="章节结构、原文定位、质量检测、发音词典与时间轴编辑统一工作区。">
    <el-select v-model="novelId" filterable placeholder="选择小说" class="production-novel-select" @change="novelChanged">
      <el-option v-for="n in novels" :key="n.id" :label="n.title" :value="n.id"/>
    </el-select>
    <el-button class="ghost-button" :loading="loading" @click="switchTab(activeTab)"><el-icon><Refresh/></el-icon>刷新</el-button>
  </PageHeader>

  <section class="glass-panel content-card production-shell" v-loading="loading">
    <div v-if="selectedNovel" class="production-current"><span>当前项目</span><b>{{selectedNovel.title}}</b></div>
    <el-tabs v-model="activeTab" class="production-tabs" @tab-change="switchTab">
      <el-tab-pane name="chapters" label="章节结构">
        <div class="production-tab-fill">
          <div class="production-toolbar">
            <div><span class="eyebrow">CHAPTER MAP</span><h3>卷 / 章节结构</h3><p>按小说标题行自动识别，重建时同步回填 Script Source Offset 和章节归属。</p></div>
            <el-button class="neon-button" @click="rebuildChapters">重新识别章节</el-button>
          </div>
          <div class="table-flex-region">
            <el-table :data="chapters" height="100%" class="cyber-table">
              <el-table-column prop="chapterOrder" label="#" width="70"/>
              <el-table-column prop="volumeTitle" label="卷" min-width="150"><template #default="{row}">{{row.volumeTitle||'—'}}</template></el-table-column>
              <el-table-column prop="title" label="章节标题" min-width="260"/>
              <el-table-column prop="scriptCount" label="脚本数" width="100"/>
              <el-table-column label="原文区间" width="210"><template #default="{row}">{{row.sourceStart.toLocaleString()}} ~ {{row.sourceEnd.toLocaleString()}}</template></el-table-column>
            </el-table>
          </div>
        </div>
      </el-tab-pane>

      <el-tab-pane name="timeline" label="时间轴编辑">
        <div class="production-tab-fill">
          <div class="list-filter-bar">
            <el-select v-model="timelineQuery.chapterId" clearable filterable placeholder="全部章节"><el-option v-for="c in chapters" :key="c.id" :label="c.chapterOrder+'. '+c.title" :value="c.id"/></el-select>
            <el-input v-model="timelineQuery.keyword" clearable placeholder="搜索角色 / 文本" @keyup.enter="searchTimeline"><template #prefix><el-icon><Search/></el-icon></template></el-input>
            <el-button class="neon-button" @click="searchTimeline">查询</el-button>
          </div>
          <div class="table-flex-region">
            <el-table :data="timeline" height="100%" class="cyber-table timeline-table">
              <el-table-column prop="order" label="#" width="70"/>
              <el-table-column prop="chapterTitle" label="章节" min-width="160" show-overflow-tooltip/>
              <el-table-column prop="speaker" label="角色" width="120"/>
              <el-table-column prop="text" label="脚本文本" min-width="420" show-overflow-tooltip/>
              <el-table-column prop="emotion" label="情绪" width="110"/>
              <el-table-column label="原文定位" width="180"><template #default="{row}"><span :class="row.sourceStart>=0?'offset-ok':'offset-missing'">{{offsetText(row)}}</span></template></el-table-column>
              <el-table-column label="音频" width="105"><template #default="{row}"><StatusBadge :status="row.status"/></template></el-table-column>
              <el-table-column label="操作" width="180" fixed="right"><template #default="{row}"><el-button text type="primary" @click="openEdit(row)"><el-icon><EditPen/></el-icon>编辑</el-button><el-button text @click="regenerate(row)"><el-icon><Headset/></el-icon>重生音频</el-button></template></el-table-column>
            </el-table>
          </div>
          <ListPager v-model:page="timelineQuery.page" v-model:page-size="timelineQuery.pageSize" :total="timelineTotal" @change="loadTimeline"/>
        </div>
      </el-tab-pane>

      <el-tab-pane name="qa" label="质量检测 QA">
        <div class="production-tab-fill">
          <div class="production-toolbar qa-toolbar">
            <div><span class="eyebrow">QUALITY ASSURANCE</span><h3>有声书质量检测</h3><p>检测缺音色、原文定位失败、顺序跳号、重复脚本、超长片段、音频失败与文件丢失。</p></div>
            <div class="qa-actions"><el-select v-model="qaFilter.severity" clearable placeholder="全部级别" @change="loadQa"><el-option label="错误" value="Error"/><el-option label="警告" value="Warning"/></el-select><el-button class="neon-button" @click="runQa">执行质量检测</el-button></div>
          </div>
          <div class="qa-metrics"><div class="mini-stat"><span>问题总数</span><b>{{qaSummary.total}}</b></div><div class="mini-stat"><span>错误</span><b>{{qaSummary.errors}}</b></div><div class="mini-stat"><span>警告</span><b>{{qaSummary.warnings}}</b></div></div>
          <div class="table-flex-region">
            <el-table :data="qaRows" height="100%" class="cyber-table">
              <el-table-column label="级别" width="90"><template #default="{row}"><span :class="row.severity==='Error'?'qa-error':'qa-warning'">{{row.severity==='Error'?'错误':'警告'}}</span></template></el-table-column>
              <el-table-column prop="type" label="类型" width="190"/>
              <el-table-column prop="scriptLineId" label="脚本ID" width="95"><template #default="{row}">{{row.scriptLineId||'—'}}</template></el-table-column>
              <el-table-column prop="message" label="问题说明" min-width="420"/>
              <el-table-column label="状态" width="100"><template #default="{row}">{{row.resolved?'已处理':'待处理'}}</template></el-table-column>
              <el-table-column label="操作" width="100"><template #default="{row}"><el-button text type="primary" @click="resolveQa(row)"><el-icon><Check/></el-icon>{{row.resolved?'重新打开':'标记处理'}}</el-button></template></el-table-column>
            </el-table>
          </div>
        </div>
      </el-tab-pane>

      <el-tab-pane name="pronunciation" label="发音词典">
        <div class="production-tab-fill">
          <div class="production-toolbar">
            <div><span class="eyebrow">PRONUNCIATION</span><h3>小说发音词典</h3><p>原文和脚本保持不变，仅在 TTS 生成前替换读音文本；适合人名、地名、生僻字和专有名词。</p></div>
            <el-button class="neon-button" @click="addPronunciation"><el-icon><Plus/></el-icon>新增规则</el-button>
          </div>
          <div class="table-flex-region">
            <el-table :data="pronunciations" height="100%" class="cyber-table">
              <el-table-column prop="pattern" label="原词" min-width="180"/>
              <el-table-column prop="replacement" label="TTS 发音替换" min-width="220"/>
              <el-table-column prop="note" label="备注" min-width="260"/>
              <el-table-column label="启用" width="90"><template #default="{row}">{{row.isEnabled?'是':'否'}}</template></el-table-column>
              <el-table-column label="操作" width="150"><template #default="{row}"><el-button text type="primary" @click="editPronunciation(row)">编辑</el-button><el-button text type="danger" @click="removePronunciation(row)"><el-icon><Delete/></el-icon>删除</el-button></template></el-table-column>
            </el-table>
          </div>
        </div>
      </el-tab-pane>
    </el-tabs>
  </section>

  <el-dialog v-model="editVisible" title="编辑时间轴脚本" width="720px" class="theme-dialog">
    <el-form v-if="editRow" label-position="top">
      <div class="voice-form-grid"><el-form-item label="角色"><el-input v-model="editRow.speaker"/></el-form-item><el-form-item label="情绪"><el-input v-model="editRow.emotion"/></el-form-item></div>
      <el-form-item label="朗读文本"><el-input v-model="editRow.text" type="textarea" :rows="8"/></el-form-item>
      <div class="timeline-edit-meta">原文位置：{{offsetText(editRow)}} · 修改文本后旧音频会自动失效，需要重新生成。</div>
    </el-form>
    <template #footer><el-button @click="editVisible=false">取消</el-button><el-button class="neon-button" @click="saveTimeline">保存脚本</el-button></template>
  </el-dialog>

  <el-dialog v-model="pronunciationVisible" :title="pronunciationForm.id?'编辑发音规则':'新增发音规则'" width="620px" class="theme-dialog">
    <el-form label-position="top">
      <div class="voice-form-grid"><el-form-item label="原词"><el-input v-model="pronunciationForm.pattern" placeholder="例如：单于"/></el-form-item><el-form-item label="TTS 发音替换"><el-input v-model="pronunciationForm.replacement" placeholder="例如：蝉于 / shan yu（按模型效果填写）"/></el-form-item></div>
      <el-form-item label="备注"><el-input v-model="pronunciationForm.note" placeholder="人物名、地名、专有词等"/></el-form-item>
      <el-form-item label="启用"><el-switch v-model="pronunciationForm.isEnabled"/></el-form-item>
    </el-form>
    <template #footer><el-button @click="pronunciationVisible=false">取消</el-button><el-button class="neon-button" @click="savePronunciation">保存规则</el-button></template>
  </el-dialog>
</div>
</template>
