# Windows DHCP WebUI

一个用于集中管理多台 **Windows Server DHCP** 的 Web 平台。通过部署在被管服务器上的轻量
Agent(Windows 服务),用 **SignalR 出站长连接** 远程执行 DHCP 相关的 PowerShell cmdlet,
在浏览器里统一管理作用域、租约、保留、选项、工区分组、租约日志与告警。

Agent 只建立**出站**连接,被管服务器无需开放任何入站端口。

---

## 功能特性

- **多服务器纳管**:一个平台管理多台 Windows DHCP 服务器,按「工区」分组。
- **作用域管理**:作用域的增删改查、使用率统计。
- **租约 / 保留 / 选项**:查看租约、维护静态绑定(保留)、配置网关 / DNS 等选项。
- **租约日志**:后台持续采集租约快照,支持按关键字 / 服务器分页检索历史。
- **飞书告警**:作用域使用率超阈值时推送飞书群机器人告警。
- **登录鉴权与两级角色**:内置 JWT 登录,`Admin`(全权)与 `Viewer`(只读)两级角色。
- **主题**:内置浅色 / 深色主题,可用 URL 参数或 postMessage 与上层平台联动。
- **可嵌入**:SPA + hash 路由,支持 `?embed` 隐藏侧栏 / 顶栏,方便 iframe 嵌入上层平台。
- **在线纳管 / 卸载**:平台内下载 Agent、生成预填安装命令;删除服务器时可远程卸载 Agent。
- **免迁移改址**:平台搬家或换域名时,可在线下发 Agent 回连地址变更。

---

## 技术栈

| 层 | 技术 |
|----|------|
| 后端 API | ASP.NET Core 8 · EF Core 8 (SQLite) · SignalR · JWT Bearer |
| Agent | .NET 8 Worker Service(Windows 服务)· SignalR Client · PowerShell |
| 前端 | Vue 3 · Vite · Element Plus · Pinia · Vue Router |

密码使用 PBKDF2(`Rfc2898DeriveBytes`,HMACSHA256)哈希存储;数据库通过 `EnsureCreated()`
加幂等的原生 SQL 补丁演进(不依赖 EF migration)。

---

## 目录结构

```
backend/
  DhcpWeb.Api/     后端 API(控制器、SignalR Hub、服务、EF 实体)
  DhcpAgent/       Windows Agent(安装为服务,执行 DHCP cmdlet)
  DhcpWeb.Tests/   单元测试(service / 工具层)
frontend/
  dhcp-web/        Vue 3 前端
集成说明.md          iframe 嵌入 / API / 部署 参考
```

---

## 快速开始(本地开发)

前置:.NET 8 SDK、Node.js 18+。

### 后端

```bash
cd backend/DhcpWeb.Api
# 本地开发默认用假传输(Dhcp:UseFakeTransport=true),无需真实 Windows DHCP
dotnet run
# 监听 http://localhost:5280
```

首次启动会自动建库并创建默认管理员:**用户名 `admin` / 密码 `admin`**,请登录后尽快修改。

### 前端

```bash
cd frontend/dhcp-web
npm install
npm run dev
# 访问 http://localhost:5173
```

### 测试

```bash
cd backend
dotnet test
```

---

## 纳管一台 Windows DHCP 服务器

1. 以管理员登录平台,点右上角「接入服务器」,下载 `DhcpAgent.exe` 并拷贝到目标服务器。
2. 在目标服务器上以**管理员** PowerShell 运行弹窗中预填好的安装命令:

   ```powershell
   .\DhcpAgent.exe install --server http://<平台地址>:8090 --secret <连接密钥>
   ```

3. Agent 会注册为 Windows 服务、自动连回平台并出现在列表中。卸载:`.\DhcpAgent.exe uninstall`。

前置条件:运行账号需在 **DHCP Administrators** 组,且服务器能访问平台地址。

Agent 连接密钥默认 `admin`,可在「接入服务器」弹窗中修改(修改后已装旧 Agent 需用新密钥重装)。

---

## 生产部署要点

- 后端可发布为自包含单文件:

  ```bash
  dotnet publish backend/DhcpWeb.Api/DhcpWeb.Api.csproj -c Release \
    -r linux-x64 --self-contained true -p:PublishSingleFile=true
  ```

- 前端 `npm run build` 生成 `dist/`,由 Nginx 等静态服务器托管,并反代 `/api`、`/hubs`。
- **务必**在 `appsettings.Production.json` 配置一个强随机的 JWT 密钥:

  ```json
  {
    "Jwt": { "Key": "<至少 32 字节的强随机字符串>", "Issuer": "DhcpWeb", "ExpireHours": 12 }
  }
  ```

- 首次上线后立即修改默认 `admin` 密码;`Agent:EnrollmentSecret` 建议改为自定义值。

更完整的部署 / 集成 / API 说明见 [集成说明.md](集成说明.md)。

---

## 安全提示

- 默认管理员 `admin/admin` 与默认连接密钥 `admin` 仅为开箱即用,**生产环境请务必修改**。
- 源码中的 `Jwt:Key` 默认值是明确标注的开发用占位,**切勿用于生产**。
- 本平台假设部署在可信网络,或由上层平台 / 网关做额外的访问控制。

---

## 许可证

[MIT](LICENSE)
