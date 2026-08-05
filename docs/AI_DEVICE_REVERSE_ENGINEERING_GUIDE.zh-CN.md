# 使用 AI 测试和逆向 Moto Buds 设备

本文档面向希望用 AI 助手分析自己耳机、验证功能并向 Moto Buds Manager 贡献适配信息的用户。它规定了安全测试顺序、证据要求和统一输出格式，使不同贡献者得到的报告可以比较、复查并转化为代码。

> 重要：只测试你拥有或已获得授权的设备。任何会修改设备状态的命令都必须由人在场逐项确认。不要测试未知 Opcode、固件升级、恢复出厂、密钥、账户绑定或其他不可逆功能。

## 目标产物

一次完整测试应产生：

1. 一份符合 [`device-reverse-report.md`](templates/device-reverse-report.md) 的 Markdown 报告；
2. 一份符合 [`device-reverse-report.schema.json`](schemas/device-reverse-report.schema.json) 的 JSON 数据；
3. 必要时附上已脱敏的收发 PDU、截图和软件日志；
4. 明确区分“APK 静态推断”“协议观察”“实机验证”，不能把推断写成已验证事实。

## 安全边界

AI 在整个任务中必须遵守以下规则：

- 先只读发现，再测试写命令。
- 每次只改变一个功能和一个变量。
- 写入前读取并记录初始状态；测试后恢复初始状态。
- 未能读取初始状态时，不发送修改命令。
- 仅测试仓库已知且属于当前型号候选白名单的 Opcode。
- 不枚举或暴力尝试未知 Opcode、payload、UUID 或 AT 命令。
- 不发送 OTA、DFU、恢复出厂、配对清除、账户密钥、nonce、日志清除等命令。
- 不自动播放高音量提示音；查找耳机功能必须由用户确认耳机未佩戴。
- 测试 ANC、通透或佩戴检测时提醒用户注意环境安全。
- 任一命令出现断连、异常音量、持续响铃或设备无响应，立即停止并恢复连接。
- 报告中不得出现完整蓝牙地址、账户密钥、PFX、证书、原始系统身份信息或用户音频。
- 不上传未经检查的完整系统日志。蓝牙地址统一写成 `XX:XX:XX:12:34:56`。

## AI 开始前必须读取的仓库资料

要求 AI 先阅读：

- `README.md`
- `AGENTS.md`
- `src/MotoBudsManager.Core/Protocol/ProtocolCodec.cs`
- `src/MotoBudsManager.Core/Protocol/SppFrameCodec.cs`
- `src/MotoBudsManager.Core/Models/FeatureCatalog.cs`
- `src/MotoBudsManager.Core/Models/ProductCatalog.cs`
- `tests/MotoBudsManager.Tests/` 中对应协议测试
- 本文档、报告模板和 JSON Schema

如果 APK、反编译代码、抓包或历史逆向报告由贡献者另行提供，AI 应同时读取并在报告的 `sources` 中记录文件名、SHA-256 和来源，不能假设其版本与当前设备固件一致。

## 测试准备

贡献者需要准备：

- Windows 10 2004 或更高版本；
- 已在 Windows 系统设置中完成配对的耳机；
- 当前仓库的 Debug 构建；
- 耳机和耳机盒电量充足；
- 安静、安全的测试环境；
- 能够人工确认左右耳、耳机盒、声音模式和佩戴状态。

建议先执行：

```powershell
dotnet restore MotoBudsManager.sln
dotnet build MotoBudsManager.sln -p:Platform=x64
dotnet test tests/MotoBudsManager.Tests -p:Platform=x64
```

硬件测试禁止设置 `MOTOBUDS_TEST_MODE=1`，否则连接的是模拟传输而不是真实耳机。

## 分阶段测试流程

### 阶段 0：建立测试身份

只记录与适配有关的信息：

- Windows 版本和应用 commit SHA；
- 耳机显示名称；
- 型号 ID，例如 `XT2443-1`；
- 颜色 ID；
- firmware version；
- Profile/transport：RFCOMM/SPP 或 BLE GATT；
- 来源是广播 Manufacturer Data、型号 UUID、设备响应还是人工标签。

完整蓝牙地址不得进入报告。

### 阶段 1：只读发现

在不改变耳机设置的前提下记录：

- Manufacturer Specific Data 的 11 字节结构；
- 型号、颜色、capability 和 transport 解析结果；
- GATT service/characteristic 或 RFCOMM service UUID；
- profile、features、configs、名称、硬件、firmware；
- 左耳、右耳、耳机盒电量和充电状态；
- 当前 ANC、EQ、入耳检测、双设备等已知状态；
- 设备主动通知是否到达以及通知 Opcode。

所有数值都要同时保留：

- 原始十六进制；
- 解析后的字段；
- 字节序；
- 数据来源；
- 时间戳。

如果型号无法稳定解析，应停止写测试，只提交只读发现结果。

### 阶段 2：建立基线

在测试任何控制功能前：

1. 保存所有可读功能的初始状态。
2. 连续观察至少 10 秒，确认没有状态自行变化。
3. 记录连接是否稳定、是否存在重复通知和通知频率。
4. 验证 UI 展示与人工观察一致。
5. 为每项准备明确的恢复动作。

### 阶段 3：逐项功能测试

每项功能都使用相同的状态机：

```text
读取初始状态
  -> 人工确认测试条件
  -> 发送一个已知命令
  -> 记录 transport write 结果
  -> 等待匹配 response/notification
  -> 再次读取状态
  -> 人工确认实际效果
  -> 恢复初始状态
  -> 再次读取并确认恢复
```

单项测试记录至少包括：

- feature ID；
- 测试前状态；
- Opcode、PDU type、sequence 和 payload；
- 发送时间和发送结果；
- 所有相关响应/通知；
- 首次响应延迟和最终稳定延迟；
- UI 是否先乐观更新；
- 最终读取值；
- 人工观察；
- 恢复命令和恢复结果；
- 结论与证据等级。

### 阶段 4：功能类别的特定要求

#### ANC / 通透

- 测试关闭、标准降噪、通透时分别记录 `[mode,type]`。
- 观察是否出现旧状态或中间态通知。
- 只有设备回传值与人工听感均匹配时，才标记 `hardware_verified`。
- 测试结束恢复最初模式。

#### 普通开关

适用于 Hi-Res、游戏模式、低音增强、音量增强、自动音量、入耳检测和双设备连接。

- 分别测试 `off -> on` 和 `on -> off`。
- 区分“写入 API 返回成功”和“设备确认成功”。
- 如果 UI 先改变而设备没有回传，标记为 `optimistic_only`，不能标记已验证。

#### EQ

- 先记录可用预设、当前预设、波段数量和自定义波段。
- 切换预设后等待所有分段 EQ 响应。
- 自定义 EQ 每次只修改一个波段，记录定点数/浮点数转换。
- 测试结束恢复完整原始 EQ，而不只是预设编号。

#### 左右耳控制

适用于查找耳机、佩戴、入盒和触控映射。

- 左右耳必须分别记录，禁止只写一个总状态。
- 查找耳机前人工确认耳机未佩戴并从低风险时长开始。
- 触控需要区分“绑定发生变化”和“用户实际触发手势”。

#### 电量和充电

- 只读观察，不制造低电状态。
- 区分连接快照、读取响应和固件主动通知。
- 同时记录 case/left/right 与三个充电位。
- Android 或 Windows 系统通用电量只能作为对照，不能替代私有协议数据。

#### 贴合度检测

- 必须先确认双耳佩戴。
- “命令已写入”不等于测试完成。
- 等待左右耳异步结果，分别记录 fit/not-fit/unknown。
- 超时后停止，不重复高速触发。

#### Audio Sharing、日志和固件

- 默认不纳入普通贡献者自动测试。
- Audio Sharing 还依赖平台 LE Audio 服务，单独标记 `platform_dependency`。
- 日志收集只能在贡献者明确同意且完成隐私检查后进行。
- 固件升级、DFU 和恢复出厂不得由此流程执行。

### 阶段 5：重复性验证

安全且可恢复的功能至少重复两轮。只有以下条件全部成立，才能提升为实机支持：

- 两轮 TX payload 一致；
- 回传 Opcode 和字段含义一致；
- UI、最终读取值和人工观察一致；
- 初始状态成功恢复；
- 未发生额外断连或异常副作用。

## 证据等级

报告只能使用以下等级：

| 等级 | 含义 |
|---|---|
| `static_analysis` | 仅从 APK、源码或资源推断，未观察真实设备 |
| `protocol_observed` | 已抓到真实 TX/RX，但未完整确认物理效果或恢复 |
| `hardware_verified` | 已完成至少两轮实机测试、人工确认和状态恢复 |
| `conflicting` | 不同轮次、设备或证据互相冲突 |
| `unsupported` | 设备明确返回不支持，且静态能力信息也不声明支持 |
| `inconclusive` | 超时、断连或证据不足，不能下结论 |

“UI 中出现此开关”只能证明应用认为功能可用，不等于硬件支持。

## AI 的结构化输出要求

AI 必须同时返回 Markdown 和 JSON，且不能省略失败、未知或未执行的项目。

### Markdown

使用 [`templates/device-reverse-report.md`](templates/device-reverse-report.md)，保留全部一级和二级标题。未获得的数据写 `unknown`，不得猜测。

### JSON

JSON 必须通过 [`schemas/device-reverse-report.schema.json`](schemas/device-reverse-report.schema.json) 校验。十六进制统一为：

- 大写；
- 两位字节；
- 空格分隔；
- 不带 `0x`，例如 `02 01 A0 00 02 00 03 00 01 01`。

时间统一为 ISO 8601 UTC。延迟使用整数毫秒。

## 可直接交给 AI 的主提示词

复制以下内容，并附上本仓库路径、设备型号以及允许 AI 读取的日志或 APK：

```text
你正在为 MotoBudsManager 仓库执行一项经过授权的耳机兼容性测试。

目标：识别当前设备的型号、颜色、传输方式、功能能力、命令 payload、响应/通知以及 UI 状态同步行为，输出可供维护者直接审查的结构化逆向报告。

开始前必须完整阅读：
1. AGENTS.md
2. docs/AI_DEVICE_REVERSE_ENGINEERING_GUIDE.zh-CN.md
3. docs/templates/device-reverse-report.md
4. docs/schemas/device-reverse-report.schema.json
5. Core 中的协议、型号和功能目录代码及相关测试

执行约束：
- 先完成只读发现和基线；在型号无法确认或初始状态无法读取时停止。
- 不扫描未知 Opcode，不猜测 payload，不执行 OTA、DFU、恢复出厂、清除配对、账户密钥或其他不可逆命令。
- 所有写操作必须逐项说明预期改变、恢复方式和风险，并等待我的明确确认。
- 每次只改变一个变量；每项测试后恢复初始状态。
- 查找耳机前确认耳机未佩戴。
- 不输出或提交完整蓝牙地址、密钥、证书和未审查的原始日志。
- 传输写成功不等于设备执行成功；必须区分 optimistic UI、response、notification、readback 和人工观察。
- 不因设备沉默而断言 unsupported；应写 inconclusive。

对每项功能记录：初始状态、TX PDU、全部 RX PDU、sequence 匹配、result、时间延迟、UI 变化、readback、人工观察、恢复过程、证据等级和异常。

最终必须生成：
1. 完整 Markdown 报告，严格使用 docs/templates/device-reverse-report.md；
2. 完整 JSON，严格符合 docs/schemas/device-reverse-report.schema.json；
3. 建议修改的 ProductCatalog、ModelCatalog/FeatureCatalog、协议解析器和精确单元测试向量；
4. 一份仍需人工确认的清单。

不得直接修改功能白名单或提交代码，除非我在看过报告后另行授权。
```

## 从报告转化为适配代码

维护者审查报告后，通常按以下顺序集成：

1. 将型号和颜色范围加入 `ProductCatalog`。
2. 只有 `hardware_verified` 功能才能加入默认功能白名单。
3. `static_analysis` 或 `protocol_observed` 功能保持实验性并显示警告。
4. 把报告中的精确 PDU 向量加入 xUnit 测试。
5. 为响应和主动通知分别增加解析测试。
6. 验证 fragmented SPP、sequence rollover、timeout 和 write-then-readback。
7. 硬件测试必须恢复初始状态，且不得放进自动 CI。

建议 PR 同时包含：

- 脱敏后的 Markdown 和 JSON 报告；
- 对应目录/解析代码；
- 精确字节向量测试；
- 硬件验证说明；
- 功能仍属实验性的明确标记。

## 提交报告

1. 先使用 GitHub 的“Device compatibility report” Issue 表单提交报告摘要。
2. 将完整 Markdown/JSON 作为 PR 放入 `docs/device-reports/<model>/<firmware>/`。
3. 原始抓包在确认不含隐私信息后再上传；无法确认时不要上传。
4. 一个 PR 只适配一个型号/固件系列，避免证据混杂。

建议目录：

```text
docs/device-reports/
└── XT2443-1/
    └── 01.00.129.12/
        ├── report.md
        └── report.json
```
