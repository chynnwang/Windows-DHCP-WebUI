<script setup lang="ts">
import { computed, onMounted, ref } from 'vue'
import { ElMessage } from 'element-plus'
import { api, type AlertConfig } from '../api'
import { useAuth } from '../stores/auth'

const auth = useAuth()
const isAdmin = computed(() => auth.isAdmin)

const form = ref<AlertConfig>({ enabled: false, webhookUrl: '', threshold: 95, intervalMinutes: 10 })
const loading = ref(false)
const saving = ref(false)
const testing = ref(false)

async function load() {
  loading.value = true
  try {
    const { data } = await api.alertConfig()
    form.value = data
  } finally {
    loading.value = false
  }
}

async function save() {
  if (form.value.enabled && !form.value.webhookUrl.trim()) {
    ElMessage.warning('启用告警时必须填写 webhook 地址')
    return
  }
  saving.value = true
  try {
    const { data } = await api.saveAlertConfig({
      enabled: form.value.enabled,
      webhookUrl: form.value.webhookUrl.trim(),
      threshold: form.value.threshold,
      intervalMinutes: form.value.intervalMinutes,
    })
    form.value = data
    ElMessage.success('配置已保存')
  } catch {
    /* 拦截器提示 */
  } finally {
    saving.value = false
  }
}

async function test() {
  if (!form.value.webhookUrl.trim()) {
    ElMessage.warning('请先填写 webhook 地址')
    return
  }
  testing.value = true
  try {
    const { data } = await api.testAlert(form.value.webhookUrl.trim())
    ElMessage.success(data.message || '测试卡片已发送')
  } catch {
    /* 拦截器提示 */
  } finally {
    testing.value = false
  }
}

onMounted(load)
</script>

<template>
  <div v-loading="loading">
    <div class="page-header">
      <h2 class="page-title">告警配置</h2>
      <div class="page-sub">地址使用率达到阈值时向飞书机器人推送告警卡片</div>
    </div>

    <el-alert
      type="info"
      :closable="false"
      title="启用后,平台会定时巡检各在线服务器的作用域地址使用率,达到阈值时向飞书机器人推送告警卡片。同一作用域仅在跨过阈值时推送一次,使用率回落后自动重置。"
      style="margin-bottom: 18px"
    />

    <el-form label-width="130px" style="max-width: 720px">
      <el-form-item label="启用告警">
        <el-switch v-model="form.enabled" :disabled="!isAdmin" />
      </el-form-item>
      <el-form-item label="飞书机器人 webhook">
        <el-input
          v-model="form.webhookUrl"
          :disabled="!isAdmin"
          placeholder="https://open.feishu.cn/open-apis/bot/v2/hook/xxxxxxxx"
        />
        <div class="hint">
          在飞书群 → 设置 → 群机器人 → 添加「自定义机器人」,复制其 webhook 地址粘贴到这里。
          若机器人开启了「关键词」安全设置,请把关键词设为包含 <code>DHCP</code>。
        </div>
      </el-form-item>
      <el-form-item label="使用率阈值(%)">
        <el-input-number v-model="form.threshold" :min="1" :max="100" :step="1" :disabled="!isAdmin" />
        <span class="hint inline">作用域地址使用率达到或超过此值即告警(默认 95)。</span>
      </el-form-item>
      <el-form-item label="巡检间隔(分钟)">
        <el-input-number v-model="form.intervalMinutes" :min="1" :max="1440" :step="1" :disabled="!isAdmin" />
        <span class="hint inline">每隔多少分钟巡检一次(默认 10)。</span>
      </el-form-item>
      <el-form-item v-if="isAdmin">
        <el-button type="primary" :loading="saving" @click="save">保存</el-button>
        <el-button :loading="testing" @click="test">测试发送</el-button>
      </el-form-item>
    </el-form>
  </div>
</template>

<style scoped>
.hint {
  color: var(--app-text-muted);
  font-size: 13px;
  line-height: 1.6;
  margin-top: 4px;
}
.hint.inline {
  margin-top: 0;
  margin-left: 12px;
}
code {
  background: var(--app-panel-2);
  padding: 1px 5px;
  border-radius: 3px;
}
</style>
