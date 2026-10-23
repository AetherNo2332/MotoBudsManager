# Moto Buds Manager

简体中文 | [English](README.md)

一款非官方、离线运行的 WinUI 3 桌面应用，用于管理已与 Windows 配对的 Motorola 耳机。经过实机验证的稳定路径面向 Moto Buds `XT2443-1` 和 RFCOMM/SPP；其他型号及逆向功能会明确标记为实验性功能。

> **由 Codex 生成：** 本仓库由 OpenAI Codex 在用户指导下生成并实现。AI 生成的代码可能存在缺陷，连接真实硬件前请审查并测试相关更改。

## 功能

- 发现 Windows 已配对耳机，不在应用内执行配对。
- 显示与型号、颜色匹配的产品图片，以及左耳、右耳和耳机盒的电量与充电状态。
- 为 XT2443-1 提供经过实机测试的降噪、空间音频、Hi-Res、游戏模式、音效增强、入耳检测、查找耳机、佩戴测试和双设备连接控制。
- 使用私有电量协议：连接时读取一次快照，随后接收固件实时更新，不进行固定轮询。
- 左右耳最低电量为 6–10% 时每次连接仅显示一次应用内提示；0–5% 且低电量耳机未充电时发送 Windows 通知，耳机盒不参与判断。
- 关闭窗口后在通知区域保持当前耳机连接，可通过托盘菜单重新打开或退出。
- 设置中可开启“登录后在后台静默启动”（默认关闭）。登录 Windows 后驻留托盘；打开应用时唤醒已有窗口，并尊重 Windows 启动应用设置及系统策略。
- Windows 连接兼容耳机后自动建立管理会话，每 2 秒检查连接和降噪状态；连接或降噪模式变化时通知，首次读取仅建立基线。手动断开后暂停自动重连，直到耳机与 Windows 再次连接。
- 支持简体中文、英文、浅色/深色主题，以及最小 760 × 600 的响应式窗口。
- 诊断日志仅保存在内存中并脱敏蓝牙地址；不包含遥测或网络上传。

## 环境要求

- Windows 10 2004 或更高版本，x64
- .NET 10 SDK、Windows App SDK 2.3 和 WinApp CLI
- 本地部署需要启用 Windows 开发者模式

## 构建与测试

```powershell
dotnet build MotoBudsManager.sln -c Release -p:Platform=x64
dotnet test MotoBudsManager.sln -c Release -p:Platform=x64
.\BuildAndRun.ps1 .\src\MotoBudsManager\MotoBudsManager.csproj
```

调试 UI 时可以使用仅限 Debug 构建的假设备模式：

```powershell
winapp run .\src\MotoBudsManager\bin\x64\Debug\net10.0-windows10.0.26100.0\win-x64 --args '--test-mode'
```

侧载方法请参阅 [INSTALL.md](INSTALL.md)，贡献规范请参阅 [AGENTS.md](AGENTS.md)。

## 安全与法律声明

实验性控制可能修改耳机设置。本项目与 Motorola 或 Lenovo 无隶属、授权或背书关系；相关名称和商标归各自权利人所有。MIT 许可证仅覆盖项目代码，随附的第三方产品图片和商标不包含在 MIT 授权范围内，详见 [THIRD_PARTY_NOTICES.md](THIRD_PARTY_NOTICES.md)。

## 许可证

项目代码采用 [MIT License](LICENSE) 开源。
