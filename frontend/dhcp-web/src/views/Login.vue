<script setup lang="ts">
import { ref } from 'vue'
import { useRoute, useRouter } from 'vue-router'
import { ElMessage } from 'element-plus'
import { useAuth } from '../stores/auth'

const auth = useAuth()
const route = useRoute()
const router = useRouter()

const username = ref('')
const password = ref('')
const loading = ref(false)

async function submit() {
  if (!username.value || !password.value) {
    ElMessage.warning('请输入用户名和密码')
    return
  }
  loading.value = true
  try {
    await auth.login(username.value.trim(), password.value)
    const redirect = (route.query.redirect as string) || '/manage'
    router.replace(redirect)
  } catch {
    // 错误提示由 http 拦截器统一处理
  } finally {
    loading.value = false
  }
}
</script>

<template>
  <div class="login-wrap">
    <div class="login-card">
      <div class="login-brand">
        <div class="login-logo"><el-icon><Monitor /></el-icon></div>
        <div class="login-title">DHCP 管理平台</div>
        <div class="login-sub">请登录后使用</div>
      </div>
      <el-form class="login-form" @submit.prevent="submit">
        <el-input v-model="username" placeholder="用户名" size="large" :prefix-icon="'User'" @keyup.enter="submit" />
        <el-input
          v-model="password"
          type="password"
          placeholder="密码"
          size="large"
          show-password
          :prefix-icon="'Lock'"
          @keyup.enter="submit"
        />
        <el-button type="primary" size="large" :loading="loading" class="login-btn" @click="submit">
          登录
        </el-button>
      </el-form>
    </div>
  </div>
</template>

<style scoped>
.login-wrap {
  height: 100vh;
  display: flex;
  align-items: center;
  justify-content: center;
  background: var(--app-bg);
}
.login-card {
  width: 360px;
  padding: 36px 32px;
  border-radius: 14px;
  background: var(--app-panel);
  border: 1px solid var(--app-border);
  box-shadow: 0 8px 30px rgba(0, 0, 0, 0.08);
}
.login-brand {
  text-align: center;
  margin-bottom: 24px;
}
.login-logo {
  width: 48px;
  height: 48px;
  margin: 0 auto 12px;
  border-radius: 12px;
  background: var(--app-brand);
  color: #fff;
  display: flex;
  align-items: center;
  justify-content: center;
  font-size: 24px;
}
.login-title {
  font-size: 18px;
  font-weight: 700;
  color: var(--app-text);
}
.login-sub {
  font-size: 12px;
  color: var(--app-text-muted);
  margin-top: 4px;
}
.login-form {
  display: flex;
  flex-direction: column;
  gap: 16px;
}
.login-btn {
  width: 100%;
  margin-top: 4px;
}
</style>
