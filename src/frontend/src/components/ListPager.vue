<script setup lang="ts">
const props=defineProps<{page:number;pageSize:number;total:number}>();
const emit=defineEmits<{(e:'update:page',value:number):void;(e:'update:pageSize',value:number):void;(e:'change'):void}>();

function pageChange(value:number){emit('update:page',value);emit('change');}
function sizeChange(value:number){emit('update:pageSize',value);emit('update:page',1);emit('change');}
</script>
<template>
  <div class="list-pager" v-if="total>0">
    <span>共 {{total}} 条</span>
    <el-pagination
      background
      layout="sizes, prev, pager, next, jumper"
      :current-page="page"
      :page-size="pageSize"
      :page-sizes="[10,20,50,100]"
      :total="total"
      @current-change="pageChange"
      @size-change="sizeChange"
    />
  </div>
</template>