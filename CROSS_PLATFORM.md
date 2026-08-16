# 跨平台构建与验证

## 支持边界

跨平台宿主目标是 Windows x64/arm64 与 macOS Apple Silicon/Intel，macOS 最低版本为 11.0。公共路径使用 Avalonia 11.3 + Skia；两个平台共享 `PetWindow<TSnapshot>`、Engine-owned `DesktopPetRuntime<TSnapshot>`、presenter 与矢量几何，仅在透明顶层窗口、原生命中/点击穿透、窗口放置和全局指针采样处使用原生适配。

兼容契约是：相同 Profile、seed 与录制的 world-space 输入，在 Engine 使用同一 `SimulationRate`（默认 120 Hz）的 fixed-step runtime 中产生相同的模拟轨迹与渲染几何。每块屏幕使用 96-DIP world frame，跨混合 DPI/热插拔由 `ActiveDisplaySpace` 有状态 rebase。操作系统合成后的像素不做 bit-identical 承诺。

## 本地运行

```bash
dotnet restore InfiniteLizards.sln
dotnet build InfiniteLizards.sln -c Release --no-restore
dotnet run --project src/InfiniteLizards.Desktop/InfiniteLizards.Desktop.csproj -- \
  --config="$PWD/lizard-settings.local.json" --diagnostic --multi-instance
```

`--diagnostic` 会让宠物窗口保持可交互，显示与 WPF/Windows 相同的 360×132 物理像素调试侧栏、八动作概率按钮及轨迹/骨架叠层，并输出额外日志。`--debug-overlay` 只打开同一侧栏和叠层，宠物窗口仍走 production 点击穿透策略。两种模式都可通过右键“显示调试信息”在运行中关闭；真实桌面行为仍必须在完全不带调试参数时复测一次。

完整无 UI 自测：

```bash
dotnet run --project tests/DesktopPet.Engine.SelfTests/DesktopPet.Engine.SelfTests.csproj -c Release
dotnet run --project tests/InfiniteLizards.Gameplay.SelfTests/InfiniteLizards.Gameplay.SelfTests.csproj -c Release
dotnet run --project tests/InfiniteLizards.Desktop.SelfTests/InfiniteLizards.Desktop.SelfTests.csproj -c Release
```

当前套件基线为 Engine `12/12`、Gameplay `16/16`、Desktop `29/29`。

### Windows 原生验收控制器

在解锁、可交互的 Windows 11 x64 桌面中运行：

```powershell
dotnet run --project tests/InfiniteLizards.Windows.NativeAcceptance/InfiniteLizards.Windows.NativeAcceptance.csproj -c Release
```

该程序会暂时移动鼠标并注入左键，只能在专用、测试期间无人操作的桌面运行；运行前必须松开所有鼠标键。它创建两个独立进程/独立 UI 线程的真实 HWND，验证 shaped `SetWindowRgn`、`GetWindowRgn` 所有权、`WindowFromPoint` + `SendInput` 的跨进程消息投递、`EnableWindow`/`WS_DISABLED`、`NOACTIVATE`、PMv2、负坐标 roundtrip 和重复 region 更新后的 GDI/USER 资源平台期。部分输入注入失败时会把带专用标记的 `LEFTUP` 送到已验证的自有 HWND，只有目标 WndProc 明确确认该包且异步按键态已释放后，才允许销毁窗口和恢复原光标位置。

默认命令只是 `Win32-primitives-smoke`：它不引用或启动生产 Desktop/Avalonia/Skia，不覆盖生产 Bezier GDI builder、render/region fence 或 DWM composite。要验收真实发布程序，先发布，再显式传入它：

```powershell
dotnet publish src/InfiniteLizards.Desktop/InfiniteLizards.Desktop.csproj `
  -c Release -r win-x64 --self-contained true -o artifacts/win-x64
dotnet run --project tests/InfiniteLizards.Windows.NativeAcceptance/InfiniteLizards.Windows.NativeAcceptance.csproj `
  -c Release -- --production "$PWD\artifacts\win-x64\InfiniteLizards.Desktop.exe" `
  --require-mixed-dpi
```

`production-desktop-native` 模式为每块显示器启动一个独立纯色 probe 和实际 `InfiniteLizards.Desktop.exe`。nonce、真实父 PID/Session 与 64 位输入 tag 共同证明被测 HWND 来自本次生产进程；同一 nonce 还会派生本轮独有的高对比身体/瞳孔色，同时写入临时配置与截图 oracle，旧的保活实例不能冒充当前 HWND 的 DWM 像素。外部控制器再读取 PMv2、client/outer rect、DPI、真实 window region 与屏幕 DC，并用 tagged `SendInput` 验证 region 外命中 probe、region 内命中生产桌宠、生产进程从未成为前台。它还检查 DWM 合成变化仅落在 region 内，以及短周期 GDI/USER 资源平台期。`--require-mixed-dpi` 要求至少两种真实窗口 DPI，并比较各显示器上的归一化 360-DIP client 尺寸。

这仍是逐显示器冷启动验收，不覆盖运行中的跨屏拖动/`WM_DPICHANGED`、热插拔、右键菜单、底层应用已经 capture 的拖动或长时间 soak。Windows 上遇到锁屏、secure desktop、非活动或非交互会话时默认输出 `SKIP` 并返回 `77`；只有显式 `--allow-skip` 才把 SKIP 转为 `0`，因此发布门禁不得使用该参数。仓库手动 workflow 同时运行 primitives 与 production 两种模式，只面向带 `interactive-desktop` 和 `mixed-dpi` 标签、以已登录用户交互运行的自托管 Windows runner；普通服务型或 GitHub 托管 runner 不能充当真机签收。

## 自包含发布

Windows：

```powershell
$rid = "win-x64" # 或 win-arm64
dotnet publish src/InfiniteLizards.Desktop/InfiniteLizards.Desktop.csproj `
  -c Release -r $rid --self-contained true `
  -p:DebugType=None -p:DebugSymbols=false `
  -o "artifacts/$rid"
```

macOS：

```bash
# 允许的 RID 是 osx-arm64 或 osx-x64
./scripts/publish-macos.sh osx-arm64
open artifacts/osx-arm64/InfiniteLizards.app
```

macOS 脚本会创建标准 `.app/Contents/{MacOS,Resources}` 布局与 `Info.plist`，逐个签名 `Contents/MacOS` 中 self-contained .NET 的嵌套代码，再以 Hardened Runtime 封装 bundle。Developer ID 构建使用 `scripts/macos-entitlements.plist`，只启用 runtime 执行 JIT 所需的 `com.apple.security.cs.allow-jit`。未指定身份时使用 ad-hoc 签名；由于它没有可供 hardened library validation 比对的 Team ID，本地包改用 `scripts/macos-entitlements-adhoc.plist`，额外启用 `com.apple.security.cs.disable-library-validation`，且不会用于 Developer ID 产物：

```bash
CODESIGN_IDENTITY="Developer ID Application: Example Corp (TEAMID)" \
  ./scripts/publish-macos.sh osx-arm64
```

脚本对 Developer ID 加 timestamp，并在结束前执行 `codesign --verify --deep --strict`。它不会替用户持有或推断 Apple 凭据，也不会自动公证。对外分发还需发布者使用自己的 Developer ID/Apple 凭据执行 `xcrun notarytool submit ... --wait`，成功后再 `xcrun stapler staple`；不要把证书、密码或 app-specific password 提交到仓库。

## Windows 原生验收重点

Windows 不依赖透明像素或同线程 `HTTRANSPARENT` 语义来决定跨应用命中：

- Presenter 输出身体/阴影/腿脚/头眼的共享可见与 hit pose；GDI 将 Bezier fill/stroke 和 ellipse 转成 outer-HWND 坐标 region，含目标 DPI、实测 client insets 与 1 device pixel AA 安全膨胀。
- `SetWindowRgn` 同时裁定 HWND 的可见形状和输入形状。每个新 pose/version 先安装 compositor-confirmed pose 与未确认 pose 的有界并集；只有对应 `CompositionBatch.Rendered` fence 完成后才收窄，不能让新 region 抢跑裁掉旧 backing。
- fence sequence、pose version 和 raster scale 分开单调跟踪；跨 DPI 时每个保留 pose 覆盖自己的历史 scale 与当前/目标 live scale。最多保留 8 个未确认 pose，满载、capture 或 native 安装失败时冻结新的视觉提交并保持 fail-closed，确认/恢复后再继续。
- 点击穿透由 `EnableWindow(false)`/`WS_DISABLED` 独立控制，所以关闭输入时可见 region 仍保留。只有 shaped region 安装成功后才能启用交互。
- 首次 `Show` 和构造 region 失败时必须同步安装空 region，并保持 disabled/transparent fallback。锁定的 Avalonia 版本正常不重建 HWND；若验证后句柄意外变化，新句柄先 fail-closed，随后进程 fail-stop，不能以未重放状态继续。
- backend 独占该 HWND 的 `WS_DISABLED` 作为穿透状态；`PetWindow` 不得成为 modal dialog owner。若未来加入模态设置窗口，必须提供显式 disable-ownership 仲裁或独立 owner。

真实 Windows 机器需检查：100%/125%/150%/200% 混合 DPI、多屏负坐标、跨屏拖动、鼠标丢失、底层应用正在拖动时冻结、生产窗口的跨进程点击穿透、右键菜单、热插拔/HWND rebuild、DWM 对 `SetWindowRgn` 的视觉裁切，以及 120 Hz 长时间运行的 CPU/GDI handle。当前仓库完成了自动 contract tests、Windows 交叉构建、独立 primitives smoke 与真实 production acceptance 入口；这台 macOS 开发机不能执行 USER32/DWM，尚无 Windows 真机 PASS。在真实 Windows 上跑完 production 入口与其余动态矩阵前，不能写成 Windows 真机已验。

## macOS 原生验收重点

macOS 保留 Avalonia.Native 的窗口/渲染关系：

- 原始 AvnWindow/NSWindow 持有 AvnView/content view/Skia render target，是唯一渲染和输入 surface。
- 自建的 1×1 透明 non-activating `NSPanel` 始终忽略鼠标，只作为 parent anchor；它不接管 Avalonia content。两窗都会声明跨 Space/辅助全屏 collection behavior，并使用动态取得的 status/main-menu overlay level（当前机器读回为 `25`）；本轮产品契约只覆盖普通桌面 Spaces 与多显示器，不承诺压过其他应用的原生全屏窗口或系统 UI。
- 零 ivar 动态 Objective-C subclass 明确令 AvnWindow `canBecomeKeyWindow` 和 `canBecomeMainWindow` 为 false；生产策略验证 class、getter、父子/内容所有权、level 和 collection behavior。
- 任一 nonactivation/身份验证失败会锁存 fail-closed，保持 `ignoresMouseEvents=true`，不得静默降级成抢焦点的可交互普通窗口。
- 真实 surface 用一次 `setFrame:display:` 原子放置；anchor 随 screen ID/frame、parent 丢失或热插拔重新选择目标屏。
- `Program` 用 `MacOSPlatformOptions` 关闭 Dock 显示与 Avalonia 11.3.20 默认 AppDelegate，从源头避免其启动时无条件强制激活当前进程。当前 LSUIElement 不支持 Dock reopen 或 Finder 文档/URL 打开；右键菜单退出使用显式 `desktop.Shutdown()`。

Apple Silicon 开发机上与当前交付包使用同一原生 runtime 的上一轮 arm64 包，已遍历 screen ID `1/2/4`，backing scale 分别为 `2/2/1`。每次放置均从正式 `SurfacePlacement` 入口走 managed placement、Y 转换、screen/anchor 和原子 native frame，并读回 managed/native 几何；parent/content、level/collection、动态 nonactivation subclass、非 key/main 与 application inactive 也全部 PASS。

该包在解锁后又用公开的当前 Space 窗口列表、异步 WindowServer 命中查询和整屏合成做了第二层 arm64 验收：三屏交互态都命中 surface，穿透态都命中下层窗口；2x/1x composite 均真实包含蜥蜴。默认 `open -n` 受控启动期间，`NSWorkspace` 221 个约 100 ms 样本与 `lsappinfo` 101 个约 250 ms 样本全部保持原前台应用，且宠物进程一直为 inactive。child 的 `isOnActiveSpace` getter 在跨屏重挂载时可能与实际 current-Space 列表和 composite 不一致，因此只保留作诊断。当前会话没有事件投递权限，所以这些原生命中查询不能冒充真实左/右键注入；其他应用的原生全屏 Space 也不在本版本承诺内。

加入最终启动不抢焦点选项后，上一轮 osx-x64 bundle 已重新发布、严格签名，并在 Apple Silicon/Rosetta 的解锁三屏环境重新遍历 screen ID `1/2/4`（backing `2/2/1`）。正式 `SurfacePlacement`、managed/native frame、Space 成员、非 key/main、前台应用保持和交互态→surface/穿透态→下层窗口的 WindowServer 命中读回全部 PASS；主屏整屏 composite 也真实包含 x64/Rosetta 宠物并透出下层应用。该结果证明该 x64 包的 Rosetta 路径，不替代 Intel 真机验收，也不冒充实体左/右键事件注入。当前 arm64/x64 交付包复用了这套已验原生 runtime，只更新最终 managed assemblies 并重新严格签名；它们通过自动测试与签名校验，但尚未再次运行 GUI/native acceptance。

## DPI/backing 映射核对

自动测试覆盖：

- 100%/125%/150%/200% 每块屏幕内部的 device↔world 往返映射。
- 不同 scale 屏幕之间切换 `ActiveDisplaySpace` 时，被跟踪宠物的 device 位置保持不变；不对无状态静态 world seam 做全点连续性断言。
- 主屏左侧/上方屏幕的负坐标与超出 Int16 的大坐标。
- `SurfacePlacement` 向外取整后不裁剪宠物画布。
- 分辨率、scale、排列变化和显示器移除后，持久 gameplay 状态、拖动 offset 与窗口位置经 device space 原子 rebase；rebase 不消费 RNG、不改变 FSM/计时并不中断 fixed-step carry。
- 同 seed/同 world 输入在不同 scale 和 50–240 Hz display cadence 下的 gameplay 快照一致性。
- 使用真实 `DisplayTopology`、`DesktopPetRuntime` 和 gameplay adapter 执行 420 帧 1x/1.25x/1.5x/2x 端到端回归，逐帧比较模拟状态与不可变渲染几何，包括抓取/拖动/释放区间。
- 使用生产 `LizardView` 与 `RenderTargetBitmap.Render` 执行三种姿态 × 1x/1.25x/1.5x/2x 的真实 Avalonia/Skia 离屏栅格；归一化后检查 alpha bounding box、coverage、active-cell IoU、预乘色误差与重复渲染 SHA-256，并用 4-DIP 平移负控证明比较器能拒绝错位。

真机发布矩阵：

| 平台 | 屏幕场景 | 必查行为 | 当前状态 |
|---|---|---|---|
| Windows | 100%/125%/150%/200%，主屏左/上外接屏 | region 视觉/输入形状、跨屏 rebase、拖动偏移、跨进程穿透 | 待真实 Windows 硬件 |
| Windows | 热插拔、DPI/Explorer/HWND 重建 | 首次和重建 fail-closed、无全矩形输入窗口、GDI/CPU 稳定 | 待真实 Windows 硬件 |
| macOS Apple Silicon | 三屏 backing 2/2/1、普通桌面 Space | frame/backing、parent/content、nonactivation、current-Space 成员、WindowServer 命中与 composite | 同原生 runtime 的上一轮 arm64 与 x64/Rosetta 包已过；当前重签包未重跑 GUI |
| macOS Apple Silicon | 生产交互与其他应用原生全屏 | 实体左/右键、拖动、焦点保持；若扩展全屏承诺则另测其层级 | 实体事件与全屏扩展待验，不阻塞当前多屏契约 |
| macOS Intel | Retina/非 Retina 与外接屏 | 启动、backing、Space、输入/焦点 | x64 已构建/签名并通过 Rosetta 三屏；仍待 Intel 真机 |

## 当前可复现范围

- 已建立可独立构建的 Engine、Gameplay、Desktop、三套无 UI SelfTests 与 Windows primitives/production 原生验收项目及 solution/CI 入口；当前三套测试定义为 `12 + 15 + 28` 项，原生验收控制器另有纯逻辑安全测试 `5/5`，不计入前三套数字。
- Engine 覆盖固定步所有权/过载诊断、插件输入/状态/快照保护与随机拓扑；Gameplay 覆盖行为、步态、物理、配置、reset/rebase、420 帧跨 DPI/cadence 几何一致性，并使用 MSBuild/源码门禁保证 Gameplay 是领域文件的唯一编译所有者；Desktop 覆盖 Win32 放置、两阶段 render/region 事务、shaped-region、fail-closed、混合 DPI 拖动 rebase、macOS current-Space/命中策略与真实 Skia 栅格契约。
- macOS arm64 与 osx-x64/Rosetta 的原生验收范围如上；win-x64 已完成交叉构建及 primitives/真实 production 两种验收入口，但 production 入口仍待 Windows 真机实际运行；osx-x64 仍需 Intel 真机补验，Windows arm64 也需单独发布和真机验收。
- Developer ID notarization 未执行，因为它需要发布者自己的证书和 Apple 凭据。

## 常见问题

- **macOS 首次打开被 Gatekeeper 拦截**：内部 ad-hoc 产物可在“系统设置 → 隐私与安全性”确认；对外产物应正式签名和公证，不应要求用户全局关闭 Gatekeeper。
- **宠物窗口可见但无法右键**：先使用 `--diagnostic --multi-instance` 区分 presenter 命中几何与原生点击穿透；然后在无 `--diagnostic` 的生产策略下复测。
- **Windows 宠物完全不可见**：首次/失败状态会故意安装空 fail-closed region。查看异常或诊断输出，不要删除该保护来绕过 region 构造失败。
- **多屏上体型或轨迹不同**：确认宿主平台坐标与 `Screen.Scaling` 语义一致，并运行 device↔world/`ActiveDisplaySpace` 测试；不要在 gameplay 内增加 DPI 补偿。
