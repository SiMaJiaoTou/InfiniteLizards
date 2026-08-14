# 桌面小蜥参数配置

桌面小蜥的行为、状态时长、状态转移矩阵、步态、悬挂物理、外观、次级动画、绘制尺寸和运行时参数都来自同一份 JSON 配置。生产代码只接收启动时解析完成的 `LizardProfile`，不会在行为、动画或窗口类中自行读取文件。

## 配置文件位置

默认位置：

```text
%LOCALAPPDATA%\DesktopLizard\lizard-settings.json
```

第一次启动会写出包含全部字段和默认值的完整文件，并生成一个持久的 `IndividualSeed`。右键蜥蜴后选择“编辑个体配置（重启后生效）”可以直接打开它。

也可以给任意副本指定独立配置：

```powershell
DesktopLizard.exe --config="D:\Pets\green-lizard.json"
```

JSON 允许注释、末尾逗号和不区分大小写的属性名。配置在启动时统一校验；若文件无效，原文件不会被覆盖，程序会暂时使用内存默认值，并把原因写入：

```text
%LOCALAPPDATA%\DesktopLizard\error.log
```

当前 schema 为 `5`。旧的 schema 1/2/3/4 文件会在首次启动时保留已有数值、补齐新分组，并原子写回完整 schema 5 文件。schema 1/2/3 的旧状态矩阵迁移时，会从每行末尾一个足够大的权重桶中划出 `0.02` 给 `LostGripFall`，其余参数保持不变。schema 4 的固定上限 `LostGripFall.MaximumDistance` 会被移除：旧默认 `MinimumDistance=78` 会升级为新默认 `150`，显式自定义的 `MinimumDistance` 则原样保留。schema 5 若仍包含已废弃的 `MaximumDistance`，会按未知字段拒绝，避免旧语义被误以为仍然生效。

## 顶层分组

| 分组 | 控制内容 |
| --- | --- |
| `Behavior` | 所有状态时长、速度、加减速、转弯、S 弯、追鼠、边缘恢复、失手下坠、释放冲刺、情绪、随机决策与状态转移矩阵 |
| `Gait` | 四腿长度、落点、步幅、抬脚高度、对角组错峰、支撑期、步频、转弯内外侧差异及 reach 保护 |
| `Physics` | 抓起后的重力、阻尼、约束迭代、速度上限、骨段弹簧、释放姿态恢复和有限值保护 |
| `Appearance` | 画布、缩放、脊柱、体宽、颜色、肢体线宽和阴影 |
| `SecondaryMotion` | 呼吸、尾摆、身体起伏、落地压缩、眼鼻位置和眨眼 |
| `Rendering` | 鼻、眼、瞳孔、脚掌、阴影偏移、命中半径、命中刷新率 |
| `Runtime` | 固定步进、追帧上限、导航留白、出生淡入、调试轨迹及调试侧栏尺寸 |
| `IndividualVariation` | 个体间速度、时长、转向、步态、物理、颜色和转移权重的变化幅度 |

数值稳定用的 epsilon、Win32 消息号等算法/平台不变量不属于宠物参数，因此不会暴露到 JSON。

## 状态转移矩阵

`Behavior.TransitionMatrix` 有四行：

- `AfterForward`：普通向前爬结束后的选择。
- `AfterCurve`：普通单向曲线结束后的选择。
- `AfterSCurve`：连续 S 弯结束后的选择。
- `AfterFast`：快速直爬或快速 S 弯结束后的选择。

每一行由 `Action` 和 `Weight` 组成，同一行权重必须相加等于 `1.0`，动作不可重复。可用动作包括：

```text
Forward
ForwardExtension
Curve
SCurve
FastForward
FastSCurve
TurnAround
LostGripFall
```

权重写成 `0` 会严格禁用该动作；个体差异重权也不会把它重新开启。`AfterFast` 同时服务于快速直爬和快速 S 弯，因此只允许 `Forward`、`ForwardExtension`、`Curve`、`SCurve`、`LostGripFall`，以保证两个来源都有合法落点。四行默认都给 `LostGripFall` 分配 `0.02`，低于普通爬行动作。

例如，把普通向前爬结束后的掉头降到 3%，并让 S 弯更常出现：

```json
{
  "Behavior": {
    "TransitionMatrix": {
      "AfterForward": {
        "Entries": [
          { "Action": "SCurve", "Weight": 0.52 },
          { "Action": "Curve", "Weight": 0.18 },
          { "Action": "TurnAround", "Weight": 0.03 },
          { "Action": "FastForward", "Weight": 0.05 },
          { "Action": "FastSCurve", "Weight": 0.03 },
          { "Action": "ForwardExtension", "Weight": 0.17 },
          { "Action": "LostGripFall", "Weight": 0.02 }
        ]
      }
    }
  }
}
```

实际文件会包含另外三行；请保留它们。快速状态默认只回到普通状态或低概率失手，避免连续高速动作形成长时间爆发。

## 失手下坠

`Behavior.LostGripFall` 控制一次完整的 `Falling → Regripping → Idle → 正常自主爬行` 生命周期。该状态只能由五种爬行态通过矩阵随机进入，不会从休息、观察、追鼠、边缘恢复或释放冲刺误入。

| 字段 | 含义 |
| --- | --- |
| `MinimumDistance` | 一次有效失手必须达到的最小可见下坠距离，单位为屏幕像素；默认 `150` |
| `MinimumInitialVelocity` / `MaximumInitialVelocity` | 起始下落速度范围 |
| `Gravity` | 下落加速度 |
| `MaximumFallVelocity` | 下落速度上限 |
| `BottomSafetyInset` | 相对正常导航区四边额外保留的完整渲染安全距离；底边同时以它确定下坠落点上限 |
| `RegripDuration` | 失手状态中的重新抓稳与压缩反馈时长 |
| `ResumeIdleDuration` | 抓稳后的短暂停顿时长，不属于长尾随机休息 |
| `PointerSuppressionDuration` | 下坠及抓稳期间的追鼠抑制时长 |

每次进入状态时，下坠目标会在 `MinimumDistance` 与“当前位置到本次完整渲染安全底边的全部可用距离”之间抽样，因此没有固定最大距离：屏幕越高、起点越靠上，就可能出现越长的自由落体。目标只在状态入口抽样一次，运行中不会因帧率或随机序列改变。

若安全底边以下的可用距离不足 `MinimumDistance`，或当前位置落入左、右、上方的完整渲染安全留白，本次失手都会取消并进入 `EdgeTurn`，不会用边界 Clamp 伪造一次下坠。宿主窗口会通过 `LostGripSafetyContext` 提供当前物理工作区的精确安全区域；若工作区过窄或过矮，无法在四边完整应用 `BottomSafetyInset`，即使常规导航区能被压缩出来，也必须拒绝失手。鼠标真实抓取和暂停可立即抢占；普通鼠标靠近不能把下坠切成追鼠。运行时按实际可绘制包络（含身体宽度、笔画、眼睛、脚掌、阴影和光栅留白）校验透明画布与屏幕四边，而不只检查蜥蜴中心点。

`RegripDuration` 控制行为阶段和抓稳反馈；骨架从粒子姿态恢复到规范爬行姿态的混合速度由共享的 `Physics.ReleasePoseRecoveryDuration` 控制。通常应让骨架恢复时长不大于重新抓稳阶段，以便状态结束前完成姿态收敛。

## 休息分布

`Behavior.Rest.Bands` 是可编辑的长尾分布。默认值为：

| 档位 | 权重 | 时长 |
| --- | ---: | ---: |
| 短休 | 30% | 1–2.8 秒 |
| 中休 | 35% | 2.8–7 秒 |
| 长休 | 25% | 7–13 秒 |
| 超长休 | 10% | 13–65 秒 |

规则：权重总和必须为 `1.0`，相邻档位必须首尾连续，所有时长必须是有限正数。个体差异开启后会依据活跃度和冷静度重新分配权重，但仍严格归一化。

## 普通与快速 S 弯

路径参数位于：

```text
Behavior.SCurve.Normal
Behavior.SCurve.Fast
```

关键字段：

- `MinimumCycleCount` / `MaximumCycleCount`：一次状态中连续 S 周期数。
- `MinimumCycleDuration` / `MaximumCycleDuration`：单个 S 周期时长；越长，空间转弯半径通常越大。
- `MinimumAmplitude` / `MaximumAmplitude`：目标角速度幅度；越大，弯越紧。
- `SpeedFactor`：该状态相对巡航/最大速度的倍率。
- `SteeringResponse`：角速度跟随响应。
- `ExtraCycleProbability`：在周期范围内追加周期的概率。

快速直爬和快速 S 弯会使用 `Behavior.Speed.MaximumCrawl` 作为速度上限。普通巡游区间由 `ReferenceMinimumCrawl` 与 `ReferenceMaximumCrawl` 控制。

## 个体差异

`IndividualSeed` 决定一只蜥蜴稳定的名字和性格值。相同配置 + 相同 seed 会产生完全相同的个体 Profile；换 seed 会得到不同的活跃度、好奇心、玩心、胆量、敏捷、冷静和体型。

`IndividualVariation.Enabled` 为 `true` 时，性格会在配置允许的幅度内联动：

- 活跃/敏捷影响巡航速度、快速动作和步态。
- 好奇/玩心影响曲线、S 弯、追鼠与状态矩阵权重。
- 冷静/活跃影响休息长尾和状态时长。
- 体型影响骨长、身体宽度、脚掌、眼睛和悬挂物理。
- 颜色变化只作用于外观，不会改变运动。

若需要所有实例严格使用 JSON 原值，将 `IndividualVariation.Enabled` 设为 `false`。

## 校验配置

开发版和发布版都可以单独运行配置自测：

```powershell
DesktopLizard.exe --configuration-test="D:\Temp\configuration-report.txt"
DesktopLizard.exe --lost-grip-test="D:\Temp\lost-grip-report.txt"
```

配置测试覆盖：默认配置校验、JSON 往返、seed 持久化、schema 3 状态矩阵迁移、schema 4 固定下坠上限迁移、schema 5 废弃字段拒绝、坏文件保留与回退、同 seed 确定性、不同个体差异、概率归一化，以及自定义状态矩阵是否真正控制选择结果。失手专项测试使用确定性矩阵，在 3 种屏幕高度、4 个起始 Y 区间和 36 个 seed 上覆盖自由落体、动态安全下界、同 seed 随可用高度增长的长下坠、重新抓稳连续性、恢复自主态、实际可绘制 AABB 的透明画布/完整 HWND 四边安全、极小工作区拒绝、鼠标/抓取/暂停优先级和有限值。

## 个体变化的精细控制

`IndividualVariation` 除了各模块的总变化幅度，还提供以下具名系数组：

- `Multipliers`：速度、时长、转向、步态、物理和体型的性格映射；
- `Behavior`：追鼠确认、追踪宽限和快速动作时长；
- `Transitions`：各个状态动作（含低概率失手）受活跃、好奇、敏捷、胆量和玩心影响的比例；
- `Rest`：短休与长休权重的个体偏移；
- `Animation`：起伏、尾摆、呼吸和眨眼；
- `Physics`：阻尼、约束响应、速度和肢体弹簧；
- `Appearance`：体色与阴影的个体偏色。

`RestWeightVariation` 单独控制休息档位的个体重分配：设为 `0` 时，`Behavior.Rest.Bands` 的权重会逐位保持原值。`TransitionWeightVariation` 或任意动作权重设为 `0` 也不会被个体变化重新启用。

启用矩阵个体差异时，`Transitions` 中每一种动作的性格系数组必须相加为 `1`，这样中性个体不会无意中重排矩阵；若要完全关闭矩阵个体差异，请把 `TransitionWeightVariation` 设为 `0`。启用个体差异时，速度、时长、转向、步态和追鼠注意力的组合系数也必须保证最坏个体仍产生正倍率，启动校验会拒绝可能得到零或负值的组合。

这些系数只改变“性格如何映射到参数”，不会改变 seed 的生成顺序，也不会消费行为状态机的随机流。保持相同配置与 `IndividualSeed`，最终 Profile 始终相同。

## 配置安全边界

- 未知 JSON 字段会被拒绝，避免拼写错误悄悄回落到默认值；注释、末尾逗号和属性名大小写仍然支持。
- `SimulationRate` 必须在 60–480 Hz，固定步长与 physics 的最小/最大步长必须闭合，fallback 步长也必须位于同一区间。
- `Behavior.Speed.BoundaryRecoverySpeedMultiplier` 只控制碰到桌面边缘后的安全恢复速度；普通爬行速度由 `ReferenceMinimumCrawl` / `ReferenceMaximumCrawl` 控制。schema 1/2 中旧名 `BaseSpeedMultiplier` 会在迁移时自动改名。
- 调试轨迹容量最多 10,000 点，冲刺方向候选最多 256 个，防止合法 JSON 造成无界分配或超长循环。
- `CreatureCanvasSize` 必须覆盖正常姿态，`RenderCanvasSize` 必须覆盖完整悬挂/自由落体姿态。校验统一考虑脊柱、腿长、脚掌、眼睛、鼻子、阴影和次级动作；个体体型变化超过原画布时，resolved Profile 会自动把透明画布向上扩展，并同步提升 `LostGripFall.BottomSafetyInset`，因此不会重新出现窗口内部裁切或屏幕四边裁切。
- 无效配置永远不会覆盖原文件。程序会记录失败原因并使用内存默认值启动。
- 启动时不仅校验物种原始配置，还会用当前 `IndividualSeed` 解析并校验最终个体 Profile；因此合法字段组合若会在该个体上产生无效步态、尺寸或时长，也会安全回退而不会在创建窗口时崩溃。
