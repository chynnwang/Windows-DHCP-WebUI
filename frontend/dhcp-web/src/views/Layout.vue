<script setup lang="ts">
import { useRoute, useRouter } from 'vue-router'
import { computed, reactive, ref } from 'vue'
import { ElMessage } from 'element-plus'
import { useTheme } from '../stores/theme'
import { useAuth } from '../stores/auth'
import { api } from '../api'

const theme = useTheme()
const auth = useAuth()
const route = useRoute()
const router = useRouter()
const isEmbedded = computed(() => new URLSearchParams(window.location.search).has('embed'))

const activeMenu = computed(() => {
  if (route.path.startsWith('/lease-logs')) return '/lease-logs'
  if (route.path.startsWith('/alerts')) return '/alerts'
  if (route.path.startsWith('/users')) return '/users'
  return '/manage'
})

function logout() {
  auth.logout()
  router.replace('/login')
}

// 修改密码
const pwdVisible = ref(false)
const pwdForm = reactive({ oldPassword: '', newPassword: '' })
function openChangePwd() {
  pwdForm.oldPassword = ''
  pwdForm.newPassword = ''
  pwdVisible.value = true
}
async function submitChangePwd() {
  if (pwdForm.newPassword.length < 6) return ElMessage.warning('新密码至少 6 位')
  await api.changePassword(pwdForm.oldPassword, pwdForm.newPassword)
  ElMessage.success('密码已修改')
  pwdVisible.value = false
}
</script>

<template>
  <el-container class="app-layout">
    <el-aside v-if="!isEmbedded" width="220px" class="app-aside">
      <div class="brand">
        <div class="brand-logo"><el-icon><Monitor /></el-icon></div>
        <div class="brand-text">
          <div class="brand-title">DHCP 管理</div>
          <div class="brand-sub">多工区统一管理平台</div>
        </div>
      </div>
      <el-menu :default-active="activeMenu" router class="app-menu">
        <el-menu-item index="/manage">
          <el-icon><Monitor /></el-icon>
          <span>工区 / 服务器</span>
        </el-menu-item>
        <el-menu-item index="/lease-logs">
          <el-icon><Tickets /></el-icon>
          <span>租约日志</span>
        </el-menu-item>
        <el-menu-item index="/alerts">
          <el-icon><Bell /></el-icon>
          <span>告警配置</span>
        </el-menu-item>
        <el-menu-item v-if="auth.isAdmin" index="/users">
          <el-icon><User /></el-icon>
          <span>用户管理</span>
        </el-menu-item>
      </el-menu>
    </el-aside>
    <el-container>
      <el-header v-if="!isEmbedded" class="app-header">
        <div class="spacer" />
        <el-tooltip :content="theme.mode === 'dark' ? '切换到浅色' : '切换到深色'" placement="bottom">
          <button class="theme-btn" @click="theme.toggle()">
            <el-icon v-if="theme.mode === 'dark'"><Sunny /></el-icon>
            <el-icon v-else><Moon /></el-icon>
          </button>
        </el-tooltip>
        <el-dropdown v-if="auth.user" trigger="click">
          <span class="user-chip">
            <el-icon><UserFilled /></el-icon>
            <span class="user-name">{{ auth.user.username }}</span>
            <el-tag size="small" :type="auth.isAdmin ? 'danger' : 'info'" effect="light">
              {{ auth.isAdmin ? '管理员' : '只读' }}
            </el-tag>
            <el-icon><ArrowDown /></el-icon>
          </span>
          <template #dropdown>
            <el-dropdown-menu>
              <el-dropdown-item @click="openChangePwd">修改密码</el-dropdown-item>
              <el-dropdown-item divided @click="logout">退出登录</el-dropdown-item>
            </el-dropdown-menu>
          </template>
        </el-dropdown>
      </el-header>
      <el-main class="app-main" :class="{ embedded: isEmbedded }">
        <router-view />
      </el-main>
    </el-container>

    <el-dialog v-model="pwdVisible" title="修改密码" width="420px">
      <el-form label-width="80px">
        <el-form-item label="原密码">
          <el-input v-model="pwdForm.oldPassword" type="password" show-password />
        </el-form-item>
        <el-form-item label="新密码">
          <el-input v-model="pwdForm.newPassword" type="password" show-password placeholder="至少 6 位" />
        </el-form-item>
      </el-form>
      <template #footer>
        <el-button @click="pwdVisible = false">取消</el-button>
        <el-button type="primary" @click="submitChangePwd">确定</el-button>
      </template>
    </el-dialog>
  </el-container>
</template>

<style scoped>
.app-layout {
  height: 100vh;
}
.app-aside {
  border-right: 1px solid var(--app-border);
  background: var(--app-sidebar-bg);
  display: flex;
  flex-direction: column;
}
.brand {
  display: flex;
  align-items: center;
  gap: 10px;
  height: 60px;
  padding: 0 16px;
  border-bottom: 1px solid var(--app-border);
}
.brand-logo {
  width: 34px;
  height: 34px;
  border-radius: 9px;
  background: var(--app-brand);
  color: #fff;
  display: flex;
  align-items: center;
  justify-content: center;
  font-size: 18px;
  flex-shrink: 0;
}
.brand-title {
  font-weight: 700;
  font-size: 15px;
  color: var(--app-text);
  line-height: 1.2;
}
.brand-sub {
  font-size: 11px;
  color: var(--app-text-muted);
  margin-top: 2px;
}
.app-menu {
  border-right: none;
  background: transparent;
  padding: 8px;
}
.app-menu :deep(.el-menu-item) {
  border-radius: 8px;
  margin-bottom: 4px;
  height: 44px;
}
.app-menu :deep(.el-menu-item.is-active) {
  background: var(--app-brand-soft);
  color: var(--app-brand);
  font-weight: 600;
}
.app-header {
  display: flex;
  align-items: center;
  background: var(--app-header-bg);
  border-bottom: 1px solid var(--app-border);
}
.spacer {
  flex: 1;
}
.theme-btn {
  width: 36px;
  height: 36px;
  border-radius: 9px;
  border: 1px solid var(--app-border);
  background: var(--app-panel-2);
  color: var(--app-text);
  cursor: pointer;
  display: flex;
  align-items: center;
  justify-content: center;
  font-size: 17px;
  margin-right: 12px;
  transition: all 0.15s;
}
.theme-btn:hover {
  color: var(--app-brand);
  border-color: var(--app-brand);
}
.user-chip {
  display: flex;
  align-items: center;
  gap: 6px;
  height: 36px;
  padding: 0 12px;
  margin-right: 16px;
  border-radius: 9px;
  border: 1px solid var(--app-border);
  background: var(--app-panel-2);
  color: var(--app-text);
  cursor: pointer;
  outline: none;
}
.user-chip:hover {
  border-color: var(--app-brand);
}
.user-name {
  font-size: 13px;
  font-weight: 600;
}
.app-main {
  background: var(--app-bg);
  padding: 20px;
}
.app-main.embedded {
  height: 100vh;
  padding: 18px;
}
</style>
