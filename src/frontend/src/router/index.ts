import {createRouter,createWebHistory,type RouteRecordRaw} from 'vue-router';
import AppLayout from '../layouts/AppLayout.vue';

const routes:RouteRecordRaw[]=[
  {
    path:'/',
    component:AppLayout,
    children:[
      {path:'',redirect:'/dashboard'},
      {path:'dashboard',component:()=>import('../views/DashboardView.vue')},
      {path:'novels',component:()=>import('../views/NovelLibraryView.vue')},
      {path:'novels/:id',component:()=>import('../views/NovelWorkspaceView.vue')},
      {path:'production',component:()=>import('../views/ProductionStudioView.vue')},
      {path:'jobs',component:()=>import('../views/JobCenterView.vue')},
      {path:'styles',component:()=>import('../views/WritingStyleView.vue')},
      {path:'writing',component:()=>import('../views/WritingStudioView.vue')},
      {path:'writing/generated/:id',component:()=>import('../views/GeneratedNovelView.vue')},
      {path:'tokens',component:()=>import('../views/TokenUsageView.vue')},
      {path:'settings',component:()=>import('../views/SettingsView.vue')}
    ]
  }
];

export default createRouter({history:createWebHistory(),routes});