<script setup lang="ts">
defineProps<{status:string}>();
const map:Record<string,string>={
  Completed:'success',
  Analyzed:'success',
  PromptReady:'success',
  Ready:'queued',
  Running:'running',
  Analyzing:'running',
  Queued:'queued',
  Uploaded:'queued',
  Stopping:'warn',
  Stopped:'stopped',
  Failed:'danger',
  Pending:'queued',
  BuildingPrompt:'running'
};
</script>
<template><span class="status-badge" :class="map[status]||'queued'"><i></i>{{status}}</span></template>