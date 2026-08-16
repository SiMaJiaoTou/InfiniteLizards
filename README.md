# Infinite Lizards

一个使用 .NET 8、Avalonia 与 Skia 的 Windows/macOS 桌面宠物。两个平台共享同一个窗口宿主、矢量渲染路径与通用引擎；平台差异只留在薄原生窗口适配层。引擎和蜥蜴 gameplay 是独立项目，显示帧率、DPI/backing scale 与原生窗口对象都不会进入 gameplay 状态机。

## 快速开始

需要 [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0) `8.0.424`，或 `global.json` 允许的更新 .NET 8 feature band。macOS 产物的最低系统版本为 macOS 11.0。

```bash
git clone https://github.com/SiMaJiaoTou/InfiniteLizards.git
cd InfiniteLizards
dotnet restore InfiniteLizards.sln
dotnet build InfiniteLizards.sln -c Release --no-restore
dotnet run --project src/InfiniteLizards.Desktop/InfiniteLizards.Desktop.csproj
```

可选启动参数：

```text
--config=/absolute/path/lizard-settings.json  使用指定配置
--diagnostic                                  启用可交互宠物、Windows 同款调试侧栏/叠层和详细日志
--debug-overlay                               保留生产窗口策略，仅显示调试侧栏与叠层
--multi-instance                              允许多实例
```

普通模式下，透明区域不拦截桌面点击；在蜥蜴身上拖动可抓起，右键菜单可暂停、居中、打开配置或退出。配置文件在首次运行时生成；使用 `--config` 可获得跨平台明确且可控的位置。

调试侧栏与原 WPF/Windows 版本使用相同的物理尺寸、两列状态字段、最近转移矩阵概率和 2×4 八动作按钮；宠物画布同时显示轨迹、目标、脊柱、腿与步进范围。右键菜单的“显示调试信息”可在运行中开关它。

## 一致性契约

- `DesktopPetRuntime<TSnapshot>` 由通用 Engine 拥有，独占显示帧累积与默认 120 Hz 固定步进调度。`IDesktopPetGame<TSnapshot>` 只接收完整的 fixed-step input，不暴露按显示帧推进的入口。
- 每块屏幕建立独立的 96-DIP/world 坐标帧。`ActiveDisplaySpace` 在跨混合 DPI 屏、缩放/排列变化与热插拔时，把持久 world 状态经 device space 有状态 rebase，避免把切屏误当成一次 gameplay 移动。
- Windows 与 macOS 共用 Avalonia + Skia 渲染及同一份不可变几何快照。相同配置、seed 与录制的 world-space 输入应产生相同固定步状态、轨迹和渲染几何；不承诺 DWM 与 WindowServer 合成像素逐位相同。

## 测试

```bash
dotnet run --project tests/DesktopPet.Engine.SelfTests/DesktopPet.Engine.SelfTests.csproj -c Release
dotnet run --project tests/InfiniteLizards.Gameplay.SelfTests/InfiniteLizards.Gameplay.SelfTests.csproj -c Release
dotnet run --project tests/InfiniteLizards.Desktop.SelfTests/InfiniteLizards.Desktop.SelfTests.csproj -c Release
```

当前自测基线为 Engine `12/12`、Gameplay `16/16`、Desktop `29/29`；Windows 原生验收控制器另有不触碰 USER32 的纯逻辑安全测试 `5/5`。它们覆盖固定步所有权、插件输入/状态保护、1x/1.25x/1.5x/2x 映射与有状态 rebase、行为/动画/物理确定性、可移植调试桥、架构依赖边界、真实 Avalonia/Skia 离屏栅格一致性，以及 Win32 shaped-region、compositor fence、生产验收协议、macOS 当前 Space 成员、前台应用身份与原生命中切换契约。

另有一个只在解锁、可交互 Windows 桌面运行的 Win32 基础能力烟测。它会暂时移动鼠标并注入左键，因此应在专用、无人操作的测试桌面运行；运行前先保存工作并松开所有鼠标键：

```powershell
dotnet run --project tests/InfiniteLizards.Windows.NativeAcceptance/InfiniteLizards.Windows.NativeAcceptance.csproj -c Release
```

默认模式用两个独立进程验证 `SetWindowRgn`、`EnableWindow`、跨进程 `SendInput`、前台保持和 GDI/USER 资源平台期。生产模式会启动实际发布的 `InfiniteLizards.Desktop.exe`，而不是替代窗口：

```powershell
dotnet publish src/InfiniteLizards.Desktop/InfiniteLizards.Desktop.csproj `
  -c Release -r win-x64 --self-contained true -o artifacts/win-x64
dotnet run --project tests/InfiniteLizards.Windows.NativeAcceptance/InfiniteLizards.Windows.NativeAcceptance.csproj `
  -c Release -- --production "$PWD\artifacts\win-x64\InfiniteLizards.Desktop.exe" `
  --require-mixed-dpi
```

生产模式逐显示器冷启动真实 Avalonia/Skia 桌宠，核对生产 HWND 身份、PMv2/client DIP、Bezier `SetWindowRgn`、DWM 屏幕合成、跨进程命中/点击、前台保持及资源平台期。Windows 上遇到锁屏、secure desktop 或非交互会话会返回退出码 `77`，不会伪装成 PASS；`--allow-skip` 只适合信息采集。该入口已交叉构建，但本机是 macOS，尚未产生 Windows 真机 PASS；逐屏冷启动也不替代跨屏拖动、热插拔与长时间 soak。

这些自测不能替代目标系统的人机验收：Windows 产物已交叉构建，但尚未在真实 Windows 硬件完成混合 DPI、DWM、点击/拖动和热插拔矩阵。与当前交付包使用同一原生 runtime 的上一轮 macOS arm64 与 osx-x64/Rosetta 包，已在解锁的 Apple Silicon 三屏环境完成当前 Space 成员、原生命中切换、2x/1x WindowServer composite 及不抢前台验收；当前 arm64 包也已重新签名并在 2x/1x 外接屏实际运行、目视核对 Windows 同款调试侧栏和 Skia 叠层，但未重跑完整 production native-acceptance 矩阵。实体左/右键与拖动事件投递、其他应用的原生全屏 Space 与 Intel 真机仍是补充验收边界。完整范围见 [CROSS_PLATFORM.md](CROSS_PLATFORM.md)。

## 发布

Windows x64：

```powershell
dotnet publish src/InfiniteLizards.Desktop/InfiniteLizards.Desktop.csproj `
  -c Release -r win-x64 --self-contained true `
  -o artifacts/win-x64
```

Apple Silicon macOS：

```bash
./scripts/publish-macos.sh osx-arm64
```

Intel macOS 使用 `./scripts/publish-macos.sh osx-x64`。产物位于 `artifacts/<rid>/InfiniteLizards.app`。脚本会逐个签名 self-contained .NET 的嵌套代码，再以 Hardened Runtime 封装并严格验证 bundle。Developer ID 产物只启用 JIT 所需的 `com.apple.security.cs.allow-jit`；没有 Team ID 的本地 ad-hoc 产物另用受限的 library-validation 例外以便实际启动。对外分发仍需用户自己的 Developer ID/Apple 公证凭据完成 notarization，详见 [CROSS_PLATFORM.md](CROSS_PLATFORM.md)。

## 代码导航

- `src/DesktopPet.Engine`：无 UI/无 gameplay 的通用契约、`DesktopPetRuntime`、固定步进、逐屏显示拓扑与 `ActiveDisplaySpace`。
- `src/InfiniteLizards.Gameplay`：独占蜥蜴的 `Core`/`Application` 行为、动画和物理源码，只实现 Engine 的 fixed-step port。
- `src/InfiniteLizards.Desktop`：Avalonia/Skia composition root、共享 presenter/view，以及 Windows HWND 与 macOS AppKit 原生边界。
- `Application/LegacyPetSimulationFrameAdapter.cs`：仅供旧 WPF 宿主使用的显示帧兼容 adapter，不进入 Gameplay assembly。
- `DesktopLizard.csproj`：上游 Windows-only WPF 兼容入口，不在跨平台 solution 内；显示帧兼容 adapter 只编入该宿主和诊断测试，不进入 Gameplay assembly。

分层规则、坐标契约和新宠物接入方式见 [ARCHITECTURE.md](ARCHITECTURE.md)。参数 schema、路径和安全边界见 [CONFIGURATION.md](CONFIGURATION.md)。
