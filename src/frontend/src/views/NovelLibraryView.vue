<script setup lang="ts">
import {onMounted,ref} from 'vue';
import {useRouter} from 'vue-router';
import {ElMessage,ElMessageBox} from 'element-plus';
import {UploadFilled,MagicStick,Edit,Delete} from '@element-plus/icons-vue';
import {novelApi} from '../api/novels';
import {writingApi} from '../api/writing';
import PageHeader from '../components/PageHeader.vue';
import StatusBadge from '../components/StatusBadge.vue';
import EmptyState from '../components/EmptyState.vue';

const router=useRouter();
const novels=ref<any[]>([]);
const uploading=ref(false);
const editorVisible=ref(false);
const editorLoading=ref(false);
const editorForm=ref({id:0,title:'',content:''});

async function load(){novels.value=await novelApi.list();}

async function upload(o:any){
  uploading.value=true;
  try{
    await novelApi.upload(o.file);
    o.onSuccess();
    ElMessage.success('小说导入成功，已自动识别文本编码');
    await load();
  }catch(e){o.onError(e);}
  finally{uploading.value=false;}
}

async function learn(row:any){
  await writingApi.learn(row.id);
  router.push('/writing');
}

async function openEditor(row:any){
  editorLoading.value=true;
  editorVisible.value=true;
  try{
    const detail=await novelApi.detail(row.id);
    editorForm.value={id:row.id,title:detail.novel.title,content:detail.novel.content};
  }finally{editorLoading.value=false;}
}

async function saveNovel(){
  await novelApi.update(editorForm.value.id,{title:editorForm.value.title,content:editorForm.value.content});
  ElMessage.success('小说资产已保存');
  editorVisible.value=false;
  await load();
}

async function removeNovel(row:any){
  await ElMessageBox.confirm(
    '删除小说“'+row.title+'”后，将同时删除人物、脚本等解析数据。此操作不可恢复。',
    '删除小说资产',
    {type:'warning',confirmButtonText:'确认删除',cancelButtonText:'取消'}
  );
  await novelApi.remove(row.id);
  ElMessage.success('小说资产已删除');
  await load();
}

onMounted(load);
</script>

<template>
<div>
  <PageHeader eyebrow="NOVEL ASSETS" title="小说资产库" description="导入原始小说，建立可持续解析、配音、学习与再创作的知识资产。">
    <el-upload :http-request="upload" :show-file-list="false" accept=".txt">
      <el-button class="neon-button" :loading="uploading"><el-icon><UploadFilled/></el-icon>导入小说</el-button>
    </el-upload>
  </PageHeader>

  <section class="glass-panel content-card" v-if="novels.length">
    <div class="novel-grid">
      <article v-for="novel in novels" :key="novel.id" class="novel-card" @click="router.push('/novels/'+novel.id)">
        <div class="book-holo"><span>N</span><i></i></div>
        <div class="novel-info">
          <StatusBadge :status="novel.status"/>
          <h3>{{novel.title}}</h3>
          <p>{{novel.sourceFile}}</p>
          <small>{{novel.createdAt}}</small>
        </div>
        <div class="novel-actions">
          <el-button text @click.stop="openEditor(novel)"><el-icon><Edit/></el-icon>编辑</el-button>
          <el-button text @click.stop="learn(novel)"><el-icon><MagicStick/></el-icon>学习写法</el-button>
          <el-button text type="danger" @click.stop="removeNovel(novel)"><el-icon><Delete/></el-icon>删除</el-button>
        </div>
      </article>
    </div>
  </section>

  <EmptyState v-else title="等待第一部小说" description="上传 TXT 小说后，系统会自动识别 UTF-8、UTF-16、GBK/GB18030 等常见编码。"/>

  <el-dialog v-model="editorVisible" title="编辑小说资产" width="78%" class="theme-dialog" destroy-on-close>
    <div v-loading="editorLoading">
      <el-form label-position="top">
        <el-form-item label="小说标题"><el-input v-model="editorForm.title"/></el-form-item>
        <el-form-item label="小说正文">
          <el-input v-model="editorForm.content" type="textarea" :rows="26" resize="vertical"/>
        </el-form-item>
      </el-form>
    </div>
    <template #footer>
      <el-button class="ghost-button" @click="editorVisible=false">取消</el-button>
      <el-button class="neon-button" @click="saveNovel">保存修改</el-button>
    </template>
  </el-dialog>
</div>
</template>