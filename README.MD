# SqlBackupTool
一款兼容多版本SQL Server 控制台备份工具
支持 SQL Server 2000 / 2005 / 2008 R2 / 2016

## ✨ 功能特性
- ✅ 自动识别SQL Server版本，自适应兼容语法
  - SQL2000：移除备份压缩、使用旧系统表 `sysdatabases`
  - SQL2005~2016：支持备份压缩、使用 `sys.databases`
- ✅ Windows身份验证 / SQL账号密码登录
- ✅ 开机倒计时自动备份（30秒等待，按键进入手动模式）
- ✅ 备份指定数据库 / 一键备份所有用户库
- ✅ 自动扫描U盘/移动硬盘，复制bak备份文件
- ✅ 备份操作日志持久化 `BackupLog.txt`
- ✅ 记住待备份数据库列表 `LastDbNames.txt`
- ✅ 纯控制台程序，无GUI、轻量绿色运行

## 🖥 运行环境
- 操作系统：Windows 7 / Windows 10 / Windows 11
- .NET Framework 4.8（必须预先安装）
> 【重要】不支持 .NET Core/.NET5+，无法连接SQL Server2000旧协议

## 📦 使用方法
1. 下载 Releases 中编译好的 `SqlBackupTool.zip`
2. 解压直接运行 `SqlBackupTool.exe`
3. 程序启动：
   - 优先尝试Windows集成身份登录本机SQL Server
   - 登录失败后手动填写IP、端口、账号密码
4. 启动30秒倒计时：
   - 按任意键进入手动菜单
   - 超时自动执行上次选中数据库备份

### 菜单说明
【1】备份所有用户数据库
【2】备份指定数据库（保存列表用于下次自动备份）
【0】退出程序

## ⚠️ 兼容性注意事项
1. **SQL Server 2000**
   - 需要服务器开启TCP/IP、1433端口放行
   - Win10/11环境建议开启旧版TLS/TDS协议兼容
2. SQL Server 2005 标准版：不支持备份压缩，程序自动适配语法
3. 远程数据库请确认防火墙开放数据库端口

## 🛠 源码编译
1. 使用 Visual Studio 2019/2022 打开 `SqlBackupTool.sln`
2. 项目目标框架：`.NET Framework 4.8`
3. 解决方案配置选择：`Release | Any CPU`
4. 编译输出目录：`SqlBackupTool\Release\`

## 📝 开源协议
MIT License，可自由修改、商用分发。