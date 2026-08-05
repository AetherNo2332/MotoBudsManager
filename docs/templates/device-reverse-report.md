# Moto Buds 设备逆向报告：`<model-id>`

## 1. 报告元数据

| 字段 | 值 |
|---|---|
| 报告版本 | `1.0` |
| 测试日期（UTC） | `<ISO-8601>` |
| 贡献者 | `<GitHub username or anonymous>` |
| 仓库 commit | `<SHA>` |
| 应用版本 | `<version>` |
| Windows 版本 | `<version>` |
| AI 工具/模型 | `<name and version>` |

## 2. 设备身份

| 字段 | 值 | 来源 | 证据等级 |
|---|---|---|---|
| 显示名称 | `<name>` | `<source>` | `<level>` |
| 型号 ID | `<XTxxxx-x>` | `<source>` | `<level>` |
| 颜色 ID | `<number>` | `<source>` | `<level>` |
| 固件版本 | `<version>` | `<source>` | `<level>` |
| Profile | `<value>` | `<source>` | `<level>` |
| Transport | `<rfcomm_spp/ble_gatt>` | `<source>` | `<level>` |
| Capability | `<hex>` | `<source>` | `<level>` |
| 地址 | `XX:XX:XX:12:34:56` | redacted | n/a |

## 3. 数据来源

| ID | 类型 | 文件/设备 | SHA-256 | 说明 |
|---|---|---|---|---|
| `source-1` | `<apk/source/capture/device/manual>` | `<name>` | `<hash or n/a>` | `<notes>` |

## 4. 只读发现结果

### 4.1 广播与型号

- Manufacturer data：`<HEX or unknown>`
- 型号解析：`<result>`
- 颜色解析：`<result>`
- Transport 判定：`<result>`

### 4.2 服务与 characteristic

| 用途 | UUID | 属性 | 证据等级 |
|---|---|---|---|
| `<purpose>` | `<uuid>` | `<read/write/notify>` | `<level>` |

### 4.3 初始状态

| 功能 | 初始值 | 数据来源 | 时间戳 |
|---|---|---|---|
| case battery | `<value>` | `<snapshot/response/notify>` | `<UTC>` |
| left battery | `<value>` | `<source>` | `<UTC>` |
| right battery | `<value>` | `<source>` | `<UTC>` |
| ANC | `<mode,type>` | `<source>` | `<UTC>` |

## 5. 功能测试结果

> 每个功能复制一节。未测试的功能也要保留并写明原因。

### `<feature-id>` — `<feature-name>`

- 测试状态：`passed / failed / skipped / inconclusive`
- 证据等级：`static_analysis / protocol_observed / hardware_verified / conflicting / unsupported / inconclusive`
- 风险等级：`read_only / reversible / disruptive / prohibited`
- APK/仓库声明支持：`true / false / unknown`
- 初始状态：`<value>`
- 目标状态：`<value>`
- 恢复状态：`<value>`
- UI 更新方式：`none / optimistic / confirmed / optimistic_then_corrected`

#### TX

| 时间 | Transport | Opcode | PDU type | Sequence | Payload | 写入结果 |
|---|---|---|---|---|---|---|
| `<UTC>` | `<transport>` | `<HHHH>` | `<type>` | `<number>` | `<HEX>` | `<result>` |

#### RX

| 时间 | 类型 | Opcode | PDU type | Result | Sequence | Payload | 延迟 ms |
|---|---|---|---|---|---|---|---|
| `<UTC>` | `<response/notification/readback>` | `<HHHH>` | `<type>` | `<number>` | `<number>` | `<HEX>` | `<ms>` |

#### 观察与结论

- UI 变化：`<description>`
- 人工观察：`<description>`
- Readback：`<description>`
- 恢复验证：`<description>`
- 异常/副作用：`<description or none>`
- 结论：`<what is proven and what is not>`

## 6. Opcode 观察表

| Opcode | 方向 | 语义 | Payload 字段 | 字节序 | Response/Notify | 证据等级 |
|---|---|---|---|---|---|---|
| `<HHHH>` | `<host_to_device/device_to_host>` | `<meaning>` | `<fields>` | `<endianness>` | `<opcodes>` | `<level>` |

## 7. 状态同步与 UI 行为

- 是否乐观更新：`<yes/no/by feature>`
- 写应答是否代表执行成功：`<finding>`
- 主动通知：`<finding>`
- 写后读取：`<finding>`
- 中间态过滤：`<finding>`
- 超时和重试：`<finding>`

## 8. 型号能力结论

| 功能 | 建议支持状态 | 证据等级 | 是否可加入默认白名单 | 备注 |
|---|---|---|---|---|
| `<feature>` | `<supported/unsupported/unknown>` | `<level>` | `<yes/no>` | `<notes>` |

## 9. 建议代码修改

| 文件 | 修改 | 所依据证据 | 测试要求 |
|---|---|---|---|
| `<path>` | `<change>` | `<evidence>` | `<test>` |

## 10. 精确测试向量

```text
Name: <test name>
Input: <HEX>
Expected: <parsed fields or HEX>
Source: <capture/test id>
```

## 11. 未解决问题

- `<unknown or conflicting finding>`

## 12. 隐私与安全检查

- [ ] 蓝牙地址已脱敏
- [ ] 不包含账户密钥、证书或令牌
- [ ] 不包含用户音频
- [ ] 原始日志已经人工检查
- [ ] 所有写测试均已恢复初始状态
- [ ] 未执行未知 Opcode、OTA、DFU 或恢复出厂
