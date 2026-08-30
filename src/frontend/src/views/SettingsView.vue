<script setup lang="ts">
import {onMounted,ref} from 'vue';import {ElMessage} from 'element-plus';import {settingApi} from '../api/settings';import PageHeader from '../components/PageHeader.vue';
const form=ref<Record<string,string>>({});const voices=ref<any[]>([]);
async function load(){form.value=await settingApi.get();voices.value=await settingApi.voices();}
async function save(){await settingApi.save(form.value);ElMessage.success('模型与运行参数已保存');await load();}
onMounted(load);
</script>
<template><div><PageHeader eyebrow="MODEL MATRIX" title="模型与运行设置" description="集中配置本地 llama.cpp、Qwen3-TTS、声音资产目录和 FFmpeg。"><el-button class="neon-button" @click="save">保存配置</el-button></PageHeader>
<div class="settings-grid"><section class="glass-panel content-card"><div class="section-title"><span>01</span><div><h3>LLM 推理引擎</h3><p>OpenAI-compatible llama.cpp endpoint</p></div></div><el-form label-position="top"><el-form-item label="API Base URL"><el-input v-model="form.AiBaseUrl"/></el-form-item><el-form-item label="Model / Alias"><el-input v-model="form.AiModel"/></el-form-item></el-form></section>
<section class="glass-panel content-card"><div class="section-title"><span>02</span><div><h3>Qwen3-TTS 声音引擎</h3><p>多角色声音克隆与语音生成</p></div></div><el-form label-position="top"><el-form-item label="TTS Base URL"><el-input v-model="form.TtsBaseUrl"/></el-form-item><el-form-item label="TTS Endpoint"><el-input v-model="form.TtsEndpoint"/></el-form-item></el-form></section>
<section class="glass-panel content-card"><div class="section-title"><span>03</span><div><h3>本地资源</h3><p>声音样本与音频处理工具</p></div></div><el-form label-position="top"><el-form-item label="WAV 音色目录"><el-input v-model="form.VoiceDirectory"/></el-form-item><el-form-item label="FFmpeg 路径"><el-input v-model="form.FfmpegPath"/></el-form-item></el-form><div class="voice-count">{{voices.length}} <small>VOICE PROFILES DETECTED</small></div></section></div></div></template>