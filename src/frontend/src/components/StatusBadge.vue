<script setup lang="ts">
defineProps<{status:string}>();
const map:Record<string,string>={Completed:'success',Analyzed:'success',Running:'running',Analyzing:'running',Queued:'queued',Uploaded:'queued',Failed:'danger',Pending:'queued'};
</script>
<template><span class="status-badge" :class="map[status]||'queued'"><i></i>{{status}}</span></template>