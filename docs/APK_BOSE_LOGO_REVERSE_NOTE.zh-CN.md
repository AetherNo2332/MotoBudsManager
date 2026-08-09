# APK Bose 标识逆向说明

本文记录 Moto Buds APK 中 `SOUND BY BOSE` 标识的资源、型号开关和界面引用，供桌面端适配与后续复核使用。结论均来自 APK 静态逆向，未通过联网服务或耳机协议读取验证。

## 标识预览

![SOUND BY BOSE](assets/reverse-engineering/sound-by-bose.svg)

仓库内可复用文件：[`assets/reverse-engineering/sound-by-bose.svg`](assets/reverse-engineering/sound-by-bose.svg)。该 SVG 由 APK 的 Android Vector Drawable 等比例转换，保持原始 `79 × 14` 视口和路径数据。

## 来源与完整性

| 项目 | 值 |
| --- | --- |
| APK | `moto-buds-01-0-129-12-release.apk` |
| APK SHA-256 | `CD51050F54017FE0C9B373F8628F6CB810817AA13FB6E8EA1357E49351C8F23D` |
| APK 原始资源 | `res/drawable/ic_bose.xml` |
| 原始资源 SHA-256 | `FBCABD122EFDC5B36260DD9D8A3476980352E18642098F39165B704DB34D80EC` |
| Android 资源 ID | `drawable/ic_bose`，`0x7f08014c` |
| 原始尺寸 | `79dp × 14dp` |
| 原始视口 | `79 × 14` |

## 资源实现

`ic_bose.xml` 是 Android Vector Drawable，并非 PNG 或 JPEG。它包含 11 条矢量路径，组成两行的 `SOUND BY BOSE` 字样。所有路径都引用 `@color/icon_color`：

- 普通主题：`#000000`；
- 夜间主题：`#FFFFFF`。

因此二次开发时应把它当作单色、可随主题着色的图标。仓库中的 SVG 固定为黑色，用于文档预览；应用界面若同时支持深浅主题，应在 XAML 中通过主题资源着色，或分别提供黑白变体。

## APK 界面引用

布局 `res/layout/layout_battery_info.xml` 定义了以下 ImageView：

```xml
<androidx.appcompat.widget.AppCompatImageView
    android:id="@+id/soundBose"
    android:visibility="gone"
    android:layout_width="wrap_content"
    android:layout_height="wrap_content"
    android:scaleType="fitCenter"
    app:layout_constraintEnd_toEndOf="@+id/guideline_right"
    app:layout_constraintTop_toTopOf="parent"
    app:srcCompat="@drawable/ic_bose" />
```

由约束可知，标识位于耳机电量信息区域顶部靠右位置。布局默认设置为 `gone`，没有匹配能力标志时既不显示，也不占据布局空间。

## 型号显示条件

`assets/buds_features.json` 的每个设备变体可携带布尔字段 `sound_by_bose`。反编译类 `p006a5.g` 将该字段解析为可空 `Boolean soundByBose`；`p006a5.c.g(modelId)` 遍历指定型号的所有变体，只要任一变体的值为 `true` 就返回 `true`。

APK 内明确启用该标识的型号如下：

| 型号 ID | 内部名称 | 市场名称 | `sound_by_bose` |
| --- | --- | --- | --- |
| `XT2545-1` | `oboe` | `moto buds loop` | `true` |
| `XT2641-1` | `bass` | `Moto Buds 2 Plus` | `true` |
| `XT2441-1` | `flute` | `Moto Buds +` | `true` |

以下变体没有声明 `sound_by_bose`，静态配置不支持显示该标识：

- `XT2643-1` / `uke` / `Moto Buds 2`；
- `XT2443-1` / `guitar` / `Moto Buds`；
- `XT2443-1` / `guitar25` / `Moto Buds Bass`。

注意：当前 APK 的判断粒度最终是 `modelId`。如果同一型号下存在多个设备变体，只要其中一个变体标记为 `true`，辅助函数就会对该型号返回真。二次开发若需要更严格地区分地区版、颜色或硬件变体，应优先按设备 UUID 或内部名称匹配，不应只依赖型号 ID。

## 二次开发建议

1. 在产品目录中增加显式字段，例如 `SoundByBose`，不要从产品名称中搜索 `Bose`。
2. 默认值必须为 `false`；只有存在 APK 配置证据或实机产品标识证据时才启用。
3. 标识仅用于展示认证/调音信息，不属于耳机控制协议能力，不能据此推断 ANC、EQ 或空间音频支持情况。
4. 为避免 `XT2443-1` 这类共享型号产生误判，优先采用“型号 + 服务 UUID/内部变体”的组合键。
5. 深色主题应使用白色图标，浅色主题使用黑色图标，并保留 `79:14` 的宽高比。

## 法律与授权说明

`Bose`、`SOUND BY BOSE` 及相关图形是其权利人的商标。这里保存的矢量路径来自被分析 APK，仅用于兼容性研究、识别和文档说明，不属于本项目 MIT 许可证授权内容；使用和分发时还应遵守仓库的 `THIRD_PARTY_NOTICES.md`。
