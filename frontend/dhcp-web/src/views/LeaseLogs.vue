<script setup lang="ts">
import { computed, onMounted, ref } from 'vue'
import { ElMessage, ElMessageBox } from 'element-plus'
import { api, type LeaseLog, type ServerDto } from '../api'
import { formatUtcTime } from '../utils/time'
import { useAuth } from '../stores/auth'

const auth = useAuth()
const isAdmin = computed(() => auth.isAdmin)

const logs = ref<LeaseLog[]>([])
const total = ref(0)
const page = ref(1)
const pageSize = ref(50)
const q = ref('')
const serverId = ref<number | undefined>(undefined)
const servers = ref<ServerDto[]>([])
const loading = ref(false)

function fmtTime(v?: string) {
  return formatUtcTime(v)
}

async function load() {
  loading.value = true
  try {
    const { data } = await api.leaseLogs({
      q: q.value.trim() || undefined,
      serverId: serverId.value,
      page: page.value,
      pageSize: pageSize.value,
    })
    logs.value = data.items
    total.value = data.total
  } finally {
    loading.value = false
  }
}

function search() {
  page.value = 1
  load()
}

function reset() {
  q.value = ''
  serverId.value = undefined
  page.value = 1
  load()
}

async function clearLogs() {
  try {
    await ElMessageBox.confirm(
      '将清理 15 天以前的租约日志,最近 15 天会保留。确认继续?',
      '清空日志',
      { type: 'warning', confirmButtonText: '清空日志', cancelButtonText: '取消' }
    )
    const { data } = await api.clearLeaseLogs()
    ElMessage.success(data.message || '租约日志已清理')
    page.value = 1
    await load()
  } catch {
    /* 用户取消或拦截器提示 */
  }
}

onMounted(async () => {
  try {
    const { data } = await api.servers()
    servers.value = data
  } catch {
    /* 拦截器提示 */
  }
  await load()
})
</script>

<template>
  <div v-loading="loading">
    <div class="toolbar">
      <div class="page-header" style="margin-bottom: 0">
        <h2 class="page-title">租约日志</h2>
        <div class="page-sub">记录客户端获取地址的历史,支持按多字段快速检索</div>
      </div>
      <div class="spacer" />
      <el-button v-if="isAdmin" type="danger" plain :loading="loading" @click="clearLogs">
        <el-icon><Delete /></el-icon>清空日志
      </el-button>
      <el-button :loading="loading" @click="load">
        <el-icon><Refresh /></el-icon>刷新
      </el-button>
    </div>

    <el-alert
      type="info"
      :closable="false"
      title="平台每 5 分钟巡检各在线服务器的租约,发现新客户端获取地址时记录一条。可按 IP / 主机名 / MAC / 服务器 搜索。日志保留 30 天。"
      style="margin-bottom: 16px"
    />

    <div class="filters">
      <el-input
        v-model="q"
        placeholder="搜索 IP / 主机名 / MAC / 作用域"
        clearable
        style="width: 300px"
        @keyup.enter="search"
        @clear="search"
      >
        <template #prefix><el-icon><Search /></el-icon></template>
      </el-input>
      <el-select v-model="serverId" placeholder="全部服务器" clearable style="width: 200px" @change="search">
        <el-option v-for="s in servers" :key="s.id" :label="s.name" :value="s.id" />
      </el-select>
      <el-button type="primary" @click="search">搜索</el-button>
      <el-button @click="reset">重置</el-button>
    </div>

    <el-table :data="logs" border stripe>
      <el-table-column label="获取时间" width="180">
        <template #default="{ row }">{{ fmtTime(row.seenAtUtc) }}</template>
      </el-table-column>
      <el-table-column prop="serverName" label="服务器" min-width="140" />
      <el-table-column label="作用域" min-width="150">
        <template #default="{ row }">{{ row.scopeName || row.scopeId }}</template>
      </el-table-column>
      <el-table-column prop="ipAddress" label="IP 地址" width="140" />
      <el-table-column label="主机名" min-width="160">
        <template #default="{ row }">{{ row.hostName || '-' }}</template>
      </el-table-column>
      <el-table-column label="客户端 ID(MAC)" width="180">
        <template #default="{ row }">{{ row.clientId || '-' }}</template>
      </el-table-column>
    </el-table>

    <div class="pager">
      <el-pagination
        v-model:current-page="page"
        v-model:page-size="pageSize"
        :total="total"
        :page-sizes="[20, 50, 100, 200]"
        layout="total, sizes, prev, pager, next"
        @current-change="load"
        @size-change="search"
      />
    </div>
  </div>
</template>

<style scoped>
.toolbar {
  display: flex;
  align-items: center;
  gap: 12px;
  margin-bottom: 16px;
}
.toolbar h3 {
  margin: 0;
}
.spacer {
  flex: 1;
}
.filters {
  display: flex;
  gap: 10px;
  margin-bottom: 14px;
}
.pager {
  display: flex;
  justify-content: flex-end;
  margin-top: 14px;
}
</style>
