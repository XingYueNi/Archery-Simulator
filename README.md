# Archery Simulator · VR 射箭

用 Unity + PICO 做的 VR 射箭原型。一条完整的交互链：**抓弓 → 搭箭 → 拉弓 → 放箭 → 箭插在墙上**。

## 功能

- **抓弓 / 搭箭**：手抓住弓，另一只手把箭插进弓上的箭槽（`XRSocketInteractor` 吸附）
- **拉弓**：手部位移投影到弓的拉伸轴，归一化出 0~1 的拉弓量
- **放箭**：按拉弓量给初速（5~25 m/s），低于 10% 视为没拉动
- **飞行**：每帧对齐速度方向，弹道自然下坠
- **插住**：撞到什么就插住，保留撞击瞬间的角度
- **无限箭堆**：拿走一支补一支
- **弹道预览**：拉弓时实时画出预测抛物线 + 落点
- **音效**：拉弓（随拉弓量变调，停下就静音）/ 放箭 / 破空 / 命中
- **空放保护**：弓上没有箭时，不发射、不显示预览

## 环境要求

| 项 | 版本 |
|---|---|
| Unity | 2020.3.26f1c1 |
| 渲染管线 | URP 10.8.1 |
| XR | XR Interaction Toolkit 2.3.2 + XR Plug-in Management 4.2.1 |
| 头显 | PICO（PICO Unity Integration SDK 2.1.4） |

## 运行前必改：manifest 里的本地路径

`Packages/manifest.json` 里两个 PICO 包指向本机绝对路径，**别人 clone 下来必须改成自己的**：

```json
"com.unity.pico.livepreview": "file:D:/pico neo3",
"com.unity.xr.picoxr": "file:D:/PicoSDk"
```

PICO SDK 需要到 [PICO 开发者平台](https://developer.picoxr.com/) 下载（2.1.4 版本）。

**没有头显也能调**：用 XR Device Simulator（XRI 自带示例），键盘鼠标模拟双手。

## 操作

| 输入 | 行为 |
|---|---|
| 左手握持键 | 抓弓 |
| 右手握持键（靠近弓弦） | 抓住弓弦 |
| 向后拉 | 拉弓（能看到弹道预览） |
| 松开握持键 | 放箭 |
| 抓箭 → 靠近箭槽 | 自动搭箭 |

## 脚本结构

7 个脚本，约 1,230 行。

| 脚本 | 行数 | 职责 |
|---|---|---|
| `Arrow/PullInteraction.cs` | 217 | 弓弦：算拉弓量 + 弓弦视觉 + 松手发射 |
| `Archery/ArrowTrajectoryPreview.cs` | 304 | 弹道预览：模拟抛物线 + 画线 + 落点 |
| `ArrowControl.cs` | 52 | 箭槽：记录弓上那支箭 + 关碰撞体 |
| `Archery/ArrowLauncher.cs` | 59 | 发箭器：唯一的造箭入口 |
| `Archery/ArrowProjectile.cs` | 187 | 箭：设初速 + 飞行姿态 + 插住 |
| `Arrow/BowAudio.cs` | 170 | 弓的音效 |
| `ArrowSpawner.cs` | 242 | 刷箭点：维持箭堆 |

## 已知限制

- 拉弓用**握持键**选中弓弦，不是扳机键
- 箭堆的箭是 `FreezeAll` 冻结状态，抓起来才解冻
- 只有一张场景（`level_0`），没有关卡流程
- 插住只处理静态表面，插运动物体的表现没细调

## 素材与授权

| 内容 | 来源 | 授权 |
|---|---|---|
| 代码（`Assets/Scripts`） | 本人 | MIT（见 `LICENSE`） |
| 弓 / 箭模型 | Shelby Drabant | MIT（许可证随素材保留） |
| 手部模型 | Tilia BasicHand | MIT |
| 音效 | 脚本程序化合成 | 无版权限制 |