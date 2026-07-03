<script setup lang="ts">
import { computed, onMounted, ref } from 'vue'
import { useRouter } from 'vue-router'
import { ElMessage, ElMessageBox } from 'element-plus'
import { api, type ServerDto, type Site } from '../api'
import { formatUtcTime } from '../utils/time'
import { useAuth } from '../stores/auth'

const router = useRouter()
const auth = useAuth()
const isAdmin = computed(() => auth.isAdmin)

const sites = ref<Site[]>([])
const servers = ref<ServerDto[]>([])
const loading = ref(false)

interface Group {
  siteId: number | null
  name: string
  servers: ServerDto[]
}

const groups = computed<Group[]>(() => {
  const g: Group[] = sites.value.map((s) => ({
    siteId: s.id,
    name: s.name,
    servers: servers.value.filter((sv) => sv.siteId === s.id),
  }))
  g.push({
    siteId: null,
    name: '未分组',
    servers: servers.value.filter((sv) => sv.siteId == null),
  })
  return g
})

async function load() {
  loading.value = true
  try {
    const [{ data: siteList }, { data: srvList }] = await Promise.all([api.sites(), api.servers()])
    sites.value = siteList
    servers.value = srvList
  } finally {
    loading.value = false
  }
}

function manage(s: ServerDto) {
  if (!s.online) {
    ElMessage.warning('该服务器离线,无法管理')
    return
  }
  router.push({ name: 'scopes', params: { id: s.id } })
}

function fmtTime(v?: string) {
  return formatUtcTime(v)
}

// ---- 工区管理(Admin)----
async function createSite() {
  try {
    const { value } = await ElMessageBox.prompt('请输入工区名称', '新建工区', {
      inputPattern: /^.{1,64}$/,
      inputErrorMessage: '名称长度需为 1-64',
    })
    await api.createSite(value)
    ElMessage.success('工区已创建')
    await load()
  } catch {
    /* 取消 */
  }
}

async function renameSite(g: Group) {
  if (g.siteId == null) return
  try {
    const { value } = await ElMessageBox.prompt('请输入新的工区名称', '重命名工区', {
      inputValue: g.name,
      inputPattern: /^.{1,64}$/,
      inputErrorMessage: '名称长度需为 1-64',
    })
    await api.renameSite(g.siteId, value)
    ElMessage.success('已重命名')
    await load()
  } catch {
    /* 取消 */
  }
}

async function deleteSite(g: Group) {
  if (g.siteId == null) return
  try {
    await ElMessageBox.confirm(
      `确认删除工区「${g.name}」?其下服务器不会被删除,将移到「未分组」。`,
      '删除确认',
      { type: 'warning' }
    )
    await api.deleteSite(g.siteId)
    ElMessage.success('工区已删除')
    await load()
  } catch {
    /* 取消 */
  }
}

async function moveServer(s: ServerDto, targetSiteId: number | null) {
  try {
    await api.assignServerSite(s.id, targetSiteId)
    ElMessage.success('已调整归属')
    await load()
  } catch {
    /* 拦截器提示 */
  }
}

async function changeCallbackUrl(s: ServerDto) {
  if (!s.online) {
    ElMessage.warning('该服务器离线,需在线时才能下发改址指令')
    return
  }
  try {
    const { value } = await ElMessageBox.prompt(
      '输入 Agent 回连本平台的新地址(域名或 IP,含端口)。平台搬家前先在此改址,Agent 会自动重启并按新地址重连。',
      `更改回连地址 — ${s.name}`,
      {
        inputValue: window.location.origin,
        inputPlaceholder: 'http://dhcp.example.com:8090',
        inputPattern: /\S/,
        inputErrorMessage: '地址不能为空',
      }
    )
    const { data } = await api.setServerCallbackUrl(s.id, value.trim())
    ElMessage.success((data as any)?.message || '已下发改址指令')
    await load()
  } catch {
    /* 取消或拦截器提示 */
  }
}

async function renameServer(s: ServerDto) {
  try {
    const { value } = await ElMessageBox.prompt('请输入新的服务器名称', '重命名', {
      inputValue: s.name,
      inputPattern: /^.{1,64}$/,
      inputErrorMessage: '名称长度需为 1-64',
    })
    await api.renameServer(s.id, value)
    ElMessage.success('已重命名')
    await load()
  } catch {
    /* 取消 */
  }
}

async function deleteServer(s: ServerDto) {
  const msg = s.online
    ? `确认删除服务器「${s.name}」?平台会先远程卸载该机器上的 Agent(停止并删除服务、清理配置与程序文件),随后从平台移除。此操作不可恢复。`
    : `确认删除服务器「${s.name}」?该 Agent 当前离线,将仅从平台移除记录(无法远程卸载);如需彻底清理请到该服务器手动运行 DhcpAgent.exe uninstall。`
  try {
    await ElMessageBox.confirm(msg, '删除服务器', {
      type: 'warning',
      confirmButtonText: '确认删除',
      confirmButtonClass: 'el-button--danger',
    })
    const { data } = await api.deleteServer(s.id)
    ElMessage.success((data as any)?.message || '服务器已删除')
    await load()
  } catch {
    /* 取消 */
  }
}

// ---- 接入新服务器 ----
const setupDialog = ref(false)
const setupInfo = ref<Awaited<ReturnType<typeof api.agentSetupInfo>>['data'] | null>(null)
const loadingSetup = ref(false)

async function loadSetup() {
  loadingSetup.value = true
  try {
    const { data } = await api.agentSetupInfo()
    setupInfo.value = data
  } catch {
    /* 拦截器提示 */
  } finally {
    loadingSetup.value = false
  }
}

async function openSetup() {
  setupDialog.value = true
  await loadSetup()
}

// 修改「接入服务器」使用的平台对外地址(新 Agent 安装命令 / 下载链接都基于它生成)。
async function editPlatformUrl() {
  try {
    const { value } = await ElMessageBox.prompt(
      '输入本平台对外访问地址(域名或 IP,含端口),新 Agent 的下载链接与安装命令都会用它生成。留空则恢复为自动识别当前访问地址。',
      '更改平台地址',
      {
        inputValue: setupInfo.value?.platformUrl || '',
        inputPlaceholder: 'http://dhcp.example.com:8090',
        inputValidator: () => true, // 允许留空(表示恢复自动)
      }
    )
    const { data } = await api.setPlatformUrl((value || '').trim())
    ElMessage.success((data as any)?.message || '已保存')
    await loadSetup()
  } catch {
    /* 取消或拦截器提示 */
  }
}

async function resetPlatformUrl() {
  try {
    const { data } = await api.setPlatformUrl('')
    ElMessage.success((data as any)?.message || '已恢复自动识别')
    await loadSetup()
  } catch {
    /* 拦截器提示 */
  }
}

// 修改 Agent 连接密钥(新 Agent 安装命令基于它生成)。改后已装旧 Agent 会失联,需重装。
async function editEnrollmentSecret() {
  try {
    const { value } = await ElMessageBox.prompt(
      '输入新的 Agent 连接密钥(至少 4 位、不含空格),新 Agent 安装命令会用它生成。留空则恢复为服务器默认密钥。注意:修改后已安装的旧 Agent 会因密钥不符而无法重连,需用新密钥重新安装。',
      '更改 Agent 连接密钥',
      {
        inputValue: setupInfo.value?.enrollmentSecret || '',
        inputPlaceholder: '至少 4 位,如 admin',
        inputValidator: (v: string) => !v || v.trim().length === 0 || (v.trim().length >= 4 && !v.trim().includes(' ')) ? true : '密钥至少 4 位且不能包含空格',
      }
    )
    const { data } = await api.setEnrollmentSecret((value || '').trim())
    ElMessage.success((data as any)?.message || '已保存')
    await loadSetup()
  } catch {
    /* 取消或拦截器提示 */
  }
}

// HTTP(非安全上下文)或 iframe 内 navigator.clipboard 不可用,降级到 execCommand。
function legacyCopy(text: string): boolean {
  try {
    const ta = document.createElement('textarea')
    ta.value = text
    ta.setAttribute('readonly', '')
    ta.style.position = 'fixed'
    ta.style.top = '-9999px'
    ta.style.opacity = '0'
    document.body.appendChild(ta)
    ta.focus()
    ta.select()
    const ok = document.execCommand('copy')
    document.body.removeChild(ta)
    return ok
  } catch {
    return false
  }
}

async function copyCmd() {
  if (!setupInfo.value) return
  const text = setupInfo.value.installCommand
  if (navigator.clipboard && window.isSecureContext) {
    try {
      await navigator.clipboard.writeText(text)
      ElMessage.success('安装命令已复制')
      return
    } catch {
      /* 降级 */
    }
  }
  if (legacyCopy(text)) ElMessage.success('安装命令已复制')
  else ElMessage.warning('复制失败,请手动选中复制')
}

onMounted(load)
</script>

<template>
  <div v-loading="loading">
    <div class="page-header">
      <h2 class="page-title">工区 / 服务器</h2>
      <div class="page-sub">按工区组织并管理已纳管的 Windows DHCP 服务器</div>
    </div>
    <div class="toolbar">
      <div class="spacer" />
      <el-button v-if="isAdmin" @click="createSite">
        <el-icon><FolderAdd /></el-icon>新建工区
      </el-button>
      <el-button v-if="isAdmin" type="primary" @click="openSetup">
        <el-icon><Plus /></el-icon>接入服务器
      </el-button>
      <el-button :loading="loading" @click="load">
        <el-icon><Refresh /></el-icon>刷新
      </el-button>
    </div>

    <el-table :data="groups" row-key="name" default-expand-all class="site-table">
      <el-table-column type="expand">
        <template #default="{ row }">
          <div class="sub-wrap">
            <el-table v-if="row.servers.length" :data="row.servers" size="small" class="server-table">
              <el-table-column label="服务器名称" min-width="160">
                <template #default="{ row: s }">
                  <el-icon class="srv-ico"><Monitor /></el-icon>{{ s.name }}
                </template>
              </el-table-column>
              <el-table-column label="主机名" min-width="150">
                <template #default="{ row: s }">{{ s.hostname || '-' }}</template>
              </el-table-column>
              <el-table-column label="纳管状态" width="100">
                <template #default="{ row: s }">
                  <el-tag :type="s.online ? 'success' : 'info'" size="small" effect="plain">
                    {{ s.online ? '在线' : '离线' }}
                  </el-tag>
                </template>
              </el-table-column>
              <el-table-column label="DHCP 版本" width="110">
                <template #default="{ row: s }">{{ s.dhcpVersion || '-' }}</template>
              </el-table-column>
              <el-table-column label="Agent 版本" width="110">
                <template #default="{ row: s }">{{ s.agentVersion || '-' }}</template>
              </el-table-column>
              <el-table-column label="最近心跳" min-width="170">
                <template #default="{ row: s }">{{ fmtTime(s.lastSeenUtc) }}</template>
              </el-table-column>
              <el-table-column label="操作" width="150">
                <template #default="{ row: s }">
                  <div class="action-cell">
                  <el-button size="small" type="primary" link :disabled="!s.online" @click="manage(s)">
                    管理
                  </el-button>
                  <el-dropdown v-if="isAdmin" trigger="click">
                    <el-button size="small" link class="more-btn">
                      更多<el-icon class="el-icon--right"><ArrowDown /></el-icon>
                    </el-button>
                    <template #dropdown>
                      <el-dropdown-menu>
                        <el-dropdown-item @click="renameServer(s)">重命名</el-dropdown-item>
                        <el-dropdown-item :disabled="!s.online" @click="changeCallbackUrl(s)">
                          更改回连地址
                        </el-dropdown-item>
                        <el-dropdown-item divided :disabled="s.siteId == null" @click="moveServer(s, null)">
                          移到「未分组」
                        </el-dropdown-item>
                        <el-dropdown-item
                          v-for="site in sites.filter((x) => x.id !== s.siteId)"
                          :key="site.id"
                          @click="moveServer(s, site.id)"
                        >
                          移到「{{ site.name }}」
                        </el-dropdown-item>
                        <el-dropdown-item divided class="danger-item" @click="deleteServer(s)">
                          删除服务器
                        </el-dropdown-item>
                      </el-dropdown-menu>
                    </template>
                  </el-dropdown>
                  </div>
                </template>
              </el-table-column>
            </el-table>
            <el-empty v-else :image-size="50" description="该工区下暂无服务器" />
          </div>
        </template>
      </el-table-column>

      <el-table-column label="工区" min-width="220">
        <template #default="{ row }">
          <el-icon class="folder"><Folder /></el-icon>
          <span class="group-name">{{ row.name }}</span>
        </template>
      </el-table-column>
      <el-table-column label="服务器数" width="120">
        <template #default="{ row }">
          <el-tag size="small" effect="plain" type="info">{{ row.servers.length }} 台</el-tag>
        </template>
      </el-table-column>
      <el-table-column label="操作" width="160">
        <template #default="{ row }">
          <template v-if="row.siteId != null && isAdmin">
            <el-button link size="small" @click="renameSite(row)">重命名</el-button>
            <el-button link size="small" type="danger" @click="deleteSite(row)">删除</el-button>
          </template>
          <span v-else class="dash">-</span>
        </template>
      </el-table-column>
    </el-table>

    <el-alert
      v-if="!loading && servers.length === 0"
      title="暂无服务器。点击右上角「接入服务器」下载 Agent 并按说明安装,安装后将自动出现。"
      type="info"
      :closable="false"
      style="margin-top: 12px"
    />

    <el-dialog v-model="setupDialog" title="接入新服务器 — 安装 Agent" width="640px">
      <div v-loading="loadingSetup">
        <el-alert
          type="success"
          :closable="false"
          title="在每台需要纳管的 Windows DHCP 服务器上执行以下步骤,Agent 安装后会自动连回本平台并出现在列表中。"
          style="margin-bottom: 16px"
        />

        <div class="platform-url-box">
          <div class="pu-label">平台地址(新 Agent 回连本平台的地址)</div>
          <div class="pu-row">
            <code class="pu-value">{{ setupInfo?.platformUrl }}</code>
            <el-tag size="small" :type="setupInfo?.platformUrlConfigured ? 'warning' : 'info'" effect="plain">
              {{ setupInfo?.platformUrlConfigured ? '手动设置' : '自动识别' }}
            </el-tag>
            <div class="spacer" />
            <el-button size="small" @click="editPlatformUrl">更改地址</el-button>
            <el-button
              v-if="setupInfo?.platformUrlConfigured"
              size="small"
              @click="resetPlatformUrl"
            >
              恢复自动
            </el-button>
          </div>
          <div class="hint">
            平台搬家或需用固定域名/其它地址接入时,在此修改;下方下载链接与安装命令会立即按新地址生成。
          </div>
        </div>

        <div class="platform-url-box">
          <div class="pu-label">Agent 连接密钥(Agent 接入本平台的校验密钥)</div>
          <div class="pu-row">
            <code class="pu-value">{{ setupInfo?.enrollmentSecret }}</code>
            <el-tag size="small" :type="setupInfo?.enrollmentSecretConfigured ? 'warning' : 'info'" effect="plain">
              {{ setupInfo?.enrollmentSecretConfigured ? '已自定义' : '默认' }}
            </el-tag>
            <div class="spacer" />
            <el-button size="small" @click="editEnrollmentSecret">更改密钥</el-button>
          </div>
          <div class="hint">
            所有 Agent 用此密钥接入。<b>修改后下方安装命令会立即更新;但已安装的旧 Agent 会因密钥不符而无法重连,需用新密钥重新安装。</b>
          </div>
        </div>

        <el-steps direction="vertical" :active="4" space="72px">
          <el-step title="1. 下载 Agent">
            <template #description>
              <div class="step-body">
                <el-button type="primary" tag="a" :href="setupInfo?.downloadUrl" download>
                  <el-icon><Download /></el-icon>下载 DhcpAgent.exe
                </el-button>
                <div class="hint">把该文件拷贝到目标 Windows DHCP 服务器上。</div>
              </div>
            </template>
          </el-step>
          <el-step title="2. 以管理员身份打开 PowerShell">
            <template #description>
              <div class="hint">在开始菜单搜索 PowerShell → 右键「以管理员身份运行」,然后 cd 到 exe 所在目录。</div>
            </template>
          </el-step>
          <el-step title="3. 运行安装命令">
            <template #description>
              <div class="step-body">
                <el-input type="textarea" :model-value="setupInfo?.installCommand" readonly :autosize="{ minRows: 2 }" />
                <el-button size="small" @click="copyCmd" style="margin-top: 8px">
                  <el-icon><CopyDocument /></el-icon>复制命令
                </el-button>
                <div class="hint">命令已预填本平台地址与注册密钥,复制后直接粘贴执行即可。</div>
              </div>
            </template>
          </el-step>
          <el-step title="4. 完成">
            <template #description>
              <div class="hint">
                安装成功后回到本页点「刷新」,新服务器即会出现。<br />
                前置条件:该 Windows 账号需在 <b>DHCP Administrators</b> 组,且服务器能访问
                <code>{{ setupInfo?.platformUrl }}</code>。
              </div>
            </template>
          </el-step>
        </el-steps>
      </div>
      <template #footer>
        <el-button @click="setupDialog = false">关闭</el-button>
      </template>
    </el-dialog>
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
.site-table {
  width: 100%;
}
.folder {
  color: #e6a23c;
  margin-right: 6px;
  vertical-align: middle;
}
.group-name {
  font-weight: 600;
  font-size: 14px;
  vertical-align: middle;
}
.sub-wrap {
  padding: 8px 16px 12px 48px;
  background: var(--app-panel-2);
}
.server-table {
  width: 100%;
}
.srv-ico {
  color: var(--app-text-muted);
  margin-right: 5px;
  vertical-align: middle;
}
.dash {
  color: var(--app-text-muted);
}
.action-cell {
  display: flex;
  align-items: center;
  gap: 12px;
}
.more-btn {
  display: inline-flex;
  align-items: center;
}
.danger-item {
  color: var(--el-color-danger);
}
.platform-url-box {
  border: 1px solid var(--app-border);
  border-radius: 8px;
  padding: 12px 14px;
  margin-bottom: 16px;
  background: var(--app-panel-2);
}
.pu-label {
  font-size: 13px;
  font-weight: 600;
  color: var(--app-text);
  margin-bottom: 8px;
}
.pu-row {
  display: flex;
  align-items: center;
  gap: 10px;
}
.pu-value {
  font-size: 13px;
}
.step-body {
  padding: 4px 0 8px;
}
.hint {
  color: var(--app-text-muted);
  font-size: 13px;
  margin-top: 6px;
  line-height: 1.6;
}
code {
  background: var(--app-panel-2);
  padding: 1px 5px;
  border-radius: 3px;
}
</style>
