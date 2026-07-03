<script setup lang="ts">
import { onMounted, reactive, ref } from 'vue'
import { ElMessage, ElMessageBox } from 'element-plus'
import { api, type UserDto } from '../api'
import { useAuth } from '../stores/auth'

const auth = useAuth()
const users = ref<UserDto[]>([])
const loading = ref(false)

async function load() {
  loading.value = true
  try {
    const { data } = await api.users()
    users.value = data
  } finally {
    loading.value = false
  }
}
onMounted(load)

// ---- 新建 ----
const createVisible = ref(false)
const createForm = reactive({ username: '', password: '', role: 'Viewer' })
function openCreate() {
  createForm.username = ''
  createForm.password = ''
  createForm.role = 'Viewer'
  createVisible.value = true
}
async function submitCreate() {
  if (!createForm.username.trim()) return ElMessage.warning('请输入用户名')
  if (createForm.password.length < 6) return ElMessage.warning('密码至少 6 位')
  await api.createUser(createForm.username.trim(), createForm.password, createForm.role)
  ElMessage.success('用户已创建')
  createVisible.value = false
  await load()
}

// ---- 编辑(角色/启停)----
const editVisible = ref(false)
const editForm = reactive({ id: 0, username: '', role: 'Viewer', enabled: true })
function openEdit(u: UserDto) {
  editForm.id = u.id
  editForm.username = u.username
  editForm.role = u.role
  editForm.enabled = u.enabled
  editVisible.value = true
}
async function submitEdit() {
  await api.updateUser(editForm.id, { role: editForm.role, enabled: editForm.enabled })
  ElMessage.success('已保存')
  editVisible.value = false
  await load()
}

// ---- 重置密码 ----
const pwdVisible = ref(false)
const pwdForm = reactive({ id: 0, username: '', password: '' })
function openReset(u: UserDto) {
  pwdForm.id = u.id
  pwdForm.username = u.username
  pwdForm.password = ''
  pwdVisible.value = true
}
async function submitReset() {
  if (pwdForm.password.length < 6) return ElMessage.warning('密码至少 6 位')
  await api.updateUser(pwdForm.id, { password: pwdForm.password })
  ElMessage.success('密码已重置')
  pwdVisible.value = false
}

// ---- 删除 ----
async function remove(u: UserDto) {
  await ElMessageBox.confirm(`确定删除用户「${u.username}」?`, '删除确认', {
    type: 'warning',
    confirmButtonText: '删除',
    cancelButtonText: '取消',
  })
  await api.deleteUser(u.id)
  ElMessage.success('用户已删除')
  await load()
}
</script>

<template>
  <div class="page">
    <div class="page-head">
      <div>
        <div class="page-title">用户管理</div>
        <div class="page-sub">管理登录账号与角色权限(Admin 可写,Viewer 只读)</div>
      </div>
      <el-button type="primary" :icon="'Plus'" @click="openCreate">新建用户</el-button>
    </div>

    <el-card shadow="never" class="card">
      <el-table :data="users" v-loading="loading" stripe>
        <el-table-column prop="username" label="用户名" min-width="140" />
        <el-table-column label="角色" width="120">
          <template #default="{ row }">
            <el-tag :type="row.role === 'Admin' ? 'danger' : 'info'" effect="light">
              {{ row.role === 'Admin' ? '管理员' : '只读' }}
            </el-tag>
          </template>
        </el-table-column>
        <el-table-column label="状态" width="100">
          <template #default="{ row }">
            <el-tag :type="row.enabled ? 'success' : 'info'" effect="light">
              {{ row.enabled ? '启用' : '停用' }}
            </el-tag>
          </template>
        </el-table-column>
        <el-table-column prop="createdAt" label="创建时间" min-width="180">
          <template #default="{ row }">{{ new Date(row.createdAt).toLocaleString() }}</template>
        </el-table-column>
        <el-table-column label="操作" width="240" align="right">
          <template #default="{ row }">
            <el-button link type="primary" @click="openEdit(row)">编辑</el-button>
            <el-button link type="primary" @click="openReset(row)">重置密码</el-button>
            <el-button
              link
              type="danger"
              :disabled="row.username === auth.user?.username"
              @click="remove(row)"
              >删除</el-button
            >
          </template>
        </el-table-column>
      </el-table>
    </el-card>

    <!-- 新建 -->
    <el-dialog v-model="createVisible" title="新建用户" width="420px">
      <el-form label-width="80px">
        <el-form-item label="用户名"><el-input v-model="createForm.username" /></el-form-item>
        <el-form-item label="密码">
          <el-input v-model="createForm.password" type="password" show-password placeholder="至少 6 位" />
        </el-form-item>
        <el-form-item label="角色">
          <el-select v-model="createForm.role" style="width: 100%">
            <el-option label="管理员(可写)" value="Admin" />
            <el-option label="只读(Viewer)" value="Viewer" />
          </el-select>
        </el-form-item>
      </el-form>
      <template #footer>
        <el-button @click="createVisible = false">取消</el-button>
        <el-button type="primary" @click="submitCreate">创建</el-button>
      </template>
    </el-dialog>

    <!-- 编辑 -->
    <el-dialog v-model="editVisible" :title="`编辑 ${editForm.username}`" width="420px">
      <el-form label-width="80px">
        <el-form-item label="角色">
          <el-select v-model="editForm.role" style="width: 100%">
            <el-option label="管理员(可写)" value="Admin" />
            <el-option label="只读(Viewer)" value="Viewer" />
          </el-select>
        </el-form-item>
        <el-form-item label="状态">
          <el-switch v-model="editForm.enabled" active-text="启用" inactive-text="停用" />
        </el-form-item>
      </el-form>
      <template #footer>
        <el-button @click="editVisible = false">取消</el-button>
        <el-button type="primary" @click="submitEdit">保存</el-button>
      </template>
    </el-dialog>

    <!-- 重置密码 -->
    <el-dialog v-model="pwdVisible" :title="`重置 ${pwdForm.username} 的密码`" width="420px">
      <el-form label-width="80px">
        <el-form-item label="新密码">
          <el-input v-model="pwdForm.password" type="password" show-password placeholder="至少 6 位" />
        </el-form-item>
      </el-form>
      <template #footer>
        <el-button @click="pwdVisible = false">取消</el-button>
        <el-button type="primary" @click="submitReset">确定</el-button>
      </template>
    </el-dialog>
  </div>
</template>

<style scoped>
.page {
  max-width: 1000px;
}
.page-head {
  display: flex;
  align-items: center;
  justify-content: space-between;
  margin-bottom: 16px;
}
.page-title {
  font-size: 18px;
  font-weight: 700;
  color: var(--app-text);
}
.page-sub {
  font-size: 12px;
  color: var(--app-text-muted);
  margin-top: 4px;
}
.card {
  border: 1px solid var(--app-border);
  background: var(--app-panel);
}
</style>
