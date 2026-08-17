# DnsRouteUI — Windows DNS 分流与缓存隔离管理工具

一个为 Windows Server 2019 DNS Server 提供图形化 DNS 分流（Split-Brain DNS）配置能力的桌面管理工具。

管理员可按客户端 IPv4 网段将 DNS 递归查询转发到不同上游 DNS，并按"上游解析配置档"隔离缓存，既避免不同上游解析结果互相污染，也避免按网段单独隔离造成的缓存碎片化。

## 功能特性

### 核心能力

- **按网段分流**：基于客户端 IPv4 CIDR 将 DNS 请求路由到不同上游 DNS 服务器
- **缓存隔离**：按上游解析配置档隔离缓存，同一配置档的多个网段共享缓存，提高命中率
- **条件转发开关**：一键开启/关闭 DNS 分流，关闭时 DNS Server 回退默认递归行为
- **默认策略**：未命中任何手工规则的客户端使用默认上游配置，保证可用性

### 安全与可靠性

- 所有托管对象统一使用 `DnsRouteUI_` 前缀，不触碰手工配置
- 应用前强制展示变更预览，支持仅导出脚本不执行
- 每次应用前自动创建配置备份与 DNS 对象快照
- 修改默认递归范围 `.` 时脚本自动检测，仅在配置不同时才变更
- 应用后可清理受影响缓存范围

### 运维辅助

- 环境与服务状态实时检测（管理员权限、DNS 服务、模块可用性）
- 一键导出完整诊断信息（含服务器实际对象查询、配置内容、运行日志）
- 结构化 JSONL + 人类可读文本双写日志
- 上游 DNS 连通性测试与 DNS 解析验证

## 截图

> 可在此处添加应用界面截图

## 系统要求

| 项目 | 要求 |
|------|------|
| 操作系统 | Windows Server 2019 x64（兼容 Windows 10/11 x64） |
| DNS 角色 | 必须安装 DNS Server 角色及 DnsServer PowerShell 模块 |
| 权限 | 管理员权限（UAC 提权） |
| 依赖 | 无需预装 .NET 运行时（单文件自包含发布） |

## 快速开始

### 方式一：直接使用发布版

1. 从 [Releases](../../releases) 下载 `DnsRouteUI.exe`（约 68 MB）
2. 复制到 Windows Server 2019 任意目录
3. 双击运行（会弹出 UAC 提权请求）
4. 首次运行会自动初始化默认配置到 `C:\ProgramData\DnsRouteUI\config.json`

> 首次运行时会将原生依赖解压到 `%TEMP%\.net\DnsRouteUI\<hash>\`，需要约 200 MB 临时空间。

### 方式二：自行编译发布

```bash
git clone <repository-url>
cd Winserver-DNS转发器
dotnet publish DnsRouteUI/DnsRouteUI.csproj -c Release -r win-x64 --self-contained true
```

发布产物位于：
```
DnsRouteUI/bin/Release/net8.0-windows/win-x64/publish/DnsRouteUI.exe
```

## 使用指南

### 1. 配置上游解析配置档

在"上游解析配置档"页创建配置档，每个配置档代表一套可共享的 DNS 解析上下文：

| 字段 | 说明 | 示例 |
|------|------|------|
| 名称 | 配置档显示名 | 阿里 DNS |
| 转发器 | 上游 DNS 服务器列表 | 223.5.5.5, 223.6.6.6 |
| 启用递归 | 是否允许递归查询 | 是 |
| 缓存隔离 | 是否独立缓存范围 | 是 |
| 缓存范围名 | 自动生成，允许修改 | DnsRouteUI_Cache_AliDNS |

### 2. 配置网段策略

在"网段策略"页创建规则，将客户端网段映射到配置档：

| 优先级 | 规则名称 | 客户端网段 | 解析配置档 |
|-------:|---------|-----------|-----------|
| 1 | 办公网段 | 192.168.1.0/24 | 阿里 DNS |
| 2 | 研发网段 | 192.168.3.0/24 | 114 DNS |
| 默认 | 默认策略 | 其他所有客户端 | 默认公共 DNS |

- 支持上移/下移调整优先级
- 支持启用/禁用单条规则
- 网段重叠时给出警告
- 默认策略不可删除、不可移动

### 3. 开启条件转发

在"环境与服务状态"页勾选"启用条件转发"开关：

- **开启**：应用网段递归策略与缓存策略，按客户端网段分流到不同上游 DNS
- **关闭**：移除所有 `DnsRouteUI_` 前缀的递归策略/缓存策略/客户端子网，DNS Server 回退默认行为

> 开关状态仅影响下次"应用"时生成的脚本内容，必须点击"应用"按钮才会实际修改 DNS Server。

### 4. 变更预览与应用

在"变更预览与应用"页：

1. 自动生成变更预览（展示将创建/更新/删除的 DNS 对象）
2. 确认变更内容
3. 点击"应用"按钮（自动创建备份 + 执行 PowerShell 脚本）
4. 查看应用结果

也可选择"导出 PowerShell 脚本"，在生产环境审批后手动执行。

### 5. 验证与诊断

应用完成后，可在 PowerShell 中验证：

```powershell
# 查看递归范围（上游转发器组）
Get-DnsServerRecursionScope | Where-Object { $_.Name -like 'DnsRouteUI_*' }

# 查看客户端子网
Get-DnsServerClientSubnet | Where-Object { $_.Name -like 'DnsRouteUI_*' }

# 查看服务器级递归分流策略
Get-DnsServerQueryResolutionPolicy | Where-Object { $_.Name -like 'DnsRouteUI_*' }

# 查看 ..cache 分区级缓存策略
Get-DnsServerQueryResolutionPolicy -ZoneName '..cache' | Where-Object { $_.Name -like 'DnsRouteUI_*' }
```

如遇问题，在"环境与服务状态"页点击"导出诊断信息"，生成的文件位于软件目录下的 `logs` 文件夹，包含 7 个分区：环境摘要、软件运行环境、配置内容、DNS Server 概况、托管对象实际查询、默认范围状态、运行日志。

## DNS Server 对象映射

程序创建的所有对象统一使用 `DnsRouteUI_` 前缀，避免误操作手工配置：

| 程序对象 | Windows DNS 对象 | 示例名称 |
|---------|------------------|----------|
| 客户端网段 | Client Subnet | `DnsRouteUI_Subnet_office` |
| 上游配置档 | Recursion Scope | `DnsRouteUI_Resolver_AliDNS` |
| 配置档缓存 | `..cache` Zone Scope | `DnsRouteUI_Cache_AliDNS` |
| 网段缓存规则 | Cache Query Resolution Policy | `DnsRouteUI_CachePolicy_office` |
| 网段递归规则 | Recursion Query Resolution Policy | `DnsRouteUI_RecursionPolicy_office` |
| 默认策略 | 默认 Recursion Scope `.` | `.` |

每条网段规则创建 2 条策略（缓存策略 + 递归策略），每个配置档创建 1 个递归范围 + 1 个缓存范围。

## 查询处理流程

```text
客户端 DNS 请求
  ↓
查询本机权威区域
  ↓
按客户端网段匹配缓存策略（..cache 分区）
  ↓
查询对应的缓存范围
  ↓
缓存未命中时，按客户端网段匹配递归策略（服务器级）
  ↓
使用对应配置档的转发器
  ↓
将结果写入该配置档对应的缓存范围
```

## 配置文件

配置持久化为 JSON，存储位置：

| 路径 | 说明 |
|------|------|
| `C:\ProgramData\DnsRouteUI\config.json` | 主配置文件 |
| `C:\ProgramData\DnsRouteUI\backups\` | 应用前自动备份 |
| `C:\ProgramData\DnsRouteUI\logs\` | 结构化日志（JSONL + 文本） |
| `<软件目录>\logs\` | 诊断信息导出 |

配置结构示例：

```json
{
  "version": 1,
  "conditionalForwardingEnabled": true,
  "cacheIsolation": "ByResolverProfile",
  "resolverProfiles": [
    {
      "id": "alidns",
      "name": "阿里 DNS",
      "forwarders": ["223.5.5.5", "223.6.6.6"],
      "enableRecursion": true,
      "cacheIsolationEnabled": true,
      "cacheScopeName": "DnsRouteUI_Cache_AliDNS"
    }
  ],
  "rules": [
    {
      "id": "office",
      "name": "办公网段",
      "clientSubnet": "192.168.1.0/24",
      "resolverProfileId": "alidns",
      "priority": 1,
      "enabled": true
    }
  ],
  "defaultPolicy": {
    "resolverProfileId": "default-public",
    "enabled": true
  }
}
```

## 缓存隔离模式

| 模式 | 说明 | 适用场景 |
|------|------|----------|
| 共享默认缓存 | 所有策略共用默认缓存 | 所有上游结果可互换，追求最高缓存命中率 |
| 按配置档隔离缓存 | 同一配置档共享缓存 | 推荐默认模式 |
| 按策略隔离缓存 | 每条网段规则单独缓存 | 解析结果必须完全隔离的高要求场景 |

## 技术栈

- **开发语言**：C# 12 / .NET 8
- **桌面框架**：WPF (Windows Presentation Foundation)
- **UI 架构**：MVVM（[CommunityToolkit.Mvvm](https://github.com/CommunityToolkit/dotnet)）
- **依赖注入**：[Microsoft.Extensions.DependencyInjection](https://learn.microsoft.com/dotnet/core/extensions/dependency-injection)
- **DNS 管理**：Windows DnsServer PowerShell 模块
- **配置存储**：System.Text.Json
- **发布方式**：单文件自包含（SelfContained + PublishSingleFile）

## 项目结构

```
DnsRouteUI/
├── Models/                  # 数据模型
│   ├── DnsRouteConfig.cs    # 配置根模型
│   ├── ResolverProfile.cs   # 上游解析配置档
│   ├── SegmentRule.cs       # 网段策略
│   ├── DefaultPolicy.cs     # 默认策略
│   └── ...
├── Services/                # 业务服务
│   ├── ConfigService.cs         # 配置持久化
│   ├── ScriptExportService.cs   # PowerShell 脚本生成
│   ├── DnsServerService.cs      # DNS Server 操作
│   ├── ChangePreviewService.cs  # 变更预览
│   ├── EnvironmentService.cs    # 环境检测与诊断
│   ├── BackupService.cs         # 应用前备份
│   ├── PowerShellService.cs     # PowerShell 执行器
│   ├── ValidationService.cs     # 配置校验
│   └── FileLogger.cs            # 日志服务
├── ViewModels/              # MVVM 视图模型
│   ├── MainViewModel.cs
│   ├── ResolverProfilesViewModel.cs
│   ├── SegmentRulesViewModel.cs
│   ├── PreviewApplyViewModel.cs
│   ├── EnvironmentViewModel.cs
│   └── TestLogViewModel.cs
├── Views/                   # WPF 视图
│   ├── ResolverProfilesView.xaml
│   ├── SegmentRulesView.xaml
│   ├── PreviewApplyView.xaml
│   ├── EnvironmentView.xaml
│   └── TestLogView.xaml
├── Mvvm/                    # MVVM 基础设施
├── App.xaml                 # 应用入口与 DI 注册
├── appsettings.json         # 应用配置选项
├── app.manifest             # 清单（要求管理员权限）
└── DnsRouteUI.csproj        # 项目文件（含发布配置）
```

## 限制说明

当前版本不支持：

- 远程 DNS Server 管理（仅管理本机）
- IPv6 客户端网段
- 域名条件转发和域名级策略
- 多台 DNS Server 的同步与复制
- 用户、角色、审批流
- DNS-over-HTTPS / DNS-over-TLS
- DNSSEC、出站接口、时间策略等高级配置

## 许可证

本项目未指定开源许可证。如需使用、修改或分发，请联系作者获取授权。
