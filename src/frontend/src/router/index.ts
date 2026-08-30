import {createRouter,createWebHistory} from 'vue-router';
import AppLayout from '../layouts/AppLayout.vue';
const routes=[{path:'/',component:AppLayout,children:[
{path:'',redirect:'/dashboard'},
{path:'dashboard',component:()=>import('../views/DashboardView.vue')},
{path:'novels',component:()=>import('../views/NovelLibraryView.vue')},
{path:'novels/:id',component:()=>import('../views/NovelWorkspaceView.vue')},
{path:'jobs',component:()=>import('../views/JobCenterView.vue')},
{path:'writing',component:()=>import('../views/WritingStudioView.vue')},
{path:'settings',component:()=>import('../views/SettingsView.vue')}
]}];
export default createRouter({history:createWebHistory(),routes});