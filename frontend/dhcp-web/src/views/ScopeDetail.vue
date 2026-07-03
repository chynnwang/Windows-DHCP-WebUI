<script setup lang="ts">
import { computed, onMounted, ref, watch } from 'vue'
import { useRoute, useRouter } from 'vue-router'
import { ElMessage, ElMessageBox } from 'element-plus'
import { api, type Lease, type Reservation, type OptionValue } from '../api'
import { useAuth } from '../stores/auth'

const props = defineProps<{ id: string; scopeId: string }>()
const router = useRouter()
const route = useRoute()
const auth = useAuth()
const isAdmin = computed(() => auth.isAdmin)

const serverId = computed(() => Number(props.id))
const scopeName = computed(() => (route.query.name as string) || props.scopeId)
const activeTab = ref('leases')

const leases = ref<Lease[]>([])
const reservations = ref<Reservation[]>([])
const options = ref<OptionValue[]>([])
const loading = ref(false)
const leaseSearch = ref('')

const filteredLeases = computed(() => {
  const kw = leaseSearch.value.trim().toLowerCase()
  if (!kw) return leases.value
  return leases.value.filter((l) =>
    [l.ipAddress, l.hostName, l.clientId, l.addressState]
      .some((f) => (f || '').toLowerCase().includes(kw))
  )
})

function fmtTime(v?: string) {
  if (!v) return '-'
  const d = new Date(v)
  return isNaN(d.getTime()) ? v : d.toLocaleString()
}

async function loadTab() {
  loading.value = true
  try {
    if (activeTab.value === 'leases') {
      const { data } = await api.leases(serverId.value, props.scopeId)
      leases.value = data
    } else if (activeTab.value === 'reservations') {
      const { data } = await api.reservations(serverId.value, props.scopeId)
      reservations.value = data
    } else if (activeTab.value === 'options') {
      const { data } = await api.options(serverId.value, props.scopeId)
      options.value = data
    }
  } finally {
    loading.value = false
  }
}

watch(activeTab, loadTab)
watch(() => [props.id, props.scopeId], () => loadTab())

function goBack() {
  router.push({ name: 'scopes', params: { id: serverId.value } })
}

// ---- 保留:添加 / 由租约转保留 / 删除 ----
const resDialog = ref(false)
const resForm = ref<any>({})

function openAddReservation() {
  resForm.value = { ipAddress: '', clientId: '', name: '', description: '' }
  resDialog.value = true
}

function convertLease(l: Lease) {
  resForm.value = {
    ipAddress: l.ipAddress,
    clientId: l.clientId || '',
    name: l.hostName || '',
    description: '由租约转换',
  }
  resDialog.value = true
}

async function submitReservation() {
  try {
    await api.addReservation(serverId.value, props.scopeId, {
      scopeId: props.scopeId,
      ipAddress: resForm.value.ipAddress,
      clientId: resForm.value.clientId,
      name: resForm.value.name,
      description: resForm.value.description,
    })
    ElMessage.success('保留已添加')
    resDialog.value = false
    activeTab.value = 'reservations'
    await loadTab()
  } catch {
    /* 拦截器提示 */
  }
}

async function removeReservation(r: Reservation) {
  try {
    await ElMessageBox.confirm(`确认删除保留 ${r.ipAddress}(${r.clientId})?`, '删除确认', { type: 'warning' })
    await api.removeReservation(serverId.value, props.scopeId, r.ipAddress)
    ElMessage.success('保留已删除')
    await loadTab()
  } catch {
    /* 取消 */
  }
}

// ---- 选项 ----
const optDialog = ref(false)
const optEditMode = ref<'create' | 'edit'>('create')
const optForm = ref<any>({})

function openSetOption() {
  optEditMode.value = 'create'
  optForm.value = { optionId: 3, values: '' }
  optDialog.value = true
}

function openEditOption(row: OptionValue) {
  optEditMode.value = 'edit'
  optForm.value = { optionId: row.optionId, values: (row.value || []).join(', ') }
  optDialog.value = true
}

async function removeOption(row: OptionValue) {
  try {
    await ElMessageBox.confirm(
      `确认删除选项 ${row.optionId}(${row.name || ''})?删除后该作用域将回退为继承服务器级设置。`,
      '删除确认',
      { type: 'warning' }
    )
    await api.removeOption(serverId.value, row.optionId, props.scopeId)
    ElMessage.success('选项已删除')
    await loadTab()
  } catch {
    /* 取消 */
  }
}

async function submitOption() {
  const values = String(optForm.value.values)
    .split(/[,\n]/)
    .map((v: string) => v.trim())
    .filter(Boolean)
  if (values.length === 0) {
    ElMessage.warning('请至少填写一个值')
    return
  }
  try {
    await api.setOption(serverId.value, {
      scopeId: props.scopeId,
      optionId: Number(optForm.value.optionId),
      values,
    })
    ElMessage.success('选项已设置')
    optDialog.value = false
    await loadTab()
  } catch {
    /* 拦截器提示 */
  }
}

onMounted(loadTab)
</script>

<template>
  <div>
    <div class="head">
      <el-button link @click="goBack">
        <el-icon><ArrowLeft /></el-icon>返回作用域列表
      </el-button>
      <h3>{{ scopeName }} <span class="sid">({{ scopeId }})</span></h3>
    </div>

    <el-tabs v-model="activeTab">
      <el-tab-pane label="租约" name="leases">
        <div class="tab-toolbar">
          <el-input
            v-model="leaseSearch"
            placeholder="搜索 IP / 主机名 / MAC / 状态"
            clearable
            style="width: 300px"
          >
            <template #prefix><el-icon><Search /></el-icon></template>
          </el-input>
          <span class="count">共 {{ filteredLeases.length }} 条</span>
        </div>
        <el-table :data="filteredLeases" v-loading="loading" border stripe>
          <el-table-column prop="ipAddress" label="IP 地址" width="150" sortable />
          <el-table-column label="主机名" min-width="160">
            <template #default="{ row }">{{ row.hostName || '-' }}</template>
          </el-table-column>
          <el-table-column prop="clientId" label="客户端 ID(MAC)" width="180" />
          <el-table-column label="状态" width="110">
            <template #default="{ row }">
              <el-tag size="small" :type="row.addressState?.toLowerCase().includes('active') ? 'success' : 'info'">
                {{ row.addressState || '-' }}
              </el-tag>
            </template>
          </el-table-column>
          <el-table-column label="租约到期" width="190">
            <template #default="{ row }">{{ fmtTime(row.leaseExpiryTime) }}</template>
          </el-table-column>
          <el-table-column label="操作" width="140" fixed="right">
            <template #default="{ row }">
              <el-button v-if="isAdmin" size="small" type="primary" plain :disabled="!row.clientId" @click="convertLease(row)">
                转为保留
              </el-button>
            </template>
          </el-table-column>
        </el-table>
      </el-tab-pane>

      <el-tab-pane label="保留(静态绑定)" name="reservations">
        <div v-if="isAdmin" class="tab-toolbar">
          <el-button size="small" type="primary" @click="openAddReservation">
            <el-icon><Plus /></el-icon>添加保留
          </el-button>
        </div>
        <el-table :data="reservations" v-loading="loading" border stripe>
          <el-table-column prop="ipAddress" label="IP 地址" width="150" />
          <el-table-column prop="clientId" label="客户端 ID(MAC)" width="180" />
          <el-table-column prop="name" label="名称" />
          <el-table-column prop="description" label="描述" />
          <el-table-column v-if="isAdmin" label="操作" width="100" fixed="right">
            <template #default="{ row }">
              <el-button size="small" type="danger" @click="removeReservation(row)">删除</el-button>
            </template>
          </el-table-column>
        </el-table>
      </el-tab-pane>

      <el-tab-pane label="选项(网关/DNS 等)" name="options">
        <div v-if="isAdmin" class="tab-toolbar">
          <el-button size="small" type="primary" @click="openSetOption">
            <el-icon><Plus /></el-icon>设置选项
          </el-button>
        </div>
        <el-table :data="options" v-loading="loading" border stripe>
          <el-table-column label="名称" width="220">
            <template #default="{ row }">{{ row.name || `选项 ${row.optionId}` }}</template>
          </el-table-column>
          <el-table-column label="值">
            <template #default="{ row }">{{ (row.value || []).join(', ') }}</template>
          </el-table-column>
          <el-table-column v-if="isAdmin" label="操作" width="150" fixed="right">
            <template #default="{ row }">
              <el-button size="small" @click="openEditOption(row)">编辑</el-button>
              <el-button size="small" type="danger" @click="removeOption(row)">删除</el-button>
            </template>
          </el-table-column>
        </el-table>
      </el-tab-pane>
    </el-tabs>

    <!-- 添加/转换 保留对话框 -->
    <el-dialog v-model="resDialog" title="添加保留" width="480px">
      <el-form label-width="120px">
        <el-form-item label="IP 地址">
          <el-input v-model="resForm.ipAddress" placeholder="192.168.1.50" />
        </el-form-item>
        <el-form-item label="MAC 地址">
          <el-input v-model="resForm.clientId" placeholder="00-11-22-33-44-55" />
        </el-form-item>
        <el-form-item label="名称">
          <el-input v-model="resForm.name" />
        </el-form-item>
        <el-form-item label="描述">
          <el-input v-model="resForm.description" />
        </el-form-item>
      </el-form>
      <template #footer>
        <el-button @click="resDialog = false">取消</el-button>
        <el-button type="primary" @click="submitReservation">确定</el-button>
      </template>
    </el-dialog>

    <!-- 设置选项对话框 -->
    <el-dialog v-model="optDialog" :title="optEditMode === 'create' ? '设置作用域选项' : `编辑选项 ${optForm.optionId}`" width="480px">
      <el-form label-width="90px">
        <el-form-item label="选项">
          <el-select v-model="optForm.optionId" style="width: 100%" :disabled="optEditMode === 'edit'">
            <el-option :value="3" label="3 - 路由器(网关)" />
            <el-option :value="6" label="6 - DNS 服务器" />
            <el-option :value="15" label="15 - DNS 域名" />
            <el-option :value="44" label="44 - WINS/NBNS 服务器" />
            <el-option :value="51" label="51 - 租约时间(秒)" />
          </el-select>
        </el-form-item>
        <el-form-item label="值">
          <el-input v-model="optForm.values" type="textarea" placeholder="多个值用逗号或换行分隔,如 192.168.1.1" />
        </el-form-item>
      </el-form>
      <template #footer>
        <el-button @click="optDialog = false">取消</el-button>
        <el-button type="primary" @click="submitOption">确定</el-button>
      </template>
    </el-dialog>
  </div>
</template>

<style scoped>
.head {
  display: flex;
  align-items: center;
  gap: 12px;
  margin-bottom: 12px;
}
.head h3 {
  margin: 0;
}
.sid {
  color: var(--app-text-muted);
  font-weight: normal;
  font-size: 14px;
}
.tab-toolbar {
  display: flex;
  align-items: center;
  gap: 12px;
  margin-bottom: 10px;
}
.count {
  color: var(--app-text-muted);
  font-size: 13px;
}
</style>
