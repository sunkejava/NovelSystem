<script setup lang="ts">
defineProps<{label:string;value:string|number;hint?:string;accent?:string}>();
</script>
<template><div class="metric-card glass-panel"><div class="metric-glow" :style="{background:accent||'var(--neon-cyan)'}"></div><span>{{label}}</span><strong>{{value}}</strong><small>{{hint}}</small></div></template>