import { createRouter, createWebHashHistory } from 'vue-router'
import { useAuth } from '../stores/auth'

const router = createRouter({
  history: createWebHashHistory(),
  routes: [
    { path: '/login', name: 'login', component: () => import('../views/Login.vue'), meta: { public: true } },
    {
      path: '/',
      component: () => import('../views/Layout.vue'),
      children: [
        { path: '', redirect: '/manage' },
        {
          path: 'manage',
          component: () => import('../views/Workspace.vue'),
          children: [
            { path: '', name: 'workspace', component: () => import('../views/WorkspaceHome.vue') },
            { path: 'servers/:id', name: 'scopes', component: () => import('../views/Scopes.vue'), props: true },
            {
              path: 'servers/:id/scopes/:scopeId',
              name: 'scope-detail',
              component: () => import('../views/ScopeDetail.vue'),
              props: true,
            },
          ],
        },
        { path: 'lease-logs', name: 'lease-logs', component: () => import('../views/LeaseLogs.vue') },
        { path: 'alerts', name: 'alerts', component: () => import('../views/AlertConfig.vue') },
        { path: 'users', name: 'users', component: () => import('../views/Users.vue'), meta: { adminOnly: true } },
      ],
    },
  ],
})

// 始终生效(含 embed):未登录跳登录;已登录未加载用户信息先拉取;非 Admin 不得进用户管理
router.beforeEach(async (to) => {
  const auth = useAuth()
  if (to.meta.public) return true
  if (!auth.isAuthenticated) return { name: 'login', query: { redirect: to.fullPath } }
  if (!auth.user) {
    try {
      await auth.loadMe()
    } catch {
      auth.logout()
      return { name: 'login', query: { redirect: to.fullPath } }
    }
  }
  if (to.meta.adminOnly && !auth.isAdmin) return { name: 'workspace' }
  return true
})

export default router
