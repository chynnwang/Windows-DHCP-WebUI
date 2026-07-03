<script setup lang="ts">
import { computed, onMounted, ref, watch } from 'vue'
import { useRouter } from 'vue-router'
import { ElMessage, ElMessageBox } from 'element-plus'
import { api, type Scope, type ScopeStats, type ServerDto } from '../api'
import { useAuth } from '../stores/auth'

const props = defineProps<{ id: string }>()
const router = useRouter()
const auth = useAuth()
const isAdmin = computed(() => auth.isAdmin)

const serverId = ref(Number(props.id))
const server = ref<ServerDto | null>(null)
const scopes = ref<Scope[]>([])
const stats = ref<Record<string, ScopeStats>>({})
const loading = ref(false)
const lastLoadedAt = ref<Date | null>(null)

async function loadServer() {
  const { data } = await api.servers()
  server.value = data.find((s) => s.id === serverId.value) ?? null
}

async function loadScopes() {
  loading.value = true
  stats.value = {}
  try {
    const { data: scopeList } = await api.scopes(serverId.value)
    scopes.value = scopeList
    try {
      const { data: statList } = await api.allScopeStats(serverId.value)
      stats.value = Object.fromEntries(statList.map((st) => [st.scopeId, st]))
    } catch {
      loadStatsProgressively(scopeList)
    }
    lastLoadedAt.value = new Date()
  } finally {
    loading.value = false
  }
}

async function loadStatsProgressively(scopeList: Scope[]) {
  const queue = [...scopeList]
  const workers = Array.from({ length: Math.min(3, queue.length) }, async () => {
    while (queue.length) {
      const scope = queue.shift()
      if (!scope) return
      try {
        const { data } = await api.scopeStats(serverId.value, scope.scopeId)
        stats.value = { ...stats.value, [scope.scopeId]: data }
      } catch {
        /* 单个失败不阻塞页面 */
      }
    }
  })
  await Promise.all(workers)
}

async function reload() {
  serverId.value = Number(props.id)
  await Promise.all([loadServer(), loadScopes()])
}

watch(() => props.id, reload)

function goBack() {
  router.push({ name: 'workspace' })
}

function openDetail(s: Scope) {
  router.push({ name: 'scope-detail', params: { id: serverId.value, scopeId: s.scopeId }, query: { name: s.name } })
}

function fmtTime(v: Date | null) {
  if (!v) return '-'
  return v.toLocaleTimeString('zh-CN', { hour12: false })
}

// ---- 作用域写操作 ----
const scopeDialog = ref(false)
const scopeEditMode = ref<'create' | 'edit'>('create')
const scopeForm = ref<any>({})

function openCreateScope() {
  scopeEditMode.value = 'create'
  scopeForm.value = { name: '', startRange: '', endRange: '', subnetMask: '255.255.255.0', description: '', leaseDays: 8, active: true, gateway: '', dnsServers: '', dnsDomain: '' }
  scopeDialog.value = true
}

function openEditScope(s: Scope) {
  scopeEditMode.value = 'edit'
  scopeForm.value = {
    scopeId: s.scopeId,
    name: s.name,
    description: s.description || '',
    leaseDays: 8,
    active: s.state?.toLowerCase() === 'active',
  }
  scopeDialog.value = true
}

async function submitScope() {
  const f = scopeForm.value
  try {
    if (scopeEditMode.value === 'create') {
      const dnsServers = String(f.dnsServers || '')
        .split(/[,\n]/)
        .map((v: string) => v.trim())
        .filter(Boolean)
      await api.createScope(serverId.value, {
        name: f.name,
        startRange: f.startRange,
        endRange: f.endRange,
        subnetMask: f.subnetMask,
        description: f.description,
        leaseDays: f.leaseDays,
        active: f.active,
        gateway: (f.gateway || '').trim() || null,
        dnsServers,
        dnsDomain: (f.dnsDomain || '').trim() || null,
      })
      ElMessage.success('作用域已创建')
    } else {
      await api.updateScope(serverId.value, f.scopeId, {
        name: f.name,
        description: f.description,
        leaseDays: f.leaseDays,
        active: f.active,
      })
      ElMessage.success('作用域已更新')
    }
    scopeDialog.value = false
    await loadScopes()
  } catch {
    /* 拦截器提示 */
  }
}

async function deleteScope(s: Scope) {
  try {
    await ElMessageBox.confirm(
      `确认删除作用域「${s.name}」(${s.scopeId})?此操作将移除该作用域下所有租约与保留,不可恢复。`,
      '危险操作',
      { type: 'error', confirmButtonText: '确认删除' }
    )
    await api.deleteScope(serverId.value, s.scopeId)
    ElMessage.success('作用域已删除')
    await loadScopes()
  } catch {
    /* 取消 */
  }
}

onMounted(reload)
</script>

<template>
  <div>
    <div class="toolbar">
      <el-button link @click="goBack">
        <el-icon><ArrowLeft /></el-icon>返回工区/服务器
      </el-button>
      <h3 v-if="server">
        {{ server.name }}
        <el-tag :type="server.online ? 'success' : 'info'" size="small">
          {{ server.online ? '在线' : '离线' }}
        </el-tag>
      </h3>
      <el-tag v-if="server" size="small" type="warning" effect="plain">
        {{ server.siteName || '未分组' }}
      </el-tag>
      <div class="spacer" />
      <el-button :loading="loading" @click="loadScopes">
        <el-icon><Refresh /></el-icon>刷新
      </el-button>
      <el-button v-if="isAdmin" type="primary" @click="openCreateScope">
        <el-icon><Plus /></el-icon>新建作用域
      </el-button>
    </div>
    <div class="data-hint">
      统计数据短时间缓存以提升打开速度，最后刷新 {{ fmtTime(lastLoadedAt) }}。点击“刷新”可重新读取服务器。
    </div>

    <el-table :data="scopes" v-loading="loading" border>
      <el-table-column label="状态" width="80">
        <template #default="{ row }">
          <el-tag size="small" :type="row.state?.toLowerCase() === 'active' ? 'success' : 'info'">
            {{ row.state?.toLowerCase() === 'active' ? '启用' : '停用' }}
          </el-tag>
        </template>
      </el-table-column>
      <el-table-column prop="scopeId" label="作用域 ID" width="140" />
      <el-table-column prop="name" label="名称" />
      <el-table-column label="范围">
        <template #default="{ row }">{{ row.startRange }} ~ {{ row.endRange }}</template>
      </el-table-column>
      <el-table-column prop="subnetMask" label="子网掩码" width="140" />
      <el-table-column label="使用率" width="180">
        <template #default="{ row }">
          <template v-if="stats[row.scopeId]">
            <el-progress :percentage="Math.round(stats[row.scopeId].percentageInUse)" :stroke-width="14" />
            <span class="stat-text">已用 {{ stats[row.scopeId].inUse }} / 空闲 {{ stats[row.scopeId].free }}</span>
          </template>
          <span v-else>-</span>
        </template>
      </el-table-column>
      <el-table-column label="操作" width="220" fixed="right">
        <template #default="{ row }">
          <el-button size="small" type="primary" @click="openDetail(row)">明细</el-button>
          <el-button v-if="isAdmin" size="small" @click="openEditScope(row)">编辑</el-button>
          <el-button v-if="isAdmin" size="small" type="danger" @click="deleteScope(row)">删除</el-button>
        </template>
      </el-table-column>
    </el-table>

    <el-dialog v-model="scopeDialog" :title="scopeEditMode === 'create' ? '新建作用域' : '编辑作用域'" width="480px">
      <el-form label-width="90px">
        <el-form-item label="名称">
          <el-input v-model="scopeForm.name" />
        </el-form-item>
        <template v-if="scopeEditMode === 'create'">
          <el-form-item label="起始 IP">
            <el-input v-model="scopeForm.startRange" placeholder="192.168.1.100" />
          </el-form-item>
          <el-form-item label="结束 IP">
            <el-input v-model="scopeForm.endRange" placeholder="192.168.1.200" />
          </el-form-item>
          <el-form-item label="子网掩码">
            <el-input v-model="scopeForm.subnetMask" placeholder="255.255.255.0" />
          </el-form-item>
          <el-form-item label="网关">
            <el-input v-model="scopeForm.gateway" placeholder="192.168.1.1(选项 3,可留空)" />
          </el-form-item>
          <el-form-item label="DNS 服务器">
            <el-input v-model="scopeForm.dnsServers" type="textarea" :autosize="{ minRows: 1 }" placeholder="多个用逗号或换行分隔,如 8.8.8.8, 114.114.114.114(选项 6)" />
          </el-form-item>
          <el-form-item label="DNS 域名">
            <el-input v-model="scopeForm.dnsDomain" placeholder="example.local(选项 15,可留空)" />
          </el-form-item>
        </template>
        <el-form-item label="租期(天)">
          <el-input-number v-model="scopeForm.leaseDays" :min="1" :max="365" />
        </el-form-item>
        <el-form-item label="描述">
          <el-input v-model="scopeForm.description" type="textarea" />
        </el-form-item>
        <el-form-item label="启用">
          <el-switch v-model="scopeForm.active" />
        </el-form-item>
      </el-form>
      <template #footer>
        <el-button @click="scopeDialog = false">取消</el-button>
        <el-button type="primary" @click="submitScope">确定</el-button>
      </template>
    </el-dialog>
  </div>
</template>

<style scoped>
.toolbar {
  display: flex;
  align-items: center;
  gap: 12px;
  margin-bottom: 12px;
}
.toolbar h3 {
  margin: 0;
  display: flex;
  align-items: center;
  gap: 8px;
}
.spacer {
  flex: 1;
}
.data-hint {
  color: var(--app-text-muted);
  font-size: 12px;
  margin: -4px 0 12px;
}
.stat-text {
  font-size: 12px;
  color: var(--app-text-muted);
}
</style>
