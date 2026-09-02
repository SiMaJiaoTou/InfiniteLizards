# 桌面小蜥参数配置

桌面小蜥的行为、状态时长、状态转移矩阵、步态、悬挂物理、外观、次级动画、绘制尺寸和运行时参数都来自同一份 JSON 配置。生产代码只接收启动时解析并验证完成的 `LizardProfile`；行为、动画、Engine runtime 与平台窗口都不会自行读取文件。

配置中的 screen/world 距离与速度统一按 96-DIP 逻辑世界解释，和当前屏幕的 device-pixel/backing scale 无关。字段名中为兼容旧 schema 保留的 `Pixels` 后缀也遵守这一语义；明确带 `Model` 的字段先处于蜥蜴模型空间，再乘 `Appearance.VisualScale` 转成 world unit。

## 配置文件位置

实现使用 `Environment.SpecialFolder.LocalApplicationData`，因此 .NET 8 默认位置为：

```text
Windows: %LOCALAPPDATA%\DesktopLizard\lizard-settings.json
macOS:   ~/Library/Application Support/DesktopLizard/lizard-settings.json
```

第一次启动会尝试写出包含全部字段和默认值的完整文件，并持久化 `IndividualSeed`；写入失败时仅在当前会话使用内存配置。右键蜥蜴后选择“编辑个体配置（重启后生效）”可以直接打开它。

繁育进度不属于这份物种/桌宠调参 JSON。普通模式会在有效配置文件同目录单独维护：

```text
Windows: %LOCALAPPDATA%\DesktopLizard\breeding-world.json
macOS:   ~/Library/Application Support/DesktopLizard/breeding-world.json
```

使用 `--config=/some/place/lizard-settings.json` 时，繁育存档也会放在 `/some/place/breeding-world.json`。`--diagnostic` 或 `--multi-instance` 使用独立的会话临时世界并在退出时清理，防止测试/并行实例改写正式血统；`--debug-overlay` 仍使用正式持久世界。

也可以给任意副本指定独立配置；跨平台宿主可使用绝对路径：

```bash
dotnet run --project src/InfiniteLizards.Desktop/InfiniteLizards.Desktop.csproj -- \
  --config="$PWD/green-lizard.json" --multi-instance
```

Windows 发布产物同样接受：

```powershell
InfiniteLizards.Desktop.exe --config="D:\Pets\green-lizard.json" --multi-instance
```

JSON 允许注释、末尾逗号和不区分大小写的属性名。配置在启动时统一校验；若文件无效，原文件不会被覆盖，程序使用内存默认值。跨平台宿主把原因写到诊断 trace；旧 WPF 兼容入口还会尝试追加到 `%LOCALAPPDATA%\DesktopLizard\error.log`。日志写入失败不会让已经完成的安全回退再次失败。

当前源码 schema 为 `8`。schema 1–7 文件会在首次启动时尽量保留已有设置、补齐新增字段并执行以下显式安全迁移，再以临时文件+替换方式原子写回完整 schema 8：

- schema 1/2/3 的旧状态矩阵会从每行末尾一个足够大的权重桶划出 `0.02` 给 `LostGripFall`，其余参数保持不变。
- schema 4 的固定上限 `LostGripFall.MaximumDistance` 会被移除；旧默认 `MinimumDistance=78` 升级为当前默认 `150`，显式自定义值原样保留。
- schema 5 升级到 schema 6 时补齐 `LostGripFall.ReachLeadDistance`，不会改写已有下坠参数或状态矩阵权重。
- schema 6 升级到 schema 7 时补齐 `AccelerationRampDuration` 和 `InitialAccelerationRatio`；已有的初速、重力和终端速度保持不变。
- schema 7 升级到 schema 8 时只给新增的 `RegripRearReachLengthFactor` 与 `RegripRearReachOutwardWeight` 补默认值；已有前肢伸手、下坠、接触保持和其余物理值原样保留。
- schema 1–7 中过小的 `LostGripFall.BottomSafetyInset` 会按实际渲染包络向上调整，避免旧配置让完整窗口越过安全边界。
- 当前 schema 若仍包含废弃的 `MaximumDistance`，会按未知字段拒绝，避免旧语义被误以为仍然生效。

如果文件已成功读取、迁移和完整校验，但因权限/I/O 问题无法写回迁移结果或新生成的 seed，本次会话仍使用这份有效的内存配置并发出 warning；不会因为“持久化失败”而丢弃已经验证过的用户配置。真正的解析/校验失败才会保留原文件并回退到内存默认值。

## 顶层分组

| 分组 | 控制内容 |
| --- | --- |
| `Behavior` | 所有状态时长、速度、加减速、转弯、S 弯、追鼠、边缘恢复、失手下坠、释放冲刺、情绪、随机决策与状态转移矩阵 |
| `Gait` | 四腿长度、落点、步幅、抬脚高度、对角组错峰、支撑期、步频、转弯内外侧差异及 reach 保护 |
| `Physics` | 抓起后的重力、阻尼、约束迭代、速度上限、骨段弹簧、回抓伸手/接触保持、释放姿态恢复和有限值保护 |
| `Appearance` | 画布、缩放、脊柱、体宽、颜色、肢体线宽和阴影 |
| `SecondaryMotion` | 呼吸、尾摆、身体起伏、落地压缩、眼鼻位置和眨眼 |
| `Rendering` | 鼻、眼、瞳孔、脚掌、阴影偏移、命中半径、命中刷新率 |
| `Runtime` | 固定步进、追帧上限、导航留白、出生淡入、调试轨迹及调试侧栏尺寸 |
| `IndividualVariation` | 个体间速度、时长、转向、步态、物理、颜色和转移权重的变化幅度 |

数值稳定用的 epsilon、Win32 消息号等算法/平台不变量不属于宠物参数，因此不会暴露到 JSON。

## 繁育世界与规则

`breeding-world.json` 是严格校验的玩家状态快照，不是第二份可自由调参的配置。它记录 envelope schema、UTC 存档时间、下一次启动的桌宠 ID、trait registry ID、繁育规则、金币、ID 序号、确定性随机流状态，以及每只蜥蜴/蛋的完整双等位基因、年龄/孵化进度、冷却、地点、亲本和世代。

当前领域默认基础孵化时间为 20 分钟、基础成熟时间为 6 小时、繁育冷却为 12 分钟。蛋的实际孵化时长由子代孵化/代谢与双方亲本护蛋效率对称决定；幼体成熟时长由成熟速度、成长活力和代谢决定；每只亲本的合群恢复、独处需求、社交恢复与求偶准备分别决定自己的冷却时长。所有倍率均为确定性有限值，买入/卖出价格仍是领域常量 5/1，不从 `lizard-settings.json` 读取。规则随世界快照保存，因此未来修改代码默认值也不会静默重写已有世界的节奏。

正常启动会按 envelope 的 UTC 时间结算离线成长，最多推进 30 天并立即保存已结算结果。上次时间比当前时间晚超过 5 分钟时会暂停离线成长并给出警告。正常操作先写 `.tmp`，保留一份 `.bak` 后替换主文件；无效文件会尽量保留为 `.invalid-YYYYMMDD-HHMMSS`，再创建新的 10 金币世界。买、卖或繁育若无法持久化会恢复操作前快照，避免内存与磁盘经济分叉。

不建议手工编辑繁育存档：未知字段、错误 schema/registry、越界 allele、无效生命周期或负经济都会被拒绝。纯新增遗传维度只能通过包含旧 trait 完整规范序列的 predecessor manifest 升级；删除、改名或改变既有语义需要专门迁移。当前 `infinite-lizards.genetics.v2` 对 v1 的唯一删除是没有实际消费面的 `lifecycle.longevity`，迁移会原样保留其余 137 个位点并明确丢弃该旧位点。具体流程见 [BREEDING.md](BREEDING.md)。

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
| `MinimumDistance` | 一次有效失手必须达到的最小可见下坠距离，单位为 96-DIP world；默认 `150` |
| `ReachLeadDistance` | 到达抓稳点前开始准备伸展姿态的距离，单位为 96-DIP world；运行时不会超过本次实际下坠距离 |
| `MinimumInitialVelocity` / `MaximumInitialVelocity` | 起始下落速度范围 |
| `Gravity` | 下落加速度 |
| `AccelerationRampDuration` | 加速度从起始比例平滑增长到完整 `Gravity` 的时长；默认 `0.22` 秒，`0` 可恢复恒定重力 |
| `InitialAccelerationRatio` | 下落刚开始时相对 `Gravity` 的加速度比例；默认 `0.18`，有效范围 `0–1` |
| `MaximumFallVelocity` | 下落速度上限 |
| `BottomSafetyInset` | 相对正常导航区四边额外保留的完整渲染安全距离，单位为 96-DIP world；底边同时以它确定下坠落点上限 |
| `RegripDuration` | 失手状态中的重新抓稳与压缩反馈时长 |
| `ResumeIdleDuration` | 抓稳后的短暂停顿时长，不属于长尾随机休息 |
| `PointerSuppressionDuration` | 下坠及抓稳期间的追鼠抑制时长 |

每次进入状态时，下坠目标会在 `MinimumDistance` 与“当前位置到本次完整渲染安全底边的全部可用距离”之间抽样，因此没有固定最大距离：屏幕越高、起点越靠上，就可能出现越长的自由落体。目标只在状态入口抽样一次，运行中不会因帧率或随机序列改变。加速度用 smoothstep 曲线渐入，速度增量使用解析积分，因此不同显示刷新率不会改变同一固定步时间线。

进入最后 `ReachLeadDistance` 后，`LostGripReachProgress` 会在窗口仍向下移动时从 `0` 连续增长到 `1`，粒子 rig 据此让四条肢体同时伸向四个独立抓点。抓点都带有向屏幕上方和身体外侧的分量，同时前肢沿头部方向、后肢沿尾部方向分开，避免四足叠在同一点。每只足会在自己的肩关节局部空间选择持续朝抓点推进、避开远端躯干包络的短弧或外绕点路径，而不是用直线穿过肩部后先缩手再伸手。IK 默认保持捕获时的弯曲支路；只有接近完全伸直且肘部相邻帧连续时才允许一次受控换支。换支后的路径若需要绕开躯干，会在单个固定步不超过 `4` model px 的范围内投影到安全姿态，正常回抓不允许用回滚冻结脚掌。正常到达目标（`ReachedTarget`）时，最终移动子步仍保持 `FreeFall` 并完成四足接触；下一个静止子步才进入 `ContactHold` 和 `Regripping`，避免“窗口先停住、蜥蜴随后才伸手”的悬空停顿。

若伸手中途工作区变化使安全契约失效，行为会标记 `SafetyForced`。最后的位移仍由 `FreeFall` 消费，但只保留变化前的部分伸手进度，绝不会伪造进度 `1` 或四足接触；下一静止步若 IK 未完成，会跳过 `ContactHold` 并连续恢复普通姿态。

配置必须满足 `min(MinimumDistance, ReachLeadDistance) >= 3 × MaximumFallVelocity / Runtime.SimulationRate`，保证即使在最大落速下，最短一次失手也至少有三个固定模拟子步用于伸手准备。

若安全底边以下的可用距离不足 `MinimumDistance`，或当前位置落入左、右、上方的完整渲染安全留白，本次失手都会取消并进入 `EdgeTurn`，不会用边界 Clamp 伪造一次下坠。宿主窗口会通过 `LostGripSafetyContext` 提供当前物理工作区的精确安全区域；若工作区过窄或过矮，无法在四边完整应用 `BottomSafetyInset`，即使常规导航区能被压缩出来，也必须拒绝失手。鼠标真实抓取和暂停可立即抢占；普通鼠标靠近不能把下坠切成追鼠。运行时按实际可绘制包络（含身体宽度、笔画、眼睛、脚掌、阴影和光栅留白）校验透明画布与屏幕四边，而不只检查蜥蜴中心点。

`Behavior.LostGripFall.RegripDuration` 是自动回抓的完整 `ContactHold + Recovering` 时钟；`Physics.RegripContactHoldFraction` 分配前一段给四足接触保持，剩余区间重新归一化为姿态恢复。`Physics.RegripFrontReachLengthFactor` / `RegripFrontReachOutwardWeight` 分别控制前肢伸展长度和向外权重；`Physics.RegripRearReachLengthFactor` / `RegripRearReachOutwardWeight` 独立控制后肢。schema 7 配置升级到 schema 8 时会保留已有前肢、下落和保持参数，只给新增后肢参数补默认值。`Physics.ReleasePoseRecoveryDuration` 只属于真实鼠标释放路径，不参与自动回抓。

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

跨平台 solution 使用 Gameplay self-test 运行配置、行为和 DPI/rebase 回归：

```bash
dotnet run --project tests/InfiniteLizards.Gameplay.SelfTests/InfiniteLizards.Gameplay.SelfTests.csproj -c Release
```

旧 WPF 兼容入口还保留 Windows 专项报告参数：

```powershell
DesktopLizard.exe --configuration-test="D:\Temp\configuration-report.txt"
DesktopLizard.exe --lost-grip-test="D:\Temp\lost-grip-report.txt"
```

配置测试覆盖：默认配置校验、JSON 往返、seed 持久化、schema 1–7 到 schema 8 的迁移、当前 schema 废弃字段拒绝、迁移/seed 写回失败时保留有效内存配置、坏文件保留与回退、极端有限数值/派生几何溢出保护、同 seed 确定性、个体差异、概率归一化，以及自定义状态矩阵是否真正控制选择结果。失手专项测试使用确定性矩阵，在 3 种屏幕高度、4 个起始区间和 36 个 seed 上覆盖加速度渐入、自由落体、下坠中四足同步伸手、四个独立抓点、60Hz 可见提前量、每肢臂长、一次近全伸 IK 换支、两段肢体躯干间隙与 own-envelope 不重入、受限安全投影与零回滚、最终移动帧四足接触、静止后保持抓点、动态安全区失效时无假接触、真实抓取与暂停隔离、`0/±90/180°` 身体朝向、不可安全接触负例、DPI/刷新率确定性、完整渲染边界和有限值。

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
- `SimulationRate` 必须在 60–480 Hz，固定步长与 physics 的最小/最大步长必须闭合，fallback 步长也必须位于同一区间。`Runtime.MaximumFrameCatchUp` 必须为有限值并位于 `[1/30, 1]` 秒，至少能覆盖一帧受支持的 30 Hz display cadence；Engine runtime 还会拒绝会让单帧 fixed-step 数超过安全上限的 timing。
- `Behavior.Speed.BoundaryRecoverySpeedMultiplier` 只控制碰到桌面边缘后的安全恢复速度；普通爬行速度由 `ReferenceMinimumCrawl` / `ReferenceMaximumCrawl` 控制。schema 1/2 中旧名 `BaseSpeedMultiplier` 会在迁移时自动改名。
- 调试轨迹容量最多 10,000 点，冲刺方向候选最多 256 个，防止合法 JSON 造成无界分配或超长循环。
- `CreatureCanvasSize` 必须覆盖正常姿态，`RenderCanvasSize` 必须覆盖完整悬挂/自由落体姿态。单个 model surface、换算后的 96-DIP logical surface，以及导航/完整渲染/安全包络半径都不得超过 `16,384`；所有乘法后的派生量也必须有限。校验统一考虑脊柱、腿长、脚掌、眼睛、鼻子、阴影和次级动作，避免单个“有限但极大”的 JSON 数值在 `VisualScale`/个体解析后变成 infinity 或不可用原生窗口。
- 个体体型变化超过原画布时，resolved Profile 会在 `16,384` 安全上限内向上扩展透明画布，并同步提升 `LostGripFall.BottomSafetyInset`，避免窗口内部和屏幕四边裁切；无法安全容纳则整份配置回退，不会带病创建窗口。
- 无效配置永远不会覆盖原文件。程序会记录失败原因并使用内存默认值启动；反之，迁移写回失败不会让已经完整验证的内存配置回退成默认值。
- 启动时不仅校验物种原始配置，还会用当前 `IndividualSeed` 解析并校验最终个体 Profile；因此合法字段组合若会在该个体上产生无效步态、尺寸或时长，也会安全回退而不会在创建窗口时崩溃。
